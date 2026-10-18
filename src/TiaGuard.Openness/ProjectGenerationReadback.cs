// Source/export patterns adapted from heilingbrunner/tiaportal-mcp at
// 6824e4d671b403993c35d98dddf104686a950f4e, Portal.Documents.cs / Portal.Sources.cs.
// Copyright (c) 2025 J.Heilingbrunner. MIT license in third-party/project-generation-verification/.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.Units;

namespace TiaGuard.Openness
{
    public static class ProjectGenerationReadback
    {
        // Caller supplies an already-owned, offline V21 project/session and a fresh
        // evidence directory. This reader never starts, attaches, saves, compiles,
        // deletes, or closes TIA. Do not pass a user's canonical/online project.
        // Projection selects only export format; inventory always scans ALL objects.
        public static ProjectGenerationVerificationObservation Capture(Project project, string evidenceDirectory,
            Func<string, string, string> projection = null)
            => Capture(project, evidenceDirectory, projection, reopenedFromDisk: false);

        public static ProjectGenerationVerificationObservation Capture(Project project, string evidenceDirectory,
            Func<string, string, string> projection, bool reopenedFromDisk)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            var root = Path.GetFullPath(evidenceDirectory);
            FileSystemSafety.RequirePlainAncestors(root);
            if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Evidence directory must be new.");
            Directory.CreateDirectory(root);
            var reader = new Reader(root, projection ?? ((scope, name) => "simatic-sd"));
            var result = reader.Read(project);
            result.ReopenedFromDisk = reopenedFromDisk;
            File.WriteAllText(Path.Combine(root, "observation.json"),
                new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(result), new UTF8Encoding(false));
            return result;
        }

        private sealed class Reader
        {
            private readonly string root;
            private readonly Func<string, string, string> projection;
            private readonly HashSet<Device> devices = new HashSet<Device>();
            private readonly List<Tuple<DeviceItem, PlcSoftware>> plcs = new List<Tuple<DeviceItem, PlcSoftware>>();
            private int exportId;
            private readonly ProjectGenerationVerificationObservation result;

            internal Reader(string root, Func<string, string, string> projection)
            {
                this.root = root;
                this.projection = projection;
                result = new ProjectGenerationVerificationObservation
                {
                    TiaVersion = "V21", ApiVersion = typeof(PlcSoftware).Assembly.GetName().Version.ToString(),
                    CapturedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), EvidenceDirectory = root
                };
                if (typeof(PlcSoftware).Assembly.GetName().Version.Major != 21)
                    Failure("API_VERSION_UNSUPPORTED", "project", "Requires the V21 Step7 API.");
            }

            internal ProjectGenerationVerificationObservation Read(Project project)
            {
                Run("PROJECT_READ_FAILED", "project", () => result.ProjectName = project.Name);
                Collect(() => project.Devices, Device, "devices");
                Collect(() => project.DeviceGroups, DeviceGroup, "device-groups");
                Run("UNGROUPED_DEVICES_READ_FAILED", "devices", () => Collect(() => project.UngroupedDevicesGroup.Devices, Device, "ungrouped-devices"));
                if (devices.Count != 1 || plcs.Count != 1)
                    Failure("HARDWARE_SCOPE_UNSUPPORTED", "hardware", "Requires exactly one device and one PLC software/CPU.");
                if (plcs.Count == 1)
                {
                    var cpu = plcs[0].Item1;
                    var plc = plcs[0].Item2;
                    Run("CPU_READ_FAILED", "hardware", () =>
                    {
                        result.CpuTypeIdentifier = cpu.TypeIdentifier;
                        if ((cpu.Classification & DeviceItemClassifications.CPU) != DeviceItemClassifications.CPU)
                            throw new InvalidDataException("PLC software owner is not a CPU DeviceItem.");
                        result.PlcName = plc.Name;
                    });
                    if (ProjectGenerationCpuProfiles.FindByCpu(result.CpuTypeIdentifier)?.Family == "S7-1500")
                        Run("RACK_MODULE_READ_FAILED", "hardware", () =>
                        {
                            // Parent/DeviceItems describe logical ownership, not plugged
                            // hardware. Use Container/Items for the actual rack relation.
                            var rack = cpu.Container as DeviceItem;
                            if (rack == null) throw new InvalidDataException("S7-1500 CPU rack could not be read.");
                            var station = rack.Container as Device;
                            if (station == null || !devices.Contains(station))
                                throw new InvalidDataException("S7-1500 rack station could not be identified.");
                            var racks = station.Items.ToArray();
                            var modules = rack.Items.ToArray();
                            result.NoExtraRackModules = racks.Length == 1 && racks[0].Equals(rack)
                                && !station.UnpluggedItems.Any() && modules.Length == 1
                                && modules[0].Equals(cpu) && modules[0].TypeIdentifier == result.CpuTypeIdentifier;
                            result.CoverageNotes["rack-modules"] = "Container/Items; racks=" + racks.Length
                                + "; modules=" + string.Join(",", modules.Select(m => m.TypeIdentifier));
                            if (result.NoExtraRackModules != true)
                                Failure("EXTRA_RACK_MODULES_UNSUPPORTED", "hardware", "Only the exact standalone CPU without additional rack modules is in scope.");
                        });
                    Run("BLOCK_ROOT_READ_FAILED", "blocks", () => Blocks(plc.BlockGroup, "blocks", plc));
                    Run("SYSTEM_BLOCK_ROOT_READ_FAILED", "system-blocks", () =>
                        Collect(() => plc.BlockGroup.SystemBlockGroups, g => SystemBlocks(g, "system-blocks", plc), "system-blocks"));
                    Run("TYPE_ROOT_READ_FAILED", "types", () => Types(plc.TypeGroup, "types", plc));
                    Run("TAG_ROOT_READ_FAILED", "tags", () => Tags(plc.TagTableGroup, "tags"));
                    // First generation contract excludes these collections. Presence or
                    // a failed enumeration remains UNKNOWN rather than being omitted.
                    Run("EXTRA_CONTENT_READ_FAILED", "plc", () =>
                    {
                        if (plc.ExternalSourceGroup.ExternalSources.Any() || plc.ExternalSourceGroup.Groups.Any()
                            || plc.TechnologicalObjectGroup.TechnologicalObjects.Any() || plc.TechnologicalObjectGroup.Groups.Any())
                            Failure("EXTRA_PLC_CONTENT_UNSUPPORTED", "plc", "External sources/technology objects remain present.");
                    });
                    Run("SOFTWARE_UNIT_READ_FAILED", "plc", () =>
                    {
                        var provider = plc.GetService<PlcUnitProvider>();
                        if (provider != null)
                        {
                            result.SoftwareUnitsEmpty = !provider.UnitGroup.Units.Any() && !provider.UnitGroup.SafetyUnits.Any();
                            if (result.SoftwareUnitsEmpty != true)
                                Failure("SOFTWARE_UNITS_UNSUPPORTED", "plc", "Unit-scoped objects are outside the root-only contract.");
                            else result.CoverageNotes["software-units"] = "V21 PlcUnitProvider: empty Units and SafetyUnits";
                        }
                        else if (result.CpuTypeIdentifier == ProjectGenerationCpuProfiles.DefaultCpuTypeIdentifier)
                            result.CoverageNotes["software-units"] = "No PlcUnitProvider on exact S7-1200 profile; V21 software units are S7-1500-only. API exceptions remain unknown.";
                        else Failure("SOFTWARE_UNIT_SERVICE_UNAVAILABLE", "plc", "Unit service unavailable on an unproven CPU.");
                    });
                }
                result.HardwareComplete = !result.ReadFailures.Any(f => f.Path == "hardware" || f.Path.StartsWith("device", StringComparison.Ordinal) || f.Path == "ungrouped-devices");
                result.ObjectsComplete = !result.ReadFailures.Any(f => f.Path.StartsWith("blocks", StringComparison.Ordinal)
                    || f.Path.StartsWith("types", StringComparison.Ordinal) || f.Path.StartsWith("system-blocks", StringComparison.Ordinal) || f.Path == "plc");
                result.TagTablesComplete = !result.ReadFailures.Any(f => f.Path.StartsWith("tags", StringComparison.Ordinal));
                return result;
            }

            private void DeviceGroup(DeviceUserGroup g)
            {
                Collect(() => g.Devices, Device, "devices");
                Collect(() => g.Groups, DeviceGroup, "device-groups");
            }

            private void Device(Device d)
            {
                if (!devices.Add(d)) return; // same object reachable through two root navigators
                Collect(() => d.DeviceItems, Item, "device-items");
            }

            private void Item(DeviceItem item)
            {
                var software = item.GetService<SoftwareContainer>()?.Software;
                if (software is PlcSoftware plc) plcs.Add(Tuple.Create(item, plc));
                else if (software != null) Failure("SOFTWARE_UNSUPPORTED", "hardware", "Non-PLC software found.");
                Collect(() => item.DeviceItems, Item, "device-items");
            }

            private void Blocks(PlcBlockGroup g, string scope, PlcSoftware plc)
            {
                Collect(() => g.Blocks, b => Block(b, scope, plc), scope);
                Collect(() => g.Groups, child => Blocks(child, Child(scope, child.Name), plc), scope);
            }

            private void SystemBlocks(PlcSystemBlockGroup g, string scope, PlcSoftware plc)
            {
                var path = Child(scope, g.Name);
                Collect(() => g.Blocks, b => Block(b, path, plc), path);
                Collect(() => g.Groups, child => SystemBlocks(child, path, plc), path);
            }

            private void Types(PlcTypeGroup g, string scope, PlcSoftware plc)
            {
                Collect(() => g.Types, t => Type(t, scope, plc), scope);
                Collect(() => g.Groups, child => Types(child, Child(scope, child.Name), plc), scope);
            }

            private void Block(PlcBlock block, string scope, PlcSoftware plc)
            {
                var value = new ProjectGenerationVerificationObject { Scope = scope };
                result.Objects.Add(value); // retain an entry even if a later property/export fails
                value.Name = block.Name;
                value.Kind = block is OB ? "OB" : block is FB ? "FB" : block is FC ? "FC"
                    : block is GlobalDB || block is InstanceDB ? "DB" : "unknown";
                value.Language = block.ProgrammingLanguage.ToString();
                value.Number = block.Number;
                Export(value, (dir, name) => block.ExportAsDocuments(dir, name),
                    file => block.Export(file, ExportOptions.WithDefaults),
                    (file, extension) => plc.ExternalSourceGroup.GenerateSource(new[] { (IGenerateSource)block }, file, GenerateOptions.None));
            }

            private void Type(PlcType type, string scope, PlcSoftware plc)
            {
                var value = new ProjectGenerationVerificationObject { Scope = scope };
                result.Objects.Add(value);
                value.Name = type.Name;
                value.Kind = "UDT";
                value.Language = "UDT";
                Export(value, (dir, name) => type.ExportAsDocuments(dir, name),
                    file => type.Export(file, ExportOptions.WithDefaults),
                    (file, extension) => plc.ExternalSourceGroup.GenerateSource(new[] { (IGenerateSource)type }, file, GenerateOptions.None));
            }

            private void Export(ProjectGenerationVerificationObject value,
                Func<DirectoryInfo, string, DocumentExportResult> documents, Action<FileInfo> xml,
                Action<FileInfo, string> source)
            {
                var path = value.Scope + "/" + value.Name;
                var objectRoot = Path.Combine(root, (++exportId).ToString("D6", CultureInfo.InvariantCulture));
                Directory.CreateDirectory(objectRoot);
                Run("INTERFACE_EXPORT_FAILED", path, () =>
                {
                    var file = new FileInfo(Path.Combine(objectRoot, "interface.xml"));
                    xml(file);
                    FileSystemSafety.RequirePlainFile(file.FullName);
                    ProjectGenerationVerificationXml.ReadInterface(File.ReadAllText(file.FullName, new UTF8Encoding(false, true)), value);
                });
                Run("CONTENT_EXPORT_FAILED", path, () =>
                {
                    value.ContentFormat = projection(value.Scope, value.Name);
                    var content = new DirectoryInfo(Path.Combine(objectRoot, "content"));
                    content.Create();
                    if (value.ContentFormat == "simatic-sd")
                    {
                        var exported = documents(content, "object");
                        if (exported == null) throw new InvalidDataException("Document export result unavailable.");
                        value.ExportState = exported.State.ToString();
                        foreach (var message in exported.Messages) value.ExportMessages.Add(message.Message);
                        if (exported.State != DocumentResultState.Success) throw new InvalidDataException("Document export was not successful.");
                        ProjectGenerationReadbackDocuments.Read(content.FullName, exported.ExportedDocuments.Select(f => f.FullName), value);
                    }
                    else if (value.ContentFormat == "scl")
                    {
                        var extension = value.Kind == "UDT" ? ".udt" : value.Kind == "DB" ? ".db" : ".scl";
                        var file = new FileInfo(Path.Combine(content.FullName, "object" + extension));
                        source(file, extension);
                        value.ExportState = "GenerateSourceReturned";
                        ProjectGenerationReadbackDocuments.Read(content.FullName, new[] { file.FullName }, value);
                    }
                    else if (value.ContentFormat == "simaticml")
                    {
                        var file = new FileInfo(Path.Combine(content.FullName, "object.xml"));
                        xml(file);
                        value.ExportState = "XmlExportReturned";
                        ProjectGenerationReadbackDocuments.Read(content.FullName, new[] { file.FullName }, value);
                    }
                    else throw new InvalidDataException("Unknown export projection.");
                });
            }

            private void Tags(PlcTagTableGroup g, string scope)
            {
                Collect(() => g.TagTables, table =>
                {
                    var value = new ProjectGenerationVerificationTagTable { Scope = scope };
                    result.TagTables.Add(value);
                    value.Name = table.Name;
                    var before = result.ReadFailures.Count;
                    Collect(() => table.Tags, tag => value.Tags.Add(new ProjectGenerationVerificationTag
                        { Name = tag.Name, DataType = tag.DataTypeName, LogicalAddress = tag.LogicalAddress }), scope);
                    if (table.UserConstants.Any()) Failure("CONSTANTS_UNSUPPORTED", scope, "Undeclared user constants found.");
                    value.Complete = before == result.ReadFailures.Count;
                }, scope);
                Collect(() => g.Groups, child => Tags(child, Child(scope, child.Name)), scope);
            }

            private static string Child(string scope, string name)
            {
                if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("Group name unavailable.");
                return scope + "/" + Uri.EscapeDataString(name);
            }

            private void Collect<T>(Func<IEnumerable<T>> get, Action<T> append, string path)
            {
                // Catch MoveNext and per-object failures separately; retain later objects
                // when a single property fails, and record partial enumeration explicitly.
                Run("COLLECTION_READ_FAILED", path, () =>
                {
                    foreach (var item in get()) Run("OBJECT_READ_FAILED", path, () => append(item));
                });
            }

            private void Run(string code, string path, Action action)
            {
                try { action(); }
                catch (Exception e) { Failure(code, path, e.GetType().Name + ": " + e.Message); }
            }

            private void Failure(string code, string path, string detail)
            {
                result.ReadFailures.Add(new ProjectGenerationVerificationFinding { Code = code, Path = path, Detail = detail, Unknown = true });
            }
        }
    }
}
