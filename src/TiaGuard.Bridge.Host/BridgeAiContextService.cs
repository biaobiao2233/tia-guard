using System.Text.Json;
using System.Text.Json.Nodes;

namespace TiaGuard.Bridge.Host;

public interface IBridgeEngineeringGateway
{
    int Generation { get; }

    Task<string> IdentityAsync(CancellationToken cancellationToken);

    Task<string> QueryAsync(
        string kind,
        string? block,
        int? network,
        string? symbol,
        bool force,
        CancellationToken cancellationToken);

    Task<string> PreviewPatchAsync(string patchJson, CancellationToken cancellationToken);

    Task<string> ApplyPatchAsync(
        string patchJson,
        string expectedContentId,
        string expectedFingerprint,
        int expectedEpoch,
        string? injectFailure,
        CancellationToken cancellationToken);
}

public sealed class BridgeWorkerEngineeringGateway : IBridgeEngineeringGateway
{
    private readonly BridgeWorkerClient _worker;

    public BridgeWorkerEngineeringGateway(BridgeWorkerClient worker)
    {
        _worker = worker;
    }

    public int Generation => _worker.Generation;

    public Task<string> IdentityAsync(CancellationToken cancellationToken)
        => _worker.CallAsync("get_ai_context_identity", cancellationToken: cancellationToken);

    public Task<string> QueryAsync(
        string kind,
        string? block,
        int? network,
        string? symbol,
        bool force,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new QueryPayload(kind, block, network ?? 0, symbol, force));
        return _worker.CallAsync("query_ai", payloadJson: payload, cancellationToken: cancellationToken);
    }

    public Task<string> PreviewPatchAsync(string patchJson, CancellationToken cancellationToken)
        => _worker.CallAsync(
            "preview_engineering_patch",
            payloadJson: patchJson,
            cancellationToken: cancellationToken);

    public Task<string> ApplyPatchAsync(
        string patchJson,
        string expectedContentId,
        string expectedFingerprint,
        int expectedEpoch,
        string? injectFailure,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new ApplyPayload(
            patchJson, expectedContentId, expectedFingerprint, expectedEpoch, injectFailure));
        return _worker.CallAsync(
            "apply_engineering_patch",
            payloadJson: payload,
            cancellationToken: cancellationToken);
    }

    private sealed record QueryPayload(string Kind, string? Block, int Network, string? Symbol, bool Force);

    private sealed record ApplyPayload(
        string PatchJson,
        string ExpectedContentId,
        string ExpectedFingerprint,
        int ExpectedEpoch,
        string? InjectFailure);
}

public sealed class BridgeAiContextService
{
    private readonly IBridgeEngineeringGateway _gateway;
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);
    private string _scope = string.Empty;

    public BridgeAiContextService(IBridgeEngineeringGateway gateway)
    {
        _gateway = gateway;
    }

    public Task<string> GetProjectContextAsync(CancellationToken cancellationToken = default)
        => QueryAsync("project", null, null, null, false, cancellationToken);

    public Task<string> GetProgramGraphAsync(string block, CancellationToken cancellationToken = default)
        => QueryAsync("graph", block, null, null, false, cancellationToken);

    public Task<string> GetNetworkAsync(string block, int network, CancellationToken cancellationToken = default)
        => QueryAsync("network", block, network, null, false, cancellationToken);

    public Task<string> WhereUsedAsync(string symbol, CancellationToken cancellationToken = default)
        => QueryAsync("whereUsed", null, null, symbol, false, cancellationToken);

    public async Task<string> RefreshAsync(CancellationToken cancellationToken = default)
    {
        Invalidate();
        return await QueryAsync("project", null, null, null, true, cancellationToken)
            .ConfigureAwait(false);
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            _cache.Clear();
            _scope = string.Empty;
        }
    }

    private async Task<string> QueryAsync(
        string kind,
        string? block,
        int? network,
        string? symbol,
        bool force,
        CancellationToken cancellationToken)
    {
        if (!force)
        {
            var identity = await _gateway.IdentityAsync(cancellationToken).ConfigureAwait(false);
            var scope = ScopeOf(identity, _gateway.Generation);
            var key = Key(scope, kind, block, network, symbol);
            lock (_gate)
            {
                if (string.Equals(_scope, scope, StringComparison.Ordinal) &&
                    _cache.TryGetValue(key, out var cached))
                    return MarkCacheHit(cached);
            }
        }

        var response = await _gateway.QueryAsync(
            kind, block, network, symbol, force, cancellationToken).ConfigureAwait(false);
        var actualScope = ScopeOf(response, _gateway.Generation);
        var actualKey = Key(actualScope, kind, block, network, symbol);
        lock (_gate)
        {
            if (!string.Equals(_scope, actualScope, StringComparison.Ordinal))
            {
                _cache.Clear();
                _scope = actualScope;
            }
            _cache[actualKey] = response;
        }
        return response;
    }

    private static string Key(string scope, string kind, string? block, int? network, string? symbol)
        => scope + "|" + kind + "|" + (block ?? string.Empty) + "|" +
           (network?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty) +
           "|" + (symbol ?? string.Empty);

    internal static string ScopeOf(string json, int generation)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return GetString(root, "targetBindingHash") + "|" +
            GetString(root, "contentId") + "|" +
            GetInt(root, "epoch").ToString(System.Globalization.CultureInfo.InvariantCulture) +
            "|" + generation.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string MarkCacheHit(string json)
    {
        var node = JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidOperationException("AI context cache entry is not a JSON object.");
        node["cacheHit"] = true;
        return node.ToJsonString();
    }

    private static string GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static int GetInt(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number)
            ? number
            : 0;
}
