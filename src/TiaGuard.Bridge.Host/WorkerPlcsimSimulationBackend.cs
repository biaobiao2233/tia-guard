using System.Text.Json;

namespace TiaGuard.Bridge.Host;

// A private desktop binding per simulation prevents manual desktop commands from
// adopting a simulation's native session. Engineering remains in the same Worker.
public sealed class WorkerPlcsimSimulationBackend : ISimulationBackend
{
    private readonly BridgePlcsimDesktopCoordinator _desktop;
    private string? _session, _id, _name;
    public WorkerPlcsimSimulationBackend(SimulationOptions options, IPlcsimDesktopGateway gateway) =>
        _desktop = new(options, gateway);

    public async Task<SimulationProof> CreateAsync(string instanceName, CancellationToken ct)
    {
        if (_session != null) throw new InvalidOperationException("This simulation already owns a desktop binding.");
        var opened = JsonSerializer.SerializeToElement(await _desktop.OpenAsync(ct));
        if (opened.TryGetProperty("sessionId", out var session) && session.ValueKind == JsonValueKind.String) _session = session.GetString();
        RequireSuccess(opened);
        if (_session == null) throw new InvalidOperationException("The native desktop did not return an owned session.");
        var created = await ActAsync("create", instanceName, ct);
        _id = created.GetProperty("instanceId").GetString(); _name = instanceName;
        await ActAsync("power-on", null, ct);
        return await ObserveAsync(ct);
    }
    public async Task<SimulationProof> ObserveAsync(CancellationToken ct)
    {
        if (_session == null || _id == null || _name == null) throw new InvalidOperationException("No native simulation instance is bound.");
        var observed = await ActAsync("open-details", null, ct);
        RequireSuccess(observed);
        return ReadProof(observed, _id, _name);
    }
    public static SimulationProof ReadProof(JsonElement observed, string expectedId, string expectedName)
    {
        RequireSuccess(observed);
        var controls = observed.GetProperty("controls").EnumerateArray().ToArray();
        bool HasText(string name) => controls.Any(c => c.GetProperty("name").GetString() == name);
        var addressId = "x1-interface-" + expectedId;
        if (observed.GetProperty("instanceId").GetString() != expectedId || observed.GetProperty("instanceName").GetString() != expectedName ||
            observed.GetProperty("instanceCount").GetInt32() != 1 || observed.GetProperty("powered").ValueKind != JsonValueKind.True ||
            observed.GetProperty("virtualCpuProcessIds").GetArrayLength() != 1 || !HasText("S7-1200") ||
            !controls.Any(c => c.GetProperty("name").GetString()?.Trim() == "STANDARD") ||
            !controls.Any(c => c.GetProperty("name").GetString() == "192.168.0.1" &&
                c.GetProperty("ancestorIds").EnumerateArray().Any(a => a.GetString() == addressId)))
            throw new InvalidOperationException("The native simulator identity, type, power or owned address changed.");
        if (!observed.TryGetProperty("observedAtUtc", out var timestamp) || timestamp.ValueKind != JsonValueKind.String || !timestamp.TryGetDateTime(out var at))
            throw new InvalidOperationException("Native simulation observation must contain an ISO 8601 UTC timestamp.");
        if (at.Kind != DateTimeKind.Utc || DateTime.UtcNow - at > TimeSpan.FromSeconds(20) || at > DateTime.UtcNow.AddSeconds(1))
            throw new InvalidOperationException("Native simulation observation is stale.");
        return new(expectedId, expectedName, "192.168.0.1", at, 1, true, "STANDARD S7-1200");
    }
    private async Task<JsonElement> ActAsync(string action, string? name, CancellationToken ct)
    {
        if (_session == null) throw new InvalidOperationException("No native desktop session.");
        var result = JsonSerializer.SerializeToElement(await _desktop.ActAsync(new() { SessionId = _session, Action = action, Name = name }, ct));
        RequireSuccess(result); return result;
    }
    private static void RequireSuccess(JsonElement value)
    {
        if (!value.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True)
            throw new InvalidOperationException(value.TryGetProperty("error", out var error) ? error.GetString() : "Native desktop operation failed.");
    }
    private static Task NotQualified() => Task.FromException(new NotSupportedException("Native RUN/SIM table control has not yet been qualified; no guessed I/O or browser fallback is used."));
    public async Task InitializeIoAsync(string[] inputs, string[] outputs, CancellationToken ct)
    {
        if (_session == null) throw new InvalidOperationException("No native desktop session.");
        RequireSuccess(JsonSerializer.SerializeToElement(await _desktop.ActAsync(new() {
            SessionId = _session, Action = "prepare-io", InputAddresses = inputs, OutputAddresses = outputs }, ct)));
        await NotQualified();
    }
    public Task SetRunAsync(bool run, CancellationToken ct) => NotQualified();
    public Task WriteInputsAsync(IReadOnlyDictionary<string, bool> inputs, CancellationToken ct) => NotQualified();
    public Task ScanAsync(int scans, CancellationToken ct) => NotQualified();
    public Task<IReadOnlyDictionary<string, bool>> ReadOutputsAsync(CancellationToken ct) =>
        Task.FromException<IReadOnlyDictionary<string, bool>>(new NotSupportedException("Native actual-output reading has not yet been qualified."));
    public async Task CloseAsync(CancellationToken ct)
    {
        if (_session == null) return;
        RequireSuccess(JsonSerializer.SerializeToElement(await _desktop.CloseAsync(_session, ct)));
        _session = _id = _name = null;
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
