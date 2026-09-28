using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using TiaGuard.Openness;

namespace TiaGuard.Contracts.Tests
{
    internal sealed class CanonicalFixture : IDisposable
    {
        internal readonly string OwnedRoot = Path.Combine(Path.GetTempPath(), "TiaGuard.ContractTests", Guid.NewGuid().ToString("N"));
        internal string Root => Path.Combine(OwnedRoot, "source");
        internal const string HardwarePath = "tia/hardware/station-a.json";
        internal const string PlcPath = "tia/plc/plc-a/plc.json";
        internal const string TablePath = "tia/plc/plc-a/tags/table-a.json";
        internal const string BlockPath = "tia/plc/plc-a/blocks/block-a/block.json";
        internal const string XmlPath = "tia/plc/plc-a/blocks/block-a/source.xml";
        internal string Xml = "<Document><DocumentInfo><Created>1970-01-01T00:00:00Z</Created></DocumentInfo><SW.Blocks.OB><AttributeList><Name>Main</Name><Number>1</Number><ProgrammingLanguage>LAD</ProgrammingLanguage></AttributeList></SW.Blocks.OB></Document>";
        internal readonly RoundTripManifestV1 Manifest = new RoundTripManifestV1
        {
            RoundTripReady = true, Project = new RoundTripProjectV1 { Name = "Demo" },
            Hardware = new List<string> { HardwarePath }, Plcs = new List<string> { PlcPath }
        };
        internal readonly RoundTripHardwareV1 Hardware = new RoundTripHardwareV1
        {
            Id = "station-a", Name = "Station", EngineeringPath = "Station", DeviceTypeIdentifier = "System:Device.S71200",
            CreateItemName = "PLC", CreateTypeIdentifier = RoundTripProfile.CpuTypeIdentifier, Capability = "supported-round-trip"
        };
        internal readonly RoundTripPlcV1 Plc = new RoundTripPlcV1
        {
            Id = "plc-a", Name = "PLC", DeviceId = "station-a", Capability = "supported-round-trip",
            TagTables = new List<string> { TablePath }, Blocks = new List<string> { BlockPath }
        };
        internal readonly RoundTripTagV1 Tag = new RoundTripTagV1
        {
            Id = "tag-a", Name = "Start", DataType = "Bool", Address = "%I0.0",
            CommentStatus = "missing", Capability = "supported-round-trip"
        };
        internal readonly RoundTripTagTableV1 Table = new RoundTripTagTableV1
        {
            Id = "table-a", Name = "Default", ScopePath = "Station/PLC/PLC/PLC%20tags/Default", Capability = "supported-round-trip"
        };
        internal readonly RoundTripBlockV1 Block = new RoundTripBlockV1
        {
            Id = "block-a", Name = "Main", ScopePath = "Station/PLC/PLC/Program%20blocks", Kind = "OB", Number = 1,
            Language = "LAD", Capability = "supported-round-trip", Source = new RoundTripArtifactV1 { Artifact = XmlPath }
        };

        internal CanonicalFixture()
        {
            Table.Tags.Add(Tag);
            foreach (var pair in new[] { "hardware:station-a", "plc:plc-a", "tag-table:table-a", "tag:tag-a", "block:block-a" })
            {
                var parts = pair.Split(':');
                Manifest.Capabilities.Add(new RoundTripCapabilityV1 { ObjectKind = parts[0], ObjectRef = parts[1], State = "supported-round-trip" });
            }
            Save();
        }
        internal string PathOf(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
        internal void Write<T>(string relative, T value)
        {
            var path = PathOf(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, RoundTripJson.Serialize(value), new UTF8Encoding(false));
        }
        internal void Save()
        {
            Block.Source.Sha256 = Hash(Xml);
            Write("tia-guard.json", Manifest); Write(HardwarePath, Hardware); Write(PlcPath, Plc);
            Write(TablePath, Table); Write(BlockPath, Block);
            File.WriteAllText(PathOf(XmlPath), Xml, new UTF8Encoding(false));
        }
        internal static string Hash(string text)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }
        internal RoundTripManifestV1 Export(string name, Action<SnapshotV1, RoundTripExtractionHints> mutate = null)
        {
            var raw = Path.Combine(OwnedRoot, "raw");
            Directory.CreateDirectory(raw);
            File.WriteAllText(Path.Combine(raw, "block.xml"), Xml, new UTF8Encoding(false));
            var snapshot = new SnapshotV1
            {
                Project = new SnapshotProject { Name = "Demo", SourceKind = "offline-copy" },
                Tia = new SnapshotTia { Version = "V21" }, Capture = new SnapshotCapture { Status = "complete" }
            };
            snapshot.Devices.Add(new SnapshotDevice { Id = Hardware.Id, Name = Hardware.Name, EngineeringPath = Hardware.EngineeringPath, Type = Hardware.DeviceTypeIdentifier });
            var plc = new SnapshotPlc { Id = Plc.Id, Name = Plc.Name, DeviceId = Hardware.Id };
            plc.Blocks.Add(new SnapshotBlock
            {
                Id = Block.Id, Name = Block.Name, ScopePath = Block.ScopePath, Kind = "OB", Number = 1, Language = "LAD",
                Export = new SnapshotExport { Status = "exported", Format = "SimaticML", Artifact = "block.xml", Sha256 = Hash(Xml) }
            });
            plc.Tags.Add(new SnapshotTag { Id = Tag.Id, Name = Tag.Name, DataType = Tag.DataType, ScopePath = Table.ScopePath,
                Address = SnapshotAddressParser.Parse(Tag.Address), Comment = new SnapshotComment { Status = "missing" } });
            snapshot.Plcs.Add(plc);
            var hints = new RoundTripExtractionHints
            {
                TagTableScanComplete = true,
                Hardware = new RoundTripHardwareBuildIdentity { DeviceName = Hardware.Name, CpuItemName = Hardware.CreateItemName,
                    CreateTypeIdentifier = Hardware.CreateTypeIdentifier, State = "supported-round-trip" },
                TagTables = new List<RoundTripTagTableHint> { new RoundTripTagTableHint { PlcName = Plc.Name, Name = Table.Name, ScopePath = Table.ScopePath } }
            };
            mutate?.Invoke(snapshot, hints);
            return RoundTripSourceMaterializer.Write(snapshot, hints, raw, Path.Combine(OwnedRoot, name));
        }
        public void Dispose()
        {
            if (Directory.Exists(OwnedRoot)) Directory.Delete(OwnedRoot, true);
        }
    }
}
