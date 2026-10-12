using System.Text;
using C = TwinCat3.Contracts;
using Xunit;

namespace TiaGuard.TwinCat.Validation.Tests;

public class ContractServiceTests
{
    private static C.FrozenGenerationInput Input()
    {
        var manifest = new C.GenerationManifest
        {
            SchemaVersion = "1.0", SolutionName = "NewSolution", ConfigurationName = "Config", PlcProjectName = "PLC",
            Environment = new() { RequestedBuild = "4026.27" },
            Artifacts = new[] { new C.ArtifactSpec { Id = "main", Name = "MAIN", Kind = C.ArtifactKind.Program, DeclarationFile = "MAIN.decl.st", ImplementationFile = "MAIN.impl.st" } },
            Tasks = new[] { new C.TaskSpec { Name = "PlcTask", CycleTimeUs = 10000, Priority = 20, Programs = new[] { "MAIN" } } },
            Libraries = new[] { new C.LibrarySpec { Name = "Tc2_Standard", Version = "3.3.3.0", Vendor = "Beckhoff" } }
        };
        return new(manifest, new Dictionary<string, C.FrozenSourceFile>
        {
            ["MAIN.decl.st"] = new("MAIN.decl.st", Encoding.UTF8.GetBytes("PROGRAM MAIN\nVAR x:INT; END_VAR")),
            ["MAIN.impl.st"] = new("MAIN.impl.st", Encoding.UTF8.GetBytes("x:=42;"))
        }, "frozen-hash", "owned-input");
    }
    private static C.ProjectCapture Capture() => new()
    {
        ProjectIdentity = "owned-project", InputHash = "frozen-hash", SolutionName = "NewSolution", ConfigurationName = "Config", PlcProjectName = "PLC",
        ToolBuild = "3.1.4026.27", Platform = "TwinCAT RT (x64)", Saved = true, Closed = true, Reopened = true,
        ObjectsComplete = true, TasksComplete = true, LibrariesComplete = true,
        Artifacts = new[] { new C.CapturedArtifact { Name = "MAIN", Kind = C.ArtifactKind.Program,
            DeclarationText = "PROGRAM MAIN VAR x:INT; END_VAR", ImplementationText = "x:=42;", TreePath = "TIPC^PLC^PLC Project^POUs^MAIN", NativeItemType = 602 } },
        Tasks = new[] { new C.CapturedTask { Name = "PlcTask", CycleTimeUs = 10000, Priority = 20, Programs = new[] { "MAIN" }, LinkedSystemTask = "TIRT^PlcTask" } },
        Libraries = new[] { new C.CapturedLibrary { Name = "Tc2_Standard", Version = "3.3.3.0", ResolvedVersion = "3.3.3.0", Vendor = "Beckhoff" } }
    };
    [Fact] public void FrozenIndependentInputCanMatchCapturedNativeFacts() => Assert.True(new SourceBoundProjectComparer().Compare(Input(), Capture()).Passed);
    [Fact] public void ExpectedDoesNotLearnAnActualCodeEdit()
    {
        var capture = Capture();
        var modified = capture with { Artifacts = new[] { capture.Artifacts[0] with { ImplementationText = "x:=99;" } } };
        Assert.Equal(C.VerificationVerdict.Fail, new SourceBoundProjectComparer().Compare(Input(), modified).Verdict);
    }
    [Fact] public void MissingBuildPinIsUnknownRatherThanCopiedFromActual()
    {
        var baseline = Input();
        var input = new C.FrozenGenerationInput(baseline.Manifest with { Environment = baseline.Manifest.Environment with { RequestedBuild = null } },
            baseline.Files, baseline.InputHash, baseline.RootDirectory);
        Assert.Equal(C.VerificationVerdict.Unknown, new SourceBoundProjectComparer().Compare(input, Capture()).Verdict);
    }
    [Theory] [InlineData("hash")] [InlineData("identity")] [InlineData("type")] [InlineData("opaque")] [InlineData("placeholder")]
    public void MissingIdentityAndUnsupportedMetadataDoNotPass(string fault)
    {
        var capture = Capture();
        var invalid = fault switch
        {
            "hash" => capture with { InputHash = "other" },
            "identity" => capture with { ProjectIdentity = null },
            "type" => capture with { Artifacts = new[] { capture.Artifacts[0] with { NativeItemType = null } } },
            "opaque" => capture with { Artifacts = new[] { capture.Artifacts[0] with { Opaque = true } } },
            _ => capture with { Libraries = new[] { capture.Libraries[0] with { IsPlaceholder = true } } }
        };
        Assert.False(new SourceBoundProjectComparer().Compare(Input(), invalid).Passed);
    }
    [Theory] [InlineData("4026.27", "3.1.4026.27")][InlineData("3.1.4026.27", "4026.27")]
    public void ProductPrefixIsTheOnlyVersionNormalization(string shortForm, string longForm) => Assert.Equal(TwinCatBuildIdentity.Normalize(shortForm), TwinCatBuildIdentity.Normalize(longForm));
    [Theory] [InlineData("4026")][InlineData("3.2.4026.27")][InlineData("17.0")][InlineData("latest")]
    public void UnprovenVersionFormatsAreRejected(string value) => Assert.Throws<NotSupportedException>(() => TwinCatBuildIdentity.Normalize(value));
    [Fact] public async Task MissingNativeProviderIsUnavailableNeverSuccessful()
    {
        var input = Input();
        var handle = new C.NativeProjectHandle { SolutionPath = "owned/Project.sln", TwinCatProjectPath = "owned/Config.tsproj", PlcProjectName = "PLC", StageRoot = "owned", ProjectIdentity = "owned-project", InputHash = input.InputHash };
        await using var session = new FakeSession();
        var build = await new NativeEngineeringCompiler().CompileAsync(input, handle, session);
        var capture = await new NativeProjectCaptureService().CaptureAsync(input, handle, session);
        Assert.False(build.Passed); Assert.Equal(C.CompilationState.Unknown, build.State); Assert.NotEmpty(build.Failures);
        Assert.False(capture.Reopened); Assert.NotEmpty(capture.Failures);
    }
    [Fact] public async Task HandleHashMismatchFailsBeforeSessionInvocation()
    {
        var input = Input();
        var handle = new C.NativeProjectHandle { SolutionPath = "owned/Project.sln", TwinCatProjectPath = "owned/Config.tsproj", PlcProjectName = "PLC", StageRoot = "owned", ProjectIdentity = "owned-project", InputHash = "different" };
        await using var session = new FakeSession();
        Assert.False((await new NativeEngineeringCompiler().CompileAsync(input, handle, session)).Passed);
        Assert.NotEmpty((await new NativeProjectCaptureService().CaptureAsync(input, handle, session)).Failures);
        Assert.Equal(0, session.Invocations);
    }

    private sealed class FakeSession : C.IOwnedEngineeringSession
    {
        public string SessionId => "fake-owned-session";
        public C.EnvironmentReport Environment { get; } = new();
        public int Invocations { get; private set; }
        public Task<T> InvokeAsync<T>(Func<C.NativeEngineeringContext, T> callback, CancellationToken cancellationToken = default)
        { Invocations++; return Task.FromResult(callback(new() { Environment = Environment })); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
