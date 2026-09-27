using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;

namespace TiaGuard.Openness
{
    internal static class SnapshotExtractor
    {
        internal static SnapshotV1 Extract(Project project, ProjectInfo info, SnapshotCollectionOptions options)
        {
            var capturedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            var snapshot = new SnapshotV1
            {
                Project = new SnapshotProject { Name = Required(info.Name, "unnamed-project"),
                    SourceKind = info.SourceKind, ProjectVersion = info.ProjectVersion },
                Tia = new SnapshotTia { Version = Required(info.TiaVersion, "unknown"), Build = info.TiaBuild },
                Capture = new SnapshotCapture { CapturedAtUtc = capturedAt, Mode = info.SourceKind }
            };
            if (string.IsNullOrWhiteSpace(info.Name))
                Diagnose(snapshot, "PROJECT_NAME_MISSING", "error", "Project name was unavailable.", null);
            if (string.IsNullOrWhiteSpace(info.TiaVersion))
                Diagnose(snapshot, "TIA_VERSION_MISSING", "error", "TIA version was unavailable.", null);
            Diagnose(snapshot, "COLLECTOR_PATH_IDENTITY", "info",
                "Object IDs derive from engineering paths and change when an object is renamed or moved.", null);
            var rootSuccess = false;
            rootSuccess |= Collect(() => project.Devices,
                device => AppendDevice(device, new List<string>(), snapshot, options, capturedAt),
                snapshot, "DEVICE_ROOT_READ_FAILED", null);
            rootSuccess |= Collect(() => project.DeviceGroups,
                group => AppendDeviceGroup(group, new List<string>(), snapshot, options, capturedAt),
                snapshot, "DEVICE_GROUP_ROOT_READ_FAILED", null);
            snapshot.Devices = snapshot.Devices.OrderBy(value => value.Id, StringComparer.Ordinal).ToList();
            snapshot.Plcs = snapshot.Plcs.OrderBy(value => value.Id, StringComparer.Ordinal).ToList();
            foreach (var device in snapshot.Devices)
            {
                var plcIds = snapshot.Plcs.Where(value => value.DeviceId == device.Id)
                    .Select(value => value.Id).OrderBy(value => value, StringComparer.Ordinal).ToList();
                device.PlcId = plcIds.FirstOrDefault();
                if (plcIds.Count > 1)
                    Diagnose(snapshot, "MULTIPLE_PLC_SOFTWARE", "warning",
                        "Device contains multiple PLC software objects; device.plcId names the lowest sorted ID.", device.Id);
            }
            foreach (var plc in snapshot.Plcs)
            {
                plc.Blocks = plc.Blocks.OrderBy(value => value.Id, StringComparer.Ordinal).ToList();
                plc.Tags = plc.Tags.OrderBy(value => value.Id, StringComparer.Ordinal).ToList();
            }
            CheckDuplicateIds(snapshot.Devices.Select(value => value.Id), snapshot, "DEVICE_ID_COLLISION");
            CheckDuplicateIds(snapshot.Plcs.Select(value => value.Id), snapshot, "PLC_ID_COLLISION");
            foreach (var plc in snapshot.Plcs)
            {
                CheckDuplicateIds(plc.Blocks.Select(value => value.Id), snapshot, "BLOCK_ID_COLLISION");
                CheckDuplicateIds(plc.Tags.Select(value => value.Id), snapshot, "TAG_ID_COLLISION");
            }
            snapshot.Diagnostics = snapshot.Diagnostics.OrderBy(value => value.ObjectId, StringComparer.Ordinal)
                .ThenBy(value => value.Code, StringComparer.Ordinal).ToList();
            snapshot.Capture.Status = snapshot.Diagnostics.Any(value => value.Severity != "info")
                ? (rootSuccess ? "partial" : "failed") : "complete";
            if (snapshot.Capture.Status == "complete")
                snapshot.Project.ContentId = SnapshotNormalization.ComputeContentId(snapshot);
            return snapshot;
        }

        private static void AppendDeviceGroup(DeviceUserGroup group, List<string> parents,
            SnapshotV1 snapshot, SnapshotCollectionOptions options, string capturedAt)
        {
            var name = RequiredName(Read(() => group.Name, snapshot, "DEVICE_GROUP_NAME_READ_FAILED", null),
                "unnamed-group", snapshot, "DEVICE_GROUP_NAME_MISSING", null);
            var path = Extend(parents, name);
            var id = SnapshotNormalization.MakeId("group", path);
            Collect(() => group.Devices, device => AppendDevice(device, path, snapshot, options, capturedAt),
                snapshot, "DEVICE_GROUP_DEVICES_READ_FAILED", id);
            Collect(() => group.Groups, child => AppendDeviceGroup(child, path, snapshot, options, capturedAt),
                snapshot, "DEVICE_GROUP_CHILDREN_READ_FAILED", id);
        }

        private static void AppendDevice(Device device, List<string> parents, SnapshotV1 snapshot,
            SnapshotCollectionOptions options, string capturedAt)
        {
            var name = Read(() => device.Name, snapshot, "DEVICE_NAME_READ_FAILED", null);
            if (string.IsNullOrWhiteSpace(name))
            {
                Diagnose(snapshot, "DEVICE_NAME_MISSING", "error", "A device has no usable name and was omitted.", null);
                return;
            }
            var path = Extend(parents, name);
            var id = SnapshotNormalization.MakeId("device", path);
            var type = Read(() => device.TypeIdentifier, snapshot, "DEVICE_TYPE_READ_FAILED", id);
            if (string.IsNullOrWhiteSpace(type))
            {
                type = "unknown";
                Diagnose(snapshot, "DEVICE_TYPE_MISSING", "warning", "Device type was unavailable.", id);
            }
            var result = new SnapshotDevice { Id = id, Name = name, Type = type,
                EngineeringPath = JoinPath(path), OrderNumber = FindAttribute(device, "ArticleNumber", snapshot, id),
                Firmware = FindAttribute(device, "FirmwareVersion", snapshot, id) };
            snapshot.Devices.Add(result);
            Collect(() => device.DeviceItems,
                item => AppendPlcSoftware(item, path, result, snapshot, options, capturedAt),
                snapshot, "DEVICE_ITEMS_READ_FAILED", id);
        }

        private static void AppendPlcSoftware(DeviceItem item, List<string> parents, SnapshotDevice device,
            SnapshotV1 snapshot, SnapshotCollectionOptions options, string capturedAt)
        {
            var itemName = Read(() => item.Name, snapshot, "DEVICE_ITEM_NAME_READ_FAILED", device.Id);
            var path = Extend(parents, RequiredName(itemName, "unnamed-item", snapshot,
                "DEVICE_ITEM_NAME_MISSING", device.Id));
            var itemId = SnapshotNormalization.MakeId("item", path);
            var software = Read(() => item.GetService<SoftwareContainer>()?.Software as PlcSoftware,
                snapshot, "PLC_SOFTWARE_READ_FAILED", itemId);
            if (software != null)
            {
                var name = Read(() => software.Name, snapshot, "PLC_NAME_READ_FAILED", itemId);
                if (string.IsNullOrWhiteSpace(name))
                    Diagnose(snapshot, "PLC_NAME_MISSING", "error", "PLC software name was unavailable.", itemId);
                var softwarePath = Extend(path, Required(name, "unnamed-software"));
                var plc = new SnapshotPlc { Id = SnapshotNormalization.MakeId("plc", softwarePath),
                    Name = Required(name, "unnamed-software"), DeviceId = device.Id };
                snapshot.Plcs.Add(plc);
                var blockRoot = Read(() => software.BlockGroup, snapshot, "BLOCK_ROOT_READ_FAILED", plc.Id);
                if (blockRoot != null)
                {
                    AppendBlocks(blockRoot, Extend(softwarePath, "Program blocks"), plc, snapshot, options);
                    Collect(() => blockRoot.SystemBlockGroups,
                        group => AppendSystemBlocks(group, Extend(softwarePath, "System blocks"), plc, snapshot, options),
                        snapshot, "SYSTEM_BLOCK_GROUPS_READ_FAILED", plc.Id);
                }
                else Diagnose(snapshot, "BLOCK_ROOT_MISSING", "error", "PLC block root was unavailable.", plc.Id);
                var tagRoot = Read(() => software.TagTableGroup, snapshot, "TAG_ROOT_READ_FAILED", plc.Id);
                if (tagRoot != null) AppendTags(tagRoot, Extend(softwarePath, "PLC tags"), plc, snapshot);
                else Diagnose(snapshot, "TAG_ROOT_MISSING", "error", "PLC tag root was unavailable.", plc.Id);
                var observed = plc.Blocks.Where(value => value.IsConsistent.HasValue).ToList();
                if (observed.Count > 0)
                {
                    plc.Compile.Mode = "consistency-only";
                    plc.Compile.Status = observed.Any(value => !value.IsConsistent.Value) ? "issues" : "unknown";
                    plc.Compile.ObservedAtUtc = capturedAt;
                }
            }
            Collect(() => item.DeviceItems,
                child => AppendPlcSoftware(child, path, device, snapshot, options, capturedAt),
                snapshot, "DEVICE_ITEM_CHILDREN_READ_FAILED", itemId);
        }

        private static void AppendBlocks(PlcBlockGroup group, List<string> path, SnapshotPlc plc,
            SnapshotV1 snapshot, SnapshotCollectionOptions options)
        {
            Collect(() => group.Blocks, block => AppendBlock(block, path, plc, snapshot, options),
                snapshot, "BLOCKS_READ_FAILED", SnapshotNormalization.MakeId("block-group", path));
            Collect(() => group.Groups, child =>
            {
                var name = RequiredName(Read(() => child.Name, snapshot, "BLOCK_GROUP_NAME_READ_FAILED", plc.Id),
                    "unnamed-group", snapshot, "BLOCK_GROUP_NAME_MISSING", plc.Id);
                AppendBlocks(child, Extend(path, name), plc, snapshot, options);
            }, snapshot, "BLOCK_GROUPS_READ_FAILED", plc.Id);
        }

        private static void AppendSystemBlocks(PlcSystemBlockGroup group, List<string> path, SnapshotPlc plc,
            SnapshotV1 snapshot, SnapshotCollectionOptions options)
        {
            var name = RequiredName(Read(() => group.Name, snapshot, "SYSTEM_BLOCK_GROUP_NAME_READ_FAILED", plc.Id),
                "unnamed-group", snapshot, "SYSTEM_BLOCK_GROUP_NAME_MISSING", plc.Id);
            var current = Extend(path, name);
            Collect(() => group.Blocks, block => AppendBlock(block, current, plc, snapshot, options),
                snapshot, "SYSTEM_BLOCKS_READ_FAILED", SnapshotNormalization.MakeId("block-group", current));
            Collect(() => group.Groups, child => AppendSystemBlocks(child, current, plc, snapshot, options),
                snapshot, "SYSTEM_BLOCK_CHILDREN_READ_FAILED", plc.Id);
        }

        private static void AppendBlock(PlcBlock block, List<string> path, SnapshotPlc plc,
            SnapshotV1 snapshot, SnapshotCollectionOptions options)
        {
            var name = Read(() => block.Name, snapshot, "BLOCK_NAME_READ_FAILED", plc.Id);
            if (string.IsNullOrWhiteSpace(name))
            {
                Diagnose(snapshot, "BLOCK_NAME_MISSING", "error", "A block has no usable name and was omitted.", plc.Id);
                return;
            }
            var id = SnapshotNormalization.MakeId("block", Extend(path, name));
            var result = new SnapshotBlock
            {
                Id = id, ScopePath = JoinPath(path), Name = name, Kind = block.GetType().Name,
                Language = Read(() => block.ProgrammingLanguage.ToString(), snapshot, "BLOCK_LANGUAGE_READ_FAILED", id),
                Number = Read(() => (int?)block.Number, snapshot, "BLOCK_NUMBER_READ_FAILED", id),
                IsConsistent = Read(() => (bool?)block.IsConsistent, snapshot, "BLOCK_CONSISTENCY_READ_FAILED", id),
                ModifiedAtUtc = FormatUtc(Read(() => (DateTime?)block.ModifiedDate,
                    snapshot, "BLOCK_MODIFIED_DATE_READ_FAILED", id)),
                Protection = Read(() => block.IsKnowHowProtected ? "know-how" : "none",
                    snapshot, "BLOCK_PROTECTION_READ_FAILED", id) ?? "unknown"
            };
            if (result.Number < 0)
            {
                result.Number = null;
                Diagnose(snapshot, "BLOCK_NUMBER_INVALID", "warning", "Block number was negative.", id);
            }
            if (result.Protection == "know-how")
            {
                result.Export.Status = "protected";
                result.Export.DiagnosticCode = "BLOCK_PROTECTED";
                Diagnose(snapshot, "BLOCK_PROTECTED", "warning", "Block export is restricted by know-how protection.", id);
            }
            else if (result.Protection == "unknown" && options.BlockExportDirectory != null)
            {
                result.Export.Status = "unsupported";
                result.Export.DiagnosticCode = "BLOCK_PROTECTION_UNKNOWN";
                Diagnose(snapshot, "BLOCK_PROTECTION_UNKNOWN", "warning",
                    "Block protection could not be established, so export was skipped.", id);
            }
            else if (options.BlockExportDirectory != null)
                ExportBlock(block, result, options.BlockExportDirectory, snapshot);
            plc.Blocks.Add(result);
        }

        private static void ExportBlock(PlcBlock block, SnapshotBlock result, string directory, SnapshotV1 snapshot)
        {
            var relative = "blocks/" + StableHash(result.Id) + ".xml";
            var destination = Path.Combine(directory, "blocks", Path.GetFileName(relative));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                if (File.Exists(destination)) throw new IOException("Export artifact already exists.");
                block.Export(new FileInfo(destination), ExportOptions.WithDefaults);
                using (var file = File.OpenRead(destination))
                using (var sha = SHA256.Create())
                    result.Export.Sha256 = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
                result.Export.Status = "exported";
                result.Export.Format = "SimaticML";
                result.Export.Artifact = relative;
            }
            catch (EngineeringNotSupportedException)
            {
                result.Export.Status = "unsupported";
                result.Export.DiagnosticCode = "BLOCK_EXPORT_UNSUPPORTED";
                Diagnose(snapshot, "BLOCK_EXPORT_UNSUPPORTED", "warning", "Block export is unsupported by Openness.", result.Id);
            }
            catch (Exception)
            {
                result.Export.Status = "failed";
                result.Export.DiagnosticCode = "BLOCK_EXPORT_FAILED";
                Diagnose(snapshot, "BLOCK_EXPORT_FAILED", "warning", "Block export failed; no artifact is claimed.", result.Id);
            }
        }

        private static void AppendTags(PlcTagTableGroup group, List<string> path, SnapshotPlc plc, SnapshotV1 snapshot)
        {
            Collect(() => group.TagTables, table =>
            {
                var name = RequiredName(Read(() => table.Name, snapshot, "TAG_TABLE_NAME_READ_FAILED", plc.Id),
                    "unnamed-table", snapshot, "TAG_TABLE_NAME_MISSING", plc.Id);
                var tablePath = Extend(path, name);
                Collect(() => table.Tags, tag => AppendTag(tag, tablePath, plc, snapshot),
                    snapshot, "TAGS_READ_FAILED", SnapshotNormalization.MakeId("tag-table", tablePath));
            }, snapshot, "TAG_TABLES_READ_FAILED", plc.Id);
            Collect(() => group.Groups, child =>
            {
                var name = RequiredName(Read(() => child.Name, snapshot, "TAG_GROUP_NAME_READ_FAILED", plc.Id),
                    "unnamed-group", snapshot, "TAG_GROUP_NAME_MISSING", plc.Id);
                AppendTags(child, Extend(path, name), plc, snapshot);
            }, snapshot, "TAG_GROUPS_READ_FAILED", plc.Id);
        }

        private static void AppendTag(PlcTag tag, List<string> path, SnapshotPlc plc, SnapshotV1 snapshot)
        {
            var name = Read(() => tag.Name, snapshot, "TAG_NAME_READ_FAILED", plc.Id);
            if (string.IsNullOrWhiteSpace(name))
            {
                Diagnose(snapshot, "TAG_NAME_MISSING", "error", "A tag has no usable name and was omitted.", plc.Id);
                return;
            }
            var id = SnapshotNormalization.MakeId("tag", Extend(path, name));
            var dataType = Read(() => tag.DataTypeName, snapshot, "TAG_DATA_TYPE_READ_FAILED", id);
            if (string.IsNullOrWhiteSpace(dataType))
            {
                dataType = "unknown";
                Diagnose(snapshot, "TAG_DATA_TYPE_MISSING", "warning", "Tag data type was unavailable.", id);
            }
            var raw = Read(() => tag.LogicalAddress, snapshot, "TAG_ADDRESS_READ_FAILED", id);
            var address = SnapshotAddressParser.Parse(raw);
            if (address.ParseStatus == "unsupported")
                Diagnose(snapshot, "TAG_ADDRESS_UNSUPPORTED", "warning", "Raw tag address is preserved but cannot be normalized.", id);
            plc.Tags.Add(new SnapshotTag { Id = id, ScopePath = JoinPath(path), Name = name,
                DataType = dataType, Address = address, Comment = ReadComment(tag, snapshot, id) });
        }

        private static SnapshotComment ReadComment(PlcTag tag, SnapshotV1 snapshot, string id)
        {
            try
            {
                var comment = tag.Comment;
                if (comment == null)
                {
                    Diagnose(snapshot, "TAG_COMMENT_UNAVAILABLE", "warning", "Tag comment is unavailable.", id);
                    return new SnapshotComment { Status = "unavailable" };
                }
                var value = comment.Items.OrderBy(item => item.Language?.ToString(), StringComparer.Ordinal)
                    .Select(item => item.Text).FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
                return new SnapshotComment { Status = value == null ? "missing" : "present", Text = value };
            }
            catch (EngineeringNotSupportedException)
            {
                Diagnose(snapshot, "TAG_COMMENT_UNSUPPORTED", "warning", "Tag comment is unsupported by Openness.", id);
                return new SnapshotComment { Status = "unavailable" };
            }
            catch (Exception)
            {
                Diagnose(snapshot, "TAG_COMMENT_READ_FAILED", "warning", "Tag comment could not be read.", id);
                return new SnapshotComment { Status = "read-failed" };
            }
        }

        private static string FindAttribute(HardwareObject hardware, string name, SnapshotV1 snapshot, string id)
        {
            var value = ReadAttribute(hardware, name, snapshot, id);
            if (value != null) return value;
            try
            {
                foreach (var item in hardware.DeviceItems)
                {
                    value = FindAttribute(item, name, snapshot, id);
                    if (value != null) return value;
                }
            }
            catch (Exception) { Diagnose(snapshot, "DEVICE_ATTRIBUTE_TREE_READ_FAILED", "warning", "Hardware attributes could not be fully traversed.", id); }
            return null;
        }

        private static string ReadAttribute(HardwareObject hardware, string name, SnapshotV1 snapshot, string id)
        {
            try
            {
                var value = hardware.GetAttribute(name);
                return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
            }
            catch (EngineeringNotSupportedException) { return null; }
            catch (Exception)
            {
                Diagnose(snapshot, "DEVICE_ATTRIBUTE_READ_FAILED", "warning", "Optional hardware attribute could not be read.", id);
                return null;
            }
        }

        private static bool Collect<T>(Func<IEnumerable<T>> source, Action<T> append,
            SnapshotV1 snapshot, string code, string objectId)
        {
            try
            {
                foreach (var item in source())
                {
                    try { append(item); }
                    catch (Exception) { Diagnose(snapshot, code, "error", "Engineering object could not be collected.", objectId); }
                }
                return true;
            }
            catch (Exception)
            {
                Diagnose(snapshot, code, "error", "Engineering collection could not be enumerated.", objectId);
                return false;
            }
        }

        private static T Read<T>(Func<T> getter, SnapshotV1 snapshot, string code, string objectId)
        {
            try { return getter(); }
            catch (EngineeringNotSupportedException)
            {
                Diagnose(snapshot, code, "warning",
                    "Engineering property is unsupported by Openness.", objectId);
                return default(T);
            }
            catch (Exception)
            {
                Diagnose(snapshot, code, "warning", "Engineering property could not be read.", objectId);
                return default(T);
            }
        }

        private static void Diagnose(SnapshotV1 snapshot, string code, string severity, string message, string objectId)
        {
            snapshot.Diagnostics.Add(new SnapshotDiagnostic { Code = code, Severity = severity,
                Message = message, ObjectId = objectId });
        }

        private static void CheckDuplicateIds(IEnumerable<string> ids, SnapshotV1 snapshot, string code)
        {
            foreach (var group in ids.GroupBy(value => value, StringComparer.Ordinal).Where(value => value.Count() > 1))
                Diagnose(snapshot, code, "error", "Collector path identity is ambiguous for multiple objects.", group.Key);
        }

        private static List<string> Extend(List<string> parents, string name)
        {
            var result = new List<string>(parents) { name };
            return result;
        }

        private static string Required(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
        private static string RequiredName(string value, string fallback, SnapshotV1 snapshot, string code, string objectId)
        {
            if (!string.IsNullOrWhiteSpace(value)) return value;
            Diagnose(snapshot, code, "warning", "An engineering group or item name was unavailable.", objectId);
            return fallback;
        }
        private static string JoinPath(IEnumerable<string> path) => string.Join("/", path.Select(Uri.EscapeDataString));

        private static string FormatUtc(DateTime? value)
        {
            if (!value.HasValue || value.Value == DateTime.MinValue || value.Value.Kind == DateTimeKind.Unspecified)
                return null;
            return value.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        }

        private static string StableHash(string value)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value)))
                    .Replace("-", "").ToLowerInvariant();
        }
    }
}
