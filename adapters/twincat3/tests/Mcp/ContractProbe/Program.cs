using System.Text.Json;
using TwinCat3.Contracts;

var valid = new CompilationReport
{
    State = CompilationState.Success, BuildStarted = true, BuildCompleted = true,
    FailedProjectCount = 0, ErrorCount = 0, WarningCount = 0,
    DiagnosticsComplete = true, OutputCaptureComplete = true,
    Configuration = "Release", Platform = "TwinCAT RT (x64)",
    ProjectIdentity = "identity", InputHash = new string('a', 64)
};
var compilations = new Dictionary<string, CompilationReport>
{
    ["valid"] = valid,
    ["not-started"] = valid with { BuildStarted = false },
    ["not-completed"] = valid with { BuildCompleted = false },
    ["failed-project"] = valid with { FailedProjectCount = 1 },
    ["partial-diagnostics"] = valid with { DiagnosticsComplete = false },
    ["partial-output"] = valid with { OutputCaptureComplete = false },
    ["null-count"] = valid with { WarningCount = null },
    ["unknown-state"] = valid with { State = CompilationState.Unknown },
    ["missing-identity"] = valid with { ProjectIdentity = null },
    ["missing-hash"] = valid with { InputHash = null },
    ["missing-platform"] = valid with { Platform = null },
    ["failure"] = valid with { Failures = new[] { "capture failed" } },
    ["warning-mismatch"] = valid with { WarningCount = 1 },
    ["single-error"] = valid with { Diagnostics = new[] { new CompilationDiagnostic { Severity = DiagnosticSeverity.Error, Message = "one error", Correlated = true } } },
    ["uncorrelated"] = valid with { Diagnostics = new[] { new CompilationDiagnostic { Severity = DiagnosticSeverity.Message, Message = "foreign", Correlated = false } } },
    ["unknown-severity"] = valid with { Diagnostics = new[] { new CompilationDiagnostic { Severity = DiagnosticSeverity.Unknown, Message = "unknown", Correlated = true } } },
    ["valid-warning"] = valid with { WarningCount = 1, Diagnostics = new[] { new CompilationDiagnostic { Severity = DiagnosticSeverity.Warning, Message = "warning", Correlated = true } } }
};
var verified = new VerificationReport { Verdict = VerificationVerdict.Pass, Complete = true };
var verifications = new Dictionary<string, VerificationReport>
{
    ["valid"] = verified,
    ["unknown"] = verified with { Verdict = VerificationVerdict.Unknown },
    ["fail"] = verified with { Verdict = VerificationVerdict.Fail },
    ["partial"] = verified with { Complete = false },
    ["difference"] = verified with { Differences = new[] { "ST changed" } },
    ["unsupported"] = verified with { Unknowns = new[] { "opaque object" } }
};
Console.WriteLine(JsonSerializer.Serialize(new
{
    compilations = compilations.Select(p => new { name = p.Key, report = p.Value, passed = p.Value.Passed }),
    verifications = verifications.Select(p => new { name = p.Key, report = p.Value, passed = p.Value.Passed })
}, ContractJson.Options));
