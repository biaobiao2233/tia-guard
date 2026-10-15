using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TwinCat3.Contracts;
using TwinCat3.Engineering;
using TwinCat3.EnvironmentHost;
using TiaGuard.TwinCat.Validation;
using TwinCat3.Runtime;

namespace TwinCat3.Worker;

public sealed class WorkerBackend : IAsyncDisposable
{
    public const string ProviderId = "twincat3-owned-dte";
    private sealed record PreviewState(JsonObject Request, FrozenGenerationInput Input, GenerationPreview? Generation,
        ProjectSnapshot? Project, string RequestHash, DateTimeOffset Expires, RuntimeSlotSnapshot? RuntimeSlot = null);
    private sealed record GrantState(string PreviewId, DateTimeOffset Expires);
    private sealed record ExecutionState(FrozenGenerationInput Input, string SolutionPath, string Root, bool Published);
    private readonly WorkerConfiguration configuration;
    private readonly EngineeringAllocation allocation;
    private readonly LazyOwnedSession session;
    private readonly EngineeringPipeline pipeline;
    private readonly ExecutionGrantStore grants;
    private readonly IEngineeringCompiler compiler;
    private readonly IProjectCaptureService capture;
    private readonly IProjectComparer comparer;
    private readonly TimeProvider time;
    private readonly bool nativeBinding;
    private readonly Dictionary<string, PreviewState> previews = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GrantState> tokens = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ExecutionState> executions = new(StringComparer.Ordinal);
    private readonly HashSet<string> secrets = new(StringComparer.Ordinal);
    private sealed record PreparedSimulation(NativeProjectHandle Project, CompilationReport Compilation,
        VerificationReport Verification, string Root, string ProbeSymbol);
    private sealed record SimulationState(OwnedUsermodeRuntime Runtime, FrozenGenerationInput Input, string ProjectIdentity,
        PreparedSimulation? Prepared = null);
    private readonly Dictionary<string, SimulationState> simulations = new(StringComparer.Ordinal);
    private bool disposed;
    private static readonly JsonSerializerOptions Json = new(ContractJson.Options) { WriteIndented = false };

    public WorkerBackend(WorkerConfiguration configuration, IEngineeringEnvironment environment,
        EnvironmentReport report, bool nativeBinding, TimeProvider? time = null)
    {
        this.configuration = configuration; this.nativeBinding = nativeBinding; this.time = time ?? TimeProvider.System;
        allocation = new(configuration.WorkspaceRoot, configuration.ScratchRoot);
        session = new(environment, report, allocation.WorkspaceRoot);
        grants = new(configuration.OfflineWrites, this.time);
        var authorizations = new StageAuthorizationRegistry();
        compiler = new NativeEngineeringCompiler(); capture = new NativeProjectCaptureService(); comparer = new SourceBoundProjectComparer();
        pipeline = new(allocation, authorizations, grants, new NativeEngineeringCreator(allocation, authorizations), compiler, capture, comparer);
        secrets.Add(configuration.Identity);
    }

    public static async Task<WorkerBackend> CreateAsync(WorkerConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var environment = new WindowsEngineeringEnvironment(configuration.InteropPath);
        var report = await environment.InspectAsync(cancellationToken).ConfigureAwait(false);
#if TWINCAT_WORKER_NATIVE
        bool native = report.IsWindows && report.ProcessArchitecture == "X64" &&
            report.InteropAssemblyPaths.ContainsKey("TCatSysManagerLib") && report.InteropAssemblyPaths.ContainsKey("EnvDTE80") &&
            report.ProjectTemplatePath is not null && report.ProjectTemplateHash?.Length == 64;
#else
        bool native = false;
#endif
        return new(configuration, environment, report, native);
    }

    public JsonObject Hello() => new()
    {
        ["protocolVersion"] = "1", ["identity"] = configuration.Identity, ["providerId"] = ProviderId,
        ["sessionId"] = session.SessionId, ["capabilities"] = Capabilities()
    };
    private JsonObject Capabilities()
    {
        var result = new JsonObject();
        foreach (string action in new[] { "doctor", "inspect", "preview", "authorize", "build-generation", "compile", "verify", "dispose",
            "start-simulation", "prepare-simulation", "resume-simulation", "simulation-status", "simulation-state", "simulation-read", "simulation-write", "stop-simulation" })
        {
            bool readOnly = action is "doctor" or "inspect" or "preview";
            bool available = action == "dispose" || readOnly || nativeBinding && configuration.OfflineWrites;
            if (IsSimulationAction(action)) available = configuration.RuntimeWrites && configuration.OfflineWrites && nativeBinding &&
                NativeSimulationDeployment.NativeBuilt && OwnedUsermodeRuntime.Installed(session.Environment.InstallDirectory);
            result[action] = new JsonObject
            {
                ["available"] = available, ["state"] = available ? "untested" : "unavailable",
                ["reason"] = readOnly ? "Read-only detection/static inspection/preview; no COM activation. Inspect is partial static XML evidence." :
                    !nativeBinding ? "Worker lacks the concrete machine-local native compiler/capture binding." :
                    !configuration.OfflineWrites ? "Trusted worker offline-write mode is disabled." :
                    "Concrete native A/B/C binding exists; no functional qualification is inferred from registration."
            };
        }
        return result;
    }

    public async Task<JsonObject> DispatchAsync(string action, JsonElement payload, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        try
        {
            ValidatePayload(action, payload);
            if (Text(payload, "providerId") != ProviderId || Text(payload, "sessionId") != session.SessionId)
                throw new InvalidOperationException("Session binding is invalid.");
            if (IsSimulationAction(action)) RequireRuntimeMode();
            if (simulations.Values.Any(s => s.Prepared is not null) && action is "build-generation" or "compile" or "verify")
                throw new InvalidOperationException("Complete or stop the prepared simulation before replacing its owned IDE session.");
            if (action is "authorize" or "build-generation" or "compile" or "verify")
            {
                if (!configuration.OfflineWrites) throw new InvalidOperationException("Offline writes are disabled.");
                if (!nativeBinding) return Failure(action, "unavailable", "Concrete native binding is unavailable.");
            }
            JsonObject result = action switch
            {
                "doctor" => Envelope(action, new { Environment = session.Environment, Capabilities = Capabilities(), NativeCreated = session.NativeCreated }),
                "inspect" => Inspect(payload),
                "preview" => Preview(payload),
                "authorize" => Authorize(payload),
                "build-generation" => await BuildAsync(payload, cancellationToken).ConfigureAwait(false),
                "compile" => await CompileAsync(payload, cancellationToken).ConfigureAwait(false),
                "verify" => await VerifyAsync(payload, cancellationToken).ConfigureAwait(false),
                "start-simulation" => await StartSimulationAsync(payload, cancellationToken).ConfigureAwait(false),
                "prepare-simulation" => await StartSimulationAsync(payload, cancellationToken, prepareOnly: true).ConfigureAwait(false),
                "resume-simulation" => await ResumeSimulationAsync(payload, cancellationToken).ConfigureAwait(false),
                "simulation-status" or "simulation-state" or "simulation-read" or "simulation-write" or "stop-simulation"
                    => await SimulationActionAsync(action, payload).ConfigureAwait(false),
                "dispose" => await DisposeReportAsync().ConfigureAwait(false),
                _ => throw new InvalidDataException("Action is outside the allowlist.")
            };
            return Redact(result);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // No raw exception, stack, request/token or COM reference crosses this boundary.
            return Failure(action, cancellationToken.IsCancellationRequested ? "cancelled-indeterminate" : "preflight",
                "The operation was refused or failed (" + exception.GetType().Name + "); request a fresh preview after correcting the input.");
        }
    }

    private JsonObject Inspect(JsonElement payload)
    {
        string path = EngineeringPaths.RequireUnder(Text(payload, "projectPath"), allocation.WorkspaceRoot, true);
        if (Path.GetExtension(path).Equals(".sln", StringComparison.OrdinalIgnoreCase))
        {
            var snapshot = ProjectSnapshot.Capture(path, allocation);
            return Envelope("inspect", new { ProjectPath = path, snapshot.TreeHash, snapshot.FileCount, snapshot.ByteCount,
                snapshot.SolutionRelativePath, snapshot.ProjectRelativePath, Evidence = "partial-static-solution-tree",
                Complete = false, NativeCapture = false, Mutates = false,
                Unsupported = new[] { "Static project-tree inspection does not qualify native objects, tasks, libraries, compilation or independent readback." } });
        }
        if (!File.Exists(path) || new FileInfo(path).Length > WorkerProtocol.MaxBytes ||
            Path.GetExtension(path).ToLowerInvariant() is not (".tsproj" or ".plcproj"))
            throw new InvalidDataException("Static inspection requires one bounded project XML file.");
        byte[] bytes = File.ReadAllBytes(path);
        using var reader = System.Xml.XmlReader.Create(new MemoryStream(bytes), new System.Xml.XmlReaderSettings
            { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = WorkerProtocol.MaxBytes });
        var document = System.Xml.Linq.XDocument.Load(reader);
        return Envelope("inspect", new { ProjectPath = path, Sha256 = Hash(bytes), ByteCount = bytes.Length,
            RootElement = document.Root?.Name.LocalName, Attributes = document.Root?.Attributes().ToDictionary(a => a.Name.LocalName, a => a.Value),
            Evidence = "partial-static-xml", Complete = false, NativeCapture = false, Mutates = false,
            Unsupported = new[] { "Native object, task, library, compilation and close/reopen evidence are not covered by static inspection." } });
    }

    private JsonObject Preview(JsonElement payload)
    {
        ExpirePreviews();
        if (previews.Count >= 32 || executions.Count >= 256) throw new InvalidOperationException("Worker capacity reached.");
        string operation = Text(payload, "operation");
        if (operation is not ("build-generation" or "compile" or "start-simulation")) throw new InvalidDataException("Unknown preview operation.");
        if (operation == "start-simulation") RequireRuntimeMode();
        else if (payload.TryGetProperty("executionId", out _) || payload.TryGetProperty("probeSymbol", out _))
            throw new InvalidDataException("Simulation-only fields are not valid for an offline preview.");
        string source = allocation.CheckInput(Text(payload, "inputDirectory"));
        string output = operation == "start-simulation"
            ? SimulationPolicy.CheckOutput(Text(payload, "outputDirectory"), allocation.ScratchRoot, source)
            : allocation.CheckOutput(Text(payload, "outputDirectory"), source);
        FrozenGenerationInput frozen = GenerationInputLoader.Load(source);
        if (configuration.Build is not null && frozen.Manifest.Environment.RequestedBuild != configuration.Build)
            throw new InvalidDataException("Manifest build differs from the trusted worker build.");
        session.Select(frozen.Manifest.Environment);
        var request = new JsonObject { ["operation"] = operation, ["inputDirectory"] = source, ["outputDirectory"] = output };
        GenerationPreview? generation = null; ProjectSnapshot? project = null;
        if (operation == "build-generation")
        {
            if (payload.TryGetProperty("projectPath", out _)) throw new InvalidDataException("Generation does not accept an existing project.");
            generation = pipeline.Preview(new() { SourceDirectory = source, OutputDirectory = output }, session);
            if (generation.InputHash != frozen.InputHash) throw new InvalidOperationException("Source drifted during preview.");
        }
        else
        {
            if (operation == "start-simulation")
            {
                if (simulations.Count != 0) throw new InvalidOperationException("Stop the owned simulation before starting another.");
                var ownedExecutionId = Text(payload, "executionId");
                if (!executions.TryGetValue(ownedExecutionId, out var owned) || !owned.Published || owned.Input.InputHash != frozen.InputHash ||
                    !Path.GetFullPath(owned.SolutionPath).Equals(Path.GetFullPath(Text(payload, "projectPath")), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Only a successfully published execution from this Worker can be simulated.");
                if (frozen.Manifest.Environment.TargetPlatform != "TwinCAT OS (x64)") throw new NotSupportedException("Only qualified OS x64 simulation is supported.");
                EngineeringPaths.RequireNewOutput(output, allocation.ScratchRoot);
                var probe = Text(payload, "probeSymbol"); SimulationPolicy.RequireSymbol(frozen, probe, false);
                request["executionId"] = ownedExecutionId; request["probeSymbol"] = probe;
            }
            project = ProjectSnapshot.Capture(Text(payload, "projectPath"), allocation);
            if (operation == "start-simulation") EngineeringPaths.RequireDistinctTrees(project.Root, output);
            else EngineeringPaths.RequireDistinctTrees(project.Root, output, allocation.ScratchRoot);
            if (project.ByteCount + previews.Values.Sum(p => p.Project?.ByteCount ?? 0) > 192 * 1024 * 1024)
                throw new InvalidOperationException("Frozen project memory limit exceeded.");
            request["projectPath"] = EngineeringPaths.RequireUnder(Text(payload, "projectPath"), allocation.WorkspaceRoot, true);
        }
        var runtimeSlot = operation == "start-simulation" ? PersistentSlot()?.Inspect() : null;
        if (runtimeSlot is not null)
            EngineeringPaths.RequireDistinctTrees(configuration.RuntimeInstanceRoot!, source, output, project!.Root);
        string requestHash = generation?.RequestHash ?? CompileRequestHash(request, frozen, project!, runtimeSlot?.Fingerprint);
        string id = generation?.PreviewId ?? Guid.NewGuid().ToString("N");
        var preview = new PreviewState(request, frozen, generation, project, requestHash, time.GetUtcNow() + TimeSpan.FromMinutes(5), runtimeSlot);
        previews.Add(id, preview);
        return Envelope("preview", new { PreviewId = id, frozen.InputHash, ExpiresAtUtc = preview.Expires,
            Request = request, Environment = session.Environment, allocation.WorkspaceRoot, allocation.ScratchRoot,
            RequestHash = requestHash, SourceInputHash = frozen.InputHash, ProjectTreeHash = project?.TreeHash,
            BackendPreview = generation, RuntimeInstance = runtimeSlot, Mutates = false });
    }

    private PreviewState RequirePreview(JsonElement payload)
    {
        if (!previews.TryGetValue(Text(payload, "previewId"), out var preview) || preview.Expires <= time.GetUtcNow())
            throw new InvalidOperationException("Preview expired or is unknown.");
        if (Text(payload, "inputHash") != preview.Input.InputHash) throw new InvalidOperationException("Source binding changed.");
        foreach (var pair in preview.Request)
            if (!payload.TryGetProperty(pair.Key, out var field) || field.ValueKind != JsonValueKind.String || field.GetString() != pair.Value!.GetValue<string>())
                throw new InvalidOperationException("The exact reviewed request is required.");
        if (preview.Project is null && payload.TryGetProperty("projectPath", out _))
            throw new InvalidOperationException("The reviewed generation request does not contain an existing project.");
        var current = GenerationInputLoader.Load(allocation.CheckInput(preview.Input.RootDirectory));
        if (current.InputHash != preview.Input.InputHash) throw new InvalidOperationException("Source input drifted.");
        if (preview.Request["operation"]!.GetValue<string>() == "start-simulation")
            SimulationPolicy.CheckOutput(preview.Request["outputDirectory"]!.GetValue<string>(), allocation.ScratchRoot, preview.Input.RootDirectory);
        else allocation.CheckOutput(preview.Request["outputDirectory"]!.GetValue<string>(), preview.Input.RootDirectory);
        if (preview.Project is not null)
        {
            var currentProject = ProjectSnapshot.Capture(preview.Request["projectPath"]!.GetValue<string>(), allocation);
            var slotFingerprint = preview.RuntimeSlot is null ? null : PersistentSlot()!.Inspect().Fingerprint;
            if (currentProject.TreeHash != preview.Project.TreeHash || CompileRequestHash(preview.Request, current, currentProject, slotFingerprint) != preview.RequestHash)
                throw new InvalidOperationException("Original project tree drifted.");
        }
        return preview;
    }

    private JsonObject Authorize(JsonElement payload)
    {
        var preview = RequirePreview(payload);
        if (tokens.Count >= 256) throw new InvalidOperationException("Grant limit reached.");
        // The one-second margin ensures the underlying B token expiry cannot exceed the review deadline.
        var lifetime = preview.Expires - time.GetUtcNow() - TimeSpan.FromSeconds(1);
        if (lifetime <= TimeSpan.Zero) throw new InvalidOperationException("Preview deadline is too close.");
        DateTimeOffset safeDeadline = time.GetUtcNow() + lifetime;
        string token = preview.Generation is not null
            ? pipeline.IssueGrant(Text(payload, "previewId"), session.SessionId, lifetime)
            : grants.Issue(session.SessionId, preview.RequestHash, lifetime);
        secrets.Add(token);
        tokens.Add(Hash(Encoding.ASCII.GetBytes(token)), new(Text(payload, "previewId"), safeDeadline));
        // The token must be returned privately to the authenticated host, before public redaction.
        var result = Envelope("authorize", new { GrantToken = token, preview.Input.InputHash, ExpiresAtUtc = safeDeadline });
        return result;
    }

    private PreviewState ConsumeHostGrant(JsonElement payload, string action)
    {
        string token = Text(payload, "grantToken");
        if (!tokens.Remove(Hash(Encoding.ASCII.GetBytes(token)), out var binding) || binding.Expires <= time.GetUtcNow() || binding.PreviewId != Text(payload, "previewId"))
            throw new InvalidOperationException("The grant is expired, foreign or consumed.");
        var preview = RequirePreview(payload);
        if (preview.Request["operation"]!.GetValue<string>() != action) throw new InvalidOperationException("Wrong grant operation.");
        previews.Remove(Text(payload, "previewId"));
        return preview;
    }

    private async Task<JsonObject> BuildAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var preview = ConsumeHostGrant(payload, "build-generation");
        await session.ResetNativeAsync().ConfigureAwait(false);
        var generation = await pipeline.BuildAsync(new() { SourceDirectory = preview.Input.RootDirectory,
            OutputDirectory = preview.Request["outputDirectory"]!.GetValue<string>() }, Text(payload, "previewId"),
            Text(payload, "grantToken"), session, cancellationToken).ConfigureAwait(false);
        string executionId = Guid.NewGuid().ToString("N");
        if (generation.Published && generation.OutputDirectory is not null && generation.SolutionPath is not null)
            executions.Add(executionId, new(preview.Input, generation.SolutionPath, generation.OutputDirectory, true));
        var result = Envelope("build-generation", generation);
        result["generationResult"] = JsonSerializer.SerializeToNode(generation, Json);
        result["executionId"] = executionId; result["sourceInputHash"] = preview.Input.InputHash;
        result["requestHash"] = preview.RequestHash;
        return result;
    }

    private async Task<JsonObject> CompileAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var preview = ConsumeHostGrant(payload, "compile");
        grants.Consume(Text(payload, "grantToken"), session.SessionId, preview.RequestHash);
        await session.ResetNativeAsync().ConfigureAwait(false);
        string stage = Path.Combine(allocation.ScratchRoot, "compile-" + Guid.NewGuid().ToString("N"));
        preview.Project!.CopyTo(stage, allocation);
        string solution = Path.Combine(stage, preview.Project.SolutionRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var handle = await BindAsync(preview.Input, solution, stage, cancellationToken).ConfigureAwait(false);
        var compilation = await compiler.CompileAsync(preview.Input, handle, session, cancellationToken).ConfigureAwait(false);
        string executionId = Guid.NewGuid().ToString("N");
        executions.Add(executionId, new(preview.Input, solution, stage, false));
        return Envelope("compile", new { Phase = "compile", Success = compilation.Passed, Published = false,
            ExecutionId = executionId, preview.Input.InputHash, SourceInputHash = preview.Input.InputHash,
            preview.RequestHash, ProjectTreeHash = preview.Project.TreeHash, OwnedCopyRoot = stage, SolutionPath = solution,
            ProjectIdentity = handle.ProjectIdentity, Compilation = compilation,
            Failures = compilation.Passed ? Array.Empty<string>() : new[] { "Owned-copy compilation did not pass with complete evidence." }, Cleanup = Array.Empty<string>() });
    }

    private async Task<JsonObject> VerifyAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        string id = Text(payload, "executionId");
        if (!executions.TryGetValue(id, out var execution)) throw new InvalidOperationException("Unknown owned execution.");
        await session.ResetNativeAsync().ConfigureAwait(false);
        string root = execution.Root, solution = execution.SolutionPath;
        if (execution.Published)
        {
            // B moved the verified stage: preserve published files and bind a new actual owned-copy identity.
            var snapshot = ProjectSnapshot.Capture(solution, allocation);
            root = Path.Combine(allocation.ScratchRoot, "verify-" + Guid.NewGuid().ToString("N"));
            snapshot.CopyTo(root, allocation);
            solution = Path.Combine(root, snapshot.SolutionRelativePath.Replace('/', Path.DirectorySeparatorChar));
        }
        var handle = await BindAsync(execution.Input, solution, root, cancellationToken).ConfigureAwait(false);
        var observed = await capture.CaptureAsync(execution.Input, handle, session, cancellationToken).ConfigureAwait(false);
        var verification = comparer.Compare(execution.Input, observed);
        bool passed = verification.Passed && observed.InputHash == execution.Input.InputHash &&
            observed.ProjectIdentity == handle.ProjectIdentity && observed.Saved && observed.Closed && observed.Reopened &&
            observed.Failures.Count == 0 && observed.Unsupported.Count == 0;
        return Envelope("verify", new { ExecutionId = id, execution.Input.InputHash, SourceInputHash = execution.Input.InputHash,
            ProjectIdentity = handle.ProjectIdentity, OwnedCopyRoot = root, SolutionPath = solution, Capture = observed,
            Verification = verification, Success = passed, Published = false,
            Failures = passed ? Array.Empty<string>() : new[] { "Independent owned-copy capture did not completely match the original frozen source input." } });
    }

    private static bool IsSimulationAction(string action) => action is "start-simulation" or "prepare-simulation" or "resume-simulation" or "simulation-status" or
        "simulation-state" or "simulation-read" or "simulation-write" or "stop-simulation";
    private void RequireRuntimeMode()
    {
        if (!configuration.RuntimeWrites || !configuration.OfflineWrites || !nativeBinding ||
            !NativeSimulationDeployment.NativeBuilt || !OwnedUsermodeRuntime.Installed(session.Environment.InstallDirectory))
            throw new InvalidOperationException("Explicit owned-simulation mode and installed native Runtime binding are required.");
    }
    private PersistentRuntimeSlot? PersistentSlot() => configuration.RuntimeInstanceRoot is { } root
        ? new(root, allocation.WorkspaceRoot, allocation.ScratchRoot,
            Path.Combine(session.Environment.InstallDirectory ?? throw new InvalidOperationException("Runtime install directory is absent."),
                "Runtimes", "bin", "TcSystemServiceUm.exe")) : null;
    private async Task<JsonObject> StartSimulationAsync(JsonElement payload, CancellationToken cancellationToken, bool prepareOnly = false)
    {
        var preview = ConsumeHostGrant(payload, "start-simulation");
        grants.Consume(Text(payload, "grantToken"), session.SessionId, preview.RequestHash);
        if (simulations.Count != 0) throw new InvalidOperationException("An owned simulation is already allocated.");
        await session.ResetNativeAsync().ConfigureAwait(false);
        string root = EngineeringPaths.RequireNewOutput(Text(payload, "outputDirectory"), allocation.ScratchRoot);
        Directory.CreateDirectory(root);
        string stage = Path.Combine(root, "project"); preview.Project!.CopyTo(stage, allocation);
        string solution = Path.Combine(stage, preview.Project.SolutionRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var handle = await BindAsync(preview.Input, solution, stage, cancellationToken).ConfigureAwait(false);
        SimulationPolicy.RequireNoHardware(handle.TwinCatProjectPath, preview.Input.Manifest.Motion, handle.PlcProjectName);
        OwnedUsermodeRuntime? runtime = null;
        CompilationReport? compilation = null; VerificationReport? verification = null;
        string phase = "simulation-process";
        try
        {
            runtime = preview.RuntimeSlot is { } slot
                ? await OwnedUsermodeRuntime.StartPersistentAsync(session.Environment.InstallDirectory!, configuration.RuntimeInstanceRoot!,
                    allocation.WorkspaceRoot, allocation.ScratchRoot, slot.Fingerprint, cancellationToken).ConfigureAwait(false)
                : await OwnedUsermodeRuntime.StartAsync(session.Environment.InstallDirectory!, Path.Combine(root, "runtime"),
                    allocation.ScratchRoot, cancellationToken).ConfigureAwait(false);
            await runtime.EnterConfigAsync(cancellationToken).ConfigureAwait(false);
            await NativeSimulationDeployment.SelectOwnedTargetAsync(handle, session, runtime, cancellationToken, preview.Input.Manifest.Motion).ConfigureAwait(false);
            await NativeSimulationDeployment.RecordOwnedIdeAsync(handle, session, runtime, cancellationToken).ConfigureAwait(false);
            phase = "simulation-compile";
            compilation = await compiler.CompileAsync(preview.Input, handle, session, cancellationToken).ConfigureAwait(false);
            if (!compilation.Passed) throw new InvalidOperationException("Owned simulation copy did not compile with complete evidence.");
            var observed = await capture.CaptureAsync(preview.Input, handle, session, cancellationToken).ConfigureAwait(false);
            verification = comparer.Compare(preview.Input, observed);
            if (!verification.Passed || observed.InputHash != preview.Input.InputHash || observed.ProjectIdentity != handle.ProjectIdentity ||
                observed.Failures.Count != 0 || observed.Unsupported.Count != 0)
                throw new InvalidOperationException("Independent simulation-copy verification failed.");
            phase = "simulation-activation";
            await NativeSimulationDeployment.ActivateAsync(handle, session, runtime, compilation, verification, cancellationToken, preview.Input.Manifest.Motion).ConfigureAwait(false);
            var prepared = new PreparedSimulation(handle, compilation, verification, root, Text(payload, "probeSymbol"));
            if (prepareOnly)
            {
                await NativeSimulationDeployment.ShowOwnedLicensePageAsync(handle, session, runtime, cancellationToken).ConfigureAwait(false);
                simulations.Add(runtime.SimulationId, new(runtime, preview.Input, handle.ProjectIdentity, prepared));
                return Envelope("prepare-simulation", new { Phase = "awaiting-user-license", Success = true,
                    ExecutionReady = false, runtime.SimulationId, runtime.NetId, runtime.ProcessId,
                    preview.Input.InputHash, handle.ProjectIdentity, Compilation = compilation, Verification = verification,
                    SolutionPath = handle.SolutionPath,
                    NextAction = "Handle official licensing in this owned IDE, then resume this simulation handle. No license is generated by the adapter." });
            }
            phase = "simulation-run";
            return await CompleteSimulationAsync("start-simulation", runtime, preview.Input, prepared, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var cleanup = new List<string>();
            if (runtime is not null) try { await runtime.DisposeAsync().ConfigureAwait(false); }
                catch { cleanup.Add("Owned Runtime cleanup could not be confirmed."); simulations[runtime.SimulationId] = new(runtime, preview.Input, handle.ProjectIdentity); }
            await File.WriteAllTextAsync(Path.Combine(root, "runtime-failure.log"), ex.ToString(), CancellationToken.None).ConfigureAwait(false);
            return Envelope(prepareOnly ? "prepare-simulation" : "start-simulation", new { Phase = phase, Success = false, Published = false, preview.Input.InputHash,
                Compilation = compilation, Verification = verification, OwnedEvidenceRoot = root,
                Failures = new[] { ex.GetType().Name + ": " + ex.Message }, Cleanup = cleanup });
        }
    }
    private async Task<JsonObject> ResumeSimulationAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var id = Text(payload, "simulationId");
        if (!simulations.TryGetValue(id, out var state) || state.Prepared is not { } prepared)
            throw new InvalidOperationException("Unknown prepared simulation; a running or foreign handle cannot be resumed.");
        state.Runtime.RequireLive();
        CompilationReport? compiled = null;
        try
        {
            // Human licensing/UI work must not silently alter the frozen program.
            await state.Runtime.EnterConfigAsync(cancellationToken).ConfigureAwait(false);
            // Licensing/runtime messages outlive solution reloads and this XAE
            // build cannot reliably clear them through DTE. Use our existing
            // fresh-owned-IDE boundary, keeping the SAME held Runtime/license.
            await session.ResetNativeAsync().ConfigureAwait(false);
            var reopened = await BindAsync(state.Input, prepared.Project.SolutionPath,
                prepared.Project.StageRoot, cancellationToken).ConfigureAwait(false);
            if (reopened.ProjectIdentity != prepared.Project.ProjectIdentity)
                throw new InvalidOperationException("Prepared project identity changed during fresh IDE reopen.");
            prepared = prepared with { Project = reopened };
            await NativeSimulationDeployment.SelectOwnedTargetAsync(prepared.Project, session, state.Runtime, cancellationToken, state.Input.Manifest.Motion).ConfigureAwait(false);
            await NativeSimulationDeployment.RecordOwnedIdeAsync(prepared.Project, session, state.Runtime, cancellationToken).ConfigureAwait(false);
            compiled = await compiler.CompileAsync(state.Input, prepared.Project, session, cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(prepared.Root, "resume-compilation.json"), JsonSerializer.Serialize(compiled, Json), cancellationToken).ConfigureAwait(false);
            if (!compiled.Passed) throw new InvalidOperationException("Prepared simulation did not compile after user interaction.");
            var observed = await capture.CaptureAsync(state.Input, prepared.Project, session, cancellationToken).ConfigureAwait(false);
            var verified = comparer.Compare(state.Input, observed);
            if (!verified.Passed || observed.InputHash != state.Input.InputHash || observed.ProjectIdentity != prepared.Project.ProjectIdentity ||
                observed.Failures.Count != 0 || observed.Unsupported.Count != 0)
                throw new InvalidOperationException("Prepared simulation differs from its frozen source.");
            prepared = prepared with { Compilation = compiled, Verification = verified };
            await NativeSimulationDeployment.ActivateAsync(prepared.Project, session, state.Runtime, compiled, verified, cancellationToken, state.Input.Manifest.Motion).ConfigureAwait(false);
            return await CompleteSimulationAsync("resume-simulation", state.Runtime, state.Input, prepared, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(Path.Combine(prepared.Root, "resume-failure.log"), ex.ToString(), CancellationToken.None).ConfigureAwait(false);
            return Envelope("resume-simulation", new { Phase = "resume-failed", Success = false, SimulationId = id,
                state.Input.InputHash, ExecutionReady = false,
                Compilation = compiled,
                Failures = new[] { ex.GetType().Name + ": " + ex.Message } });
        }
    }
    private async Task<JsonObject> CompleteSimulationAsync(string action, OwnedUsermodeRuntime runtime, FrozenGenerationInput input,
        PreparedSimulation prepared, CancellationToken cancellationToken)
    {
            await runtime.EnterRunAsync(cancellationToken).ConfigureAwait(false);
            string probeSymbol = prepared.ProbeSymbol;
            uint before = (uint)runtime.ReadSymbol(input, probeSymbol, "UDINT");
            uint after = before;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (after == before && watch.Elapsed < TimeSpan.FromSeconds(5))
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                after = (uint)runtime.ReadSymbol(input, probeSymbol, "UDINT");
            }
            if (after == before) throw new InvalidOperationException("The actual PLC probe did not change; execution is unverified.");
            using (var ads = runtime.Connect(851))
            {
                var app = ads.ReadValue("TwinCAT_SystemInfoVarList._AppInfo.AppName", typeof(string))?.ToString();
                var runningProject = ads.ReadValue("TwinCAT_SystemInfoVarList._AppInfo.ProjectName", typeof(string))?.ToString();
                var port = (ushort)ads.ReadValue("TwinCAT_SystemInfoVarList._AppInfo.AdsPort", typeof(ushort));
                SimulationPolicy.RequireApplicationIdentity(app, runningProject, port, input.Manifest.PlcProjectName);
            }
            var result = Envelope(action, new { Phase = "complete", Success = true, Published = false, ExecutionReady = true,
                runtime.SimulationId, runtime.NetId, runtime.ProcessId, runtime.ConfigurationRoot,
                input.InputHash, ProjectIdentity = prepared.Project.ProjectIdentity, Compilation = prepared.Compilation, Verification = prepared.Verification,
                Probe = new { Symbol = probeSymbol, Type = "UDINT", Before = before, After = after, NativeExecutionObserved = true },
                State = runtime.Status(), HardwareTested = false });
            await File.WriteAllTextAsync(Path.Combine(prepared.Root, "runtime-evidence.json"), result.ToJsonString(), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            simulations[runtime.SimulationId] = new(runtime, input, prepared.Project.ProjectIdentity);
            return result;
    }
    private async Task<JsonObject> SimulationActionAsync(string action, JsonElement payload)
    {
        var id = Text(payload, "simulationId");
        if (!simulations.TryGetValue(id, out var simulation)) throw new InvalidOperationException("Unknown owned simulation handle.");
        var runtime = simulation.Runtime; runtime.RequireLive();
        if (simulation.Prepared is not null && action != "stop-simulation")
        {
            if (action != "simulation-status") throw new InvalidOperationException("Prepared simulation is not qualified for PLC operations; resume it first.");
            using var system = runtime.Connect(10000);
            return Envelope(action, new { SimulationId = id, simulation.Input.InputHash, ExecutionReady = false,
                Phase = "awaiting-user-license", SystemState = system.ReadState().AdsState.ToString() });
        }
        object details;
        switch (action)
        {
            case "simulation-status": details = new { simulationId = id, State = runtime.Status(), simulation.Input.InputHash }; break;
            case "simulation-state":
                await runtime.SetPlcStateAsync(Text(payload, "state"), CancellationToken.None).ConfigureAwait(false);
                details = new { simulationId = id, State = runtime.Status() }; break;
            case "simulation-read":
                details = new { simulationId = id, Symbol = Text(payload, "symbol"), Type = Text(payload, "type"),
                    Value = runtime.ReadSymbol(simulation.Input, Text(payload, "symbol"), Text(payload, "type")) }; break;
            case "simulation-write":
                details = new { simulationId = id, Symbol = Text(payload, "symbol"), Type = Text(payload, "type"),
                    Value = runtime.WriteSymbol(simulation.Input, Text(payload, "symbol"), Text(payload, "type"), payload.GetProperty("value")) }; break;
            case "stop-simulation":
                await runtime.DisposeAsync().ConfigureAwait(false); simulations.Remove(id);
                details = new { simulationId = id, OwnedProcessExited = true }; break;
            default: throw new InvalidDataException("Unsupported simulation action.");
        }
        return Envelope(action, details);
    }

    private Task<NativeProjectHandle> BindAsync(FrozenGenerationInput input, string solution, string root, CancellationToken cancellationToken)
    {
        EngineeringPaths.RequireUnder(root, allocation.ScratchRoot, true);
        EngineeringPaths.RequireUnder(solution, root, true);
        return session.InvokeAsync(context =>
        {
            dynamic dte = context.Dte ?? throw new InvalidOperationException("Owned DTE unavailable.");
            string current = (string)dte.Solution.FullName;
            if (!string.IsNullOrEmpty(current))
            {
                EngineeringPaths.RequireUnder(current, allocation.ScratchRoot, true);
                dte.Solution.Close(false);
            }
            foreach (var reference in new[] { context.SystemManager, context.TwinCatProject }.Where(r => r is not null).Distinct(ReferenceEqualityComparer.Instance))
                if (System.Runtime.InteropServices.Marshal.IsComObject(reference!)) System.Runtime.InteropServices.Marshal.ReleaseComObject(reference!);
            context.SystemManager = null; context.TwinCatProject = null; context.BoundProjectPath = null; context.BoundProjectIdentity = null;
            dte.Solution.Open(solution);
            if (!(bool)dte.Solution.IsOpen || !Path.GetFullPath((string)dte.Solution.FullName).Equals(solution, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The exact owned solution did not open.");
            if ((int)dte.Solution.Projects.Count != 1) throw new InvalidOperationException("Exactly one owned configuration project is required.");
            dynamic project = dte.Solution.Projects.Item(1);
            string path = EngineeringPaths.RequireUnder((string)project.FullName, root, true);
            if (Path.GetExtension(path).ToLowerInvariant() != ".tsproj" || (string)project.Name != input.Manifest.ConfigurationName ||
                Path.GetFileNameWithoutExtension(solution) != input.Manifest.SolutionName)
                throw new InvalidOperationException("The actual solution/configuration identity differs from the frozen manifest.");
            string identity = NativeProjectHandle.ComputeIdentity(path, (string)project.Name, input.Manifest.PlcProjectName);
            context.SystemManager = (object)project.Object; context.TwinCatProject = (object)project;
            context.BoundProjectPath = path; context.BoundProjectIdentity = identity;
            return new NativeProjectHandle { SolutionPath = solution, TwinCatProjectPath = path, StageRoot = root,
                PlcProjectName = input.Manifest.PlcProjectName, ProjectIdentity = identity, InputHash = input.InputHash };
        }, cancellationToken);
    }

    private string CompileRequestHash(JsonObject request, FrozenGenerationInput input, ProjectSnapshot project, string? runtimeFingerprint = null)
        => Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { ProtocolVersion = "1", Request = request,
            SourceInputHash = input.InputHash, RuntimeFingerprint = runtimeFingerprint, ProjectTreeHash = project.TreeHash, project.Root,
            project.SolutionRelativePath, project.ProjectRelativePath, ProviderId, session.SessionId,
            Environment = session.Environment, allocation.WorkspaceRoot, allocation.ScratchRoot }, Json)));

    private void ExpirePreviews()
    {
        foreach (var key in previews.Where(p => p.Value.Expires <= time.GetUtcNow()).Select(p => p.Key).ToArray()) previews.Remove(key);
        foreach (var key in tokens.Where(p => p.Value.Expires <= time.GetUtcNow()).Select(p => p.Key).ToArray()) tokens.Remove(key);
    }
    private JsonObject Envelope(string action, object? details = null)
    {
        var result = details is null ? new JsonObject() : JsonSerializer.SerializeToNode(details, Json)!.AsObject();
        result["schemaVersion"] = "1.0"; result["operation"] = action; result["providerId"] = ProviderId; result["sessionId"] = session.SessionId;
        result["phase"] ??= action; result["success"] ??= true; result["published"] ??= false;
        result["failures"] ??= new JsonArray(); result["cleanup"] ??= new JsonArray();
        return result;
    }
    private JsonObject Failure(string action, string phase, string message) => Envelope(action,
        new { Phase = phase, Success = false, Published = false, Failures = new[] { message }, Cleanup = Array.Empty<string>() });

    private JsonObject Redact(JsonObject result)
    {
        // authorize's opaque private token is required by the local host; every other occurrence is redacted.
        string? privateToken = result["operation"]?.GetValue<string>() == "authorize" ? result["grantToken"]?.GetValue<string>() : null;
        void Visit(JsonNode? node)
        {
            if (node is JsonObject obj)
                foreach (var pair in obj.ToArray())
                {
                    if (pair.Key == "grantToken" && privateToken is not null) continue;
                    if (pair.Value is JsonValue val && val.TryGetValue<string>(out var text)) obj[pair.Key] = RedactText(text);
                    else Visit(pair.Value);
                }
            else if (node is JsonArray array)
                for (int i = 0; i < array.Count; i++)
                    if (array[i] is JsonValue val && val.TryGetValue<string>(out var text)) array[i] = RedactText(text);
                    else Visit(array[i]);
        }
        string RedactText(string text)
        {
            foreach (var secret in secrets) text = text.Replace(secret, "[redacted]", StringComparison.Ordinal);
            return text;
        }
        Visit(result); return result;
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string Text(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String ||
            value.GetString() is not { Length: > 0 and <= 4096 } text || text.Contains('\0'))
            throw new InvalidDataException("A bounded string is required.");
        return text;
    }
    private static void ValidatePayload(string action, JsonElement payload)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "providerId", "sessionId" };
        foreach (string name in action switch
        {
            "inspect" => new[] { "projectPath" },
            "preview" => new[] { "operation", "inputDirectory", "outputDirectory", "projectPath", "executionId", "probeSymbol" },
            "authorize" or "build-generation" or "compile" or "start-simulation" or "prepare-simulation" => new[] { "operation", "inputDirectory", "outputDirectory", "projectPath", "executionId", "probeSymbol", "previewId", "inputHash", "grantToken" },
            "verify" => new[] { "executionId" },
            "simulation-status" or "stop-simulation" or "resume-simulation" => new[] { "simulationId" },
            "simulation-state" => new[] { "simulationId", "state" },
            "simulation-read" => new[] { "simulationId", "symbol", "type" },
            "simulation-write" => new[] { "simulationId", "symbol", "type", "value" },
            "doctor" or "dispose" => Array.Empty<string>(),
            _ => throw new InvalidDataException("Invalid action.")
        }) allowed.Add(name);
        if (payload.EnumerateObject().Any(p => !allowed.Contains(p.Name))) throw new InvalidDataException("Unknown request property.");
        if (action == "authorize" && payload.TryGetProperty("grantToken", out _)) throw new InvalidDataException("Authorization cannot accept a token.");
    }

    private async Task<JsonObject> DisposeReportAsync()
    {
        try { await DisposeAsync().ConfigureAwait(false); return Envelope("dispose"); }
        catch { return Envelope("dispose", new { Phase = "cleanup", Success = false, Cleanup = new[] { "Owned session cleanup failed." } }); }
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        foreach (var simulation in simulations.Values) await simulation.Runtime.DisposeAsync().ConfigureAwait(false);
        simulations.Clear(); disposed = true; previews.Clear(); tokens.Clear(); executions.Clear(); secrets.Clear();
        await session.DisposeAsync().ConfigureAwait(false);
    }
}
