using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using TiaGuard.Bridge.Host;

namespace TiaGuard.Bridge.Worker
{
    // Read-only process evidence. Never sends commands to, or kills, a runtime.
    internal sealed class PlcsimOwnedRuntime
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Entry
        {
            public uint Size, Usage, Pid; public IntPtr Heap; public uint Module, Threads, Parent;
            public int Priority; public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
        }
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32First(IntPtr handle, ref Entry entry);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32Next(IntPtr handle, ref Entry entry);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        private readonly Dictionary<int, long> _verified = new Dictionary<int, long>();

        public int[] Observe(int applicationId, long applicationStartTicks, string executablePath)
        {
            var parents = new Dictionary<int, int>();
            var snapshot = CreateToolhelp32Snapshot(2, 0);
            if (snapshot == new IntPtr(-1)) throw new InvalidOperationException("Cannot read simulator process ancestry.");
            try {
                var entry = new Entry { Size = (uint)Marshal.SizeOf(typeof(Entry)) };
                if (!Process32First(snapshot, ref entry)) throw new InvalidOperationException("Simulator process snapshot is unavailable.");
                do { parents[(int)entry.Pid] = (int)entry.Parent; } while (Process32Next(snapshot, ref entry));
            } finally { CloseHandle(snapshot); }
            var bin = Path.Combine(Path.GetDirectoryName(executablePath), "resources", "bin");
            var ids = new List<int>();
            foreach (var cpu in Process.GetProcessesByName("Siemens.Simatic.PlcSim.VplcHost")) {
                using (cpu) {
                    int engineId, rootId;
                    if (!parents.TryGetValue(cpu.Id, out engineId) || !parents.TryGetValue(engineId, out rootId) || rootId != applicationId) continue;
                    try {
                        using (var engine = Process.GetProcessById(engineId)) {
                            var start = cpu.StartTime.ToUniversalTime().Ticks;
                            if (cpu.HasExited || engine.HasExited || engine.ProcessName != "plcsim" ||
                                engine.StartTime.ToUniversalTime().Ticks < applicationStartTicks || start < engine.StartTime.ToUniversalTime().Ticks ||
                                !string.Equals(engine.MainModule.FileName, Path.Combine(bin, "plcsim.exe"), StringComparison.OrdinalIgnoreCase) ||
                                !string.Equals(cpu.MainModule.FileName, Path.Combine(bin, "Siemens.Simatic.PlcSim.VplcHost.exe"), StringComparison.OrdinalIgnoreCase))
                                throw new InvalidOperationException("Owned virtual CPU process identity changed.");
                            long verified;
                            if (!_verified.TryGetValue(cpu.Id, out verified) || verified != start) {
                                if (!WindowsSignedApplication.IsSiemensSigned(cpu.MainModule.FileName))
                                    throw new InvalidOperationException("The owned virtual CPU executable is not Siemens signed.");
                                _verified[cpu.Id] = start;
                            }
                            ids.Add(cpu.Id);
                        }
                    } catch (ArgumentException) { /* Process completed during observation; read again. */ }
                      catch (InvalidOperationException) when (cpu.HasExited) { }
                }
            }
            return ids.OrderBy(id => id).ToArray();
        }
    }
}
