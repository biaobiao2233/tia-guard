using TwinCat3.EnvironmentHost;
using Xunit;

namespace TwinCat3.EnvironmentTests;

public sealed class StaDispatcherTests
{
    [Fact, System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public async Task CallbacksRemainOnTheOneOwnedStaAndFailuresDoNotBreakIt()
    {
        if (!OperatingSystem.IsWindows()) return;
        await using var dispatcher = new StaDispatcher();
        var first = await dispatcher.InvokeAsync(() => (System.Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState()));
        var second = await dispatcher.InvokeAsync(() => (System.Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState()));
        Assert.Equal(first, second); Assert.Equal(ApartmentState.STA, first.Item2);
        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.InvokeAsync<int>(() => throw new InvalidOperationException("controlled")));
        Assert.Equal(first.Item1, await dispatcher.InvokeAsync(() => System.Environment.CurrentManagedThreadId));
    }

    [Fact, System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public async Task CancelledQueuedCallbacksNeverExecuteAndDisposedQueueRejectsWork()
    {
        if (!OperatingSystem.IsWindows()) return;
        var dispatcher = new StaDispatcher();
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var running = dispatcher.InvokeAsync(() => { started.TrySetResult(); release.Wait(TimeSpan.FromSeconds(5)); return 1; });
        await started.Task;
        var executed = false;
        var queued = dispatcher.InvokeAsync(() => { executed = true; return 2; }, cancellation.Token);
        cancellation.Cancel(); release.Set();
        await running;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        Assert.False(executed);
        await dispatcher.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => dispatcher.InvokeAsync(() => 3));
    }

    [Fact] public void ReadOnlyDoctorDoesNotClaimFunctionalGeneration()
    {
        var report = EnvironmentDoctor.Inspect();
        if (!OperatingSystem.IsWindows()) { Assert.False(report.IsWindows); return; }
        Assert.True(report.IsWindows);
        Assert.Equal("unqualified", report.Capabilities["generation"].State);
        Assert.Null(report.Capabilities["ownedDteSession"].Available);
        Assert.False(report.Capabilities["runtimeWrites"].Available);
    }
}
