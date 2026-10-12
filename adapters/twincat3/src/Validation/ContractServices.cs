using System.Reflection;
using C = TwinCat3.Contracts;

namespace TiaGuard.TwinCat.Validation;

public sealed class NativeEngineeringCompiler : C.IEngineeringCompiler
{
    private readonly TimeSpan timeout;
    public NativeEngineeringCompiler(TimeSpan? timeout = null) => this.timeout = timeout ?? TimeSpan.FromMinutes(5);

    public Task<C.CompilationReport> CompileAsync(C.FrozenGenerationInput input, C.NativeProjectHandle project,
        C.IOwnedEngineeringSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(project); ArgumentNullException.ThrowIfNull(session);
        if (input.InputHash != project.InputHash) return Task.FromResult(Unavailable("Project handle is not bound to the frozen input."));
#if TWINCAT_DTE
        return session.InvokeAsync(context =>
        {
            try
            {
                ValidateBinding(context, project);
                LoadInterop(context);
                var manifest = input.Manifest;
                var evidence = NativeDteBuild.Build(context.Dte!, project.SolutionPath, project.TwinCatProjectPath, project.StageRoot,
                    manifest.ConfigurationName, manifest.PlcProjectName,
                    manifest.Environment.BuildConfiguration, manifest.Environment.TargetPlatform, timeout, cancellationToken);
                return new C.CompilationReport
                {
                    State = (C.CompilationState)(int)evidence.Verdict,
                    BuildStarted = evidence.BuildStarted, BuildCompleted = evidence.BuildCompleted,
                    FailedProjectCount = evidence.FailedProjectCount, ErrorCount = evidence.ErrorCount, WarningCount = evidence.WarningCount,
                    DiagnosticsComplete = evidence.DiagnosticsComplete, OutputCaptureComplete = evidence.OutputCaptureComplete,
                    Diagnostics = evidence.Diagnostics.Select(x => new C.CompilationDiagnostic
                    {
                        Severity = x.Severity switch { DiagnosticSeverity.Error => C.DiagnosticSeverity.Error,
                            DiagnosticSeverity.Warning => C.DiagnosticSeverity.Warning, DiagnosticSeverity.Information => C.DiagnosticSeverity.Message,
                            _ => C.DiagnosticSeverity.Unknown },
                        Code = x.Code, Message = x.Message, File = x.File, Line = x.Line, Column = x.Column,
                        Project = x.Project, Origin = x.Source, Correlated = x.Correlation == DiagnosticCorrelation.CurrentBuild
                    }).ToArray(),
                    BuildOutput = evidence.BuildOutput ?? "", Configuration = manifest.Environment.BuildConfiguration,
                    Platform = manifest.Environment.TargetPlatform, ProjectIdentity = project.ProjectIdentity,
                    InputHash = input.InputHash, Failures = evidence.Problems
                };
            }
            catch (Exception ex) { return Unavailable($"Compiler unavailable/failed: {ex.GetType().Name}: {ex.Message}"); }
        }, cancellationToken);
#else
        return Task.FromResult(Unavailable("This worker was built without the machine-local DTE interop. No compilation was performed."));
#endif
    }
    private static C.CompilationReport Unavailable(string reason) => new() { Failures = new[] { reason } };

    internal static void ValidateBinding(C.NativeEngineeringContext context, C.NativeProjectHandle project)
    {
        if (context.Dte == null || context.TwinCatProject == null || context.SystemManager == null ||
            context.BoundProjectIdentity != project.ProjectIdentity ||
            !string.Equals(context.BoundProjectPath, project.TwinCatProjectPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Native session is not bound to this owned project.");
        foreach (var path in new[] { project.SolutionPath, project.TwinCatProjectPath })
            if (!Path.GetFullPath(path).StartsWith(Path.GetFullPath(project.StageRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Project path is outside the owned stage.");
    }
    internal static void LoadInterop(C.NativeEngineeringContext context)
    {
        foreach (var path in context.InteropAssemblyPaths.Values.Distinct(StringComparer.OrdinalIgnoreCase))
            if (File.Exists(path) && path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) Assembly.LoadFrom(path);
    }
}

public sealed class SourceBoundProjectComparer : C.IProjectComparer
{
    public C.VerificationReport Compare(C.FrozenGenerationInput input, C.ProjectCapture capture)
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(capture);
        try
        {
            var manifest = input.Manifest;
            if (string.IsNullOrWhiteSpace(manifest.Environment.RequestedBuild))
                return new C.VerificationReport { Unknowns = new[] { "The frozen input does not pin the selected TwinCAT tool build." } };
            var expected = new ProjectFacts(manifest.SolutionName, manifest.ConfigurationName, manifest.PlcProjectName,
                manifest.Environment.RequestedBuild == null ? "" : TwinCatBuildIdentity.Normalize(manifest.Environment.RequestedBuild), manifest.Environment.TargetPlatform,
                manifest.Artifacts.Select(x => new SourceObject(x.Name, x.Kind.ToString(), input.Files[x.DeclarationFile].Text,
                    x.ImplementationFile == null ? "" : input.Files[x.ImplementationFile].Text)).ToArray(),
                manifest.Tasks.Select(x => new TaskFact(x.Name, x.CycleTimeUs, x.Priority, "TIRT^" + x.Name, x.Programs)).ToArray(),
                manifest.Libraries.Select(x => new LibraryFact(x.Name, x.Vendor, x.Version, x.Version)).ToArray());
            var actual = new ProjectFacts(capture.SolutionName ?? "", capture.ConfigurationName ?? "", capture.PlcProjectName ?? "",
                capture.ToolBuild == null ? "" : TwinCatBuildIdentity.Normalize(capture.ToolBuild), capture.Platform ?? "",
                capture.Artifacts.Select(x => new SourceObject(x.Name, x.Kind.ToString(), x.DeclarationText, x.ImplementationText ?? "")).ToArray(),
                capture.Tasks.Select(x => new TaskFact(x.Name, x.CycleTimeUs, x.Priority, x.LinkedSystemTask, x.Programs)).ToArray(),
                capture.Libraries.Select(x => new LibraryFact(x.Name, x.Vendor, x.Version, x.ResolvedVersion)).ToArray());
            var problems = capture.Failures.ToList();
            if (string.IsNullOrWhiteSpace(capture.ProjectIdentity)) problems.Add("Captured project identity is missing.");
            if (capture.InputHash != input.InputHash) problems.Add("Capture is not correlated to the frozen input.");
            if (capture.Artifacts.Any(x => x.Opaque)) problems.Add("Capture contains opaque artifacts.");
            if (capture.Artifacts.Any(x => x.NativeItemType != ExpectedNativeType(x.Kind))) problems.Add("Native artifact kind is missing or inconsistent.");
            if (capture.Libraries.Any(x => x.IsPlaceholder)) problems.Add("Placeholder library resolution is outside this supported slice.");
            var result = IndependentComparison.Compare(expected, new(actual, capture.Saved, capture.Closed, capture.Reopened,
                capture.ObjectsComplete, capture.TasksComplete, capture.LibrariesComplete, capture.Unsupported, problems));
            return new C.VerificationReport
            {
                Verdict = (C.VerificationVerdict)(int)result.Verdict, Complete = result.Complete,
                Differences = result.Differences, Unknowns = result.Unknowns
            };
        }
        catch (Exception ex) { return new() { Unknowns = new[] { $"Independent expected/captured projection failed: {ex.GetType().Name}: {ex.Message}" } }; }
    }
    public static int ExpectedNativeType(C.ArtifactKind kind) => kind switch
    {
        C.ArtifactKind.Program => 602, C.ArtifactKind.Function => 603, C.ArtifactKind.FunctionBlock => 604,
        C.ArtifactKind.GlobalVariableList => 615, C.ArtifactKind.Struct => 606, C.ArtifactKind.Enum => 605,
        _ => throw new NotSupportedException("Unsupported artifact kind.")
    };
}

public sealed class NativeProjectCaptureService : C.IProjectCaptureService
{
    public Task<C.ProjectCapture> CaptureAsync(C.FrozenGenerationInput input, C.NativeProjectHandle project,
        C.IOwnedEngineeringSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(project); ArgumentNullException.ThrowIfNull(session);
        if (input.InputHash != project.InputHash) return Task.FromResult(Unavailable("Project handle is not bound to the frozen input."));
#if TWINCAT_DTE && TWINCAT_COM
        return session.InvokeAsync(context =>
        {
            try
            {
                NativeEngineeringCompiler.ValidateBinding(context, project);
                NativeEngineeringCompiler.LoadInterop(context);
                return NativeReadback.Capture(input, project, context, cancellationToken);
            }
            catch (Exception ex) { return Unavailable($"Native capture unavailable/failed: {ex.GetType().Name}: {ex.Message}"); }
        }, cancellationToken);
#else
        return Task.FromResult(Unavailable("This worker was built without both machine-local DTE and TwinCAT interop. No readback was performed."));
#endif
    }
    private static C.ProjectCapture Unavailable(string reason) => new() { Failures = new[] { reason } };
}
