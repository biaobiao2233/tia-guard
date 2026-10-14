namespace TiaGuard.TwinCat.Validation;

public enum DiagnosticSeverity { Unknown, Error, Warning, Information }
public enum DiagnosticCorrelation { Unknown, CurrentBuild, Stale, Foreign }
public enum CompilationVerdict { Unknown, Error, Success }

public sealed record CapturedDiagnostic(
    DiagnosticSeverity Severity,
    string Message,
    string? Project,
    string? File,
    int? Line,
    int? Column,
    string? Code,
    DiagnosticCorrelation Correlation,
    string Source = "DTE.ErrorList");

public sealed record DiagnosticCollection(
    int? DeclaredCount,
    int? FinalCount,
    IReadOnlyList<CapturedDiagnostic> Items,
    IReadOnlyList<string> Problems)
{
    public bool Complete => DeclaredCount.HasValue && FinalCount == DeclaredCount &&
        DeclaredCount == Items.Count && Problems.Count == 0 &&
        Items.All(x => x.Severity != DiagnosticSeverity.Unknown && x.Correlation == DiagnosticCorrelation.CurrentBuild);
}

public sealed record BuildObservation
{
    public bool BuildInvoked { get; init; }
    public bool BuildStarted { get; init; }
    public bool BuildCompleted { get; init; }
    public bool TimedOut { get; init; }
    public bool IntendedSolution { get; init; }
    public bool IntendedConfiguration { get; init; }
    public bool IntendedPlatform { get; init; }
    public bool IntendedPlcProject { get; init; }
    public bool PlcProjectBuildEnabled { get; init; }
    public bool PlcProjectBuildStarted { get; init; }
    public bool PlcProjectBuildCompleted { get; init; }
    public bool? PlcProjectBuildSucceeded { get; init; }
    public bool PlcProjectEventsCorrelated { get; init; }
    public bool FreshDiagnosticBaseline { get; init; }
    public int? FailedProjectCount { get; init; }
    public DiagnosticCollection? ErrorList { get; init; }
    public bool BuildOutputComplete { get; init; }
    public string? BuildOutput { get; init; }
    public IReadOnlyList<string> CollectorProblems { get; init; } = Array.Empty<string>();
}

public sealed record CompilationEvidence(
    CompilationVerdict Verdict,
    bool BuildStarted,
    bool BuildCompleted,
    int? FailedProjectCount,
    bool DiagnosticsComplete,
    int ErrorCount,
    int WarningCount,
    IReadOnlyList<CapturedDiagnostic> Diagnostics,
    string? BuildOutput,
    IReadOnlyList<string> Problems)
{
    public bool Success => Verdict == CompilationVerdict.Success && DiagnosticsComplete;
    public bool OutputCaptureComplete { get; init; }
}

public static class CompilationGate
{
    // Empty errors, successful transport, and LastBuildInfo alone are never sufficient.
    public static CompilationEvidence Evaluate(BuildObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var problems = observation.CollectorProblems.ToList();
        void Require(bool condition, string message) { if (!condition) problems.Add(message); }
        Require(observation.BuildInvoked, "The intended build was not invoked.");
        Require(observation.BuildStarted, "No actual build-start evidence.");
        Require(observation.BuildCompleted, "No actual build-completion evidence.");
        Require(!observation.TimedOut, "Build timeout.");
        Require(observation.IntendedSolution, "Solution identity is missing or different.");
        Require(observation.IntendedConfiguration, "Configuration identity is missing or different.");
        Require(observation.IntendedPlatform, "Platform identity is missing or different.");
        Require(observation.IntendedPlcProject, "The intended PLC project context/path/configuration/platform is missing or different.");
        Require(observation.PlcProjectBuildEnabled, "The intended PLC project is not enabled for this build (ShouldBuild).");
        Require(observation.PlcProjectBuildStarted, "No correlated PLC project build-start event.");
        Require(observation.PlcProjectBuildCompleted, "No correlated PLC project build-completion event.");
        Require(observation.PlcProjectBuildSucceeded.HasValue, "PLC project build success result is missing.");
        Require(observation.PlcProjectEventsCorrelated, "PLC project build events are foreign, ambiguous, or out of order.");
        Require(observation.FreshDiagnosticBaseline, "Diagnostics cannot be correlated to a fresh build.");
        Require(observation.FailedProjectCount >= 0, "LastBuildInfo is missing or invalid.");
        Require(observation.ErrorList?.Complete == true, "Error List capture is incomplete or uncorrelated.");
        Require(observation.BuildOutputComplete && !string.IsNullOrWhiteSpace(observation.BuildOutput), "Build Output capture is missing or incomplete.");
        if (observation.ErrorList != null) problems.AddRange(observation.ErrorList.Problems);
        var items = observation.ErrorList?.Items ?? Array.Empty<CapturedDiagnostic>();
        if (observation.BuildOutput != null) problems.AddRange(OutputDiagnosticGuard.Check(observation.BuildOutput, items));
        var errors = items.Count(x => x.Severity == DiagnosticSeverity.Error);
        var warnings = items.Count(x => x.Severity == DiagnosticSeverity.Warning);
        var complete = problems.Count == 0;
        var verdict = !complete ? CompilationVerdict.Unknown :
            errors > 0 || observation.FailedProjectCount > 0 || observation.PlcProjectBuildSucceeded == false
                ? CompilationVerdict.Error : CompilationVerdict.Success;
        return new CompilationEvidence(verdict, observation.BuildStarted, observation.BuildCompleted,
            observation.FailedProjectCount, complete, errors, warnings, items,
            observation.BuildOutput, problems.Distinct(StringComparer.Ordinal).ToArray())
            { OutputCaptureComplete = observation.BuildOutputComplete && !string.IsNullOrWhiteSpace(observation.BuildOutput) };
    }
}

public sealed record PlcSolutionContext(string ProjectName, string Configuration, string Platform, bool ShouldBuild);
public sealed record PlcContextEvidence(bool IntendedProject, bool BuildEnabled, IReadOnlyList<string> Problems);

// Solution success can exclude a disabled PLC. Resolve the actual plcproj context independently.
public static class PlcBuildIdentity
{
    public static bool MatchesProject(string solutionPath, string intendedProjectPath, string observedProject)
    {
        if (string.IsNullOrWhiteSpace(observedProject)) return false;
        try
        {
            var solutionDirectory = Path.GetDirectoryName(Path.GetFullPath(solutionPath))!;
            var observedPath = Path.GetFullPath(observedProject, solutionDirectory);
            return string.Equals(observedPath, Path.GetFullPath(intendedProjectPath), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    public static PlcContextEvidence CheckContexts(string solutionPath, string projectPath, string configuration,
        string platform, IEnumerable<PlcSolutionContext> contexts)
    {
        var matches = contexts.Where(x => MatchesProject(solutionPath, projectPath, x.ProjectName)).ToArray();
        if (matches.Length != 1)
            return new(false, false, new[] { $"Expected exactly one SolutionContext for the intended PLC project; found {matches.Length}." });
        var context = matches[0];
        var problems = new List<string>();
        var identity = context.Configuration == configuration && context.Platform == platform;
        if (!identity) problems.Add($"PLC SolutionContext identity differs: {context.ProjectName} | {context.Configuration} | {context.Platform}.");
        if (!context.ShouldBuild) problems.Add($"PLC SolutionContext.ShouldBuild is false: {context.ProjectName}.");
        return new(identity, identity && context.ShouldBuild, problems);
    }
}

// Only exact PLC path/configuration/platform events count. The owned tsproj may also emit events.
public sealed class PlcProjectBuildTracker(string solutionPath, string projectPath, string configuration,
    string platform, string configurationProjectPath)
{
    private readonly List<string> problems = new();
    public bool Started { get; private set; }
    public bool Completed { get; private set; }
    public bool? Succeeded { get; private set; }
    public bool Correlated => problems.Count == 0;
    public IReadOnlyList<string> Problems => problems;

    private bool IsIntended(string project, string projectConfiguration, string observedPlatform, string solutionConfiguration,
        bool currentSolutionBuild)
    {
        if (!currentSolutionBuild)
        {
            problems.Add("Project build event occurred outside the invoked solution build.");
            return false;
        }
        var isConfigurationProject = PlcBuildIdentity.MatchesProject(solutionPath, configurationProjectPath, project);
        if ((!isConfigurationProject && !PlcBuildIdentity.MatchesProject(solutionPath, projectPath, project)) || projectConfiguration != configuration ||
            observedPlatform != platform || (solutionConfiguration != configuration && solutionConfiguration != configuration + "|" + platform))
        {
            problems.Add($"Uncorrelated PLC project event: {project} | {projectConfiguration} | {observedPlatform} | {solutionConfiguration}.");
            return false;
        }
        return !isConfigurationProject;
    }

    public void Begin(string project, string projectConfiguration, string observedPlatform, string solutionConfiguration,
        bool currentSolutionBuild)
    {
        if (!IsIntended(project, projectConfiguration, observedPlatform, solutionConfiguration, currentSolutionBuild)) return;
        if (Started || Completed) { problems.Add("Duplicate/out-of-order PLC project build-start event."); return; }
        Started = true;
    }

    public void Done(string project, string projectConfiguration, string observedPlatform, string solutionConfiguration,
        bool success, bool currentSolutionBuild)
    {
        if (!IsIntended(project, projectConfiguration, observedPlatform, solutionConfiguration, currentSolutionBuild)) return;
        if (!Started || Completed) { problems.Add("Duplicate/out-of-order PLC project build-completion event."); return; }
        Completed = true;
        Succeeded = success;
    }
}

public static class OneBasedErrorCollector
{
    // DTE is COM: Item(1)..Item(Count), inclusive. Keep failures; never swallow them into an empty list.
    public static DiagnosticCollection Read(Func<int> count, Func<int, CapturedDiagnostic> item, int limit = 10000)
    {
        ArgumentNullException.ThrowIfNull(count);
        ArgumentNullException.ThrowIfNull(item);
        if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit));
        var items = new List<CapturedDiagnostic>();
        var problems = new List<string>();
        int? first = null, last = null;
        try
        {
            first = count();
            if (first < 0) problems.Add("Negative ErrorItems.Count.");
            else if (first > limit) problems.Add($"Diagnostic limit {limit} is smaller than ErrorItems.Count {first}.");
            for (var index = 1; index <= Math.Min(first.Value, limit); index++)
            {
                try
                {
                    var captured = item(index);
                    if (captured is null) throw new InvalidOperationException("Null diagnostic.");
                    items.Add(captured);
                    if (captured.Severity == DiagnosticSeverity.Unknown) problems.Add($"Unknown severity at index {index}.");
                    if (captured.Correlation != DiagnosticCorrelation.CurrentBuild) problems.Add($"Uncorrelated diagnostic at index {index}: {captured.Correlation}.");
                    if (string.IsNullOrWhiteSpace(captured.Message)) problems.Add($"Empty message at index {index}.");
                }
                catch (Exception ex) { problems.Add($"Diagnostic {index} could not be captured: {ex.GetType().Name}: {ex.Message}"); }
            }
            last = count();
            if (last != first) problems.Add($"Diagnostic collection changed during capture: {first} -> {last}.");
        }
        catch (Exception ex) { problems.Add($"Diagnostic enumeration failed: {ex.GetType().Name}: {ex.Message}"); }
        return new(first, last, items, problems);
    }
}
