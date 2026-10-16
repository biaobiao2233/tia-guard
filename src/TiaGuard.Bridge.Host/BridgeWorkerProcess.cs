using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace TiaGuard.Bridge.Host;

// A packaged desktop caller changes native DLL lookup in its descendants. TIA's
// online libraries use installed OAM dependencies on PATH. Start only our Worker
// outside that desktop package environment, with the same user/token/environment.
// No shell broker, vendor DLL copying, installation or system PATH changes.
internal sealed class BridgeWorkerProcess : IDisposable
{
    public Process Process { get; }
    public StreamWriter StandardInput { get; }
    public StreamReader StandardOutput { get; }
    public StreamReader StandardError { get; }
    public bool HasExited => Process.HasExited;

    private BridgeWorkerProcess(Process process, StreamWriter input, StreamReader output, StreamReader error)
    {
        Process = process; StandardInput = input; StandardOutput = output; StandardError = error;
    }

    internal static BridgeWorkerProcess Start(ProcessStartInfo info)
    {
        if (!OperatingSystem.IsWindows() || !HasPackageIdentity())
        {
            var ordinary = Process.Start(info) ?? throw new InvalidOperationException("Failed to start the Bridge worker.");
            return new(ordinary, ordinary.StandardInput, ordinary.StandardOutput, ordinary.StandardError);
        }
        return StartDesktopWorker(info);
    }

    private static bool HasPackageIdentity()
    {
        uint length = 0;
        var status = GetCurrentPackageFullName(ref length, null);
        if (status == 15700) return false; // APPMODEL_ERROR_NO_PACKAGE
        if (status == 122) return true; // ERROR_INSUFFICIENT_BUFFER: an identity exists
        throw new Win32Exception(status, "Could not determine the desktop package context.");
    }

    internal static string QuoteArgument(string value)
    {
        // CommandLineToArgvW/CRT quoting, including embedded quotes and trailing '\'.
        var text = new StringBuilder("\"");
        var slashes = 0;
        foreach (var character in value)
        {
            if (character == '\\') { slashes++; continue; }
            text.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            text.Append(character);
            slashes = 0;
        }
        return text.Append('\\', slashes * 2).Append('"').ToString();
    }

    private static BridgeWorkerProcess StartDesktopWorker(ProcessStartInfo info)
    {
        if (info.UseShellExecute || !info.RedirectStandardInput || !info.RedirectStandardOutput || !info.RedirectStandardError ||
            !Path.IsPathFullyQualified(info.FileName) || !string.IsNullOrEmpty(info.Arguments))
            throw new ArgumentException("Desktop Worker launch requires an absolute executable, ArgumentList and three redirected streams.");

        AnonymousPipeServerStream? input = null, output = null, error = null;
        Process? process = null;
        StreamWriter? writer = null;
        StreamReader? reader = null, errors = null;
        IntPtr attributes = IntPtr.Zero, policy = IntPtr.Zero, handles = IntPtr.Zero, environment = IntPtr.Zero;
        bool initialized = false, transferred = false, createdProcess = false;
        ProcessInformation created = default;
        try
        {
            input = new(PipeDirection.Out, HandleInheritability.Inheritable);
            output = new(PipeDirection.In, HandleInheritability.Inheritable);
            error = new(PipeDirection.In, HandleInheritability.Inheritable);
            // Explicit HANDLE_LIST prevents unrelated inheritable handles from leaking.
            var childHandles = new[] { input.ClientSafePipeHandle.DangerousGetHandle(), output.ClientSafePipeHandle.DangerousGetHandle(), error.ClientSafePipeHandle.DangerousGetHandle() };
            handles = Marshal.AllocHGlobal(IntPtr.Size * childHandles.Length);
            Marshal.Copy(childHandles, 0, handles, childHandles.Length);
            policy = Marshal.AllocHGlobal(sizeof(int));
            Marshal.WriteInt32(policy, 1); // PROCESS_CREATION_DESKTOP_APP_BREAKAWAY_ENABLE_PROCESS_TREE
            IntPtr size = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 2, 0, ref size);
            attributes = Marshal.AllocHGlobal(size);
            Check(InitializeProcThreadAttributeList(attributes, 2, 0, ref size));
            initialized = true;
            Check(UpdateProcThreadAttribute(attributes, 0, new IntPtr(0x20012), policy, new IntPtr(sizeof(int)), IntPtr.Zero, IntPtr.Zero));
            Check(UpdateProcThreadAttribute(attributes, 0, new IntPtr(0x20002), handles, new IntPtr(IntPtr.Size * childHandles.Length), IntPtr.Zero, IntPtr.Zero));

            var block = string.Join("\0", info.Environment.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                .Where(p => p.Value != null).Select(p => p.Key + "=" + p.Value)) + "\0\0";
            environment = Marshal.StringToHGlobalUni(block);
            var startup = new StartupInfoEx { Attributes = attributes };
            startup.Info.Size = Marshal.SizeOf<StartupInfoEx>();
            startup.Info.Flags = 0x100; // STARTF_USESTDHANDLES
            startup.Info.Input = childHandles[0]; startup.Info.Output = childHandles[1]; startup.Info.Error = childHandles[2];
            var command = new StringBuilder(QuoteArgument(info.FileName));
            foreach (var argument in info.ArgumentList) command.Append(' ').Append(QuoteArgument(argument));
            // Suspended creation lets us hold the exact process handle before any
            // Worker/Portal code runs; PID reuse cannot redirect cleanup or probes.
            Check(CreateProcess(info.FileName, command, IntPtr.Zero, IntPtr.Zero, true,
                0x00080000 | 0x08000000 | 0x00000400 | 0x00000004, environment, info.WorkingDirectory, ref startup, out created));
            createdProcess = true;
            uint packageLength = 0;
            var packageStatus = GetPackageFullName(created.Process, ref packageLength, null);
            if (packageStatus != 15700)
                throw new InvalidOperationException("The Worker still has a desktop package identity; TIA native dependency loading is not safe to assume.");
            process = Process.GetProcessById((int)created.Pid);
            _ = process.Handle;
            input.DisposeLocalCopyOfClientHandle(); output.DisposeLocalCopyOfClientHandle(); error.DisposeLocalCopyOfClientHandle();
            writer = new StreamWriter(input, new UTF8Encoding(false)) { AutoFlush = true };
            reader = new StreamReader(output, info.StandardOutputEncoding ?? Encoding.UTF8);
            errors = new StreamReader(error, info.StandardErrorEncoding ?? Encoding.UTF8);
            if (ResumeThread(created.Thread) == uint.MaxValue) throw new Win32Exception(Marshal.GetLastWin32Error());
            transferred = true;
            return new(process, writer, reader, errors);
        }
        finally
        {
            if (!transferred)
            {
                if (createdProcess) { TerminateProcess(created.Process, 2); WaitForSingleObject(created.Process, 5000); }
                writer?.Dispose(); reader?.Dispose(); errors?.Dispose();
                input?.Dispose(); output?.Dispose(); error?.Dispose(); process?.Dispose();
            }
            if (created.Thread != IntPtr.Zero) CloseHandle(created.Thread);
            if (created.Process != IntPtr.Zero) CloseHandle(created.Process);
            if (initialized) DeleteProcThreadAttributeList(attributes);
            if (attributes != IntPtr.Zero) Marshal.FreeHGlobal(attributes);
            if (policy != IntPtr.Zero) Marshal.FreeHGlobal(policy);
            if (handles != IntPtr.Zero) Marshal.FreeHGlobal(handles);
            if (environment != IntPtr.Zero) Marshal.FreeHGlobal(environment);
        }
    }

    public bool WaitForExit(int milliseconds) => Process.WaitForExit(milliseconds);
    public void Kill(bool entireProcessTree) => Process.Kill(entireProcessTree);
    public void Dispose()
    {
        StandardInput.Dispose(); StandardOutput.Dispose(); StandardError.Dispose(); Process.Dispose();
    }
    private static void Check(bool success) { if (!success) throw new Win32Exception(Marshal.GetLastWin32Error()); }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size; public string? Reserved, Desktop, Title;
        public uint X, Y, Width, Height, CharsX, CharsY, Fill, Flags;
        public ushort Show, ReservedSize; public IntPtr ReservedBytes, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfoEx { public StartupInfo Info; public IntPtr Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { public IntPtr Process, Thread; public uint Pid, Tid; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetCurrentPackageFullName(ref uint length, StringBuilder? name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetPackageFullName(IntPtr process, ref uint length, StringBuilder? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateProcess(string executable, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfoEx startup, out ProcessInformation process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll")] private static extern bool TerminateProcess(IntPtr process, uint code);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr process, uint milliseconds);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
