using TiaGuard.TwinCat.Validation;
using Xunit;

namespace TiaGuard.TwinCat.Validation.Tests;

public class PlcBuildEvidenceTests
{
    private static readonly string Stage = Path.Combine(Path.GetTempPath(), "tc53-buildgate-fixture");
    private static readonly string Solution = Path.Combine(Stage, "Demo.sln");
    private static readonly string PlcProject = Path.Combine(Stage, "Configuration", "PLC", "PLC.plcproj");
    private static readonly string ConfigurationProject = Path.Combine(Stage, "Configuration", "Configuration.tsproj");
    private static readonly string PlcUniqueName = Path.GetRelativePath(Stage, PlcProject);
    private const string Configuration = "Release";
    private const string Platform = "TwinCAT RT (x64)";

    private static PlcSolutionContext ValidContext() => new(PlcUniqueName, Configuration, Platform, true);
    private static PlcContextEvidence Context(params PlcSolutionContext[] contexts)
        => PlcBuildIdentity.CheckContexts(Solution, PlcProject, Configuration, Platform, contexts);
    private static PlcProjectBuildTracker Tracker() => new(Solution, PlcProject, Configuration, Platform, ConfigurationProject);
    private static void Begin(PlcProjectBuildTracker tracker, string? project = null, bool active = true)
        => tracker.Begin(project ?? PlcUniqueName, Configuration, Platform, Configuration, active);
    private static void Done(PlcProjectBuildTracker tracker, string? project = null, bool success = true, bool active = true)
        => tracker.Done(project ?? PlcUniqueName, Configuration, Platform, Configuration, success, active);
    private static CompilationEvidence Evaluate(PlcContextEvidence context, PlcProjectBuildTracker tracker)
        => CompilationGate.Evaluate(new()
        {
            BuildInvoked = true, BuildStarted = true, BuildCompleted = true,
            IntendedSolution = true, IntendedConfiguration = true, IntendedPlatform = true,
            IntendedPlcProject = context.IntendedProject, PlcProjectBuildEnabled = context.BuildEnabled,
            PlcProjectBuildStarted = tracker.Started, PlcProjectBuildCompleted = tracker.Completed,
            PlcProjectBuildSucceeded = tracker.Succeeded, PlcProjectEventsCorrelated = tracker.Correlated,
            FreshDiagnosticBaseline = true, FailedProjectCount = 0,
            ErrorList = new(0, 0, Array.Empty<CapturedDiagnostic>(), Array.Empty<string>()),
            BuildOutputComplete = true, BuildOutput = "Build: 1 succeeded, 0 failed. 0 errors, 0 warnings.",
            CollectorProblems = context.Problems.Concat(tracker.Problems).ToArray()
        });

    [Fact]
    public void RelativeContextAndAbsoluteProjectEventsResolveToSamePlcProject()
    {
        var tracker = Tracker();
        Begin(tracker, PlcProject); Done(tracker, PlcProject);
        Assert.True(Evaluate(Context(ValidContext()), tracker).Success);
    }

    [Theory]
    [InlineData("disabled")] [InlineData("configuration")] [InlineData("platform")]
    [InlineData("foreignpath")] [InlineData("simplename")] [InlineData("missing")] [InlineData("duplicate")]
    public void InvalidSolutionContextCannotPassEvenWithSuccessfulPlcEvents(string fault)
    {
        var expected = ValidContext();
        var contexts = fault switch
        {
            "disabled" => new[] { expected with { ShouldBuild = false } },
            "configuration" => new[] { expected with { Configuration = "Debug" } },
            "platform" => new[] { expected with { Platform = "TwinCAT RT (x86)" } },
            "foreignpath" => new[] { expected with { ProjectName = Path.Combine(Stage, "Other", "PLC.plcproj") } },
            "simplename" => new[] { expected with { ProjectName = "PLC" } },
            "duplicate" => new[] { expected, expected },
            _ => Array.Empty<PlcSolutionContext>()
        };
        var tracker = Tracker(); Begin(tracker); Done(tracker);
        var result = Evaluate(Context(contexts), tracker);
        Assert.Equal(CompilationVerdict.Unknown, result.Verdict);
        Assert.False(result.Success);
    }

    [Fact]
    public void OtherContextsDoNotSubstituteForMissingPlcContext()
    {
        var context = Context(new PlcSolutionContext(ConfigurationProject, Configuration, Platform, true));
        Assert.False(context.IntendedProject);
        Assert.False(context.BuildEnabled);
    }

    [Theory]
    [InlineData("none")] [InlineData("beginonly")] [InlineData("donefirst")]
    [InlineData("foreign")] [InlineData("simplename")] [InlineData("configurationproject")]
    [InlineData("configuration")] [InlineData("platform")] [InlineData("solutionconfiguration")]
    [InlineData("outsidebuild")] [InlineData("duplicatebegin")] [InlineData("duplicatedone")]
    public void SolutionSuccessCannotHideMissingOrUncorrelatedPlcBuildEvents(string fault)
    {
        var tracker = Tracker();
        switch (fault)
        {
            case "none": break;
            case "beginonly": Begin(tracker); break;
            case "donefirst": Done(tracker); Begin(tracker); break;
            case "foreign":
                Begin(tracker, Path.Combine(Stage, "Other", "PLC.plcproj"));
                Done(tracker, Path.Combine(Stage, "Other", "PLC.plcproj")); break;
            case "simplename": Begin(tracker, "PLC"); Done(tracker, "PLC"); break;
            case "configurationproject": Begin(tracker, ConfigurationProject); Done(tracker, ConfigurationProject); break;
            case "configuration":
                tracker.Begin(PlcUniqueName, "Debug", Platform, Configuration, true);
                tracker.Done(PlcUniqueName, "Debug", Platform, Configuration, true, true); break;
            case "platform":
                tracker.Begin(PlcUniqueName, Configuration, "TwinCAT RT (x86)", Configuration, true);
                tracker.Done(PlcUniqueName, Configuration, "TwinCAT RT (x86)", Configuration, true, true); break;
            case "solutionconfiguration":
                tracker.Begin(PlcUniqueName, Configuration, Platform, "Debug", true);
                tracker.Done(PlcUniqueName, Configuration, Platform, "Debug", true, true); break;
            case "outsidebuild": Begin(tracker, active: false); Done(tracker, active: false); break;
            case "duplicatebegin": Begin(tracker); Begin(tracker); Done(tracker); break;
            case "duplicatedone": Begin(tracker); Done(tracker); Done(tracker); break;
        }
        var result = Evaluate(Context(ValidContext()), tracker);
        Assert.Equal(CompilationVerdict.Unknown, result.Verdict);
        Assert.False(result.Success);
    }

    [Fact]
    public void ForeignEventCannotBeRepairedByLaterValidEvents()
    {
        var tracker = Tracker();
        Begin(tracker, Path.Combine(Stage, "Foreign.plcproj"));
        Begin(tracker); Done(tracker);
        Assert.Equal(CompilationVerdict.Unknown, Evaluate(Context(ValidContext()), tracker).Verdict);
    }

    [Fact]
    public void FailedPlcCompletionCannotPassZeroErrorSolution()
    {
        var tracker = Tracker(); Begin(tracker); Done(tracker, success: false);
        Assert.Equal(CompilationVerdict.Error, Evaluate(Context(ValidContext()), tracker).Verdict);
    }

    [Fact]
    public void OwnedConfigurationProjectEventsDoNotReplaceOrInvalidatePlcEvidence()
    {
        var tracker = Tracker();
        Begin(tracker, ConfigurationProject); Done(tracker, ConfigurationProject);
        Begin(tracker); Done(tracker);
        Assert.True(Evaluate(Context(ValidContext()), tracker).Success);
    }

    [Fact]
    public void MismatchedConfigurationProjectEventCannotHideBehindValidPlcEvents()
    {
        var tracker = Tracker();
        tracker.Begin(ConfigurationProject, "Debug", Platform, Configuration, true);
        Begin(tracker); Done(tracker);
        Assert.Equal(CompilationVerdict.Unknown, Evaluate(Context(ValidContext()), tracker).Verdict);
    }
}
