using System.Text.RegularExpressions;

namespace TiaGuard.TwinCat.Validation;

public static class OutputDiagnosticGuard
{
    // Detect known compiler diagnostic syntax. Localized/opaque capture never becomes a substitute for Error List/LastBuildInfo.
    private static readonly Regex Diagnostic = new(@"\b(?<severity>error|warning)\s+(?:(?:[A-Za-z]{1,4}[0-9]{3,8}|0x[0-9A-Fa-f]+)\s*)?:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static IReadOnlyList<string> Check(string output, IReadOnlyList<CapturedDiagnostic> items)
    {
        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).ToArray();
        var errors = lines.Count(x => Diagnostic.Matches(x).Any(m => m.Groups["severity"].Value.Equals("error", StringComparison.OrdinalIgnoreCase)));
        var warnings = lines.Count(x => Diagnostic.Matches(x).Any(m => m.Groups["severity"].Value.Equals("warning", StringComparison.OrdinalIgnoreCase)));
        var problems = new List<string>();
        if (errors > items.Count(x => x.Severity == DiagnosticSeverity.Error)) problems.Add("Build Output contains compiler errors missing from the captured Error List.");
        if (warnings > items.Count(x => x.Severity == DiagnosticSeverity.Warning)) problems.Add("Build Output contains compiler warnings missing from the captured Error List.");
        return problems;
    }
}
