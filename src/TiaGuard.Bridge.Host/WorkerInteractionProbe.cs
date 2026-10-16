using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace TiaGuard.Bridge.Host;

// Observation only: never acknowledges a dialog or changes Openness permissions.
public static class WorkerInteractionProbe
{
    public static bool IsOpennessAccessPrompt(string title) =>
        title.Contains("Openness", StringComparison.OrdinalIgnoreCase) && title.Contains("0033:000666", StringComparison.Ordinal);
    public static object? Read(Process? worker)
    {
        if (!OperatingSystem.IsWindows() || worker == null) return null;
        try
        {
            if (worker.HasExited) return null;
            var parentIds = Parents();
            object? found = null;
            EnumWindows((window, _) => {
                GetWindowThreadProcessId(window, out var pid);
                if (!IsWindowVisible(window) || !parentIds.TryGetValue((int)pid, out var parent) || parent != worker.Id) return true;
                using var portal = Process.GetProcessById((int)pid);
                if (portal.ProcessName != "Siemens.Automation.Portal" || portal.StartTime.ToUniversalTime() < worker.StartTime.ToUniversalTime()) return true;
                var title = new StringBuilder(512); GetWindowText(window, title, title.Capacity);
                if (IsOpennessAccessPrompt(title.ToString()))
                {
                    found = new { state = "waiting-for-openness-access", userActionRequired = true,
                        portalProcessId = portal.Id, dialogCode = "0033:000666", observedAtUtc = DateTime.UtcNow,
                        message = "TIA is waiting for the user to confirm this application's Openness access. The Bridge will not acknowledge it automatically." };
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or ArgumentException)
        {
            return new { state = "interaction-observation-unavailable", userActionRequired = (bool?)null, error = error.GetType().Name };
        }
    }
    private static Dictionary<int, int> Parents()
    {
        var handle = CreateToolhelp32Snapshot(2, 0);
        if (handle == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var entry = new Entry { Size = (uint)Marshal.SizeOf<Entry>() }; var result = new Dictionary<int, int>();
            if (!Process32First(handle, ref entry)) throw new Win32Exception(Marshal.GetLastWin32Error());
            do { result[(int)entry.Pid] = (int)entry.Parent; } while (Process32Next(handle, ref entry));
            return result;
        }
        finally { CloseHandle(handle); }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Entry
    {
        public uint Size, Usage, Pid; public IntPtr Heap; public uint Module, Threads, Parent;
        public int Priority; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
    }
    private delegate bool Callback(IntPtr window, IntPtr data);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32First(IntPtr h, ref Entry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32Next(IntPtr h, ref Entry entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("user32.dll")] private static extern bool EnumWindows(Callback callback, IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder text, int size);
}
