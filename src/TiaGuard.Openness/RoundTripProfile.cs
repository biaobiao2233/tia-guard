using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TiaGuard.Openness
{
    // An explicit, evidenced profile, not a prefix-based claim about a CPU family.
    internal static class RoundTripProfile
    {
        internal const string CpuTypeIdentifier = "OrderNumber:6ES7 212-1AE40-0XB0/V4.7";

        internal static bool SupportsCpu(string identifier) => identifier == CpuTypeIdentifier;

        // Observed on the fresh evidenced V21 CPU: a built-in, empty force table.
        // Force values, renamed tables and additional tables are not reconstructable.
        internal static bool IsDefaultForceTable(string name, int entries) =>
            name == "Force table" && entries == 0;

        private static readonly Dictionary<string, int> TagWidths =
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["Bool"] = 1, ["Byte"] = 8, ["Char"] = 8, ["SInt"] = 8, ["USInt"] = 8,
                ["Word"] = 16, ["Int"] = 16, ["UInt"] = 16,
                ["DWord"] = 32, ["DInt"] = 32, ["UDInt"] = 32, ["Real"] = 32
            };

        internal static bool SupportsTag(string dataType, string rawAddress)
        {
            if (dataType == null || !TagWidths.TryGetValue(dataType, out var width)) return false;
            var address = SnapshotAddressParser.Parse(rawAddress);
            return address.ParseStatus == "parsed" && address.BitWidth == width;
        }

        internal static string Segment(string name) =>
            Uri.EscapeDataString((name ?? string.Empty).Normalize(NormalizationForm.FormC));

        internal static bool SafeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name.EndsWith(".") ||
                name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0) return false;
            var stem = name.Split('.')[0].ToUpperInvariant();
            if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL") return false;
            return !(stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) &&
                stem[3] >= '1' && stem[3] <= '9');
        }
    }
}
