using System.Text;
using System.Text.Json;
using TwinCat3.Contracts;
using TwinCat3.Engineering;
using TwinCat3.Worker;
using Xunit;

namespace TwinCat3.WorkerTests;

public sealed class WorkerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "tc53-worker-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string input;
    private readonly string scratch;
    private readonly FakeEnvironment environment = new();
    public WorkerTests()
    {
        input = Path.Combine(root, "input"); scratch = Path.Combine(root, "scratch");
        Directory.CreateDirectory(input); Directory.CreateDirectory(scratch);
        var manifest = new GenerationManifest
        {
            SchemaVersion = "1.0", SolutionName = "Fixture", ConfigurationName = "Config", PlcProjectName = "PLC",
            Environment = new() { RequestedBuild = "4026.27" },
            Artifacts = new[] { new ArtifactSpec { Id = "main", Name = "MAIN", Kind = ArtifactKind.Program,
                DeclarationFile = "main.decl.st", ImplementationFile = "main.impl.st" } },
            Tasks = new[] { new TaskSpec { Name = "Cycle", CycleTimeUs = 10000, Priority = 20, Programs = new[] { "MAIN" } } },
            Libraries = Array.Empty<LibrarySpec>()
        };
        File.WriteAllText(Path.Combine(input, GenerationInputLoader.ManifestFileName), JsonSerializer.Serialize(manifest, ContractJson.Options));
        File.WriteAllText(Path.Combine(input, "main.decl.st"), "PROGRAM MAIN\nVAR\nEND_VAR\n");
        File.WriteAllText(Path.Combine(input, "main.impl.st"), "// Empty test program\n");
    }
    public void Dispose() => Directory.Delete(root, true);
    private WorkerBackend Backend(bool writes = false, bool native = false, TimeProvider? time = null)
        => new(new("test-pipe", "identity0123456789", root, scratch, null, "4026.27", writes), environment, environment.Report, native, time);
    private static JsonElement Payload(WorkerBackend backend, object value)
    {
        var node = JsonSerializer.SerializeToNode(value)!.AsObject();
        node["providerId"] = WorkerBackend.ProviderId; node["sessionId"] = backend.Hello()["sessionId"]!.GetValue<string>();
        return JsonSerializer.SerializeToElement(node);
    }
    private JsonElement PreviewPayload(WorkerBackend backend) => Payload(backend,
        new { operation = "build-generation", inputDirectory = input, outputDirectory = Path.Combine(root, "output") });

    [Fact] public async Task EveryAdvertisedOperationIsAcceptedByTheWireAllowlist()
    {
        await using var backend = Backend();
        foreach (var capability in backend.Hello()["capabilities"]!.AsObject())
        {
            var frame = JsonSerializer.SerializeToUtf8Bytes(new { id = "contract", identity = "bound-test",
                action = capability.Key, payload = new { } });
            Assert.Equal(capability.Key, WorkerProtocol.Parse(frame, "bound-test").Action);
        }
        Assert.Equal(0, environment.Created); // Framing does not authorize execution.
    }

    [Fact] public async Task OfflineModeDoesNotPermitRuntimeActionsOrCreateProcesses()
    {
        await using var backend = Backend(writes: true, native: true);
        foreach (var action in new[] { "start-simulation", "prepare-simulation", "resume-simulation", "simulation-status", "simulation-state", "simulation-read", "simulation-write", "stop-simulation" })
        {
            var denied = await backend.DispatchAsync(action, Payload(backend, new { }));
            Assert.False(denied["success"]!.GetValue<bool>());
            Assert.False(backend.Hello()["capabilities"]![action]!["available"]!.GetValue<bool>());
        }
        Assert.Equal(0, environment.Created); Assert.Empty(Directory.EnumerateFileSystemEntries(scratch));
    }

    [Fact] public async Task DoctorAndPreviewNeverCreateAnOwnedNativeSession()
    {
        await using var backend = Backend();
        var doctor = await backend.DispatchAsync("doctor", Payload(backend, new { }));
        var preview = await backend.DispatchAsync("preview", PreviewPayload(backend));
        Assert.True(doctor["success"]!.GetValue<bool>()); Assert.True(preview["success"]!.GetValue<bool>());
        Assert.Equal(64, preview["inputHash"]!.GetValue<string>().Length);
        Assert.Equal(0, environment.Created); Assert.Empty(Directory.EnumerateFileSystemEntries(scratch));
        Assert.False(Directory.Exists(Path.Combine(root, "output")));
    }

    [Fact] public async Task MissingBindingNeverCreatesComOrIssuesAuthority()
    {
        await using var backend = Backend(writes: true);
        var preview = await backend.DispatchAsync("preview", PreviewPayload(backend));
        var request = preview["request"]!.AsObject().DeepClone().AsObject();
        request["previewId"] = preview["previewId"]!.DeepClone(); request["inputHash"] = preview["inputHash"]!.DeepClone();
        var result = await backend.DispatchAsync("authorize", Payload(backend, request));
        Assert.False(result["success"]!.GetValue<bool>()); Assert.Equal("unavailable", result["phase"]!.GetValue<string>());
        Assert.Null(result["grantToken"]); Assert.Equal(0, environment.Created);
        Assert.False(backend.Hello()["capabilities"]!["build-generation"]!["available"]!.GetValue<bool>());
    }

    [Fact] public async Task BackendGrantIsPrivateExpiringSingleUseAndSourceBound()
    {
        var clock = new FakeTime();
        await using var backend = Backend(writes: true, native: true, time: clock);
        var preview = await backend.DispatchAsync("preview", PreviewPayload(backend));
        var request = preview["request"]!.AsObject().DeepClone().AsObject();
        request["previewId"] = preview["previewId"]!.DeepClone(); request["inputHash"] = preview["inputHash"]!.DeepClone();
        var authorized = await backend.DispatchAsync("authorize", Payload(backend, request));
        Assert.True(authorized["success"]!.GetValue<bool>()); Assert.Equal(64, authorized["grantToken"]!.GetValue<string>().Length);
        Assert.DoesNotContain("redacted", authorized["grantToken"]!.GetValue<string>());
        Assert.True(DateTimeOffset.Parse(authorized["expiresAtUtc"]!.GetValue<string>()) <= DateTimeOffset.Parse(preview["expiresAtUtc"]!.GetValue<string>()));
        request["grantToken"] = authorized["grantToken"]!.DeepClone();
        File.AppendAllText(Path.Combine(input, "main.impl.st"), "changed\n");
        var refused = await backend.DispatchAsync("build-generation", Payload(backend, request));
        Assert.False(refused["success"]!.GetValue<bool>()); Assert.Equal(0, environment.Created);
        Assert.Empty(Directory.EnumerateFileSystemEntries(scratch));
        File.WriteAllText(Path.Combine(input, "main.impl.st"), "// Empty test program\n");
        var retry = await backend.DispatchAsync("build-generation", Payload(backend, request));
        Assert.False(retry["success"]!.GetValue<bool>()); Assert.Equal(0, environment.Created);
    }

    [Fact] public async Task ExpiredPreviewCannotAuthorize()
    {
        var clock = new FakeTime(); await using var backend = Backend(writes: true, native: true, time: clock);
        var preview = await backend.DispatchAsync("preview", PreviewPayload(backend));
        var request = preview["request"]!.AsObject().DeepClone().AsObject();
        request["previewId"] = preview["previewId"]!.DeepClone(); request["inputHash"] = preview["inputHash"]!.DeepClone();
        clock.Advance(TimeSpan.FromMinutes(6));
        var result = await backend.DispatchAsync("authorize", Payload(backend, request));
        Assert.False(result["success"]!.GetValue<bool>()); Assert.Null(result["grantToken"]); Assert.Equal(0, environment.Created);
    }

    [Fact] public async Task StrictUtf8SupportsSplitMultibyteFrames()
    {
        string wire = "{\"id\":\"1\",\"action\":\"inspect\",\"identity\":\"test\",\"payload\":{\"projectPath\":\"中文\"}}\n";
        using var stream = new OneByteStream(Encoding.UTF8.GetBytes(wire));
        var frames = new List<byte[]>(); await foreach (var frame in WorkerProtocol.ReadFramesAsync(stream)) frames.Add(frame);
        var request = WorkerProtocol.Parse(Assert.Single(frames), "test");
        Assert.Equal("中文", request.Payload.GetProperty("projectPath").GetString());
    }

    [Theory]
    [InlineData("{\"id\":\"1\",\"id\":\"2\",\"action\":\"doctor\",\"identity\":\"test\",\"payload\":{}}")]
    [InlineData("{\"id\":\"1\",\"action\":\"arbitrary-com\",\"identity\":\"test\",\"payload\":{}}")]
    [InlineData("{\"id\":\"1\",\"action\":\"doctor\",\"identity\":\"foreign\",\"payload\":{}}")]
    public void InvalidEnvelopeCannotDispatch(string json) => Assert.Throws<InvalidDataException>(() => WorkerProtocol.Parse(Encoding.UTF8.GetBytes(json), "test"));
    [Fact] public void InvalidUtf8CannotDispatch() => Assert.Throws<DecoderFallbackException>(() => WorkerProtocol.Parse(new byte[] { 0xff }, "test"));

    [Fact] public async Task OversizedAndTruncatedFramesAreRejected()
    {
        using var oversized = new MemoryStream(new byte[WorkerProtocol.MaxBytes + 1]);
        await Assert.ThrowsAsync<InvalidDataException>(async () => { await foreach (var _ in WorkerProtocol.ReadFramesAsync(oversized)) { } });
        using var truncated = new MemoryStream(Encoding.UTF8.GetBytes("{}"));
        await Assert.ThrowsAsync<InvalidDataException>(async () => { await foreach (var _ in WorkerProtocol.ReadFramesAsync(truncated)) { } });
    }

    [Fact] public void ProjectSnapshotCopiesExactTreeAndRejectsExternalReferences()
    {
        var allocation = new EngineeringAllocation(root, scratch); string projectRoot = Path.Combine(root, "project");
        Directory.CreateDirectory(projectRoot);
        File.WriteAllText(Path.Combine(projectRoot, "Fixture.sln"), "Project(\"id\") = \"Config\", \"Config.tsproj\", \"id\"\nEndProject\n");
        File.WriteAllText(Path.Combine(projectRoot, "Config.tsproj"), "<TcSmProject><Project /></TcSmProject>");
        File.WriteAllText(Path.Combine(projectRoot, "MAIN.TcPOU"), "<TcPlcObject />");
        var snapshot = ProjectSnapshot.Capture(Path.Combine(projectRoot, "Fixture.sln"), allocation);
        string destination = Path.Combine(scratch, "copy"); snapshot.CopyTo(destination, allocation);
        Assert.Equal(3, snapshot.FileCount); Assert.Equal(snapshot.TreeHash, EngineeringPipeline.HashTree(destination));
        Assert.Equal(EngineeringPipeline.HashTree(projectRoot), snapshot.TreeHash);
        File.WriteAllText(Path.Combine(projectRoot, "Config.tsproj"), "<TcSmProject><Project PrjFilePath=\"../input/main.impl.st\" /></TcSmProject>");
        Assert.Throws<ArgumentException>(() => ProjectSnapshot.Capture(Path.Combine(projectRoot, "Fixture.sln"), allocation));
    }

    private sealed class FakeEnvironment : IEngineeringEnvironment
    {
        public int Created;
        public EnvironmentReport Report { get; } = new()
        {
            IsWindows = true, ProcessArchitecture = "X64", InstalledBuilds = new[] { "4026.27" },
            ProjectTemplatePath = "fixture-only-no-native-template", ProjectTemplateHash = new string('a', 64),
            PlcTemplateNames = new[] { "Empty PLC Template" },
            ComRegistrations = new[] { new ComRegistration { ProgId = "TcXaeShell.DTE.17.0", Registered = true } }
        };
        public Task<EnvironmentReport> InspectAsync(CancellationToken cancellationToken = default) => Task.FromResult(Report);
        public Task<IOwnedEngineeringSession> CreateOwnedSessionAsync(EnvironmentSelection selection, string ownedWorkspaceRoot, CancellationToken cancellationToken = default)
        { Created++; throw new InvalidOperationException("Native activation is forbidden in pure worker tests."); }
    }
    private sealed class FakeTime : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }
    private sealed class OneByteStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken);
    }
}
