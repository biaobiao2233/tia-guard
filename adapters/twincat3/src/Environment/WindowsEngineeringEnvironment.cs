using System.Diagnostics;
using System.Runtime.InteropServices;
using TwinCat3.Contracts;

namespace TwinCat3.EnvironmentHost;

public sealed class WindowsEngineeringEnvironment : IEngineeringEnvironment
{
    private readonly string? interopPath;
    public WindowsEngineeringEnvironment(string? twinCatInteropAssemblyPath = null)
    {
        interopPath = twinCatInteropAssemblyPath;
        if (OperatingSystem.IsWindows()) LocalInteropResolver.Register(EnvironmentDoctor.Inspect(interopPath).InteropAssemblyPaths);
    }
    public Task<EnvironmentReport> InspectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(EnvironmentDoctor.Inspect(interopPath));
    }
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public async Task<IOwnedEngineeringSession> CreateOwnedSessionAsync(EnvironmentSelection selection,
        string ownedWorkspaceRoot, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows is required.");
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64) throw new PlatformNotSupportedException("Native worker must be x64.");
        var workspace = Path.GetFullPath(ownedWorkspaceRoot);
        if (!Directory.Exists(workspace)) throw new InvalidDataException("Allocated owned workspace must already exist.");
        GenerationInputLoader.RequireNoReparseComponents(workspace);
        var report = await InspectAsync(cancellationToken);
        if (selection.RequestedBuild is { } requested && !report.InstalledBuilds.Contains(requested.Replace("3.1.", ""), StringComparer.Ordinal))
            throw new InvalidDataException("Requested TwinCAT build is not installed.");
        if (selection.RequestedBuild is null && report.InstalledBuilds.Count != 1)
            throw new InvalidDataException("An exact build must be selected when installed version identity is ambiguous.");
        if (selection.Provider is not ("dte" or "standalone")) throw new InvalidDataException("Unknown native provider.");
        var progId = selection.Provider == "standalone" ? "TcSysManagerRM" : selection.DteProgId;
        if (!report.ComRegistrations.Any(r => r.ProgId == progId && r.Registered))
            throw new InvalidDataException("Requested COM provider is not registered.");
        var dispatcher = new StaDispatcher();
        var context = new NativeEngineeringContext { Environment = report };
        var session = new OwnedSession(dispatcher, context);
        try
        {
            var preexisting = Process.GetProcessesByName("TcXaeShell").Select(p => p.Id).ToHashSet();
            await dispatcher.InvokeAsync(() =>
            {
                var type = Type.GetTypeFromProgID(progId, throwOnError: true)!;
                var factory = Activator.CreateInstance(type) ?? throw new InvalidOperationException("COM activation returned null.");
                if (selection.Provider == "standalone")
                {
                    try { context.SystemManager = ((dynamic)factory).CreateSysManager15(); }
                    finally { Release(factory); }
                }
                else
                {
                    object? window = null;
                    try
                    {
                        dynamic dte = factory;
                        // Prove this is a new process before changing UI/session state.
                        window = dte.MainWindow;
                        var handle = new IntPtr(Convert.ToInt64(((dynamic)window).HWnd));
                        GetWindowThreadProcessId(handle, out var processId);
                        if (processId == 0 || preexisting.Contains((int)processId))
                            throw new InvalidOperationException("COM activation did not prove a fresh owned IDE process; refusing to take control.");
                        context.Dte = factory;
                        session.OwnedProcessId = (int)processId;
                        dte.SuppressUI = true; ((dynamic)window).Visible = false; dte.UserControl = false;
                    }
                    finally
                    {
                        Release(window);
                        if (!ReferenceEquals(context.Dte, factory)) Release(factory);
                    }
                }
                return true;
            }, cancellationToken).ConfigureAwait(false);
            return session;
        }
        catch (Exception creationFailure)
        {
            try { await session.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanupFailure) { throw new AggregateException("Owned session creation and cleanup both failed.", creationFailure, cleanupFailure); }
            throw;
        }
    }
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private sealed class OwnedSession : IOwnedEngineeringSession
    {
        private readonly StaDispatcher dispatcher;
        private readonly NativeEngineeringContext context;
        private int disposing;
        public int? OwnedProcessId { get; set; }
        public string SessionId { get; } = "tc3-" + Guid.NewGuid().ToString("N");
        public EnvironmentReport Environment => context.Environment;
        public OwnedSession(StaDispatcher dispatcher, NativeEngineeringContext context)
        { this.dispatcher = dispatcher; this.context = context; }
        public Task<T> InvokeAsync<T>(Func<NativeEngineeringContext, T> callback, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposing) != 0, this);
            return dispatcher.InvokeAsync(() => callback(context), cancellationToken);
        }
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposing, 1) != 0) return;
            Exception? failure = null;
            try
            {
                await dispatcher.InvokeAsync(() =>
                {
                    var failures = new List<Exception>();
                    if (context.Dte is { } raw)
                    {
                        try { ((dynamic)raw).Solution.Close(false); } catch (Exception ex) { failures.Add(ex); }
                        try { ((dynamic)raw).Quit(); } catch (Exception ex) { failures.Add(ex); }
                    }
                    var references = new[] { context.SystemManager, context.TwinCatProject, context.Dte }
                        .Where(o => o is not null).Distinct(ReferenceEqualityComparer.Instance).ToArray();
                    foreach (var reference in references)
                        try { Release(reference); } catch (Exception ex) { failures.Add(ex); }
                    context.Dte = null; context.SystemManager = null; context.TwinCatProject = null;
                    if (failures.Count > 0) throw new AggregateException("Owned COM cleanup failed.", failures);
                    return true;
                }).WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
            }
            catch (Exception ex) { failure = ex; }
            try { await dispatcher.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { failure = failure is null ? ex : new AggregateException(failure, ex); }
            if (OwnedProcessId is { } processId)
            {
                try
                {
                    using var process = Process.GetProcessById(processId);
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }
                catch (ArgumentException) { /* Owned process already exited. */ }
                catch (Exception ex) { failure = failure is null ? ex : new AggregateException(failure, ex); }
            }
            if (failure is not null) throw failure;
        }
    }
}
