using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        internal static SnapshotV1 Extract(Project project, ProjectInfo info)
        {
            var snapshot = new SnapshotV1
            {
                Project = new SnapshotProject { Name = info.Name, Path = info.Path },
                Tia = new SnapshotTia { Version = info.TiaVersion, ProcessId = info.ProcessId }
            };
            foreach (var device in project.Devices)
                AppendDevice(device, snapshot);
            foreach (var group in project.DeviceGroups)
                AppendDeviceGroup(group, snapshot);

            snapshot.Devices = snapshot.Devices
                .OrderBy(device => device.Name, StringComparer.Ordinal)
                .ThenBy(device => device.Type, StringComparer.Ordinal)
                .ToList();
            snapshot.Plcs = snapshot.Plcs
                .OrderBy(plc => plc.Name, StringComparer.Ordinal)
                .ToList();
            return snapshot;
        }

        private static void AppendDeviceGroup(DeviceUserGroup group, SnapshotV1 snapshot)
        {
            foreach (var device in group.Devices)
                AppendDevice(device, snapshot);
            foreach (var child in group.Groups)
                AppendDeviceGroup(child, snapshot);
        }

        private static void AppendDevice(Device device, SnapshotV1 snapshot)
        {
            snapshot.Devices.Add(new SnapshotDevice
            {
                Name = device.Name ?? string.Empty,
                Type = device.TypeIdentifier ?? string.Empty,
                OrderNumber = FindAttribute(device, "ArticleNumber"),
                Firmware = FindAttribute(device, "FirmwareVersion")
            });
            foreach (var item in device.DeviceItems)
                AppendPlcSoftware(device.Name, item, snapshot.Plcs);
        }

        private static void AppendPlcSoftware(string deviceName, DeviceItem item, List<SnapshotPlc> plcs)
        {
            var software = item.GetService<SoftwareContainer>()?.Software as PlcSoftware;
            if (software != null)
            {
                var plc = new SnapshotPlc
                {
                    Name = string.Equals(deviceName, software.Name, StringComparison.Ordinal)
                        ? deviceName : deviceName + "/" + software.Name
                };
                AppendBlocks(software.BlockGroup, plc.Blocks);
                foreach (var systemGroup in software.BlockGroup.SystemBlockGroups)
                    AppendSystemBlocks(systemGroup, plc.Blocks);
                AppendTags(software.TagTableGroup, plc.Tags);
                plc.Blocks = plc.Blocks
                    .OrderBy(block => block.Name, StringComparer.Ordinal)
                    .ThenBy(block => block.Kind, StringComparer.Ordinal)
                    .ThenBy(block => block.Language, StringComparer.Ordinal)
                    .ToList();
                plc.Tags = plc.Tags
                    .OrderBy(tag => tag.Name, StringComparer.Ordinal)
                    .ThenBy(tag => tag.Address, StringComparer.Ordinal)
                    .ThenBy(tag => tag.DataType, StringComparer.Ordinal)
                    .ToList();
                plcs.Add(plc);
            }
            foreach (var child in item.DeviceItems)
                AppendPlcSoftware(deviceName, child, plcs);
        }

        private static void AppendBlocks(PlcBlockGroup group, List<SnapshotBlock> blocks)
        {
            foreach (var block in group.Blocks)
                blocks.Add(new SnapshotBlock
                {
                    Name = block.Name ?? string.Empty,
                    Kind = block.GetType().Name,
                    Language = ReadLanguage(block)
                });
            foreach (var child in group.Groups)
                AppendBlocks(child, blocks);
        }

        private static void AppendSystemBlocks(PlcSystemBlockGroup group, List<SnapshotBlock> blocks)
        {
            foreach (var block in group.Blocks)
                blocks.Add(new SnapshotBlock
                {
                    Name = block.Name ?? string.Empty,
                    Kind = block.GetType().Name,
                    Language = ReadLanguage(block)
                });
            foreach (var child in group.Groups)
                AppendSystemBlocks(child, blocks);
        }

        private static void AppendTags(PlcTagTableGroup group, List<SnapshotTag> tags)
        {
            foreach (var table in group.TagTables)
                foreach (var tag in table.Tags)
                    tags.Add(new SnapshotTag
                    {
                        Name = tag.Name ?? string.Empty,
                        DataType = tag.DataTypeName ?? string.Empty,
                        Address = tag.LogicalAddress,
                        Comment = ReadComment(tag.Comment)
                    });
            foreach (var child in group.Groups)
                AppendTags(child, tags);
        }

        private static string ReadComment(MultilingualText comment)
        {
            if (comment == null) return null;
            // The contract has one comment string. Select the first populated
            // language in ordinal language order for reproducible snapshots.
            return comment.Items
                .OrderBy(item => item.Language?.ToString(), StringComparer.Ordinal)
                .Select(item => item.Text)
                .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
        }

        private static string ReadLanguage(PlcBlock block)
        {
            try { return block.ProgrammingLanguage.ToString(); }
            catch (EngineeringNotSupportedException) { return null; }
        }

        private static string FindAttribute(HardwareObject hardware, string name)
        {
            var value = ReadOptionalAttribute(hardware, name);
            if (value != null) return value;
            foreach (var item in hardware.DeviceItems)
            {
                value = FindAttribute(item, name);
                if (value != null) return value;
            }
            return null;
        }

        private static string ReadOptionalAttribute(HardwareObject hardware, string name)
        {
            try
            {
                var value = hardware.GetAttribute(name);
                return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
            }
            catch (EngineeringNotSupportedException)
            {
                return null;
            }
        }
    }
}
