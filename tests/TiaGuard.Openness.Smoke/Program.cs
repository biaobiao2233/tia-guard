using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using TiaGuard.Openness;

namespace TiaGuard.Openness.Smoke
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length == 1 && args[0] == "probe") return Probe();
                if (args.Length == 1 && args[0] == "self-test") return SelfTest();
                if (args.Length < 2 || (args[0] != "info" && args[0] != "snapshot"))
                    return Usage();

                TiaProjectSession session;
                if (args[1] == "attach" && args.Length <= 3)
                {
                    int pid = 0;
                    if (args.Length == 3 && !int.TryParse(args[2], out pid)) return Usage();
                    session = TiaProjectSession.Attach(args.Length == 3 ? (int?)pid : null);
                }
                else if (args[1] == "open-copy" && args.Length == 3)
                    session = TiaProjectSession.OpenOfflineCopy(args[2]);
                else
                    return Usage();

                using (session)
                {
                    if (args[0] == "info")
                    {
                        var info = session.ReadProjectInfo();
                        Console.WriteLine("name=" + info.Name);
                        Console.WriteLine("path=" + info.Path);
                        Console.WriteLine("tiaVersion=" + info.TiaVersion);
                        Console.WriteLine("processId=" + (info.ProcessId?.ToString() ?? "null"));
                    }
                    else
                        Console.WriteLine(SnapshotV1Json.Serialize(session.ReadSnapshot()));
                }
                return 0;
            }
            catch (OpennessAccessException error)
            {
                Console.Error.WriteLine("BLOCKED: " + error.Message);
                return 3;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("ERROR: " + error.GetType().Name + ": " + error.Message);
                return 2;
            }
        }

        private static int Probe()
        {
            var api = @"C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48";
            var filesPresent = File.Exists(Path.Combine(api, "Siemens.Engineering.Base.dll")) &&
                File.Exists(Path.Combine(api, "Siemens.Engineering.Step7.dll"));
            var groupEffective = OpennessAccess.HasEffectiveGroupMembership();
            var runningPortals = Process.GetProcessesByName("Siemens.Automation.Portal").Length;
            Console.WriteLine("V21 PublicAPI files present: " + filesPresent);
            Console.WriteLine("Openness group effective in current token: " + groupEffective);
            Console.WriteLine("TIA Portal processes visible: " + runningPortals);
            if (filesPresent)
            {
                OpennessRuntime.Initialize();
                var contract = Path.GetFullPath(Path.Combine(api,
                    @"..\..\..\Bin\PublicAPI\Siemens.Engineering.Contract.dll"));
                Assembly.Load(AssemblyName.GetAssemblyName(contract));
                var getProcesses = Assembly.LoadFrom(Path.Combine(api, "Siemens.Engineering.Base.dll"))
                    .GetType("Siemens.Engineering.TiaPortal", throwOnError: true)
                    .GetMethod("GetProcesses");
                if (getProcesses == null) throw new MissingMethodException("TiaPortal.GetProcesses");
                Console.WriteLine("V21 Siemens assemblies resolved: True");
            }
            return !filesPresent ? 2 : !groupEffective ? 3 : 0;
        }

        private static int SelfTest()
        {
            var sample = new SnapshotV1
            {
                Project = new SnapshotProject { Name = "Demo", Path = null },
                Tia = new SnapshotTia { Version = "V21", ProcessId = null }
            };
            sample.Devices.Add(new SnapshotDevice { Name = "PLC_1", Type = "CPU" });
            var plc = new SnapshotPlc { Name = "PLC_1" };
            plc.Blocks.Add(new SnapshotBlock { Name = "Main", Kind = "OB", Language = "LAD" });
            plc.Tags.Add(new SnapshotTag { Name = "Start", DataType = "Bool", Address = "%I0.0" });
            sample.Plcs.Add(plc);
            var json = SnapshotV1Json.Serialize(sample);
            if (!string.Equals(json, SnapshotV1Json.Serialize(sample), StringComparison.Ordinal))
                throw new InvalidOperationException("Snapshot serialization is unstable.");
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                var parsed = (SnapshotV1)new DataContractJsonSerializer(typeof(SnapshotV1)).ReadObject(stream);
                if (parsed.SchemaVersion != "1.0" || parsed.Project.Name != "Demo" ||
                    parsed.Devices.Count != 1 || parsed.Plcs.Count != 1 ||
                    parsed.Plcs[0].Blocks.Count != 1 || parsed.Plcs[0].Tags[0].Address != "%I0.0" ||
                    parsed.Plcs[0].Compile != null)
                    throw new InvalidOperationException("Snapshot v1 JSON round trip failed.");
            }
            Console.WriteLine(json);
            return 0;
        }

        private static int Usage()
        {
            Console.Error.WriteLine("Usage: probe | self-test | info|snapshot attach [pid] | info|snapshot open-copy <project.ap21>");
            return 2;
        }
    }
}
