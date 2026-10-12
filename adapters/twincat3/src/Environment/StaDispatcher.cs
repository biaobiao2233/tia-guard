using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TwinCat3.EnvironmentHost;

// A real queue executes every callback on this thread. A plain
// SynchronizationContext would post work to the thread pool, not the STA.
[SupportedOSPlatform("windows")]
public sealed class StaDispatcher : IAsyncDisposable
{
    private readonly BlockingCollection<Action> work = new();
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread thread;
    private int disposing;
    private int ownerThread;
    public StaDispatcher()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("TwinCAT COM requires Windows.");
        thread = new Thread(Run) { IsBackground = true, Name = "TwinCAT owned STA" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }
    public async Task<T> InvokeAsync<T>(Func<T> callback, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        cancellationToken.ThrowIfCancellationRequested();
        await ready.Task.ConfigureAwait(false);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposing) != 0, this);
        if (System.Environment.CurrentManagedThreadId == ownerThread) return callback();
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            work.Add(() =>
            {
                if (cancellationToken.IsCancellationRequested) { completion.TrySetCanceled(cancellationToken); return; }
                try
                {
                    var result = callback();
                    if (cancellationToken.IsCancellationRequested) completion.TrySetCanceled(cancellationToken);
                    else completion.TrySetResult(result);
                }
                catch (Exception ex) { completion.TrySetException(ex); }
            });
        }
        catch (InvalidOperationException) { throw new ObjectDisposedException(nameof(StaDispatcher)); }
        return await completion.Task.ConfigureAwait(false);
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposing, 1) == 0) work.CompleteAdding();
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
    }
    private void Run()
    {
        var initialized = false;
        Exception? failure = null;
        try
        {
            ownerThread = System.Environment.CurrentManagedThreadId;
            Marshal.ThrowExceptionForHR(OleInitialize(IntPtr.Zero));
            initialized = true;
            using var filter = OleMessageFilter.Register();
            ready.TrySetResult();
            while (!work.IsCompleted)
            {
                if (work.TryTake(out var action, 20)) action();
                PumpMessages();
            }
        }
        catch (Exception ex) { ready.TrySetException(ex); failure = ex; }
        finally
        {
            if (initialized) OleUninitialize();
            if (failure is null) stopped.TrySetResult(); else stopped.TrySetException(failure);
        }
    }
    private static void PumpMessages()
    {
        while (PeekMessage(out var message, IntPtr.Zero, 0, 0, 1))
        {
            if (message.Message == 0x12) continue;
            TranslateMessage(ref message); DispatchMessage(ref message);
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct MessageData
    {
        public IntPtr Hwnd; public uint Message; public UIntPtr WParam; public IntPtr LParam;
        public uint Time; public int X; public int Y; public uint Private;
    }
    [DllImport("ole32.dll")] private static extern int OleInitialize(IntPtr reserved);
    [DllImport("ole32.dll")] private static extern void OleUninitialize();
    [DllImport("user32.dll")] private static extern bool PeekMessage(out MessageData message, IntPtr hwnd, uint minimum, uint maximum, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MessageData message);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MessageData message);
}
