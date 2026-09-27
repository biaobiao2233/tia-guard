using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TiaGuard.Openness
{
    public static class SnapshotAddressParser
    {
        private static readonly Regex Bit = new Regex(
            @"^%?(?<area>[IQM])X?(?<byte>[0-9]+)\.(?<bit>[0-7])$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Sized = new Regex(
            @"^%?(?<area>[IQM])(?<size>[BWD])(?<byte>[0-9]+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static SnapshotAddress Parse(string raw)
        {
            var result = new SnapshotAddress { Raw = raw };
            if (string.IsNullOrWhiteSpace(raw)) return result;
            var candidate = raw.Trim();
            var bit = Bit.Match(candidate);
            if (bit.Success && int.TryParse(bit.Groups["byte"].Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out var byteOffset))
            {
                result.ParseStatus = "parsed";
                result.Area = bit.Groups["area"].Value.ToUpperInvariant();
                result.ByteOffset = byteOffset;
                result.BitOffset = int.Parse(bit.Groups["bit"].Value, CultureInfo.InvariantCulture);
                result.BitWidth = 1;
                return result;
            }
            var sized = Sized.Match(candidate);
            if (sized.Success && int.TryParse(sized.Groups["byte"].Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out byteOffset))
            {
                result.ParseStatus = "parsed";
                result.Area = sized.Groups["area"].Value.ToUpperInvariant();
                result.ByteOffset = byteOffset;
                result.BitOffset = 0;
                switch (sized.Groups["size"].Value.ToUpperInvariant())
                {
                    case "B": result.BitWidth = 8; break;
                    case "W": result.BitWidth = 16; break;
                    case "D": result.BitWidth = 32; break;
                }
                return result;
            }
            result.ParseStatus = "unsupported";
            return result;
        }
    }
}
