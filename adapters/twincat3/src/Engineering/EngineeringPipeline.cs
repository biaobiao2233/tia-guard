using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TwinCat3.Contracts;

namespace TwinCat3.Engineering;

public sealed record GenerationRequest
{
    public required string SourceDirectory { get; init; }
    public required string OutputDirectory { get; init; }
}

public sealed record GenerationPreview
{
    public required string PreviewId { get; init; }
    public required string SessionId { get; init; }
    public required string InputHash { get; init; }
    public required string RequestHash { get; init; }
    public required string SourceDirectory { get; init; }
    public required string OutputDirectory { get; init; }
    public required string OwnedScratchRoot { get; init; }
    public required GenerationManifest Manifest { get; init; }
    public required EnvironmentReport Environment { get; init; }
    public bool Mutates => false;
}

public sealed record GenerationResult
{
    public string SchemaVersion { get; init; } = "1.0";
    public string Operation { get; init; } = "build-generation";
    public required string SessionId { get; init; }
    public string? InputHash { get; init; }
    public required string Phase { get; init; }
    public bool Success { get; init; }
    public bool Published { get; init; }
    public string? OutputDirectory { get; init; }
    public string? SolutionPath { get; init; }
    public string? RetainedStageRoot { get; init; }
    public string? ArtifactTreeHash { get; init; }
    public required EnvironmentReport Environment { get; init; }
    public CompilationReport? Compilation { get; init; }
    public ProjectCapture? Capture { get; init; }
    public VerificationReport? Verification { get; init; }
    public IReadOnlyList<string> Failures { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Cleanup { get; init; } = Array.Empty<string>();
}

/// <summary>Every native mutation/publication is behind this worker-side checked operation.</summary>
public sealed class EngineeringPipeline
{
    private sealed record FrozenPreview(GenerationPreview Public, FrozenGenerationInput Input);
    private readonly EngineeringAllocation allocation;
    private readonly StageAuthorizationRegistry authorizations;
    private readonly ExecutionGrantStore grants;
    private readonly IEngineeringCreator creator;
    private readonly IEngineeringCompiler? compiler;
    private readonly IProjectCaptureService? capture;
    private readonly IProjectComparer? comparer;
    private readonly Dictionary<string, FrozenPreview> previews = new(StringComparer.Ordinal);
    private readonly object previewSync = new();
    private readonly SemaphoreSlim operationGate = new(1, 1);

    public EngineeringPipeline(EngineeringAllocation allocation, StageAuthorizationRegistry authorizations,
        ExecutionGrantStore grants, IEngineeringCreator creator, IEngineeringCompiler? compiler,
        IProjectCaptureService? capture, IProjectComparer? comparer)
    {
        this.allocation = allocation ?? throw new ArgumentNullException(nameof(allocation));
        this.authorizations = authorizations ?? throw new ArgumentNullException(nameof(authorizations));
        this.grants = grants ?? throw new ArgumentNullException(nameof(grants));
        this.creator = creator ?? throw new ArgumentNullException(nameof(creator));
        this.compiler = compiler;
        this.capture = capture;
        this.comparer = comparer;
    }

    public GenerationPreview Preview(GenerationRequest request, IOwnedEngineeringSession session)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(session);
        string inputDirectory = allocation.CheckInput(request.SourceDirectory);
        string outputDirectory = allocation.CheckOutput(request.OutputDirectory, inputDirectory);
        FrozenGenerationInput input = GenerationInputLoader.Load(inputDirectory);
        RequireAuthoringShape(input);
        RequireEnvironment(input.Manifest.Environment, session.Environment);
        string requestHash = RequestHash(input, outputDirectory, session);
        var preview = new GenerationPreview
        {
            PreviewId = Guid.NewGuid().ToString("N"), SessionId = session.SessionId,
            InputHash = input.InputHash, RequestHash = requestHash,
            SourceDirectory = inputDirectory, OutputDirectory = outputDirectory,
            OwnedScratchRoot = allocation.ScratchRoot, Manifest = input.Manifest, Environment = session.Environment
        };
        lock (previewSync)
        {
            if (previews.Count >= 256) throw new InvalidOperationException("Too many pending previews; restart the owned worker.");
            previews.Add(preview.PreviewId, new FrozenPreview(preview, input));
        }
        return preview;
    }

    public string IssueGrant(string previewId, string sessionId)
    {
        lock (previewSync)
        {
            if (!previews.TryGetValue(previewId, out FrozenPreview? preview) || preview.Public.SessionId != sessionId)
                throw new InvalidOperationException("A preview from this owned worker session is required.");
            return grants.Issue(sessionId, preview.Public.RequestHash);
        }
    }

    public async Task<GenerationResult> BuildAsync(GenerationRequest request, string previewId, string grant,
        IOwnedEngineeringSession session, CancellationToken cancellationToken = default)
    {
        string phase = "preflight";
        string? stage = null;
        FrozenGenerationInput? input = null;
        string? output = null;
        NativeProjectHandle? project = null;
        CompilationReport? compilation = null;
        ProjectCapture? observed = null;
        VerificationReport? verification = null;
        string? treeHash = null;
        var failures = new List<string>();
        var cleanup = new List<string>();
        bool published = false;
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        IDisposable? lease = null;
        try
        {
            if (compiler is null || capture is null || comparer is null)
                throw new InvalidOperationException("Compilation, independent capture and comparison services are all mandatory before mutation.");
            string inputDirectory = allocation.CheckInput(request.SourceDirectory);
            output = allocation.CheckOutput(request.OutputDirectory, inputDirectory);
            input = GenerationInputLoader.Load(inputDirectory);
            RequireAuthoringShape(input);
            RequireEnvironment(input.Manifest.Environment, session.Environment);
            string currentHash = RequestHash(input, output, session);
            grants.Consume(grant, session.SessionId, currentHash);
            FrozenPreview prior;
            lock (previewSync)
            {
                if (!previews.Remove(previewId, out FrozenPreview? found))
                    throw new InvalidOperationException("The preview is invalid or already consumed.");
                prior = found;
            }
            if (prior.Public.SessionId != session.SessionId || prior.Public.RequestHash != currentHash
                || prior.Input.InputHash != input.InputHash)
                throw new InvalidOperationException("Preview/request/input/session identity changed; request a new preview.");
            cancellationToken.ThrowIfCancellationRequested();
            stage = Path.Combine(allocation.ScratchRoot, "generation-" + Guid.NewGuid().ToString("N"));
            allocation.CheckStageParent();
            Directory.CreateDirectory(stage);
            lease = authorizations.Authorize(stage, session.SessionId, input.InputHash);
            phase = "create";
            project = await creator.CreateAsync(prior.Input, stage, session, cancellationToken).ConfigureAwait(false);
            RequireProjectBinding(project, prior.Input, stage);
            phase = "compile";
            compilation = await compiler.CompileAsync(prior.Input, project, session, cancellationToken).ConfigureAwait(false);
            if (!compilation.Passed || compilation.InputHash != input.InputHash
                || compilation.ProjectIdentity != project.ProjectIdentity
                || compilation.Configuration != input.Manifest.Environment.BuildConfiguration
                || compilation.Platform != input.Manifest.Environment.TargetPlatform)
                throw new InvalidOperationException("The intended project did not complete a fully captured, successful compilation.");
            phase = "capture";
            observed = await capture.CaptureAsync(prior.Input, project, session, cancellationToken).ConfigureAwait(false);
            phase = "verify";
            verification = comparer.Compare(prior.Input, observed);
            if (!verification.Passed || observed.InputHash != input.InputHash
                || observed.ProjectIdentity != project.ProjectIdentity || !observed.Saved || !observed.Closed
                || !observed.Reopened || !observed.ObjectsComplete || !observed.TasksComplete || !observed.LibrariesComplete
                || observed.Failures.Count > 0 || observed.Unsupported.Count > 0)
                throw new InvalidOperationException("Independent close/reopen readback did not fully match the frozen input.");
            phase = "close";
            await CloseOwnedStageAsync(stage, session, cancellationToken).ConfigureAwait(false);
            phase = "publish";
            // Re-read input and paths after the native calls; no earlier permission becomes a broad write grant.
            FrozenGenerationInput finalInput = GenerationInputLoader.Load(allocation.CheckInput(request.SourceDirectory));
            if (finalInput.InputHash != input.InputHash)
                throw new InvalidOperationException("Input drifted during engineering; the new project will not be published.");
            output = allocation.CheckOutput(output, finalInput.RootDirectory);
            allocation.CheckStage(stage);
            EngineeringPaths.CheckTree(stage);
            treeHash = HashTree(stage);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(stage, output);
            published = true;
            phase = "complete";
        }
        catch (Exception exception)
        {
            failures.Add(exception.GetType().Name + ": " + exception.Message);
        }
        finally
        {
            if (stage is not null && !published)
            {
                try { await CloseOwnedStageAsync(stage, session, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception exception) { cleanup.Add(exception.GetType().Name + ": " + exception.Message); }
            }
            lease?.Dispose();
            operationGate.Release();
        }
        return new GenerationResult
        {
            SessionId = session.SessionId, InputHash = input?.InputHash, Phase = phase,
            Success = published && failures.Count == 0 && cleanup.Count == 0,
            Published = published, OutputDirectory = published ? output : null,
            SolutionPath = published && project is not null ? Path.Combine(output!, Path.GetRelativePath(stage!, project.SolutionPath)) : null,
            RetainedStageRoot = !published ? stage : null, ArtifactTreeHash = treeHash,
            Environment = session.Environment, Compilation = compilation, Capture = observed, Verification = verification,
            Failures = failures.AsReadOnly(), Cleanup = cleanup.AsReadOnly()
        };
    }

    private string RequestHash(FrozenGenerationInput input, string output, IOwnedEngineeringSession session)
    {
        string serialized = JsonSerializer.Serialize(new
        {
            Operation = "build-generation", Version = "1.0", session.SessionId, input.InputHash,
            Source = input.RootDirectory, Output = output, allocation.WorkspaceRoot, allocation.ScratchRoot,
            Environment = session.Environment
        }, ContractJson.Options);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serialized))).ToLowerInvariant();
    }

    private static void RequireEnvironment(EnvironmentSelection selection, EnvironmentReport observed)
    {
        if (!observed.IsWindows || observed.ProcessArchitecture != "X64" || selection.Provider != "dte")
            throw new NotSupportedException("Offline native generation requires the detected Windows x64 DTE provider.");
        if (selection.RequestedBuild is null || !observed.InstalledBuilds.Contains(selection.RequestedBuild, StringComparer.Ordinal))
            throw new InvalidOperationException("The generation manifest must request one detected, explicit TwinCAT build.");
        if (string.IsNullOrEmpty(observed.ProjectTemplatePath) || observed.ProjectTemplateHash?.Length != 64
            || !observed.PlcTemplateNames.Contains(selection.PlcTemplate, StringComparer.Ordinal))
            throw new InvalidOperationException("The selected environment must include detected template identities.");
        if (!observed.ComRegistrations.Any(r => r.ProgId == selection.DteProgId && r.Registered))
            throw new InvalidOperationException("The requested DTE provider is not registered in the detected environment.");
    }

    private static void RequireAuthoringShape(FrozenGenerationInput input)
    {
        foreach (string name in new[] { input.Manifest.SolutionName, input.Manifest.ConfigurationName, input.Manifest.PlcProjectName })
            EngineeringPaths.RequireIdentifier(name);
        foreach (ArtifactSpec artifact in input.Manifest.Artifacts)
        {
            EngineeringPaths.RequireIdentifier(artifact.Name);
            if (artifact.Kind == ArtifactKind.Function)
                NativeEngineeringCreator.FunctionReturnType(input.Files[artifact.DeclarationFile].Text, artifact.Name);
        }
        foreach (TaskSpec task in input.Manifest.Tasks) EngineeringPaths.RequireIdentifier(task.Name);
    }

    private static void RequireProjectBinding(NativeProjectHandle project, FrozenGenerationInput input, string stage)
    {
        EngineeringPaths.RequireUnder(project.SolutionPath, stage, true);
        EngineeringPaths.RequireUnder(project.TwinCatProjectPath, stage, true);
        if (project.StageRoot != stage || project.InputHash != input.InputHash
            || project.PlcProjectName != input.Manifest.PlcProjectName
            || project.ProjectIdentity != NativeProjectHandle.ComputeIdentity(project.TwinCatProjectPath,
                input.Manifest.ConfigurationName, input.Manifest.PlcProjectName))
            throw new InvalidOperationException("The creator returned a foreign project/input/stage binding.");
    }

    private static async Task CloseOwnedStageAsync(string stage, IOwnedEngineeringSession session, CancellationToken cancellationToken)
    {
        await session.InvokeAsync(context =>
        {
            if (context.Dte is null) return false;
            dynamic dte = context.Dte;
            string solutionPath = (string)dte.Solution.FullName;
            if (string.IsNullOrEmpty(solutionPath)) return false;
            EngineeringPaths.RequireUnder(solutionPath, stage, true);
            // C already saved the verified candidate. Closing without another save preserves it.
            dte.Solution.Close(false);
            context.SystemManager = null;
            context.TwinCatProject = null;
            context.BoundProjectPath = null;
            context.BoundProjectIdentity = null;
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    public static string HashTree(string directory)
    {
        EngineeringPaths.CheckTree(directory);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[8];
        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .OrderBy(f => Path.GetRelativePath(directory, f).Replace('\\', '/'), StringComparer.Ordinal))
        {
            string relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
            byte[] name = Encoding.UTF8.GetBytes(relative);
            System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(length, name.LongLength);
            hash.AppendData(length); hash.AppendData(name);
            using var contents = File.OpenRead(file);
            hash.AppendData(SHA256.HashData(contents));
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
