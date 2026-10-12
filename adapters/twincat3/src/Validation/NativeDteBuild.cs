#if TWINCAT_DTE
using System.Diagnostics;
using System.Runtime.InteropServices;
using EnvDTE;
using EnvDTE80;

namespace TiaGuard.TwinCat.Validation;

internal static class NativeDteBuild
{
    // Adapted API calls from eponce BuildCommand / Edge XaeActions, with strict fresh-build correlation.
    public static CompilationEvidence Build(object nativeDte, string solutionPath, string twinCatProjectPath, string stageRoot,
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
        PlcProjectBuildTracker? plcEvents = null;
        PlcContextEvidence? plcContextBefore = null;
        PlcContextEvidence? plcContextAfter = null;
        BuildEvents? events = null;
        OutputWindowPane? buildPane = null;
        var baselineEmpty = false;
        var outputFresh = false;
        var output = "";
        int? failedProjectCount = null;
        DiagnosticCollection? diagnostics = null;
        var collectorPhase = "identity";
        var nativeTrace = new List<object>();
        void Begin(vsBuildScope scope, vsBuildAction action)
        {
            nativeTrace.Add(new { kind = "solution-begin", scope = scope.ToString(), action = action.ToString() });
            if (!invoked || started || completed || scope != vsBuildScope.vsBuildScopeSolution || action != vsBuildAction.vsBuildActionBuild) foreignEvent = true;
            else started = true;
        }
        void Done(vsBuildScope scope, vsBuildAction action)
        {
            nativeTrace.Add(new { kind = "solution-done", scope = scope.ToString(), action = action.ToString() });
            if (!invoked || !started || completed || scope != vsBuildScope.vsBuildScopeSolution || action != vsBuildAction.vsBuildActionBuild) foreignEvent = true;
            else completed = true;
        }
        void ProjectBegin(string project, string projectConfig, string platform, string solutionConfig)
        {
            nativeTrace.Add(new { kind = "project-begin", project, projectConfig, platform, solutionConfig });
            plcEvents?.Begin(project, projectConfig, platform, solutionConfig, invoked && started && !completed);
        }
        void ProjectDone(string project, string projectConfig, string platform, string solutionConfig, bool success)
        {
            nativeTrace.Add(new { kind = "project-done", project, projectConfig, platform, solutionConfig, success });
            plcEvents?.Done(project, projectConfig, platform, solutionConfig, success, invoked && started && !completed);
        }
        bool IsSolution() => string.Equals(Path.GetFullPath(dte.Solution.FullName), Path.GetFullPath(solutionPath), StringComparison.OrdinalIgnoreCase);
        bool IsConfiguration() => string.Equals(build.ActiveConfiguration.Name, buildConfiguration, StringComparison.Ordinal);
        bool IsPlatform() => string.Equals(((SolutionConfiguration2)build.ActiveConfiguration).PlatformName, targetPlatform, StringComparison.Ordinal);
        try
        {
            if (!IsSolution() || !IsConfiguration() || !IsPlatform()) throw new InvalidOperationException("Owned solution/configuration/platform differs from the frozen request.");
            if (build.BuildState == vsBuildState.vsBuildStateInProgress) throw new InvalidOperationException("The owned solution is already building.");
            var plcProjects = Directory.EnumerateFiles(stageRoot, "*.plcproj", SearchOption.AllDirectories).ToArray();
            if (plcProjects.Length != 1 || !string.Equals(Path.GetFileName(plcProjects[0]), plcName + ".plcproj", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The owned stage does not contain exactly one intended PLC project.");
            var plcProjectPath = Path.GetFullPath(plcProjects[0]);
            collectorPhase = "plc-context";
            PlcContextEvidence ReadPlcContext()
            {
                var contexts = new List<PlcSolutionContext>();
                foreach (SolutionContext context in build.ActiveConfiguration.SolutionContexts)
                    contexts.Add(new(context.ProjectName, context.ConfigurationName, context.PlatformName, context.ShouldBuild));
                nativeTrace.Add(new { kind = "solution-contexts", contexts });
                return PlcBuildIdentity.CheckContexts(solutionPath, plcProjectPath, buildConfiguration, targetPlatform, contexts);
            }
            plcContextBefore = ReadPlcContext();
            problems.AddRange(plcContextBefore.Problems);
            if (!plcContextBefore.IntendedProject || !plcContextBefore.BuildEnabled)
                throw new InvalidOperationException("The intended PLC project context is missing, different, or disabled for this build.");
            plcEvents = new(solutionPath, plcProjectPath, buildConfiguration, targetPlatform, twinCatProjectPath);
            collectorPhase = "error-list-access";
            var list = dte.ToolWindows.ErrorList ?? throw new InvalidOperationException("DTE2 Error List is unavailable.");
            collectorPhase = "error-list-filters";
            list.ShowErrors = list.ShowWarnings = list.ShowMessages = true;
            collectorPhase = "error-list-baseline";
            if (list.ErrorItems.Count != 0)
            {
                // Only this task's fresh owned IDE is reachable here; do not clear the user's IDE.
                collectorPhase = "error-list-clear-command";
                dte.ExecuteCommand("OtherContextMenus.ErrorList.Clear", "");
            }
            baselineEmpty = list.ErrorItems.Count == 0;
            if (!baselineEmpty) throw new InvalidOperationException("Could not establish an empty fresh Error List baseline.");
            collectorPhase = "output-window-initialization";
            dte.ExecuteCommand("View.Output", "");
            dte.MainWindow.Visible = false;
            collectorPhase = "build-output-baseline";
            buildPane = FindBuildPane(dte, phase => collectorPhase = phase, entry => nativeTrace.Add(entry));
            if (buildPane != null) { buildPane.Clear(); outputFresh = string.IsNullOrWhiteSpace(ReadOutput(buildPane)); }
            else outputFresh = true; // No preexisting pane; a pane created by this build has no old output.
            if (!outputFresh) throw new InvalidOperationException("Could not clear the owned Build Output pane.");
            collectorPhase = "build-event-subscription";
            events = dte.Events.BuildEvents;
            events.OnBuildBegin += Begin;
            events.OnBuildDone += Done;
            events.OnBuildProjConfigBegin += ProjectBegin;
            events.OnBuildProjConfigDone += ProjectDone;
            invoked = true;
            collectorPhase = "build-invoke";
            var clock = Stopwatch.StartNew();
            build.Build(false);
            while (!completed && clock.Elapsed < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PumpMessages();
                System.Threading.Thread.Sleep(30);
            }
            PumpMessages();
            collectorPhase = "build-completion-capture";
            timedOut = !completed;
            plcContextAfter = ReadPlcContext();
            problems.AddRange(plcContextAfter.Problems);
            if (foreignEvent) problems.Add("Unexpected/uncorrelated build event.");
            if (completed && build.BuildState != vsBuildState.vsBuildStateDone) problems.Add("BuildDone event contradicts SolutionBuild.BuildState.");
            failedProjectCount = completed ? build.LastBuildInfo : null;
            buildPane ??= FindBuildPane(dte, phase => collectorPhase = phase, entry => nativeTrace.Add(entry));
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
                IntendedPlcProject = plcContextBefore.IntendedProject && plcContextAfter.IntendedProject,
                PlcProjectBuildEnabled = plcContextBefore.BuildEnabled && plcContextAfter.BuildEnabled,
                PlcProjectBuildStarted = plcEvents.Started, PlcProjectBuildCompleted = plcEvents.Completed,
                PlcProjectBuildSucceeded = plcEvents.Succeeded, PlcProjectEventsCorrelated = plcEvents.Correlated,
                FreshDiagnosticBaseline = baselineEmpty, FailedProjectCount = failedProjectCount,
                ErrorList = diagnostics, BuildOutputComplete = outputFresh && buildPane != null && completed,
                BuildOutput = output, CollectorProblems = problems
            };
        }
        catch (Exception ex)
        {
            problems.Add($"Native DTE build/capture failed at {collectorPhase}: {ex.GetType().Name}: {ex.Message}");
            observations = new()
            {
                BuildInvoked = invoked, BuildStarted = started, BuildCompleted = completed, TimedOut = timedOut,
                IntendedPlcProject = plcContextBefore?.IntendedProject == true && plcContextAfter?.IntendedProject == true,
                PlcProjectBuildEnabled = plcContextBefore?.BuildEnabled == true && plcContextAfter?.BuildEnabled == true,
                PlcProjectBuildStarted = plcEvents?.Started == true, PlcProjectBuildCompleted = plcEvents?.Completed == true,
                PlcProjectBuildSucceeded = plcEvents?.Succeeded, PlcProjectEventsCorrelated = plcEvents?.Correlated == true,
                FreshDiagnosticBaseline = baselineEmpty, FailedProjectCount = failedProjectCount,
                ErrorList = diagnostics, BuildOutput = output, CollectorProblems = problems
            };
        }
        finally
        {
            if (events != null)
            {
                void Detach(Action detach)
                {
                    try { detach(); }
                    catch (Exception ex) { problems.Add("Build event cleanup failed: " + ex.Message); }
                }
                Detach(() => events.OnBuildBegin -= Begin);
                Detach(() => events.OnBuildDone -= Done);
                Detach(() => events.OnBuildProjConfigBegin -= ProjectBegin);
                Detach(() => events.OnBuildProjConfigDone -= ProjectDone);
            }
            if (plcEvents != null) problems.AddRange(plcEvents.Problems);
            try
            {
                var rawDirectory = Path.Combine(stageRoot, "_build-evidence");
                Directory.CreateDirectory(rawDirectory);
                File.WriteAllText(Path.Combine(rawDirectory, Guid.NewGuid().ToString("N") + ".json"),
                    System.Text.Json.JsonSerializer.Serialize(new { collectorPhase, nativeTrace, problems }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { problems.Add("Build trace could not be retained: " + ex.Message); }
        }
        return CompilationGate.Evaluate(observations with { CollectorProblems = problems });
    }

    private static OutputWindowPane? FindBuildPane(DTE2 dte, Action<string>? phase = null, Action<object>? trace = null)
    {
        phase?.Invoke("output-window-access");
        var output = dte.ToolWindows.OutputWindow;
        phase?.Invoke("output-window-panes");
        var panes = output.OutputWindowPanes;
        foreach (OutputWindowPane pane in panes)
        {
            phase?.Invoke("output-pane-identity");
            string name = pane.Name;
            trace?.Invoke(new { kind = "output-pane", name, localeId = dte.LocaleID });
            if (name == "Build" || (dte.LocaleID == 2052 && name == "生成")) return pane;
            try { if (string.Equals(pane.Guid, "{1BD8A850-02D1-11D1-BEE7-00A0C913D1F8}", StringComparison.OrdinalIgnoreCase)) return pane; }
            catch (COMException ex) { trace?.Invoke(new { kind = "output-pane-guid-unavailable", name, hresult = ex.HResult }); }
        }
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
