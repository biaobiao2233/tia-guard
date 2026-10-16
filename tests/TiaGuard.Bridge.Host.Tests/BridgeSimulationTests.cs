using System.Text.Json;
using TiaGuard.Bridge.Host;
using Xunit;

public sealed class BridgeSimulationTests
{
    private static SimulationRequest Request() => new()
    {
        ProjectPath = Path.Combine(Path.GetTempPath(), "MotorControl.ap21"), InputAddresses = ["I0.0"], OutputAddresses = ["Q0.0"],
        Steps = [new() { Name = "start", Inputs = new() { ["I0.0"] = true }, ExpectedOutputs = new() { ["Q0.0"] = true } }]
    };
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
    private static BridgeSimulationCoordinator Coordinator(FakeBackend b, FakeEngineering? e = null, bool enabled = true) =>
        new(new() { Enabled = enabled }, e ?? new(), () => b);

    [Fact] public async Task Failed_load_cannot_execute_or_qualify_runtime_tests()
    {
        var backend = new FakeBackend(); var engineering = new FakeEngineering { LoadSuccess = false };
        var result = Json(await Coordinator(backend, engineering).TestAsync(Request(), default));
        Assert.False(result.GetProperty("qualified").GetBoolean()); Assert.Equal(0, result.GetProperty("executed").GetInt32());
        Assert.Equal(0, backend.RunCalls); Assert.Equal(0, backend.WriteCalls); Assert.Equal(0, backend.ReadCalls);
    }
    [Fact] public async Task IO_initialization_failure_preserves_successful_download_report_without_enabling_RUN()
    {
        var backend = new FakeBackend { InitializeFails = true };
        var engineering = new FakeEngineering();
        var coordinator = Coordinator(backend, engineering);
        var result = Json(await coordinator.CreateAsync(Request(), default));
        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Equal("initialize-io", result.GetProperty("phase").GetString());
        Assert.True(result.GetProperty("load").GetProperty("success").GetBoolean());
        Assert.False(Json(coordinator.Status()).GetProperty("loaded").GetBoolean());
        var id = result.GetProperty("sessionId").GetString()!;
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.SetRunAsync(id, true, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.LoadAsync(id, default));
        Assert.Equal(1, engineering.LoadCalls); Assert.Equal(0, backend.RunCalls);
        Assert.True(Json(await coordinator.CloseAsync(id, default)).GetProperty("closed").GetBoolean());
    }
    [Theory] [InlineData("compile-hardware")] [InlineData("compile-software")]
    public async Task Failed_preparation_preserves_diagnostics_and_never_starts_a_virtual_CPU(string phase)
    {
        var backend = new FakeBackend();
        var engineering = new FakeEngineering { PreparationFailure = phase };
        var coordinator = Coordinator(backend, engineering);
        var result = Json(await coordinator.CreateAsync(Request(), default));
        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Equal(phase, result.GetProperty("report").GetProperty("phase").GetString());
        Assert.Equal(0, backend.CreateCalls); Assert.Equal(0, engineering.LoadCalls);
        Assert.Equal(0, backend.RunCalls); Assert.Equal(0, backend.WriteCalls);
        var closed = Json(await coordinator.CloseAsync(result.GetProperty("sessionId").GetString()!, default));
        Assert.True(closed.GetProperty("closed").GetBoolean());
        Assert.Equal(1, engineering.DisconnectCalls);
    }
    [Theory] [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public async Task Temporary_security_options_are_independent_explicit_requests_for_owned_preparation(bool password, bool user)
    {
        Assert.False(Request().ConfigureTemporaryPlcPassword);
        Assert.False(Request().ConfigureTemporaryPlcUser);
        var request = Request(); request.ConfigureTemporaryPlcPassword = password; request.ConfigureTemporaryPlcUser = user;
        Assert.Equal(password, request.Freeze().ConfigureTemporaryPlcPassword);
        Assert.Equal(user, request.Freeze().ConfigureTemporaryPlcUser);
        var engineering = new FakeEngineering(); var coordinator = Coordinator(new(), engineering);
        var created = Json(await coordinator.CreateAsync(request, default));
        Assert.Equal(password, engineering.TemporaryPasswordRequested);
        Assert.Equal(user, engineering.TemporaryUserRequested);
        Assert.True(Json(await coordinator.CloseAsync(created.GetProperty("sessionId").GetString()!, default)).GetProperty("closed").GetBoolean());
    }
    [Fact] public async Task Actual_output_mismatch_is_preserved_and_fails()
    {
        var backend = new FakeBackend { Actual = new Dictionary<string, bool> { ["Q0.0"] = false } };
        var result = Json(await Coordinator(backend).TestAsync(Request(), default));
        Assert.False(result.GetProperty("qualified").GetBoolean()); Assert.Equal(1, result.GetProperty("executed").GetInt32());
        Assert.False(result.GetProperty("steps")[0].GetProperty("actual").GetProperty("Q0.0").GetBoolean());
        Assert.Equal(2, backend.RunCalls); // start followed by stop
    }
    [Fact] public async Task Missing_actual_output_is_not_replaced_by_expected()
    {
        var backend = new FakeBackend { Actual = new Dictionary<string, bool>() };
        var result = Json(await Coordinator(backend).TestAsync(Request(), default));
        Assert.False(result.GetProperty("qualified").GetBoolean()); Assert.Equal(0, result.GetProperty("executed").GetInt32());
        Assert.Contains("actual outputs", result.GetProperty("error").GetString());
    }
    [Fact] public async Task Complete_observations_pass_the_unit_orchestration_and_stop()
    {
        var backend = new FakeBackend(); var result = Json(await Coordinator(backend).TestAsync(Request(), default));
        Assert.True(result.GetProperty("qualified").GetBoolean()); Assert.Equal(1, result.GetProperty("executed").GetInt32());
        Assert.Equal(1, backend.ScanCalls); Assert.Equal(2, backend.RunCalls);
    }
    [Fact] public async Task Stop_failure_invalidates_an_otherwise_passing_test()
    {
        var result = Json(await Coordinator(new() { StopFails = true }).TestAsync(Request(), default));
        Assert.False(result.GetProperty("qualified").GetBoolean()); Assert.Contains("Stop/readback failed", result.GetProperty("error").GetString());
    }
    [Fact] public async Task Disabled_mode_has_no_engineering_or_runtime_effects()
    {
        var backend = new FakeBackend(); var engineering = new FakeEngineering();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Coordinator(backend, engineering, false).TestAsync(Request(), default));
        Assert.Equal(0, engineering.PrepareCalls); Assert.Equal(0, backend.CreateCalls);
    }
    [Theory] [InlineData("Q0.0")] [InlineData("I0.8")] [InlineData("I00.0")] [InlineData("I0.0:P")]
    public async Task Invalid_input_addresses_fail_before_any_effect(string address)
    {
        var request = Request(); request.InputAddresses = [address]; var backend = new FakeBackend();
        await Assert.ThrowsAsync<ArgumentException>(() => Coordinator(backend).TestAsync(request, default)); Assert.Equal(0, backend.CreateCalls);
    }
    [Fact] public async Task Output_write_is_rejected_inside_a_valid_session()
    {
        var backend = new FakeBackend(); var c = Coordinator(backend);
        var created = Json(await c.CreateAsync(Request(), default)); var id = created.GetProperty("sessionId").GetString()!;
        await Assert.ThrowsAsync<ArgumentException>(() => c.InputsAsync(id, new() { ["Q0.0"] = true }, default));
        Assert.Equal(0, backend.WriteCalls);
    }
    [Fact] public async Task Unknown_session_does_not_operate_a_simulator()
    {
        var backend = new FakeBackend(); var c = Coordinator(backend); await c.CreateAsync(Request(), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.SetRunAsync("wrong", true, default)); Assert.Equal(0, backend.RunCalls);
    }
    [Fact] public async Task Expected_output_outside_declared_scope_fails_before_creation()
    {
        var r = Request(); r.Steps[0].ExpectedOutputs.Add("Q1.0", false); var backend = new FakeBackend();
        await Assert.ThrowsAsync<ArgumentException>(() => Coordinator(backend).TestAsync(r, default)); Assert.Equal(0, backend.CreateCalls);
    }
    [Fact] public async Task Instance_creation_failure_keeps_preparation_and_can_close()
    {
        var backend = new FakeBackend { CreateFails = true }; var engineering = new FakeEngineering();
        var c = Coordinator(backend, engineering);
        var failed = Json(await c.CreateAsync(Request(), default));
        Assert.False(failed.GetProperty("success").GetBoolean());
        Assert.Equal("create-instance", failed.GetProperty("phase").GetString());
        Assert.True(failed.GetProperty("preparation").GetProperty("success").GetBoolean());
        var closed = Json(await c.CloseAsync(failed.GetProperty("sessionId").GetString()!, default));
        Assert.True(closed.GetProperty("closed").GetBoolean());
        Assert.Equal(1, engineering.DisconnectCalls);
    }
    [Fact] public async Task Failed_simulator_cleanup_releases_project_blocks_writes_and_can_retry()
    {
        var backend = new FakeBackend { CloseFails = true }; var engineering = new FakeEngineering();
        var c = Coordinator(backend, engineering);
        var created = Json(await c.CreateAsync(Request(), default)); var id = created.GetProperty("sessionId").GetString()!;
        var failed = Json(await c.CloseAsync(id, default));
        Assert.False(failed.GetProperty("success").GetBoolean());
        Assert.False(failed.GetProperty("closed").GetBoolean());
        Assert.True(failed.GetProperty("closeRequired").GetBoolean());
        Assert.Equal(1, engineering.DisconnectCalls);
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.SetRunAsync(id, true, default));
        Assert.Equal(0, backend.RunCalls);
        backend.CloseFails = false;
        Assert.True(Json(await c.CloseAsync(id, default)).GetProperty("closed").GetBoolean());
        Assert.Equal(1, engineering.DisconnectCalls);
        Assert.Equal(1, backend.DisposeCalls);
    }
    [Fact] public async Task Project_cleanup_retry_does_not_delete_or_dispose_simulator_again()
    {
        var backend = new FakeBackend(); var engineering = new FakeEngineering { DisconnectFails = true };
        var c = Coordinator(backend, engineering);
        var id = Json(await c.CreateAsync(Request(), default)).GetProperty("sessionId").GetString()!;
        Assert.False(Json(await c.CloseAsync(id, default)).GetProperty("closed").GetBoolean());
        engineering.DisconnectFails = false;
        Assert.True(Json(await c.CloseAsync(id, default)).GetProperty("closed").GetBoolean());
        Assert.Equal(1, backend.CloseCalls); Assert.Equal(1, backend.DisposeCalls);
    }
    [Fact] public async Task Backend_factory_failure_has_a_recoverable_session()
    {
        var engineering = new FakeEngineering();
        var c = new BridgeSimulationCoordinator(new() { Enabled = true }, engineering,
            () => throw new InvalidOperationException("backend factory failed"));
        var failed = Json(await c.CreateAsync(Request(), default));
        Assert.False(failed.GetProperty("success").GetBoolean()); Assert.Equal(0, engineering.PrepareCalls);
        Assert.True(Json(await c.CloseAsync(failed.GetProperty("sessionId").GetString()!, default)).GetProperty("closed").GetBoolean());
    }
    [Fact] public async Task Retry_load_uses_fresh_observation_without_recreating_project_or_instance()
    {
        var backend = new FakeBackend(); var engineering = new FakeEngineering { LoadSuccess = false };
        var c = Coordinator(backend, engineering);
        var id = Json(await c.CreateAsync(Request(), default)).GetProperty("sessionId").GetString()!;
        Assert.Equal("load", Json(c.Status()).GetProperty("phase").GetString());
        engineering.LoadSuccess = true;
        Assert.True(Json(await c.LoadAsync(id, default)).GetProperty("success").GetBoolean());
        Assert.Equal(1, engineering.PrepareCalls); Assert.Equal(2, engineering.LoadCalls);
        Assert.Equal(1, backend.CreateCalls); Assert.Equal(2, backend.ObserveCalls); Assert.Equal(0, backend.RunCalls);
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.LoadAsync(id, default));
        Assert.Equal(2, engineering.LoadCalls);
    }
    [Fact] public async Task Failed_instance_creation_cannot_be_retried_as_a_ready_download_target()
    {
        var backend = new FakeBackend { CreateFails = true }; var engineering = new FakeEngineering();
        var c = Coordinator(backend, engineering);
        var id = Json(await c.CreateAsync(Request(), default)).GetProperty("sessionId").GetString()!;
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.LoadAsync(id, default));
        Assert.Equal(0, engineering.LoadCalls); Assert.Equal(0, backend.ObserveCalls);
    }
    [Fact] public async Task Failed_RUN_acknowledgement_still_attempts_STOP_and_never_qualifies()
    {
        var backend = new FakeBackend { StartFails = true };
        var result = Json(await Coordinator(backend).TestAsync(Request(), default));
        Assert.False(result.GetProperty("qualified").GetBoolean()); Assert.Equal(0, result.GetProperty("executed").GetInt32());
        Assert.Equal(2, backend.RunCalls); Assert.Equal(0, backend.WriteCalls);
    }
    [Fact] public async Task Rejected_test_does_not_stop_or_change_an_existing_running_session()
    {
        var backend = new FakeBackend(); var engineering = new FakeEngineering();
        var c = Coordinator(backend, engineering);
        var id = Json(await c.CreateAsync(Request(), default)).GetProperty("sessionId").GetString()!;
        await c.SetRunAsync(id, true, default);
        var before = Json(c.Status()).GetRawText();
        var rejected = Json(await c.TestAsync(Request(), default));
        Assert.False(rejected.GetProperty("qualified").GetBoolean());
        Assert.Equal(0, rejected.GetProperty("executed").GetInt32());
        Assert.Equal(before, Json(c.Status()).GetRawText());
        Assert.Equal(1, backend.RunCalls); Assert.Equal(1, backend.CreateCalls);
        Assert.Equal(1, engineering.PrepareCalls); Assert.Equal(0, backend.WriteCalls);
        // The rejected request must also release the coordinator lock.
        Assert.True(Json(await c.CloseAsync(id, default)).GetProperty("closed").GetBoolean());
        Assert.Equal(2, backend.RunCalls);
    }
    [Fact] public async Task Failed_manual_RUN_has_unknown_state_and_requires_cleanup()
    {
        var backend = new FakeBackend { StartFails = true }; var c = Coordinator(backend);
        var id = Json(await c.CreateAsync(Request(), default)).GetProperty("sessionId").GetString()!;
        var failed = Json(await c.SetRunAsync(id, true, default));
        Assert.False(failed.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, Json(c.Status()).GetProperty("running").ValueKind);
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.InputsAsync(id, new() { ["I0.0"] = true }, default));
        Assert.True(Json(await c.CloseAsync(id, default)).GetProperty("closed").GetBoolean());
        Assert.Equal(2, backend.RunCalls);
    }
    [Fact] public void Another_copy_of_the_same_source_is_not_the_owned_simulation_project()
    {
        var source = Request().ProjectPath; var expected = Guid.NewGuid().ToString("N");
        var state = Json(new { connected = true, simulationBindingId = Guid.NewGuid().ToString("N"),
            project = new { Path = source, SourceKind = "offline-copy" } });
        Assert.Throws<InvalidOperationException>(() => SimulationProjectBinding.Require(state, source, expected));
    }
    [Fact] public void Attached_project_is_rejected_even_with_matching_source_path_and_token()
    {
        var source = Request().ProjectPath; var expected = Guid.NewGuid().ToString("N");
        var state = Json(new { connected = true, simulationBindingId = expected,
            project = new { Path = source, SourceKind = "attached-session" } });
        Assert.Throws<InvalidOperationException>(() => SimulationProjectBinding.Require(state, source, expected));
    }
    [Fact] public void Owned_copy_requires_current_connection_and_exact_token()
    {
        var source = Request().ProjectPath; var expected = Guid.NewGuid().ToString("N");
        var valid = Json(new { connected = true, simulationBindingId = expected,
            project = new { Path = source, SourceKind = "offline-copy" } });
        SimulationProjectBinding.Require(valid, source, expected);
        Assert.Throws<InvalidOperationException>(() => SimulationProjectBinding.Require(valid, source, "not-a-binding"));
        Assert.Throws<InvalidOperationException>(() => SimulationProjectBinding.Require(Json(new { connected = false }), source, expected));
    }
    private sealed class FakeEngineering : ISimulationEngineering
    {
        public bool LoadSuccess = true, DisconnectFails; public int PrepareCalls, DisconnectCalls, LoadCalls;
        public string? PreparationFailure;
        public bool TemporaryPasswordRequested;
        public bool TemporaryUserRequested;
        public Task<JsonElement> OpenAndPrepareAsync(string path, bool configureTemporaryPlcPassword,
            bool configureTemporaryPlcUser, CancellationToken ct)
        {
            PrepareCalls++;
            TemporaryPasswordRequested = configureTemporaryPlcPassword;
            TemporaryUserRequested = configureTemporaryPlcUser;
            return Task.FromResult(PreparationFailure == null ? Json(new { success = true }) :
                Json(new { success = false, phase = PreparationFailure, hardwareCompilation = new { ErrorCount = 1 } }));
        }
        public Task<JsonElement> LoadAsync(SimulationProof proof, CancellationToken ct) { LoadCalls++; return Task.FromResult(Json(new { success = LoadSuccess })); }
        public Task DisconnectAsync(CancellationToken ct) { DisconnectCalls++; if (DisconnectFails) throw new InvalidOperationException("disconnect failed"); return Task.CompletedTask; }
    }
    private sealed class FakeBackend : ISimulationBackend
    {
        public int CreateCalls, RunCalls, WriteCalls, ReadCalls, ScanCalls, CloseCalls, DisposeCalls, ObserveCalls;
        public bool StopFails, StartFails, CreateFails, CloseFails, InitializeFails;
        public IReadOnlyDictionary<string, bool> Actual = new Dictionary<string, bool> { ["Q0.0"] = true };
        private static SimulationProof Proof() => new(Guid.NewGuid().ToString(), "TiaGuard_unit", "192.168.0.1", DateTime.UtcNow, 1, true, "STANDARD S7-1200");
        public Task<SimulationProof> CreateAsync(string name, CancellationToken ct) { CreateCalls++; if (CreateFails) throw new InvalidOperationException("power on failed"); return Task.FromResult(Proof()); }
        public Task<SimulationProof> ObserveAsync(CancellationToken ct) { ObserveCalls++; return Task.FromResult(Proof()); }
        public Task InitializeIoAsync(string[] i, string[] q, CancellationToken ct) => InitializeFails
            ? Task.FromException(new NotSupportedException("Monitoring is unqualified.")) : Task.CompletedTask;
        public Task SetRunAsync(bool run, CancellationToken ct) { RunCalls++; if (!run && StopFails) throw new InvalidOperationException("stop failed"); if (run && StartFails) throw new InvalidOperationException("run acknowledgement failed"); return Task.CompletedTask; }
        public Task WriteInputsAsync(IReadOnlyDictionary<string, bool> inputs, CancellationToken ct) { WriteCalls++; return Task.CompletedTask; }
        public Task ScanAsync(int scans, CancellationToken ct) { ScanCalls++; return Task.CompletedTask; }
        public Task<IReadOnlyDictionary<string, bool>> ReadOutputsAsync(CancellationToken ct) { ReadCalls++; return Task.FromResult(Actual); }
        public Task CloseAsync(CancellationToken ct) { CloseCalls++; if (CloseFails) throw new InvalidOperationException("close failed"); return Task.CompletedTask; }
        public ValueTask DisposeAsync() { DisposeCalls++; return ValueTask.CompletedTask; }
    }
}
