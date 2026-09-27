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
                else if (args[1] == "open-copy" && (args.Length == 3 ||
                    (args[0] == "snapshot" && args.Length == 5 && args[3] == "--block-export-dir")))
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
                        Console.WriteLine("tiaBuild=" + (info.TiaBuild ?? "null"));
                        Console.WriteLine("projectVersion=" + (info.ProjectVersion ?? "null"));
                        Console.WriteLine("sourceKind=" + info.SourceKind);
                        Console.WriteLine("processId=" + (info.ProcessId?.ToString() ?? "null"));
                    }
                    else
                    {
                        var options = args.Length == 5 ? new SnapshotCollectionOptions
                            { BlockExportDirectory = args[4] } : null;
                        Console.WriteLine(SnapshotV1Json.Serialize(session.ReadSnapshot(options)));
                    }
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
                File.Exists(Path.Combine(api, "Siemens.Engineering.Step7.dll")) &&
                File.Exists(Path.Combine(api, "Siemens.Engineering.WinCC.dll"));
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
            var sample = new SnapshotV1 {
                Project = new SnapshotProject { Name = "Demo", SourceKind = "offline-copy" },
                Tia = new SnapshotTia { Version = "V21" },
                Capture = new SnapshotCapture { Status = "complete", Mode = "offline-copy",
                    CapturedAtUtc = "2026-09-27T00:00:00Z" }
            };
            var deviceId = SnapshotNormalization.MakeId("device", new[] { "PLC_1" });
            var plcId = SnapshotNormalization.MakeId("plc", new[] { "PLC_1", "CPU", "PLC_1" });
            sample.Devices.Add(new SnapshotDevice { Id = deviceId, PlcId = plcId,
                Name = "PLC_1", Type = "CPU", EngineeringPath = "PLC_1" });
            var plc = new SnapshotPlc { Id = plcId, Name = "PLC_1", DeviceId = deviceId };
            plc.Blocks.Add(new SnapshotBlock { Id = "block:PLC_1/Program%20blocks/Main",
                ScopePath = "Program%20blocks", Name = "Main", Kind = "OB",
                Language = "LAD", Protection = "none" });
            plc.Tags.Add(new SnapshotTag { Id = "tag:PLC_1/Start", ScopePath = "PLC%20tags/Default",
                Name = "Start", DataType = "Bool", Address = SnapshotAddressParser.Parse("%I0.0"),
                Comment = new SnapshotComment { Status = "missing" } });
            sample.Plcs.Add(plc);
            sample.Project.ContentId = SnapshotNormalization.ComputeContentId(sample);
            var firstContentId = sample.Project.ContentId;
            sample.Capture.CapturedAtUtc = "2026-09-28T00:00:00Z";
            if (SnapshotNormalization.ComputeContentId(sample) != firstContentId)
                throw new InvalidOperationException("Capture timestamp affected normalized content ID.");
            if (SnapshotAddressParser.Parse("%QW2").BitWidth != 16 ||
                SnapshotAddressParser.Parse("%DB1.DBX0.0").ParseStatus != "unsupported" ||
                SnapshotAddressParser.Parse(null).ParseStatus != "missing")
                throw new InvalidOperationException("Address parsing failed.");
            var json = SnapshotV1Json.Serialize(sample);
            if (!string.Equals(json, SnapshotV1Json.Serialize(sample), StringComparison.Ordinal))
                throw new InvalidOperationException("Snapshot serialization is unstable.");
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                var parsed = (SnapshotV1)new DataContractJsonSerializer(typeof(SnapshotV1)).ReadObject(stream);
                if (parsed.SchemaVersion != "1.0" || parsed.Project.Name != "Demo" ||
                    parsed.Devices.Count != 1 || parsed.Plcs.Count != 1 ||
                    parsed.Plcs[0].Blocks.Count != 1 || parsed.Plcs[0].Tags[0].Address.Raw != "%I0.0" ||
                    parsed.Plcs[0].Compile.Mode != "not-observed" ||
                    parsed.Project.ContentId != firstContentId)
                    throw new InvalidOperationException("Snapshot v1 JSON round trip failed.");
            }
            sample.Plcs[0].Tags[0].Address = SnapshotAddressParser.Parse("%I0.1");
            if (SnapshotNormalization.ComputeContentId(sample) == firstContentId)
                throw new InvalidOperationException("Engineering content change did not change content ID.");

            var xmlPath = Path.Combine(Path.GetTempPath(), "tia-guard-content-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                var firstXml = "<Document><DocumentInfo><Created>2026-09-27T00:00:00Z</Created></DocumentInfo><Block>Main</Block></Document>";
                File.WriteAllText(xmlPath, firstXml, new UTF8Encoding(false));
                var firstDigest = SimaticMlContentHasher.ComputeSha256(xmlPath);
                File.WriteAllText(xmlPath, firstXml.Replace("2026-09-27", "2026-09-28"), new UTF8Encoding(false));
                if (SimaticMlContentHasher.ComputeSha256(xmlPath) != firstDigest)
                    throw new InvalidOperationException("SimaticML Created changed the normalized content digest.");
                File.WriteAllText(xmlPath, firstXml.Replace("Main", "Other"), new UTF8Encoding(false));
                var changedDigest = SimaticMlContentHasher.ComputeSha256(xmlPath);
                if (changedDigest == firstDigest)
                    throw new InvalidOperationException("Changed block content did not change the content digest.");

                var export = sample.Plcs[0].Blocks[0].Export;
                export.Status = "exported";
                export.Format = "SimaticML";
                export.Sha256 = new string('a', 64);
                export.ContentSha256 = firstDigest;
                export.ContentNormalizationVersion = SimaticMlContentHasher.NormalizationVersion;
                var exportedContentId = SnapshotNormalization.ComputeContentId(sample);
                export.Sha256 = new string('b', 64);
                if (SnapshotNormalization.ComputeContentId(sample) != exportedContentId)
                    throw new InvalidOperationException("Raw export SHA changed normalized content ID.");
                export.ContentSha256 = changedDigest;
                if (SnapshotNormalization.ComputeContentId(sample) == exportedContentId)
                    throw new InvalidOperationException("Changed block content did not change content ID.");
                export.ContentSha256 = null;
                try
                {
                    SnapshotNormalization.ComputeContentId(sample);
                    throw new InvalidOperationException("Missing block content digest was accepted.");
                }
                catch (InvalidOperationException error) when (error.Message == "Exported block lacks a normalized content digest.")
                {
                }
            }
            finally
            {
                if (File.Exists(xmlPath)) File.Delete(xmlPath);
            }
            Console.WriteLine(json);
            return 0;
        }

        private static int Usage()
        {
            Console.Error.WriteLine("Usage: probe | self-test | info|snapshot attach [pid] | info|snapshot open-copy <project.ap21> [--block-export-dir <outside-project-dir>]");
            return 2;
        }
    }
}
