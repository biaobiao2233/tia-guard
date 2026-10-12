namespace TwinCat3.Contracts;

public interface IOwnedEngineeringSession : IAsyncDisposable
{
    string SessionId { get; }
    EnvironmentReport Environment { get; }
    Task<T> InvokeAsync<T>(Func<NativeEngineeringContext, T> callback,
        CancellationToken cancellationToken = default);
}

public interface IEngineeringEnvironment
{
    Task<EnvironmentReport> InspectAsync(CancellationToken cancellationToken = default);
    Task<IOwnedEngineeringSession> CreateOwnedSessionAsync(EnvironmentSelection selection,
        string ownedWorkspaceRoot, CancellationToken cancellationToken = default);
}

public interface IEngineeringCreator
{
    Task<NativeProjectHandle> CreateAsync(FrozenGenerationInput input, string stageRoot,
        IOwnedEngineeringSession session, CancellationToken cancellationToken = default);
}

public interface IEngineeringCompiler
{
    Task<CompilationReport> CompileAsync(FrozenGenerationInput input, NativeProjectHandle project,
        IOwnedEngineeringSession session, CancellationToken cancellationToken = default);
}

public interface IProjectCaptureService
{
    Task<ProjectCapture> CaptureAsync(FrozenGenerationInput input, NativeProjectHandle project,
        IOwnedEngineeringSession session, CancellationToken cancellationToken = default);
}

public interface IProjectComparer
{
    VerificationReport Compare(FrozenGenerationInput input, ProjectCapture capture);
}
