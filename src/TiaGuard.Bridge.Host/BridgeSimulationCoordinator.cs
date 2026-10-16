using System.Text.Json;
using System.Text.RegularExpressions;

namespace TiaGuard.Bridge.Host;

public sealed class SimulationOptions
{
    public bool Enabled { get; init; }
    public string? ExecutablePath { get; init; }
    public string Endpoint { get; init; } = "http://127.0.0.1:8101/";
}

public sealed class SimulationRequest
{
    public string ProjectPath { get; set; } = "";
    [System.ComponentModel.Description("Explicit operator-approved opt-in: generate a temporary configuration password only in the owned disposable PLC copy whose default protection has no password. Never changes existing credentials, originals or real PLCs. Default false.")]
    public bool ConfigureTemporaryPlcPassword { get; set; }
    [System.ComponentModel.Description("Explicit operator-approved opt-in: create a dedicated temporary local user and custom role with Full access only for the owned disposable CPU. Preserve existing users and rights; no engineering/admin or Anonymous rights. Default false.")]
    public bool ConfigureTemporaryPlcUser { get; set; }
    public string[] InputAddresses { get; set; } = [];
    public string[] OutputAddresses { get; set; } = [];
    public SimulationTestStep[] Steps { get; set; } = [];
    public SimulationRequest Freeze() => JsonSerializer.Deserialize<SimulationRequest>(JsonSerializer.Serialize(this))!;
    public void Validate(bool requireSteps = false)
    {
        if (!Path.IsPathFullyQualified(ProjectPath) || !ProjectPath.EndsWith(".ap21", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("An absolute V21 .ap21 project path is required.");
        ValidateIoAddressSets(InputAddresses, OutputAddresses);
        if (Steps == null || Steps.Length > 1000 || (requireSteps && Steps.Length == 0))
            throw new ArgumentException("A test requires 1–1000 steps.");
        foreach (var step in Steps)
        {
            if (step == null || string.IsNullOrWhiteSpace(step.Name) || step.Scans < 1 || step.Scans > 100)
                throw new ArgumentException("Each step needs a name and 1–100 scans.");
            ValidateInputs(step.Inputs);
            if (step.ExpectedOutputs == null || step.ExpectedOutputs.Count == 0 ||
                step.ExpectedOutputs.Keys.Any(k => !OutputAddresses.Contains(k, StringComparer.Ordinal)))
                throw new ArgumentException("Expected outputs must be nonempty and belong to the declared output set.");
        }
    }
    public static void ValidateIoAddressSets(string[]? inputs, string[]? outputs)
    {
        ValidateAddresses(inputs, 'I'); ValidateAddresses(outputs, 'Q');
        if (inputs!.Length > 64 || outputs!.Length > 64 || outputs.Length == 0)
            throw new ArgumentException("At most 64 Bool input/output addresses and at least one output are required.");
    }
    public void ValidateInputs(IReadOnlyDictionary<string, bool>? values)
    {
        if (values == null || values.Count != InputAddresses.Length ||
            values.Keys.Any(k => !InputAddresses.Contains(k, StringComparer.Ordinal)))
            throw new ArgumentException("Every step must provide exactly all declared inputs; output writes are forbidden.");
    }
    private static void ValidateAddresses(string[]? addresses, char area)
    {
        if (addresses == null || addresses.Distinct(StringComparer.Ordinal).Count() != addresses.Length ||
            addresses.Any(a => a == null || !Regex.IsMatch(a, @"\A" + area + @"(?:0|[1-9][0-9]{0,3})\.[0-7]\z")))
            throw new ArgumentException("Only unique canonical Bool I/Q addresses are supported.");
    }
}
public sealed class SimulationTestStep
{
    public string Name { get; set; } = "";
    public Dictionary<string, bool> Inputs { get; set; } = [];
    public Dictionary<string, bool> ExpectedOutputs { get; set; } = [];
    public int Scans { get; set; } = 1;
}
public sealed record SimulationProof(string InstanceId, string InstanceName, string Address,
    DateTime ObservedAtUtc, int InstanceCount, bool Powered, string Kind)
{
    public string? ProjectBindingId { get; init; }
}
public static class SimulationProjectBinding
{
    public static void Require(JsonElement state, string expectedSourcePath, string expectedBindingId)
    {
        if (!Guid.TryParse(expectedBindingId, out var id) || id == Guid.Empty ||
            !state.TryGetProperty("connected", out var connected) || connected.ValueKind != JsonValueKind.True ||
            !state.TryGetProperty("simulationBindingId", out var binding) || binding.GetString() != expectedBindingId ||
            !state.TryGetProperty("project", out var project) || project.ValueKind != JsonValueKind.Object ||
            !project.TryGetProperty("SourceKind", out var kind) || kind.GetString() != "offline-copy" ||
            !project.TryGetProperty("Path", out var path) ||
            !string.Equals(path.GetString(), expectedSourcePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The owned simulation project binding changed.");
    }
}
public interface ISimulationBackend : IAsyncDisposable
{
    Task<SimulationProof> CreateAsync(string instanceName, CancellationToken ct);
    Task<SimulationProof> ObserveAsync(CancellationToken ct);
    Task InitializeIoAsync(string[] inputs, string[] outputs, CancellationToken ct);
    Task SetRunAsync(bool run, CancellationToken ct);
    Task WriteInputsAsync(IReadOnlyDictionary<string, bool> inputs, CancellationToken ct);
    Task ScanAsync(int scans, CancellationToken ct);
    Task<IReadOnlyDictionary<string, bool>> ReadOutputsAsync(CancellationToken ct);
    Task CloseAsync(CancellationToken ct);
}
public interface ISimulationEngineering
{
    Task<JsonElement> OpenAndPrepareAsync(string projectPath, bool configureTemporaryPlcPassword,
        bool configureTemporaryPlcUser, CancellationToken ct);
    Task<JsonElement> LoadAsync(SimulationProof proof, CancellationToken ct);
    Task DisconnectAsync(CancellationToken ct);
}
public sealed class WorkerSimulationEngineering(BridgeWorkerClient worker) : ISimulationEngineering
{
    private string? _ownedPath;
    private string? _bindingId;
    public async Task<JsonElement> OpenAndPrepareAsync(string projectPath, bool configureTemporaryPlcPassword,
        bool configureTemporaryPlcUser, CancellationToken ct)
    {
        var state = JsonSerializer.Deserialize<JsonElement>(await worker.CallAsync("get_state", cancellationToken: ct));
        if (state.GetProperty("connected").GetBoolean())
            throw new InvalidOperationException("Disconnect the current project before starting an isolated simulation.");
        var opened = JsonSerializer.Deserialize<JsonElement>(await worker.CallAsync("open_offline_project", projectPath: projectPath, cancellationToken: ct));
        var project = opened.TryGetProperty("project", out var p) ? p : opened;
        _ownedPath = project.GetProperty("Path").GetString();
        _bindingId = opened.GetProperty("simulationBindingId").GetString();
        await RequireBindingAsync(ct);
        return JsonSerializer.Deserialize<JsonElement>(await worker.CallAsync("prepare_simulation",
            payloadJson: JsonSerializer.Serialize(new { ProjectBindingId = _bindingId,
                ConfigureTemporaryPlcPassword = configureTemporaryPlcPassword,
                ConfigureTemporaryPlcUser = configureTemporaryPlcUser }), cancellationToken: ct));
    }
    public async Task<JsonElement> LoadAsync(SimulationProof proof, CancellationToken ct)
    {
        await RequireBindingAsync(ct);
        return JsonSerializer.Deserialize<JsonElement>(await worker.CallAsync("load_simulation",
            payloadJson: JsonSerializer.Serialize(proof with { ProjectBindingId = _bindingId }), cancellationToken: ct));
    }
    private async Task RequireBindingAsync(CancellationToken ct)
    {
        var state = JsonSerializer.Deserialize<JsonElement>(await worker.CallAsync("get_state", cancellationToken: ct));
        if (_ownedPath == null || _bindingId == null) throw new InvalidOperationException("No owned simulation project binding.");
        SimulationProjectBinding.Require(state, _ownedPath, _bindingId);
    }
    public async Task DisconnectAsync(CancellationToken ct)
    {
        if (_ownedPath == null) return;
        var state = JsonSerializer.Deserialize<JsonElement>(await worker.CallAsync("get_state", cancellationToken: ct));
        if (state.GetProperty("connected").GetBoolean())
        {
            if (_bindingId == null) throw new InvalidOperationException("No owned simulation project binding.");
            SimulationProjectBinding.Require(state, _ownedPath, _bindingId);
            await worker.CallAsync("disconnect", payloadJson: JsonSerializer.Serialize(new { ProjectBindingId = _bindingId }), cancellationToken: ct);
        }
        _ownedPath = _bindingId = null;
    }
}

public sealed class BridgeSimulationCoordinator(SimulationOptions options,
    ISimulationEngineering engineering, Func<ISimulationBackend> backendFactory)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ISimulationBackend? _backend;
    private SimulationRequest? _request;
    private string? _sessionId;
    private bool _loaded;
    private bool _running;
    private bool _runMayBeActive;
    private bool _runStateKnown = true;
    private bool _closing;
    private bool _engineeringDisconnected;
    private bool _instanceReady;
    private JsonElement? _preparation;
    private JsonElement? _loadReport;
    private string _phase = "idle";
    public object Status(object? requiredInteraction = null) => new { enabled = options.Enabled, sessionId = _sessionId, loaded = _loaded,
        running = _runStateKnown ? (bool?)_running : null, runStateKnown = _runStateKnown,
        closeRequired = _closing, phase = _phase, backend = "plcsim-v21-flaui-uia3", requiredInteraction, scope = "CPU1212C/V4.7 Bool I/Q; simulator only" };
    public async Task<object> CreateAsync(SimulationRequest request, CancellationToken ct)
    {
        request = request.Freeze();
        request.Validate(); RequireEnabled();
        await _gate.WaitAsync(ct);
        try { return await CreateCoreAsync(request, ct); }
        finally { _gate.Release(); }
    }
    private async Task<object> CreateCoreAsync(SimulationRequest request, CancellationToken ct)
    {
        if (_sessionId != null) throw new InvalidOperationException("Close the existing simulation session explicitly.");
        _sessionId = Guid.NewGuid().ToString("N"); _request = request; _engineeringDisconnected = false;
        _phase = "create-backend";
        _preparation = null;
        _loadReport = null;
        try
        {
            _backend = backendFactory();
            _phase = "prepare";
            var prepared = await engineering.OpenAndPrepareAsync(request.ProjectPath,
                request.ConfigureTemporaryPlcPassword, request.ConfigureTemporaryPlcUser, ct);
            _preparation = prepared;
            if (!Successful(prepared)) return new { success = false, sessionId = _sessionId, phase = _phase, report = prepared };
            _phase = "create-instance";
            await _backend.CreateAsync("TiaGuard_" + _sessionId[..12], ct);
            _instanceReady = true;
            return await LoadCoreAsync(ct);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        { return new { success = false, sessionId = _sessionId, phase = _phase, preparation = _preparation, load = _loadReport, error = error.Message }; }
    }
    public async Task<object> LoadAsync(string sessionId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            RequireSession(sessionId);
            if (_closing || !_instanceReady || _loaded || _phase != "load")
                throw new InvalidOperationException("Retry loading only an owned, prepared instance whose previous load failed.");
            try { return await LoadCoreAsync(ct); }
            catch (Exception error) when (error is not OperationCanceledException)
            { return new { success = false, sessionId = _sessionId, phase = _phase, preparation = _preparation, load = _loadReport, error = error.Message }; }
        }
        finally { _gate.Release(); }
    }
    private async Task<object> LoadCoreAsync(CancellationToken ct)
    {
        _phase = "load";
        _loadReport = null;
        var result = await engineering.LoadAsync(await _backend!.ObserveAsync(ct), ct);
        _loadReport = result;
        if (!Successful(result)) return new { success = false, sessionId = _sessionId, phase = _phase, preparation = _preparation, report = result };
        _phase = "initialize-io";
        await _backend.InitializeIoAsync(_request!.InputAddresses, _request.OutputAddresses, ct);
        _loaded = true; _phase = "ready";
        return new { success = true, sessionId = _sessionId, phase = _phase, preparation = _preparation, load = result };
    }
    public async Task<object> SetRunAsync(string sessionId, bool run, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            RequireLoaded(sessionId);
            _runMayBeActive |= run; _runStateKnown = false;
            try
            {
                await _backend!.SetRunAsync(run, ct);
                _running = _runMayBeActive = run; _runStateKnown = true;
                return new { success = true, running = run };
            }
            catch (Exception error)
            {
                _closing = true;
                if (error is OperationCanceledException) throw;
                return new { success = false, phase = "run", closeRequired = true, error = error.Message };
            }
        }
        finally { _gate.Release(); }
    }
    public async Task<object> InputsAsync(string sessionId, Dictionary<string, bool> inputs, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { RequireLoaded(sessionId); _request!.ValidateInputs(inputs); await _backend!.WriteInputsAsync(inputs, ct); return new { success = true }; }
        finally { _gate.Release(); }
    }
    public async Task<object> ScanAsync(string sessionId, int scans, CancellationToken ct)
    {
        if (scans < 1 || scans > 100) throw new ArgumentException("scans must be 1–100.");
        await _gate.WaitAsync(ct);
        try { RequireLoaded(sessionId); if (!_running) throw new InvalidOperationException("Start simulation before scanning."); await _backend!.ScanAsync(scans, ct); return new { success = true, scans }; }
        finally { _gate.Release(); }
    }
    public async Task<object> OutputsAsync(string sessionId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { RequireLoaded(sessionId); return new { success = true, observedAtUtc = DateTime.UtcNow, values = await ReadCompleteAsync(ct) }; }
        finally { _gate.Release(); }
    }
    public async Task<object> TestAsync(SimulationRequest request, CancellationToken ct)
    {
        request = request.Freeze();
        request.Validate(requireSteps: true); RequireEnabled();
        await _gate.WaitAsync(ct);
        var steps = new List<object>(); bool passed = true, ownsTestSession = false; string? errorText = null; object? setup = null;
        try
        {
            if (_sessionId != null) throw new InvalidOperationException("Close the existing simulation session before testing.");
            ownsTestSession = true;
            setup = await CreateCoreAsync(request, ct);
            if (!_loaded) return new { success = false, qualified = false, executed = 0, planned = request.Steps.Length, setup };
            _runMayBeActive = true; _runStateKnown = false;
            await _backend!.SetRunAsync(true, ct); _running = true; _runStateKnown = true;
            foreach (var step in request.Steps)
            {
                ct.ThrowIfCancellationRequested();
                await _backend.WriteInputsAsync(step.Inputs, ct);
                await _backend.ScanAsync(step.Scans, ct);
                var actual = await ReadCompleteAsync(ct);
                bool matches = step.ExpectedOutputs.All(p => actual[p.Key] == p.Value); passed &= matches;
                steps.Add(new { step.Name, inputs = step.Inputs, expected = step.ExpectedOutputs,
                    actual, pass = matches, observedAtUtc = DateTime.UtcNow, scans = step.Scans });
            }
        }
        catch (Exception e) when (e is not OperationCanceledException) { passed = false; errorText = e.Message; }
        finally
        {
            try { if (ownsTestSession && _loaded && _runMayBeActive) { await _backend!.SetRunAsync(false, CancellationToken.None); _running = _runMayBeActive = false; _runStateKnown = true; } }
            catch (Exception e) { passed = false; _runStateKnown = false; _closing = true; errorText = "Stop/readback failed: " + e.Message; }
            _gate.Release();
        }
        return new { success = passed && steps.Count == request.Steps.Length, qualified = passed && steps.Count == request.Steps.Length,
            executed = steps.Count, planned = request.Steps.Length, sessionId = _sessionId, setup, steps, error = errorText };
    }
    public async Task<object> CloseAsync(string sessionId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            RequireSession(sessionId);
            _closing = true;
            _phase = "close";
            var errors = new List<string>();
            if (_runMayBeActive && _backend != null)
            {
                try { await _backend.SetRunAsync(false, ct); _running = _runMayBeActive = false; _runStateKnown = true; }
                catch (Exception e) when (e is not OperationCanceledException) { _runStateKnown = false; errors.Add("stop: " + e.Message); }
            }
            if (_backend != null)
            {
                try
                {
                    await _backend.CloseAsync(ct);
                    _running = _loaded = _runMayBeActive = false; _runStateKnown = true;
                    await _backend.DisposeAsync();
                    _backend = null;
                }
                catch (Exception e) when (e is not OperationCanceledException) { errors.Add("simulator: " + e.Message); }
            }
            if (!_engineeringDisconnected)
            {
                try { await engineering.DisconnectAsync(ct); _engineeringDisconnected = true; }
                catch (Exception e) when (e is not OperationCanceledException) { errors.Add("project: " + e.Message); }
            }
            bool closed = _backend == null && _engineeringDisconnected;
            if (closed) { _sessionId = null; _request = null; _preparation = null; _loaded = _running = _closing = _instanceReady = _runMayBeActive = false; _runStateKnown = true; _phase = "idle"; }
            return new { success = closed && errors.Count == 0, closed, sessionId = closed ? null : _sessionId,
                phase = "close", closeRequired = !closed, errors };
        }
        finally { _gate.Release(); }
    }
    private async Task<IReadOnlyDictionary<string, bool>> ReadCompleteAsync(CancellationToken ct)
    {
        var actual = await _backend!.ReadOutputsAsync(ct);
        if (actual == null || actual.Count != _request!.OutputAddresses.Length ||
            _request.OutputAddresses.Any(a => !actual.ContainsKey(a)))
            throw new InvalidOperationException("The simulator did not return all requested actual outputs.");
        return new Dictionary<string, bool>(actual, StringComparer.Ordinal);
    }
    private void RequireEnabled() { if (!options.Enabled) throw new InvalidOperationException("Start the Bridge with --allow-write --enable-simulation to operate an owned simulator."); }
    private void RequireSession(string id) { RequireEnabled(); if (_sessionId == null || id != _sessionId) throw new InvalidOperationException("Unknown simulation session."); }
    private void RequireLoaded(string id) { RequireSession(id); if (_closing) throw new InvalidOperationException("Complete pending simulation cleanup before operating this session."); if (!_loaded) throw new InvalidOperationException("Simulation loading has not completed successfully."); }
    private static bool Successful(JsonElement report) => report.TryGetProperty("success", out var v) && v.ValueKind == JsonValueKind.True;
}
