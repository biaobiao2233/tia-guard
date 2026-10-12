using System.Security.Cryptography;

namespace TwinCat3.Engineering;

/// <summary>Worker-local authority. Tokens are opaque; caller-created payloads grant no authority.</summary>
public sealed class ExecutionGrantStore
{
    private sealed record Grant(string SessionId, string RequestHash, DateTimeOffset ExpiresAt);
    private readonly Dictionary<string, Grant> grants = new(StringComparer.Ordinal);
    private readonly object sync = new();
    private readonly bool writesEnabled;
    private readonly TimeProvider time;

    public ExecutionGrantStore(bool writesEnabled = false, TimeProvider? time = null)
    {
        this.writesEnabled = writesEnabled;
        this.time = time ?? TimeProvider.System;
    }

    public string Issue(string sessionId, string requestHash, TimeSpan? lifetime = null)
    {
        if (!writesEnabled) throw new InvalidOperationException("Offline writes are not enabled for this worker.");
        RequireBinding(sessionId, requestHash);
        TimeSpan ttl = lifetime ?? TimeSpan.FromMinutes(5);
        if (ttl <= TimeSpan.Zero || ttl > TimeSpan.FromMinutes(10))
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        lock (sync)
        {
            DateTimeOffset now = time.GetUtcNow();
            foreach (string key in grants.Where(p => p.Value.ExpiresAt <= now).Select(p => p.Key).ToArray())
                grants.Remove(key);
            if (grants.Count >= 256) throw new InvalidOperationException("Too many pending write grants.");
            grants.Add(TokenHash(token), new Grant(sessionId, requestHash, now + ttl));
        }
        return token;
    }

    public void Consume(string token, string sessionId, string requestHash)
    {
        if (!writesEnabled) throw new InvalidOperationException("Offline writes are not enabled for this worker.");
        RequireBinding(sessionId, requestHash);
        if (token is null || token.Length != 64 || token.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidOperationException("A valid single-use write grant is required.");
        lock (sync)
        {
            // Remove on any attempted use: a wrong binding cannot preserve a token for retry.
            if (!grants.Remove(TokenHash(token), out Grant? grant))
                throw new InvalidOperationException("The write grant is invalid or already consumed.");
            if (grant.ExpiresAt <= time.GetUtcNow() || grant.SessionId != sessionId || grant.RequestHash != requestHash)
                throw new InvalidOperationException("The write grant expired or belongs to a different request/session.");
        }
    }

    private static string TokenHash(string token) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(token)));

    private static void RequireBinding(string sessionId, string requestHash)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > 128 || string.IsNullOrEmpty(requestHash)
            || requestHash.Length != 64 || requestHash.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("A concrete worker session and SHA-256 request binding are required.");
    }
}
