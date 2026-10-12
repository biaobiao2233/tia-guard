#if TWINCAT_DTE
using System.Diagnostics;
using System.Runtime.InteropServices;
using EnvDTE;
using EnvDTE80;

namespace TiaGuard.TwinCat.Validation;

internal static class NativeDteBuild
{
    // Adapted API calls from eponce BuildCommand / Edge XaeActions, with strict fresh-build correlation.
    public static CompilationEvidence Build(object nativeDte, string solutionPath, string stageRoot,
        string configurationName, string plcName, string buildConfiguration, string targetPlatform,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        var dte = nativeDte as DTE2 ?? throw new InvalidOperationException("The owned DTE does not expose DTE2.");
        var build = dte.Solution.SolutionBuild;
        var observations = new BuildObservation();
        var problems = new List<string>();
        var invoked = false;
        var started = false;
        var completed = false;
        var timedOut = false;
        var foreignEvent = false;
        BuildEvents? events = null;
        OutputWindowPane? buildPane = null;
        var baselineEmpty = false;
        var outputFresh = false;
        var output = "";
        int? failedProjectCount = null;
        DiagnosticCollection? diagnostics = null;
        void Begin(vsBuildScope scope, vsBuildAction action)
        {
            if (!invoked || scope != vsBuildScope.vsBuildScopeSolution || action != vsBuildAction.vsBuildActionBuild) foreignEvent = true;
            else started = true;
        }
        void Done(vsBuildScope scope, vsBuildAction action)
        {
            if (!invoked || !started || scope != vsBuildScope.vsBuildScopeSolution || action != vsBuildAction.vsBuildActionBuild) foreignEvent = true;
            else completed = true;
        }
        bool IsSolution() => string.Equals(Path.GetFullPath(dte.Solution.FullName), Path.GetFullPath(solutionPath), StringComparison.OrdinalIgnoreCase);
        bool IsConfiguration() => string.Equals(build.ActiveConfiguration.Name, buildConfiguration, StringComparison.Ordinal);
        bool IsPlatform() => string.Equals(((SolutionConfiguration2)build.ActiveConfiguration).PlatformName, targetPlatform, StringComparison.Ordinal);
        try
        {
            if (!IsSolution() || !IsConfiguration() || !IsPlatform()) throw new InvalidOperationException("Owned solution/configuration/platform differs from the frozen request.");
            if (build.BuildState == vsBuildState.vsBuildStateInProgress) throw new InvalidOperationException("The owned solution is already building.");
            var list = dte.ToolWindows.ErrorList ?? throw new InvalidOperationException("DTE2 Error List is unavailable.");
            list.ShowErrors = list.ShowWarnings = list.ShowMessages = true;
            if (list.ErrorItems.Count != 0)
            {
                // Only this task's fresh owned IDE is reachable here; do not clear the user's IDE.
                dte.ExecuteCommand("OtherContextMenus.ErrorList.Clear", "");
            }
            baselineEmpty = list.ErrorItems.Count == 0;
            if (!baselineEmpty) throw new InvalidOperationException("Could not establish an empty fresh Error List baseline.");
            buildPane = FindBuildPane(dte);
            if (buildPane != null) { buildPane.Clear(); outputFresh = string.IsNullOrWhiteSpace(ReadOutput(buildPane)); }
            else outputFresh = true; // No preexisting pane; a pane created by this build has no old output.
            if (!outputFresh) throw new InvalidOperationException("Could not clear the owned Build Output pane.");
            events = dte.Events.BuildEvents;
            events.OnBuildBegin += Begin;
            events.OnBuildDone += Done;
            invoked = true;
            var clock = Stopwatch.StartNew();
            build.Build(false);
            while (!completed && clock.Elapsed < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PumpMessages();
                if (build.BuildState == vsBuildState.vsBuildStateInProgress) started = true;
                System.Threading.Thread.Sleep(30);
            }
            PumpMessages();
            timedOut = !completed;
            if (foreignEvent) problems.Add("Unexpected/uncorrelated build event.");
            if (completed && build.BuildState != vsBuildState.vsBuildStateDone) problems.Add("BuildDone event contradicts SolutionBuild.BuildState.");
            failedProjectCount = completed ? build.LastBuildInfo : null;
            buildPane ??= FindBuildPane(dte);
            if (buildPane != null) output = ReadOutput(buildPane);
            diagnostics = OneBasedErrorCollector.Read(() => list.ErrorItems.Count, index =>
            {
                var item = list.ErrorItems.Item(index);
                var severity = item.ErrorLevel switch
                {
                    vsBuildErrorLevel.vsBuildErrorLevelHigh => DiagnosticSeverity.Error,
                    vsBuildErrorLevel.vsBuildErrorLevelMedium => DiagnosticSeverity.Warning,
                    vsBuildErrorLevel.vsBuildErrorLevelLow => DiagnosticSeverity.Information,
                    _ => DiagnosticSeverity.Unknown
                };
                var message = item.Description;
                var project = OptionalText(() => item.Project);
                var file = OptionalText(() => item.FileName);
                var recognizedProject = project == configurationName || project == plcName ||
                    string.Equals(project, configurationName + ".tsproj", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(project, plcName + ".plcproj", StringComparison.OrdinalIgnoreCase);
                var ownedFile = file != null && IsWithin(stageRoot, file);
                var outputCorrelation = !string.IsNullOrEmpty(message) && output.Contains(message, StringComparison.Ordinal);
                var correlation = !baselineEmpty || !completed ? DiagnosticCorrelation.Unknown :
                    recognizedProject || ownedFile || (project == null && outputCorrelation) ? DiagnosticCorrelation.CurrentBuild :
                    project != null ? DiagnosticCorrelation.Foreign : DiagnosticCorrelation.Unknown;
                return new CapturedDiagnostic(severity, message, project, file, OptionalLocation(() => item.Line), OptionalLocation(() => item.Column), null, correlation);
            });
            observations = new()
            {
                BuildInvoked = invoked, BuildStarted = started, BuildCompleted = completed, TimedOut = timedOut,
                IntendedSolution = IsSolution(), IntendedConfiguration = IsConfiguration(), IntendedPlatform = IsPlatform(),
                FreshDiagnosticBaseline = baselineEmpty, FailedProjectCount = failedProjectCount,
                ErrorList = diagnostics, BuildOutputComplete = outputFresh && buildPane != null && completed,
                BuildOutput = output, CollectorProblems = problems
            };
        }
        catch (Exception ex)
        {
            problems.Add($"Native DTE build/capture failed: {ex.GetType().Name}: {ex.Message}");
            observations = new()
            {
                BuildInvoked = invoked, BuildStarted = started, BuildCompleted = completed, TimedOut = timedOut,
                FreshDiagnosticBaseline = baselineEmpty, FailedProjectCount = failedProjectCount,
                ErrorList = diagnostics, BuildOutput = output, CollectorProblems = problems
            };
        }
        finally
        {
            if (events != null)
            {
                try { events.OnBuildBegin -= Begin; events.OnBuildDone -= Done; }
                catch (Exception ex) { problems.Add("Build event cleanup failed: " + ex.Message); }
            }
        }
        return CompilationGate.Evaluate(observations with { CollectorProblems = problems });
    }

    private static OutputWindowPane? FindBuildPane(DTE2 dte)
    {
        foreach (OutputWindowPane pane in dte.ToolWindows.OutputWindow.OutputWindowPanes)
            if (string.Equals(pane.Guid, "{1BD8A850-02D1-11D1-BEE7-00A0C913D1F8}", StringComparison.OrdinalIgnoreCase) || pane.Name == "Build") return pane;
        return null;
    }
    private static string ReadOutput(OutputWindowPane pane) => pane.TextDocument.StartPoint.CreateEditPoint().GetText(pane.TextDocument.EndPoint);
    private static string? OptionalText(Func<string?> read)
    {
        try { var value = read(); return string.IsNullOrWhiteSpace(value) ? null : value; }
        catch (Exception) { return null; } // DTE documents that global errors need not have file/project locations.
    }
    private static int? OptionalLocation(Func<int> read)
    {
        try { var value = read(); return value > 0 ? value : null; }
        catch (Exception) { return null; }
    }
    private static bool IsWithin(string root, string path)
    {
        try { return Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase); }
        catch (ArgumentException) { return false; }
    }
    private static void PumpMessages()
    {
        while (PeekMessage(out var message, IntPtr.Zero, 0, 0, 1)) { TranslateMessage(ref message); DispatchMessage(ref message); }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage { public IntPtr Window; public uint Message; public UIntPtr WParam; public IntPtr LParam; public uint Time; public int X; public int Y; public uint Private; }
    [DllImport("user32.dll")] private static extern bool PeekMessage(out NativeMessage message, IntPtr window, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref NativeMessage message);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref NativeMessage message);
}
#endif
