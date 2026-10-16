using System.Text.Json;

namespace TiaGuard.Bridge.Host;

public interface IPlcsimDesktopGateway
{
    int Generation { get; }
    Task<JsonElement> CallAsync(string method, object payload, CancellationToken ct);
}
public sealed class WorkerPlcsimDesktopGateway(BridgeWorkerClient worker) : IPlcsimDesktopGateway
{
    public int Generation => worker.Generation;
    public async Task<JsonElement> CallAsync(string method, object payload, CancellationToken ct) =>
        JsonSerializer.Deserialize<JsonElement>(await worker.CallAsync(method, payloadJson: JsonSerializer.Serialize(payload), cancellationToken: ct));
}
public sealed class PlcsimDesktopActionRequest
{
    public string SessionId { get; set; } = "";
    public string Action { get; set; } = "";
    public string? Name { get; set; }
    public string[]? InputAddresses { get; set; }
    public string[]? OutputAddresses { get; set; }
}
public sealed class BridgePlcsimDesktopCoordinator(SimulationOptions options, IPlcsimDesktopGateway gateway)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _session;
    private int _generation;
    public object Status() => new { success = true, enabled = options.Enabled, sessionId = _session,
        workerIdentityKnown = _session == null || gateway.Generation == _generation, backend = "flaui-uia3", experimental = true,
        runtimeQualified = false, supportedActions = new[] { "create", "rename", "open-details", "delete", "power-on", "power-off", "prepare-io" } };
    public async Task<object> OpenAsync(CancellationToken ct)
    {
        RequireEnabled();
        await _gate.WaitAsync(ct);
        try
        {
            if (_session != null) throw new InvalidOperationException("Close the current owned desktop session first.");
            var result = await gateway.CallAsync("open_plcsim_desktop", new { options.ExecutablePath }, ct);
            if (result.TryGetProperty("sessionId", out var session) && session.ValueKind == JsonValueKind.String)
            {
                _session = session.GetString();
                _generation = gateway.Generation;
            }
            return result;
        }
        finally { _gate.Release(); }
    }
    public Task<object> InspectAsync(string sessionId, CancellationToken ct) => InvokeAsync("inspect_plcsim_desktop", sessionId, new { SessionId = sessionId }, false, ct);
    public Task<object> CloseAsync(string sessionId, CancellationToken ct) => InvokeAsync("close_plcsim_desktop", sessionId, new { SessionId = sessionId }, true, ct);
    public Task<object> ActAsync(PlcsimDesktopActionRequest request, CancellationToken ct)
    {
        var action = request.Action;
        var name = request.Name;
        var session = request.SessionId;
        var inputs = request.InputAddresses?.ToArray(); var outputs = request.OutputAddresses?.ToArray();
        if (action is not ("create" or "rename" or "open-details" or "delete" or "power-on" or "power-off" or "prepare-io"))
            throw new ArgumentException("Supported desktop actions: create, rename, open-details, delete, power-on, power-off, prepare-io.");
        if (action == "prepare-io") SimulationRequest.ValidateIoAddressSets(inputs, outputs);
        if (action == "rename" || (action == "create" && name != null))
        {
            if (name == null || !System.Text.RegularExpressions.Regex.IsMatch(name, @"\ATiaGuard_[A-Za-z0-9_]{1,50}\z"))
                throw new ArgumentException("Invalid owned simulator name.");
        }
        return InvokeAsync("act_plcsim_desktop", session, new { SessionId = session, Action = action, Name = name, InputAddresses = inputs, OutputAddresses = outputs }, false, ct);
    }
    private async Task<object> InvokeAsync(string method, string session, object payload, bool closing, CancellationToken ct)
    {
        RequireEnabled();
        await _gate.WaitAsync(ct);
        try
        {
            if (string.IsNullOrWhiteSpace(session) || session != _session || gateway.Generation != _generation)
                throw new InvalidOperationException("Desktop session or Worker identity changed; refusing adoption.");
            var result = await gateway.CallAsync(method, payload, ct);
            if (gateway.Generation != _generation) throw new InvalidOperationException("Worker restarted during desktop operation; result is not qualified.");
            if (closing && result.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True)
                _session = null;
            return result;
        }
        finally { _gate.Release(); }
    }
    private void RequireEnabled()
    {
        if (!options.Enabled) throw new InvalidOperationException("Start with --allow-write --enable-simulation to operate the simulator desktop.");
    }
}
