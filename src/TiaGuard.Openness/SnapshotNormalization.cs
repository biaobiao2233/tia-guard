using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TiaGuard.Openness
{
    public static class SnapshotNormalization
    {
        // Collector-owned identity. It is stable while the engineering group/name
        // path is stable, but it is not an Openness persistent object GUID.
        public static string MakeId(string kind, IEnumerable<string> pathSegments)
        {
            if (string.IsNullOrWhiteSpace(kind)) throw new ArgumentException("An ID kind is required.", nameof(kind));
            if (pathSegments == null) throw new ArgumentNullException(nameof(pathSegments));
            var segments = pathSegments.Select(segment =>
                Uri.EscapeDataString((segment ?? string.Empty).Normalize(NormalizationForm.FormC))).ToArray();
            if (segments.Length == 0) throw new ArgumentException("At least one path segment is required.", nameof(pathSegments));
            return kind + ":" + string.Join("/", segments);
        }

        // Hash only normalized engineering content. Capture time, local paths,
        // process IDs, export artifact paths, and observation time are excluded.
        public static string ComputeContentId(SnapshotV1 snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var text = new StringBuilder();
            Add(text, snapshot.SchemaVersion);
            Add(text, snapshot.Collector.NormalizationVersion);
            Add(text, snapshot.Project.Name);
            Add(text, snapshot.Project.ProjectVersion);
            Add(text, snapshot.Devices.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var device in snapshot.Devices.OrderBy(value => value.Id, StringComparer.Ordinal))
            {
                Add(text, device.Id); Add(text, device.ParentId); Add(text, device.PlcId);
                Add(text, device.Name); Add(text, device.Type); Add(text, device.EngineeringPath);
                Add(text, device.OrderNumber); Add(text, device.Firmware);
            }
            Add(text, snapshot.Plcs.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var plc in snapshot.Plcs.OrderBy(value => value.Id, StringComparer.Ordinal))
            {
                Add(text, plc.Id); Add(text, plc.Name); Add(text, plc.DeviceId);
                Add(text, plc.Blocks.Count.ToString(CultureInfo.InvariantCulture));
                foreach (var block in plc.Blocks.OrderBy(value => value.Id, StringComparer.Ordinal))
                {
                    Add(text, block.Id); Add(text, block.ScopePath); Add(text, block.Name);
                    Add(text, block.Kind); Add(text, Format(block.Number)); Add(text, block.Language);
                    Add(text, block.Protection); Add(text, Format(block.IsConsistent));
                    Add(text, block.ModifiedAtUtc); Add(text, block.Export.Status);
                    Add(text, block.Export.Format); Add(text, block.Export.Sha256);
                }
                Add(text, plc.Tags.Count.ToString(CultureInfo.InvariantCulture));
                foreach (var tag in plc.Tags.OrderBy(value => value.Id, StringComparer.Ordinal))
                {
                    Add(text, tag.Id); Add(text, tag.ScopePath); Add(text, tag.Name);
                    Add(text, tag.DataType); Add(text, tag.Address.Raw);
                    Add(text, tag.Address.ParseStatus); Add(text, tag.Address.Area);
                    Add(text, Format(tag.Address.ByteOffset)); Add(text, Format(tag.Address.BitOffset));
                    Add(text, Format(tag.Address.BitWidth)); Add(text, tag.Comment.Status);
                    Add(text, tag.Comment.Text);
                }
                Add(text, plc.Compile.Mode); Add(text, plc.Compile.Status);
                Add(text, Format(plc.Compile.Errors)); Add(text, Format(plc.Compile.Warnings));
            }
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()));
                return "sha256:" + BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static string Format(int? value) => value?.ToString(CultureInfo.InvariantCulture);
        private static string Format(bool? value) => value.HasValue ? (value.Value ? "true" : "false") : null;

        private static void Add(StringBuilder text, string value)
        {
            if (value == null) text.Append("-1:");
            else
            {
                var normalized = value.Normalize(NormalizationForm.FormC);
                text.Append(normalized.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(normalized);
            }
        }
    }
}
