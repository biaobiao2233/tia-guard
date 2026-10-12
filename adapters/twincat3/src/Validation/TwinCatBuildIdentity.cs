using System.Text.RegularExpressions;

namespace TiaGuard.TwinCat.Validation;

public static class TwinCatBuildIdentity
{
    // Doctor uses build/revision; tsproj records the TwinCAT 3.1 product prefix as well.
    public static string Normalize(string value)
    {
        var match = Regex.Match(value, @"^(?:3\.1\.)?(?<build>[0-9]{4}\.[0-9]+)$", RegexOptions.CultureInvariant);
        if (!match.Success) throw new NotSupportedException("Unproven TwinCAT 3.1 build identity: " + value);
        return match.Groups["build"].Value;
    }
}
