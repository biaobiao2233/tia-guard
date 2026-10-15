using System.Globalization;
using System.Text.RegularExpressions;

namespace TiaGuard.TwinCat.Validation;

public static class BuildOutputDiagnostics
{
    private static readonly Regex Line = new(@"^(?<file>.+)\((?<line>[0-9]+)\)\s*:\s*(?<severity>error|warning):\s*(?<code>C[0-9]+):\s*(?<message>.+)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    // This never establishes completeness or changes the verdict. Every added field comes
    // from the fresh native compiler log; no error code or source location is inferred.
    public static IReadOnlyList<CapturedDiagnostic> Supplement(IReadOnlyList<CapturedDiagnostic> original,
        string output, string stageRoot, string plcName)
    {
        var result = original.ToList();
        var root = Path.GetFullPath(stageRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var match = Line.Match(line);
            if (!match.Success || !int.TryParse(match.Groups["line"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 1) continue;
            var file = match.Groups["file"].Value;
            if (!Path.IsPathFullyQualified(file) || !Path.GetFullPath(file).StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
            var severity = match.Groups["severity"].Value.Equals("error", StringComparison.OrdinalIgnoreCase) ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning;
            var message = match.Groups["message"].Value;
            var code = match.Groups["code"].Value;
            if (result.Any(d => d.Code == code && d.File == file && d.Line == number && d.Message == message)) continue;
            var item = new CapturedDiagnostic(severity, message, plcName, file, number, null, code,
                DiagnosticCorrelation.CurrentBuild, "DTE.BuildOutput");
            var index = result.FindIndex(d => d.Severity == severity && d.Message == message && d.Code == null);
            if (index >= 0) result[index] = item; else result.Add(item);
        }
        return result;
    }
}
