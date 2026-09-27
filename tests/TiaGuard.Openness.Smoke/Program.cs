using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
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
                if (args.Length < 2 || (args[0] != "info" && args[0] != "snapshot" && args[0] != "roundtrip"))
                    return Usage();

                TiaProjectSession session;
                string roundTripOutput = null;
                if (args[1] == "attach")
                {
                    if (args[0] == "roundtrip" || args.Length > 3) return Usage();
                    int pid = 0;
                    if (args.Length == 3 && !int.TryParse(args[2], out pid)) return Usage();
                    session = TiaProjectSession.Attach(args.Length == 3 ? (int?)pid : null);
                }
                else if (args[1] == "open-copy")
                {
                    if (args[0] == "roundtrip")
                    {
                        if (args.Length != 4) return Usage();
                        roundTripOutput = args[3];
                        Console.Error.WriteLine("stage=session-open");
                        session = TiaProjectSession.OpenOfflineCopy(args[2]);
                        Console.Error.WriteLine("stage=session-opened");
                    }
                    else if (args.Length == 3 ||
                        (args[0] == "snapshot" && args.Length == 5 && args[3] == "--block-export-dir"))
                        session = TiaProjectSession.OpenOfflineCopy(args[2]);
                    else
                        return Usage();
                }
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
                    else if (args[0] == "roundtrip")
                    {
                        Console.WriteLine(RoundTripJson.Serialize(session.ExportRoundTripSource(
                            roundTripOutput,
                            stage => Console.Error.WriteLine("stage=" + stage))));
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
                Name = "PLC_1", Type = "System:Device.S71200", EngineeringPath = "PLC_1" });
            var plc = new SnapshotPlc { Id = plcId, Name = "PLC_1", DeviceId = deviceId };
            var blockArtifact = "blocks/self-test.xml";
            var blockXml = "<Document><DocumentInfo><Created>2026-09-27T00:00:00Z</Created><ExportSetting>WithDefaults</ExportSetting></DocumentInfo><SW.Blocks.OB ID=\"0\"><AttributeList><Name>Main</Name></AttributeList></SW.Blocks.OB></Document>\n";
            var selfTestRoot = Path.Combine(Path.GetTempPath(), "TiaGuard.RoundTrip.SelfTest",
                Guid.NewGuid().ToString("N"));
            var blockRoot = Path.Combine(selfTestRoot, "exports");
            Directory.CreateDirectory(Path.Combine(blockRoot, "blocks"));
            File.WriteAllText(Path.Combine(blockRoot, "blocks", "self-test.xml"), blockXml, new UTF8Encoding(false));
            var blockHash = Sha256(Path.Combine(blockRoot, "blocks", "self-test.xml"));
            plc.Blocks.Add(new SnapshotBlock { Id = "block:PLC_1/Program%20blocks/Main",
                ScopePath = "Program%20blocks", Name = "Main", Kind = "OB", Number = 1,
                Language = "LAD", Protection = "none", Export = new SnapshotExport
                {
                    Status = "exported", Format = "SimaticML", Artifact = blockArtifact, Sha256 = blockHash
                } });
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

            try
            {
                var hints = new RoundTripExtractionHints
                {
                    TagTableScanComplete = true,
                    Hardware = new RoundTripHardwareBuildIdentity
                    {
                        DeviceName = "PLC_1",
                        CpuItemName = "CPU_1",
                        CreateTypeIdentifier = "OrderNumber:6ES7 212-1AE40-0XB0/V4.6",
                        OrderNumber = "6ES7 212-1AE40-0XB0",
                        Firmware = "V4.6",
                        State = RoundTripCapabilityStates.SupportedRoundTrip
                    },
                    TagTables = new List<RoundTripTagTableHint>
                    {
                        new RoundTripTagTableHint
                        {
                            PlcName = "PLC_1",
                            Name = "Default",
                            ScopePath = "PLC_1/CPU/PLC_1/PLC%20tags/Default"
                        },
                        new RoundTripTagTableHint
                        {
                            PlcName = "PLC_1",
                            Name = "Empty",
                            ScopePath = "PLC_1/CPU/PLC_1/PLC%20tags/Empty"
                        }
                    }
                };
                sample.Plcs[0].Tags[0].ScopePath = hints.TagTables[0].ScopePath;
                var firstOutput = Path.Combine(selfTestRoot, "first");
                var secondOutput = Path.Combine(selfTestRoot, "second");
                var firstManifest = RoundTripSourceMaterializer.Write(sample, hints, blockRoot, firstOutput);
                var secondRawBlockXml = blockXml.Replace(
                    "2026-09-27T00:00:00Z", "2026-09-28T00:00:00Z");
                File.WriteAllText(Path.Combine(blockRoot, "blocks", "self-test.xml"),
                    secondRawBlockXml, new UTF8Encoding(false));
                plc.Blocks[0].Export.Sha256 = Sha256(Path.Combine(blockRoot, "blocks", "self-test.xml"));
                var secondManifest = RoundTripSourceMaterializer.Write(sample, hints, blockRoot, secondOutput);
                if (!firstManifest.RoundTripReady || !secondManifest.RoundTripReady)
                    throw new InvalidOperationException("Round-trip source self-test did not become ready.");
                AssertTreesEqual(firstOutput, secondOutput);
                var canonicalSource = Directory.GetFiles(
                    firstOutput, "source.xml", SearchOption.AllDirectories).Single();
                var canonicalSourceText = File.ReadAllText(canonicalSource);
                if (!canonicalSourceText.Contains("<Created>1970-01-01T00:00:00Z</Created>") ||
                    canonicalSourceText.Contains("2026-09-27T00:00:00Z"))
                    throw new InvalidOperationException("SimaticML volatile Created timestamp was not normalized.");
                if (!string.Equals(canonicalSourceText,
                        blockXml.Replace("2026-09-27T00:00:00Z", "1970-01-01T00:00:00Z"),
                        StringComparison.Ordinal))
                    throw new InvalidOperationException("SimaticML normalization modified content outside Created.");
                var blockDescriptorText = File.ReadAllText(Directory.GetFiles(
                    firstOutput, "block.json", SearchOption.AllDirectories).Single());
                if (!blockDescriptorText.Contains("\"normalizationVersion\":\"simaticml-v1\""))
                    throw new InvalidOperationException("SimaticML normalization version is missing.");
                if (!blockDescriptorText.Contains("\"sha256\":\"" + Sha256(canonicalSource) + "\""))
                    throw new InvalidOperationException("Block descriptor hash does not match canonical source.xml.");
                if (!File.Exists(Path.Combine(firstOutput, "tia-guard.json")) ||
                    Directory.GetFiles(firstOutput, "source.xml", SearchOption.AllDirectories).Length != 1 ||
                    Directory.GetFiles(firstOutput, "tag-table-*.json", SearchOption.AllDirectories).Length != 2)
                    throw new InvalidOperationException("Round-trip source tree is incomplete or dropped an empty tag table.");

                var incompleteTagHints = new RoundTripExtractionHints
                {
                    Hardware = hints.Hardware,
                    TagTableScanFailures = new List<RoundTripTagTableScanFailure>
                    {
                        new RoundTripTagTableScanFailure
                        {
                            PlcName = "PLC_1", ScopePath = "PLC_1/CPU/PLC_1/PLC%20tags",
                            FailureType = "SyntheticEnumerationFailure"
                        }
                    }
                };
                var incompleteTagScan = RoundTripSourceMaterializer.Write(sample, incompleteTagHints, blockRoot,
                    Path.Combine(selfTestRoot, "incomplete-tag-scan"));
                if (incompleteTagScan.RoundTripReady || !incompleteTagScan.Capabilities.Any(value =>
                        value.ObjectKind == "tag-table-scan" && value.State == RoundTripCapabilityStates.Failed) ||
                    !incompleteTagScan.Diagnostics.Any(value => value.Code == "TAG_TABLE_SCAN_FAILED" &&
                        value.ObjectRef == "PLC_1/CPU/PLC_1/PLC%20tags"))
                    throw new InvalidOperationException("A failed empty-table scan was silently treated as empty.");

                sample.Plcs[0].Tags.Clear();
                var confirmedEmptyHints = new RoundTripExtractionHints
                {
                    Hardware = hints.Hardware, TagTableScanComplete = true
                };
                var confirmedEmpty = RoundTripSourceMaterializer.Write(sample, confirmedEmptyHints, blockRoot,
                    Path.Combine(selfTestRoot, "confirmed-empty-tags"));
                if (!confirmedEmpty.RoundTripReady || confirmedEmpty.Capabilities.Any(value =>
                        value.ObjectKind == "tag-table" || value.ObjectKind == "tag-table-scan"))
                    throw new InvalidOperationException("A confirmed empty tag-table scan was treated as failure.");
                sample.Plcs[0].Tags.Add(new SnapshotTag { Id = "tag:PLC_1/Start",
                    ScopePath = hints.TagTables[0].ScopePath, Name = "Start", DataType = "Bool",
                    Address = SnapshotAddressParser.Parse("%I0.0"),
                    Comment = new SnapshotComment { Status = "missing" } });

                sample.Diagnostics.Add(new SnapshotDiagnostic { Code = "TAG_NAME_READ_FAILED",
                    Severity = "warning", ObjectId = "tag:PLC_1/Start",
                    Message = "Sanitized source diagnostic." });
                sample.Capture.Status = "partial";
                var partialSource = RoundTripSourceMaterializer.Write(sample, hints, blockRoot,
                    Path.Combine(selfTestRoot, "partial-source"));
                if (partialSource.RoundTripReady || !partialSource.Diagnostics.Any(value =>
                        value.Code == "SNAPSHOT_TAG_NAME_READ_FAILED" && value.ObjectRef == "tag:PLC_1/Start"))
                    throw new InvalidOperationException("Source diagnostic lost its blocking object and code.");
                sample.Diagnostics.Clear();
                sample.Capture.Status = "complete";

                sample.Plcs[0].Tags[0].Comment.Status = "multiple";
                var multipleComments = RoundTripSourceMaterializer.Write(sample, hints, blockRoot,
                    Path.Combine(selfTestRoot, "multiple-comments"));
                if (multipleComments.RoundTripReady || !multipleComments.Capabilities.Any(value =>
                        value.ObjectKind == "tag" && value.State == RoundTripCapabilityStates.Unsupported))
                    throw new InvalidOperationException("Multiple tag comment translations were silently accepted.");
                sample.Plcs[0].Tags[0].Comment.Status = "missing";

                foreach (var canonicalJson in Directory.GetFiles(firstOutput, "*.json", SearchOption.AllDirectories))
                {
                    var text = File.ReadAllText(canonicalJson);
                    if (text.Contains(selfTestRoot) || text.Contains("2026-09-28T00:00:00Z"))
                        throw new InvalidOperationException("Operational path/timestamp leaked into canonical source.");
                }

                sample.Tia.Version = "V20";
                if (RoundTripSourceMaterializer.Write(sample, hints, blockRoot,
                        Path.Combine(selfTestRoot, "wrong-version")).RoundTripReady)
                    throw new InvalidOperationException("Non-V21 source was marked round-trip ready.");
                sample.Tia.Version = "V21";

                sample.Devices[0].Type = "System:Device.S71500";
                if (RoundTripSourceMaterializer.Write(sample, hints, blockRoot,
                        Path.Combine(selfTestRoot, "wrong-station")).RoundTripReady)
                    throw new InvalidOperationException("Non-S7-1200 station was marked round-trip ready.");
                sample.Devices[0].Type = "System:Device.S71200";

                plc.Blocks[0].Name = "RenamedOB1";
                var renamedBlockOutput = Path.Combine(selfTestRoot, "wrong-block-name");
                var renamedBlock = RoundTripSourceMaterializer.Write(sample, hints, blockRoot, renamedBlockOutput);
                if (renamedBlock.RoundTripReady || !renamedBlock.Capabilities.Any(value =>
                        value.ObjectKind == "block" && value.State == RoundTripCapabilityStates.ExportOnly) ||
                    Directory.GetFiles(renamedBlockOutput, "source.xml", SearchOption.AllDirectories).Length != 1)
                    throw new InvalidOperationException("Renamed OB1 was marked round-trip ready.");

                plc.Blocks[0].Export.Artifact = "blocks/missing.xml";
                var missingBlockOutput = Path.Combine(selfTestRoot, "missing-export-only-artifact");
                var missingBlock = RoundTripSourceMaterializer.Write(sample, hints, blockRoot, missingBlockOutput);
                if (missingBlock.RoundTripReady || !missingBlock.Capabilities.Any(value =>
                        value.ObjectKind == "block" && value.State == RoundTripCapabilityStates.Failed) ||
                    Directory.GetFiles(missingBlockOutput, "source.xml", SearchOption.AllDirectories).Length != 0)
                    throw new InvalidOperationException("Missing export-only artifact was falsely claimed.");
                plc.Blocks[0].Export.Artifact = blockArtifact;
                plc.Blocks[0].Name = "Main";

                var malformedCreatedXml = blockXml.Replace(
                    "<Created>2026-09-27T00:00:00Z</Created>",
                    "<Created Format=\"unexpected\">2026-09-27T00:00:00Z</Created>")
                    .Replace("</Document>",
                        "<Other><DocumentInfo><Created>2026-09-28T00:00:00Z</Created></DocumentInfo></Other></Document>");
                File.WriteAllText(Path.Combine(blockRoot, "blocks", "self-test.xml"),
                    malformedCreatedXml, new UTF8Encoding(false));
                plc.Blocks[0].Export.Sha256 = Sha256(Path.Combine(blockRoot, "blocks", "self-test.xml"));
                var wrongCreated = RoundTripSourceMaterializer.Write(sample, hints, blockRoot,
                    Path.Combine(selfTestRoot, "wrong-created-path"));
                if (wrongCreated.RoundTripReady || !wrongCreated.Capabilities.Any(value =>
                        value.ObjectKind == "block" && value.Reason != null &&
                        value.Reason.Contains("Unexpected root Created shape")))
                    throw new InvalidOperationException("Unexpected root Created shape was marked round-trip ready.");

                var nestedCreatedXml = blockXml
                    .Replace("<DocumentInfo><Created>", "<DocumentInfo><!--before-created--><Created>")
                    .Replace("</Document>",
                        "<Other><DocumentInfo><Created>2026-09-28T00:00:00Z</Created></DocumentInfo></Other></Document>");
                File.WriteAllText(Path.Combine(blockRoot, "blocks", "self-test.xml"),
                    nestedCreatedXml, new UTF8Encoding(false));
                plc.Blocks[0].Export.Sha256 = Sha256(Path.Combine(blockRoot, "blocks", "self-test.xml"));
                var nestedCreated = RoundTripSourceMaterializer.Write(sample, hints, blockRoot,
                    Path.Combine(selfTestRoot, "nested-created"));
                if (nestedCreated.RoundTripReady || !nestedCreated.Capabilities.Any(value =>
                        value.ObjectKind == "block" && value.State == RoundTripCapabilityStates.Failed &&
                        value.Reason != null && value.Reason.Contains("not the root Created element")))
                    throw new InvalidOperationException("Nested Created was normalized instead of the root Created.");

                File.WriteAllBytes(Path.Combine(blockRoot, "blocks", "self-test.xml"), new byte[] { 0xff });
                plc.Blocks[0].Export.Sha256 = Sha256(Path.Combine(blockRoot, "blocks", "self-test.xml"));
                var invalidUtf8 = RoundTripSourceMaterializer.Write(sample, hints, blockRoot,
                    Path.Combine(selfTestRoot, "invalid-utf8"));
                if (invalidUtf8.RoundTripReady || !invalidUtf8.Capabilities.Any(value =>
                        value.ObjectKind == "block" && value.Reason != null &&
                        value.Reason.Contains("Invalid SimaticML UTF-8")))
                    throw new InvalidOperationException("Corrupt UTF-8 was not reported as source corruption.");

                File.WriteAllText(Path.Combine(blockRoot, "blocks", "self-test.xml"),
                    "<Document><DocumentInfo><Created>broken", new UTF8Encoding(false));
                plc.Blocks[0].Export.Sha256 = Sha256(Path.Combine(blockRoot, "blocks", "self-test.xml"));
                var invalidXml = RoundTripSourceMaterializer.Write(sample, hints, blockRoot,
                    Path.Combine(selfTestRoot, "invalid-xml"));
                if (invalidXml.RoundTripReady || !invalidXml.Capabilities.Any(value =>
                        value.ObjectKind == "block" && value.Reason != null &&
                        value.Reason.Contains("Invalid SimaticML XML")))
                    throw new InvalidOperationException("Corrupt XML was not reported as source corruption.");

                var blockedOutput = Path.Combine(selfTestRoot, "blocked");
                var blockedHints = new RoundTripExtractionHints
                {
                    TagTableScanComplete = true,
                    Hardware = new RoundTripHardwareBuildIdentity
                    {
                        DeviceName = "PLC_1",
                        CpuItemName = "CPU_1",
                        State = RoundTripCapabilityStates.Failed,
                        Reason = "Synthetic missing TypeIdentifier."
                    },
                    TagTables = hints.TagTables
                };
                var blocked = RoundTripSourceMaterializer.Write(sample, blockedHints, blockRoot, blockedOutput);
                if (blocked.RoundTripReady ||
                    !blocked.Capabilities.Any(value => value.ObjectKind == "hardware" &&
                        value.State == RoundTripCapabilityStates.Failed))
                    throw new InvalidOperationException("Missing build-grade CPU identity did not fail closed.");
            }
            finally
            {
                if (Directory.Exists(selfTestRoot)) Directory.Delete(selfTestRoot, recursive: true);
            }

            Console.WriteLine(json);
            return 0;
        }

        private static string Sha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static void AssertTreesEqual(string left, string right)
        {
            var leftFiles = Directory.GetFiles(left, "*", SearchOption.AllDirectories)
                .Select(path => path.Substring(left.Length).TrimStart(Path.DirectorySeparatorChar)
                    .Replace(Path.DirectorySeparatorChar, '/'))
                .OrderBy(path => path, StringComparer.Ordinal).ToArray();
            var rightFiles = Directory.GetFiles(right, "*", SearchOption.AllDirectories)
                .Select(path => path.Substring(right.Length).TrimStart(Path.DirectorySeparatorChar)
                    .Replace(Path.DirectorySeparatorChar, '/'))
                .OrderBy(path => path, StringComparer.Ordinal).ToArray();
            if (!leftFiles.SequenceEqual(rightFiles, StringComparer.Ordinal))
                throw new InvalidOperationException("Round-trip source file sets are unstable.");
            foreach (var relative in leftFiles)
            {
                var leftBytes = File.ReadAllBytes(Path.Combine(left, relative.Replace('/', Path.DirectorySeparatorChar)));
                var rightBytes = File.ReadAllBytes(Path.Combine(right, relative.Replace('/', Path.DirectorySeparatorChar)));
                if (!leftBytes.SequenceEqual(rightBytes))
                    throw new InvalidOperationException("Round-trip source bytes are unstable: " + relative);
            }
        }

        private static int Usage()
        {
            Console.Error.WriteLine("Usage: probe | self-test | info|snapshot attach [pid] | info|snapshot open-copy <project.ap21> [--block-export-dir <outside-project-dir>] | roundtrip open-copy <project.ap21> <repo-dir>");
            return 2;
        }
    }
}
