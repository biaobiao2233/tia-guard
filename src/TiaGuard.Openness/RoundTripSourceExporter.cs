using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Tags;

namespace TiaGuard.Openness
{
    internal static class RoundTripSourceExporter
    {
        internal static RoundTripManifestV1 Export(
            Project project,
            ProjectInfo info,
            string outputDirectory,
            Action<string> progress = null)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            if (info == null) throw new ArgumentNullException(nameof(info));
            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new ArgumentException("A round-trip output directory is required.", nameof(outputDirectory));

            var blockExportDirectory = Path.Combine(
                Path.GetTempPath(), "TiaGuard.RoundTripBlocks", Guid.NewGuid().ToString("N"));
            try
            {
                progress?.Invoke("preflight-read");
                var preflight = SnapshotExtractor.Extract(project, info, new SnapshotCollectionOptions());
                progress?.Invoke("compile-preparation");
                var compilePreparation = PrepareOfflineCopyForExport(project, info, preflight);
                progress?.Invoke("capture-export");
                var snapshot = SnapshotExtractor.Extract(project, info,
                    new SnapshotCollectionOptions { BlockExportDirectory = blockExportDirectory });
                progress?.Invoke("hints");
                var hints = ExtractHints(project, snapshot);
                hints.CompilePreparation = compilePreparation;
                progress?.Invoke("materialize");
                var manifest = RoundTripSourceMaterializer.Write(snapshot, hints, blockExportDirectory, outputDirectory);
                progress?.Invoke("done");
                return manifest;
            }
            finally
            {
                DeleteOwnedBlockExportDirectory(blockExportDirectory);
            }
        }

        private static RoundTripCompilePreparation PrepareOfflineCopyForExport(
            Project project,
            ProjectInfo info,
            SnapshotV1 preflight)
        {
            var preparation = new RoundTripCompilePreparation();
            var targetBlocks = (preflight?.Plcs ?? new List<SnapshotPlc>())
                .SelectMany(value => value.Blocks ?? new List<SnapshotBlock>())
                .Where(value => value.Number == 1 &&
                    string.Equals(value.Language, "LAD", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (targetBlocks.Count == 0 || targetBlocks.All(value => value.IsConsistent == true))
                return preparation;

            if (!string.Equals(info.SourceKind, "offline-copy", StringComparison.Ordinal))
            {
                preparation.State = "blocked-attached-session";
                return preparation;
            }

            preparation.Attempted = true;
            var plcSoftwares = FindPlcSoftware(project);
            if (plcSoftwares.Count != 1)
            {
                preparation.State = "failed";
                preparation.FailureType = "UnsupportedPlcSoftwareCount";
                return preparation;
            }

            try
            {
                var compiler = plcSoftwares[0].GetService<ICompilable>();
                if (compiler == null)
                {
                    preparation.State = "failed";
                    preparation.FailureType = "CompileServiceUnavailable";
                    return preparation;
                }

                var result = compiler.Compile();
                preparation.Errors = result.ErrorCount;
                preparation.Warnings = result.WarningCount;
                preparation.State = result.ErrorCount == 0 ? "succeeded" : "failed";
                if (result.ErrorCount != 0)
                    preparation.FailureType = "CompilerErrors";
            }
            catch (Exception error)
            {
                preparation.State = "failed";
                preparation.FailureType = error.GetType().Name;
            }
            return preparation;
        }

        private static List<PlcSoftware> FindPlcSoftware(Project project)
        {
            var result = new List<PlcSoftware>();
            foreach (var device in project.Devices)
                foreach (var item in device.DeviceItems)
                    CollectPlcSoftware(item, result);
            return result;
        }

        private static void CollectPlcSoftware(DeviceItem item, ICollection<PlcSoftware> result)
        {
            var software = SafeRead(() => item.GetService<SoftwareContainer>()?.Software as PlcSoftware);
            if (software != null)
                result.Add(software);
            foreach (var child in item.DeviceItems)
                CollectPlcSoftware(child, result);
        }

        private static RoundTripExtractionHints ExtractHints(Project project, SnapshotV1 snapshot)
        {
            var hints = new RoundTripExtractionHints { TagTableScanComplete = true };
            var rootDevices = project.Devices.ToList();
            if (rootDevices.Count == 1)
            {
                var device = rootDevices[0];
                hints.Hardware = ExtractHardwareIdentity(device);
                var deviceName = SafeRead(() => device.Name);
                if (string.IsNullOrWhiteSpace(hints.Hardware.DeviceName))
                    hints.Hardware.DeviceName = deviceName;
                ExtractSoftwareHints(device, new List<string> { deviceName ?? "unnamed-device" }, hints);
            }
            else
            {
                hints.Hardware = new RoundTripHardwareBuildIdentity
                {
                    State = RoundTripCapabilityStates.Unsupported,
                    Reason = "v0.1 requires exactly one root project device."
                };
                foreach (var device in rootDevices.OrderBy(value => SafeRead(() => value.Name), StringComparer.Ordinal))
                    ExtractSoftwareHints(device,
                        new List<string> { SafeRead(() => device.Name) ?? "unnamed-device" }, hints);
            }

            hints.TagTables = hints.TagTables
                .OrderBy(value => value.PlcName, StringComparer.Ordinal)
                .ThenBy(value => value.ScopePath, StringComparer.Ordinal)
                .ToList();
            var expectedPlcs = (snapshot.Plcs ?? new List<SnapshotPlc>())
                .Select(value => value.Name).OrderBy(value => value, StringComparer.Ordinal);
            var scannedPlcs = hints.ScannedTagTablePlcs.OrderBy(value => value, StringComparer.Ordinal);
            if (!expectedPlcs.SequenceEqual(scannedPlcs, StringComparer.Ordinal))
                RecordTagTableScanFailure(hints, null, "project", "PlcSoftwareScanMismatch");
            hints.TagTableScanFailures = hints.TagTableScanFailures
                .OrderBy(value => value.PlcName, StringComparer.Ordinal)
                .ThenBy(value => value.ScopePath, StringComparer.Ordinal)
                .ThenBy(value => value.FailureType, StringComparer.Ordinal)
                .ToList();
            return hints;
        }

        private static RoundTripHardwareBuildIdentity ExtractHardwareIdentity(Device device)
        {
            var identity = new RoundTripHardwareBuildIdentity
            {
                DeviceName = SafeRead(() => device.Name)
            };
            var cpuItems = new List<DeviceItem>();
            try
            {
                foreach (var item in device.DeviceItems)
                    CollectCpuItems(item, cpuItems);
            }
            catch (Exception error)
            {
                identity.State = RoundTripCapabilityStates.Failed;
                identity.Reason = "CPU DeviceItem traversal failed: " + error.GetType().Name + ".";
                return identity;
            }

            if (cpuItems.Count != 1)
            {
                identity.State = cpuItems.Count == 0
                    ? RoundTripCapabilityStates.Failed
                    : RoundTripCapabilityStates.Unsupported;
                identity.Reason = "Expected exactly one CPU DeviceItem but observed " +
                    cpuItems.Count.ToString(CultureInfo.InvariantCulture) + ".";
                return identity;
            }

            var cpu = cpuItems[0];
            identity.CpuItemName = SafeRead(() => cpu.Name);
            identity.CreateTypeIdentifier = SafeRead(() => cpu.TypeIdentifier);
            identity.OrderNumber = SafeAttribute(cpu, "OrderNumber");
            identity.Firmware = SafeAttribute(cpu, "FirmwareVersion");

            if (string.IsNullOrWhiteSpace(identity.CpuItemName))
            {
                identity.State = RoundTripCapabilityStates.Failed;
                identity.Reason = "CPU DeviceItem name is unavailable.";
            }
            else if (string.IsNullOrWhiteSpace(identity.CreateTypeIdentifier))
            {
                identity.State = RoundTripCapabilityStates.Failed;
                identity.Reason = "CPU DeviceItem TypeIdentifier is unavailable.";
            }
            else if (!identity.CreateTypeIdentifier.StartsWith("OrderNumber:", StringComparison.Ordinal))
            {
                identity.State = RoundTripCapabilityStates.Unsupported;
                identity.Reason = "v0.1 S7-1200 rebuild requires an OrderNumber CPU TypeIdentifier; observed '" +
                    identity.CreateTypeIdentifier + "'.";
            }
            else
            {
                try
                {
                    var topologyReason = ValidateDemoHardwareTopology(device);
                    identity.State = topologyReason == null
                        ? RoundTripCapabilityStates.SupportedRoundTrip
                        : RoundTripCapabilityStates.Unsupported;
                    identity.Reason = topologyReason;
                }
                catch (Exception error)
                {
                    identity.State = RoundTripCapabilityStates.Failed;
                    identity.Reason = "Hardware DeviceItem topology could not be read (" +
                        error.GetType().Name + ").";
                }
            }
            return identity;
        }

        // The first v0.1 proof target is the observed motor-control demo. A CPU
        // create identifier cannot reconstruct an added rack module, so reject any
        // item tree beyond this verified CPU-integrated shape.
        private static string ValidateDemoHardwareTopology(Device device)
        {
            var roots = device.DeviceItems.ToList();
            if (roots.Count != 2)
                return "v0.1 requires the verified rack and CPU DeviceItem topology.";
            var rack = roots.SingleOrDefault(item =>
                string.Equals(item.TypeIdentifier, "System:Rack.S71200", StringComparison.Ordinal));
            var rootCpu = roots.SingleOrDefault(item =>
                (item.Classification & DeviceItemClassifications.CPU) == DeviceItemClassifications.CPU);
            if (rack == null || rootCpu == null ||
                rack.Classification != (DeviceItemClassifications)0 || rack.DeviceItems.Any())
                return "v0.1 requires one empty S7-1200 rack and one direct CPU item.";

            var expectedChildren = new HashSet<string>(StringComparer.Ordinal)
            {
                "读卡器/写卡器", "PROFINET 接口_1", "HSC_1", "HSC_2", "HSC_3",
                "HSC_4", "HSC_5", "HSC_6", "AI 2_1", "DI 8/DQ 6_1",
                "OPC UA", "Pulse_1", "Pulse_2", "Pulse_3", "Pulse_4"
            };
            var children = rootCpu.DeviceItems.ToList();
            if (children.Count != expectedChildren.Count ||
                !expectedChildren.SetEquals(children.Select(item => item.Name)))
                return "v0.1 does not support additional or missing CPU DeviceItems.";
            foreach (var child in children)
            {
                if (!string.IsNullOrWhiteSpace(child.TypeIdentifier) ||
                    child.Classification != (DeviceItemClassifications)0)
                    return "v0.1 does not support typed or classified CPU child modules.";
                var descendants = child.DeviceItems.ToList();
                if (child.Name == "PROFINET 接口_1")
                {
                    if (descendants.Count != 1 || descendants[0].Name != "端口_1" ||
                        !string.IsNullOrWhiteSpace(descendants[0].TypeIdentifier) ||
                        descendants[0].Classification != (DeviceItemClassifications)0 ||
                        descendants[0].DeviceItems.Any())
                        return "v0.1 does not support an altered PROFINET interface topology.";
                }
                else if (descendants.Count != 0)
                    return "v0.1 does not support additional nested CPU DeviceItems.";
            }
            return null;
        }

        private static void CollectCpuItems(DeviceItem item, ICollection<DeviceItem> result)
        {
            if ((item.Classification & DeviceItemClassifications.CPU) == DeviceItemClassifications.CPU)
                result.Add(item);
            foreach (var child in item.DeviceItems)
                CollectCpuItems(child, result);
        }

        private static void ExtractSoftwareHints(Device device, List<string> devicePath, RoundTripExtractionHints hints)
        {
            foreach (var item in device.DeviceItems)
                ExtractSoftwareHints(item, devicePath, hints);
        }

        private static void ExtractSoftwareHints(DeviceItem item, List<string> parents, RoundTripExtractionHints hints)
        {
            var itemName = SafeRead(() => item.Name) ?? "unnamed-item";
            var itemPath = Extend(parents, itemName);
            var software = SafeRead(() => item.GetService<SoftwareContainer>()?.Software as PlcSoftware);
            if (software != null)
            {
                var plcName = SafeRead(() => software.Name) ?? "unnamed-software";
                var softwarePath = Extend(itemPath, plcName);
                var tagPath = Extend(softwarePath, "PLC tags");
                PlcTagTableGroup root = null;
                try { root = software.TagTableGroup; }
                catch (Exception error)
                {
                    RecordTagTableScanFailure(hints, plcName, JoinPath(tagPath), error.GetType().Name);
                }
                if (root == null)
                {
                    if (!hints.TagTableScanFailures.Any(value =>
                            value.PlcName == plcName && value.ScopePath == JoinPath(tagPath)))
                        RecordTagTableScanFailure(hints, plcName, JoinPath(tagPath), "TagTableGroupUnavailable");
                }
                else
                {
                    var failuresBefore = hints.TagTableScanFailures.Count;
                    ExtractTagTableHints(root, tagPath, plcName, hints);
                    if (hints.TagTableScanFailures.Count == failuresBefore)
                        hints.ScannedTagTablePlcs.Add(plcName);
                }
            }
            foreach (var child in item.DeviceItems)
                ExtractSoftwareHints(child, itemPath, hints);
        }

        private static void ExtractTagTableHints(
            PlcTagTableGroup group,
            List<string> path,
            string plcName,
            RoundTripExtractionHints hints)
        {
            try
            {
                foreach (var table in group.TagTables)
                {
                    var tableName = SafeRead(() => table.Name);
                    if (string.IsNullOrWhiteSpace(tableName))
                    {
                        RecordTagTableScanFailure(hints, plcName, JoinPath(path), "TagTableNameUnavailable");
                        continue;
                    }
                    var tablePath = Extend(path, tableName);
                    hints.TagTables.Add(new RoundTripTagTableHint
                    {
                        PlcName = plcName,
                        Name = tableName,
                        ScopePath = JoinPath(tablePath)
                    });
                }
            }
            catch (Exception error)
            {
                RecordTagTableScanFailure(hints, plcName, JoinPath(path), error.GetType().Name);
            }

            try
            {
                foreach (var child in group.Groups)
                {
                    var name = SafeRead(() => child.Name);
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        RecordTagTableScanFailure(hints, plcName, JoinPath(path), "TagTableGroupNameUnavailable");
                        continue;
                    }
                    ExtractTagTableHints(child, Extend(path, name), plcName, hints);
                }
            }
            catch (Exception error)
            {
                RecordTagTableScanFailure(hints, plcName, JoinPath(path), error.GetType().Name);
            }
        }

        private static void RecordTagTableScanFailure(
            RoundTripExtractionHints hints, string plcName, string scopePath, string failureType)
        {
            hints.TagTableScanComplete = false;
            hints.TagTableScanFailures.Add(new RoundTripTagTableScanFailure
            {
                PlcName = plcName,
                ScopePath = scopePath,
                FailureType = failureType
            });
        }

        private static string SafeAttribute(HardwareObject hardware, string name)
        {
            try
            {
                var value = hardware.GetAttribute(name);
                return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
            }
            catch (EngineeringNotSupportedException) { return null; }
            catch (Exception) { return null; }
        }

        private static T SafeRead<T>(Func<T> action) where T : class
        {
            try { return action(); }
            catch (Exception) { return null; }
        }

        private static List<string> Extend(IEnumerable<string> path, string value)
        {
            var result = new List<string>(path ?? Array.Empty<string>());
            result.Add(value ?? string.Empty);
            return result;
        }

        private static string JoinPath(IEnumerable<string> segments)
        {
            return string.Join("/", segments.Select(segment =>
                Uri.EscapeDataString((segment ?? string.Empty).Normalize(System.Text.NormalizationForm.FormC))));
        }

        private static void DeleteOwnedBlockExportDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "TiaGuard.RoundTripBlocks"))
                .TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(Path.GetDirectoryName(full), parent, StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParseExact(Path.GetFileName(full), "N", out _))
                throw new InvalidOperationException("Refusing to remove an unexpected block export directory.");
            if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
        }
    }
}
