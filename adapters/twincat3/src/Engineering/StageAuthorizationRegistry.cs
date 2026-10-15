using System.Collections.Concurrent;

namespace TwinCat3.Engineering;

/// <summary>The private lease is issued by the checked pipeline, never by a serialized worker request.</summary>
public sealed class StageAuthorizationRegistry
{
    private sealed record Binding(string SessionId, string InputHash);
    private readonly ConcurrentDictionary<string, Binding> active = new(StringComparer.OrdinalIgnoreCase);

    internal IDisposable Authorize(string stageRoot, string sessionId, string inputHash)
    {
        string root = Path.GetFullPath(stageRoot);
        if (!active.TryAdd(root, new Binding(sessionId, inputHash)))
            throw new InvalidOperationException("The stage is already allocated to an operation.");
        return new Lease(() => active.TryRemove(root, out _));
    }

    internal void Require(string stageRoot, string sessionId, string inputHash)
    {
        if (!active.TryGetValue(Path.GetFullPath(stageRoot), out Binding? binding)
            || binding.SessionId != sessionId || binding.InputHash != inputHash)
            throw new InvalidOperationException("Native creation requires an active checked pipeline allocation.");
    }

    private sealed class Lease(Action release) : IDisposable
    {
        private Action? release = release;
        public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
    }
}
