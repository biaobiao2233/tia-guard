using System.Text.Json;
using TwinCat3.Contracts;
using Xunit;

namespace TwinCat3.Engineering.Tests;

/// <summary>These fake-provider checks verify operation gates; they are never XAE evidence.</summary>
public sealed class PipelineTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "tc55-pipeline-" + Guid.NewGuid().ToString("N"));
    private readonly string input;
    private readonly string scratch;
    private readonly string output;
    private readonly FakeSession session = new();
    private readonly FakeCreator creator = new();
    private readonly FakeCompiler compiler = new();
    private readonly FakeCapture capture = new();
    private readonly FakeComparer comparer = new();
    private readonly StageAuthorizationRegistry leases = new();

    public PipelineTests()
    {
        input = Path.Combine(root, "input"); scratch = Path.Combine(root, "scratch"); output = Path.Combine(root, "output");
        Directory.CreateDirectory(input); Directory.CreateDirectory(scratch);
        File.WriteAllText(Path.Combine(input, "main.decl.st"), "PROGRAM MAIN\nVAR\nEND_VAR\n");
        File.WriteAllText(Path.Combine(input, "main.impl.st"), "// Explicitly empty test program\n");
        var manifest = new GenerationManifest
        {
            SchemaVersion = "1.0", SolutionName = "Fixture", ConfigurationName = "Config", PlcProjectName = "PLC",
            Environment = new EnvironmentSelection { RequestedBuild = "4026.27" },
            Artifacts = new[] { new ArtifactSpec { Id = "main", Name = "MAIN", Kind = ArtifactKind.Program,
                DeclarationFile = "main.decl.st", ImplementationFile = "main.impl.st" } },
            Tasks = new[] { new TaskSpec { Name = "Cycle", CycleTimeUs = 10000, Priority = 20, Programs = new[] { "MAIN" } } },
            Libraries = Array.Empty<LibrarySpec>()
        };
        File.WriteAllText(Path.Combine(input, GenerationInputLoader.ManifestFileName), JsonSerializer.Serialize(manifest, ContractJson.Options));
    }

    public void Dispose() => Directory.Delete(root, true);
    private GenerationRequest Request => new() { SourceDirectory = input, OutputDirectory = output };
    private EngineeringPipeline Pipeline(IEngineeringCompiler? build = null, IProjectCaptureService? observe = null, IProjectComparer? compare = null)
        => new(new EngineeringAllocation(root, scratch), leases, new ExecutionGrantStore(true), creator,
            build ?? compiler, observe ?? capture, compare ?? comparer);
    private (GenerationPreview Preview, string Grant) Preview(EngineeringPipeline pipeline)
    {
        GenerationPreview preview = pipeline.Preview(Request, session);
        return (preview, pipeline.IssueGrant(preview.PreviewId, session.SessionId));
    }

    [Fact] public void MotorFixtureIsAValidFullyFrozenNativeInput()
    {
        var frozen = GenerationInputLoader.Load(Path.Combine(AppContext.BaseDirectory, "TestData", "motor"));
        Assert.Equal(4, frozen.Manifest.Artifacts.Count);
        Assert.Equal(7, frozen.Files.Count);
        Assert.Equal(10000, frozen.Manifest.Tasks.Single().CycleTimeUs);
        Assert.Equal(20, frozen.Manifest.Tasks.Single().Priority);
        Assert.Equal("MAIN", frozen.Manifest.Tasks.Single().Programs.Single());
        Assert.Equal("3.4.7.0", frozen.Manifest.Libraries.Single().Version);
        Assert.Equal("4026.27", frozen.Manifest.Environment.RequestedBuild);
        Assert.Equal(64, frozen.InputHash.Length);
    }

    [Fact] public async Task CompleteVerifiedOperationPublishesNewTree()
    {
        EngineeringPipeline pipeline = Pipeline(); var preview = Preview(pipeline);
        GenerationResult result = await pipeline.BuildAsync(Request, preview.Preview.PreviewId, preview.Grant, session);
        Assert.True(result.Success, string.Join(";", result.Failures)); Assert.True(result.Published);
        Assert.Equal("complete", result.Phase); Assert.True(File.Exists(result.SolutionPath));
        Assert.Equal(EngineeringPipeline.HashTree(output), result.ArtifactTreeHash);
        Assert.Equal(1, creator.Calls); Assert.Equal(1, compiler.Calls); Assert.Equal(1, capture.Calls); Assert.Equal(1, comparer.Calls);
        Assert.Empty(Directory.EnumerateDirectories(scratch));
    }

    [Theory] [InlineData("compiler")] [InlineData("capture")] [InlineData("compare")]
    public async Task MissingMandatoryServiceRejectsBeforeMutation(string absent)
    {
        var pipeline = new EngineeringPipeline(new EngineeringAllocation(root, scratch), leases,
            new ExecutionGrantStore(true), creator, absent == "compiler" ? null : compiler,
            absent == "capture" ? null : capture, absent == "compare" ? null : comparer);
        var preview = Preview(pipeline);
        GenerationResult result = await pipeline.BuildAsync(Request, preview.Preview.PreviewId, preview.Grant, session);
        Assert.False(result.Published); Assert.Equal("preflight", result.Phase); Assert.Equal(0, creator.Calls);
        Assert.False(Directory.Exists(output)); Assert.Empty(Directory.EnumerateDirectories(scratch));
    }

    [Fact] public async Task DirectWorkerCallWithoutGrantDoesNotMutate()
    {
        EngineeringPipeline pipeline = Pipeline(); var preview = Preview(pipeline);
        GenerationResult result = await pipeline.BuildAsync(Request, preview.Preview.PreviewId, "bad", session);
        Assert.False(result.Published); Assert.Equal(0, creator.Calls); Assert.False(Directory.Exists(output));
    }

    [Fact] public async Task SessionMismatchRejectsBeforeMutation()
    {
        EngineeringPipeline pipeline = Pipeline(); var preview = Preview(pipeline);
        var otherSession = new FakeSession { SessionId = "different-worker" };
        GenerationResult result = await pipeline.BuildAsync(Request, preview.Preview.PreviewId, preview.Grant, otherSession);
        Assert.False(result.Published); Assert.Equal(0, creator.Calls);
    }

    [Fact] public async Task CommentChangeAfterPreviewRejectsBeforeMutation()
    {
        EngineeringPipeline pipeline = Pipeline(); var preview = Preview(pipeline);
        File.AppendAllText(Path.Combine(input, "main.impl.st"), "// changed\n");
        GenerationResult result = await pipeline.BuildAsync(Request, preview.Preview.PreviewId, preview.Grant, session);
        Assert.False(result.Published); Assert.Equal(0, creator.Calls);
    }

    [Fact] public async Task NewSourceFileAfterPreviewRejectsBeforeMutation()
    {
        EngineeringPipeline pipeline = Pipeline(); var preview = Preview(pipeline);
        File.WriteAllText(Path.Combine(input, "unlisted.st"), "// hidden source");
        GenerationResult result = await pipeline.BuildAsync(Request, preview.Preview.PreviewId, preview.Grant, session);
        Assert.False(result.Published); Assert.Equal(0, creator.Calls);
    }

    [Fact] public async Task ChangedOutputRequestRejectsBeforeMutation()
    {
        EngineeringPipeline pipeline = Pipeline(); var preview = Preview(pipeline);
        GenerationResult result = await pipeline.BuildAsync(Request with { OutputDirectory = output + "other" },
            preview.Preview.PreviewId, preview.Grant, session);
        Assert.False(result.Published); Assert.Equal(0, creator.Calls);
    }

    [Fact] public async Task CollisionAfterPreviewPreservesExistingOutput()
    {
        EngineeringPipeline pipeline = Pipeline(); var preview = Preview(pipeline);
        Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "keep.txt"), "existing");
        GenerationResult result = await pipeline.BuildAsync(Request, preview.Preview.PreviewId, preview.Grant, session);
        Assert.False(result.Published); Assert.Equal(0, creator.Calls);
        Assert.Equal("existing", File.ReadAllText(Path.Combine(output, "keep.txt")));
    }

    [Fact] public async Task PreviewCannotBeReusedWithAnotherToken()
    {
        EngineeringPipeline pipeline = Pipeline(); var preview = Preview(pipeline);
        string otherToken = pipeline.IssueGrant(preview.Preview.PreviewId, session.SessionId);
        compiler.Customize = report => report with { State = CompilationState.Error, ErrorCount = 1, FailedProjectCount = 1 };
        GenerationResult first = await pipeline.BuildAsync(Request, preview.Preview.PreviewId, preview.Grant, session);
        GenerationResult second = await pipeline.BuildAsync(Request, preview.Preview.PreviewId, otherToken, session);
        Assert.False(first.Published); Assert.False(second.Published); Assert.Equal(1, creator.Calls);
    }

    [Theory] [InlineData("error")] [InlineData("notstarted")] [InlineData("notcompleted")]
    [InlineData("partialdiagnostics")] [InlineData("partialoutput")] [InlineData("wronginput")]
    [InlineData("wrongproject")] [InlineData("wrongplatform")] [InlineData("wrongconfiguration")]
    public async Task InconsistentOrIncompleteCompileNeverPublishes(string problem)
    {
        compiler.Customize = report => problem switch
        {
            "error" => report with { State = CompilationState.Error, ErrorCount = 1, FailedProjectCount = 1 },
            "notstarted" => report with { BuildStarted = false },
            "notcompleted" => report with { BuildCompleted = false },
            "partialdiagnostics" => report with { DiagnosticsComplete = false },
            "partialoutput" => report with { OutputCaptureComplete = false },
            "wronginput" => report with { InputHash = new string('f', 64) },
            "wrongproject" => report with { ProjectIdentity = "foreign" },
            "wrongplatform" => report with { Platform = "TwinCAT RT (x86)" },
            _ => report with { Configuration = "Debug" }
        };
        EngineeringPipeline pipeline = Pipeline(); var preview = Preview(pipeline);
        GenerationResult result = await pipeline.BuildAsync(Request, preview.Preview.PreviewId, preview.Grant, session);
        Assert.False(result.Published); Assert.Equal("compile", result.Phase); Assert.Equal(0, capture.Calls);
        Assert.True(Directory.Exists(result.RetainedStageRoot)); Assert.False(Directory.Exists(output));
    }

    [Theory] [InlineData(VerificationVerdict.Fail)] [InlineData(VerificationVerdict.Unknown)]
    public async Task FailedOrUnknownComparisonNeverPublishes(VerificationVerdict verdict)
    {
        comparer.Customize = report => report with { Verdict = verdict };
        EngineeringPipeline pipeline = Pipeline(); var preview = Preview(pipeline);
        GenerationResult result = await pipeline.BuildAsync(Request, preview.Preview.PreviewId, preview.Grant, session);
        Assert.False(result.Published); Assert.Equal("verify", result.Phase); Assert.False(Directory.Exists(output));
    }

    [Theory] [InlineData("object")] [InlineData("task")] [InlineData("library")] [InlineData("closed")]
    [InlineData("reopened")] [InlineData("saved")] [InlineData("foreign")] [InlineData("unsupported")]
    public async Task IncompleteReadbackRejectsEvenIfComparerClaimsPass(string problem)
    {
        capture.Customize = report => problem switch
        {
            "object" => report with { ObjectsComplete = false }, "task" => report with { TasksComplete = false },
            "library" => report with { LibrariesComplete = false }, "closed" => report with { Closed = false },
            "reopened" => report with { Reopened = false }, "saved" => report with { Saved = false },
            "foreign" => report with { ProjectIdentity = "foreign" },
            _ => report with { Unsupported = new[] { "unproven metadata" } }
        };
        EngineeringPipeline pipeline = Pipeline(); var preview = Preview(pipeline);
        GenerationResult result = await pipeline.BuildAsync(Request, preview.Preview.PreviewId, preview.Grant, session);
        Assert.False(result.Published); Assert.Equal("verify", result.Phase);
    }

    [Fact] public async Task SourceDriftDuringCompileRejectsBeforePublication()
    {
        compiler.Customize = report => { File.AppendAllText(Path.Combine(input, "main.impl.st"), "// concurrent change"); return report; };
        EngineeringPipeline pipeline = Pipeline(); var preview = Preview(pipeline);
        GenerationResult result = await pipeline.BuildAsync(Request, preview.Preview.PreviewId, preview.Grant, session);
        Assert.False(result.Published); Assert.Equal("publish", result.Phase); Assert.False(Directory.Exists(output));
    }

    [Fact] public async Task OutputCollisionDuringCompileDoesNotReplaceExistingTree()
    {
        compiler.Customize = report => { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "keep.txt"), "untouched"); return report; };
        EngineeringPipeline pipeline = Pipeline(); var preview = Preview(pipeline);
        GenerationResult result = await pipeline.BuildAsync(Request, preview.Preview.PreviewId, preview.Grant, session);
        Assert.False(result.Published); Assert.Equal("publish", result.Phase);
        Assert.Equal("untouched", File.ReadAllText(Path.Combine(output, "keep.txt")));
    }

    [Fact] public async Task NativeCreatorCannotBeInvokedWithoutInternalStageLease()
    {
        string stage = Path.Combine(scratch, "manual-stage"); Directory.CreateDirectory(stage);
        var native = new NativeEngineeringCreator(new EngineeringAllocation(root, scratch), leases);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await native.CreateAsync(GenerationInputLoader.Load(input), stage, session));
        Assert.Equal(0, session.Calls); Assert.Empty(Directory.EnumerateFileSystemEntries(stage));
    }

    public sealed class FakeSession : IOwnedEngineeringSession
    {
        public string SessionId { get; set; } = "owned-fake-session";
        public int Calls { get; private set; }
        public EnvironmentReport Environment { get; } = new()
        {
            IsWindows = true, ProcessArchitecture = "X64", InstalledBuilds = new[] { "4026.27" },
            ProjectTemplatePath = "fake-only-template.tsproj", ProjectTemplateHash = new string('a', 64),
            PlcTemplateNames = new[] { "Empty PLC Template" },
            ComRegistrations = new[] { new ComRegistration { ProgId = "TcXaeShell.DTE.17.0", Registered = true } }
        };
        public Task<T> InvokeAsync<T>(Func<NativeEngineeringContext, T> callback, CancellationToken cancellationToken = default)
        {
            Calls++; return Task.FromResult(callback(new NativeEngineeringContext { Environment = Environment }));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeCreator : IEngineeringCreator
    {
        public int Calls { get; private set; }
        public Task<NativeProjectHandle> CreateAsync(FrozenGenerationInput input, string stageRoot,
            IOwnedEngineeringSession session, CancellationToken cancellationToken = default)
        {
            Calls++;
            string solution = Path.Combine(stageRoot, input.Manifest.SolutionName + ".sln");
            string project = Path.Combine(stageRoot, input.Manifest.ConfigurationName + ".tsproj");
            File.WriteAllText(solution, "Fake fixture - not a native XAE project");
            File.WriteAllText(project, "Fake fixture - not a native XAE project");
            File.WriteAllText(Path.Combine(stageRoot, "PLC.plcproj"), "Fake fixture");
            return Task.FromResult(new NativeProjectHandle { SolutionPath = solution, TwinCatProjectPath = project,
                PlcProjectName = input.Manifest.PlcProjectName, StageRoot = stageRoot, InputHash = input.InputHash,
                ProjectIdentity = NativeProjectHandle.ComputeIdentity(project, input.Manifest.ConfigurationName, input.Manifest.PlcProjectName) });
        }
    }

    private sealed class FakeCompiler : IEngineeringCompiler
    {
        public int Calls { get; private set; }
        public Func<CompilationReport, CompilationReport>? Customize { get; set; }
        public Task<CompilationReport> CompileAsync(FrozenGenerationInput input, NativeProjectHandle project,
            IOwnedEngineeringSession session, CancellationToken cancellationToken = default)
        {
            Calls++;
            var report = new CompilationReport { State = CompilationState.Success, BuildStarted = true, BuildCompleted = true,
                FailedProjectCount = 0, ErrorCount = 0, WarningCount = 0, DiagnosticsComplete = true, OutputCaptureComplete = true,
                Configuration = input.Manifest.Environment.BuildConfiguration, Platform = input.Manifest.Environment.TargetPlatform,
                ProjectIdentity = project.ProjectIdentity, InputHash = input.InputHash };
            return Task.FromResult(Customize?.Invoke(report) ?? report);
        }
    }

    private sealed class FakeCapture : IProjectCaptureService
    {
        public int Calls { get; private set; }
        public Func<ProjectCapture, ProjectCapture>? Customize { get; set; }
        public Task<ProjectCapture> CaptureAsync(FrozenGenerationInput input, NativeProjectHandle project,
            IOwnedEngineeringSession session, CancellationToken cancellationToken = default)
        {
            Calls++;
            var report = new ProjectCapture { InputHash = input.InputHash, ProjectIdentity = project.ProjectIdentity,
                Saved = true, Closed = true, Reopened = true, ObjectsComplete = true, TasksComplete = true, LibrariesComplete = true };
            return Task.FromResult(Customize?.Invoke(report) ?? report);
        }
    }

    private sealed class FakeComparer : IProjectComparer
    {
        public int Calls { get; private set; }
        public Func<VerificationReport, VerificationReport>? Customize { get; set; }
        public VerificationReport Compare(FrozenGenerationInput input, ProjectCapture capture)
        {
            Calls++; var report = new VerificationReport { Verdict = VerificationVerdict.Pass, Complete = true };
            return Customize?.Invoke(report) ?? report;
        }
    }
}
