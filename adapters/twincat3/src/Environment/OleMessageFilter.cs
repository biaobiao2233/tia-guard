// Adapted from Edge-JB/TwinCAT-XAE-MCP@30d8e2938eeed1c41ee24765b469d13430ab0a32.
// Copyright (c) 2026 Edge Automation. MIT; see THIRD_PARTY.Environment.md.
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TwinCat3.EnvironmentHost;

[ComImport, Guid("00000016-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IOleMessageFilter
{
    [PreserveSig] int HandleInComingCall(int callType, IntPtr taskCaller, int tickCount, IntPtr interfaceInfo);
    [PreserveSig] int RetryRejectedCall(IntPtr taskCallee, int tickCount, int rejectType);
    [PreserveSig] int MessagePending(IntPtr taskCallee, int tickCount, int pendingType);
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None), SupportedOSPlatform("windows")]
internal sealed class OleMessageFilter : IOleMessageFilter, IDisposable
{
    private IOleMessageFilter? previous;
    public static OleMessageFilter Register()
    {
        var filter = new OleMessageFilter();
        Marshal.ThrowExceptionForHR(CoRegisterMessageFilter(filter, out filter.previous));
        return filter;
    }
    public int HandleInComingCall(int callType, IntPtr taskCaller, int tickCount, IntPtr interfaceInfo) => 0;
    public int RetryRejectedCall(IntPtr taskCallee, int tickCount, int rejectType)
        => rejectType == 2 && tickCount < 60000 ? 150 : -1;
    public int MessagePending(IntPtr taskCallee, int tickCount, int pendingType) => 2;
    public void Dispose() => Marshal.ThrowExceptionForHR(CoRegisterMessageFilter(previous, out _));
    [DllImport("ole32.dll")] private static extern int CoRegisterMessageFilter(IOleMessageFilter? next, out IOleMessageFilter? old);
}
