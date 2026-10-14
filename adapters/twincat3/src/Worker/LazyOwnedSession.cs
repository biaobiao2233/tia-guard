using System.Text.Json;
using TwinCat3.Contracts;

namespace TwinCat3.Worker;

/// <summary>A stable logical identity; neither construction nor preview starts COM.</summary>
public sealed class LazyOwnedSession : IOwnedEngineeringSession
{
    private readonly IEngineeringEnvironment environment;
    private readonly string workspace;
    private EnvironmentSelection? selection;
    private IOwnedEngineeringSession? actual;
    private bool disposed;
    public string SessionId { get; } = "tc3-worker-" + Guid.NewGuid().ToString("N");
    public EnvironmentReport Environment { get; }
    public bool NativeCreated => actual is not null;

    public LazyOwnedSession(IEngineeringEnvironment environment, EnvironmentReport report, string workspace)
    { this.environment = environment; Environment = report; this.workspace = workspace; }

    public void Select(EnvironmentSelection value)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (value.Provider != "dte") throw new InvalidDataException("The worker supports only the owned DTE provider.");
        if (selection is not null && JsonSerializer.Serialize(selection, ContractJson.Options) != JsonSerializer.Serialize(value, ContractJson.Options))
            throw new InvalidOperationException("The environment selection is immutable within an owned worker.");
        selection = value;
    }

    public async Task<T> InvokeAsync<T>(Func<NativeEngineeringContext, T> callback, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (actual is null)
        {
            if (selection is null) throw new InvalidOperationException("A frozen environment selection is required.");
            actual = await environment.CreateOwnedSessionAsync(selection, workspace, cancellationToken).ConfigureAwait(false);
        }
        return await actual.InvokeAsync(context =>
        {
            if (Fingerprint(context.Environment) != Fingerprint(Environment))
                throw new InvalidOperationException("The detected environment changed before native execution.");
            return callback(context);
        }, cancellationToken).ConfigureAwait(false);
    }

    internal static string Fingerprint(EnvironmentReport report)
        => JsonSerializer.Serialize(report with { ObservedAtUtc = DateTimeOffset.UnixEpoch }, ContractJson.Options);

    public async Task ResetNativeAsync()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (actual is null) return;
        // Retain logical preview/grant identity, but never inherit a prior IDE's
        // stale Error List or bound native project into another operation.
        await actual.DisposeAsync().ConfigureAwait(false);
        actual = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        if (actual is not null) await actual.DisposeAsync().ConfigureAwait(false);
    }
}
