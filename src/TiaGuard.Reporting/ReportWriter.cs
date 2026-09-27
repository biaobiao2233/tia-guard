using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TiaGuard.Analysis;

namespace TiaGuard.Reporting;

public static class ReportWriter
{
    private const string SarifSchema = "https://json.schemastore.org/sarif-2.1.0.json";

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions CompactJsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static void WriteAll(DoctorReport report, string outputDirectory)
    {
        if (report == null) throw new ArgumentNullException(nameof(report));
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("An output directory is required.", nameof(outputDirectory));

        Directory.CreateDirectory(outputDirectory);
        WriteUtf8(Path.Combine(outputDirectory, "report.json"), ToJson(report));
        WriteUtf8(Path.Combine(outputDirectory, "report.md"), ToMarkdown(report));
        WriteUtf8(Path.Combine(outputDirectory, "tia-guard.sarif"), ToSarif(report));
    }

    public static string ToJson(DoctorReport report)
    {
        if (report == null) throw new ArgumentNullException(nameof(report));
        var counts = CountFindings(report);
        var document = new JsonReportDocument
        {
            SchemaVersion = "1.0",
            Status = report.Status,
            Project = new JsonProject
            {
                Name = report.ProjectName,
                SourceKind = report.SourceKind,
                TiaVersion = report.TiaVersion
            },
            Capture = new JsonCapture
            {
                Status = report.CaptureStatus,
                CapturedAtUtc = report.CapturedAtUtc
            },
            SnapshotArtifact = report.SnapshotUri,
            Summary = new JsonSummary
            {
                Errors = counts["error"],
                Warnings = counts["warning"],
                Infos = counts["info"],
                FindingCount = report.Findings.Count
            },
            Findings = report.Findings
        };

        return JsonSerializer.Serialize(document, JsonOptions) + "\n";
    }

    public static string ToMarkdown(DoctorReport report)
    {
        if (report == null) throw new ArgumentNullException(nameof(report));
        var counts = CountFindings(report);
        var builder = new StringBuilder();
        builder.AppendLine("# TIA-Guard report");
        builder.AppendLine();
        builder.AppendLine("- Status: **" + EscapeCell(report.Status) + "**");
        builder.AppendLine("- Project: " + EscapeCell(report.ProjectName));
        builder.AppendLine("- Source: " + EscapeCell(report.SourceKind));
        builder.AppendLine("- TIA version: " + EscapeCell(report.TiaVersion));
        builder.AppendLine("- Capture: " + EscapeCell(report.CaptureStatus) + " at " + EscapeCell(report.CapturedAtUtc));
        builder.AppendLine("- Snapshot: " + EscapeCell(report.SnapshotUri));
        builder.AppendLine("- Findings: " + report.Findings.Count +
            " (errors: " + counts["error"] + ", warnings: " + counts["warning"] +
            ", info: " + counts["info"] + ")");
        builder.AppendLine();

        if (report.Findings.Count == 0)
        {
            builder.AppendLine(report.Status == "pass"
                ? "No findings; all evaluated checks passed for this complete capture."
                : "No rule findings. Capture status prevents a clean pass.");
            return builder.ToString();
        }

        builder.AppendLine("| Severity | Rule | Applicability | Message | Evidence |");
        builder.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var finding in report.Findings)
        {
            var evidence = JsonSerializer.Serialize(finding.Evidence, CompactJsonOptions);
            builder.Append("| ").Append(EscapeCell(finding.Severity)).Append(" | ")
                .Append(EscapeCell(finding.RuleId)).Append(" | ")
                .Append(EscapeCell(finding.Applicability.Status)).Append(" | ")
                .Append(EscapeCell(finding.Message)).Append(" | ")
                .Append(EscapeCell(evidence)).AppendLine(" |");
        }

        return builder.ToString();
    }

    public static string ToSarif(DoctorReport report)
    {
        if (report == null) throw new ArgumentNullException(nameof(report));
        var rules = RuleCatalog.All;
        var indexes = rules.Select((rule, index) => new { rule.Id, Index = index })
            .ToDictionary(item => item.Id, item => item.Index, StringComparer.Ordinal);

        var results = report.Findings.Select(finding =>
        {
            if (finding.Location == null)
                throw new InvalidOperationException("Every SARIF result requires a Snapshot artifact location.");

            return new SarifResult
            {
                RuleId = finding.RuleId,
                RuleIndex = indexes[finding.RuleId],
                Level = ToSarifLevel(finding.Severity),
                Message = new SarifMessage { Text = finding.Message },
                Locations = new[]
                {
                    new SarifLocation
                    {
                        PhysicalLocation = ToPhysicalLocation(finding.Location)
                    }
                },
                PartialFingerprints = new SortedDictionary<string, string>(StringComparer.Ordinal)
                {
                    ["tiaGuardFindingFingerprint"] = Fingerprint(finding)
                },
                Properties = new SarifFindingProperties
                {
                    Applicability = finding.Applicability,
                    ObjectRef = finding.ObjectRef,
                    Evidence = finding.Evidence
                }
            };
        }).ToArray();

        var sarif = new SarifLog
        {
            Schema = SarifSchema,
            Version = "2.1.0",
            Runs = new[]
            {
                new SarifRun
                {
                    Tool = new SarifTool
                    {
                        Driver = new SarifDriver
                        {
                            Name = "TIA-Guard",
                            SemanticVersion = "0.1.0",
                            InformationUri = "https://github.com/biaobiao2233/tia-guard",
                            Rules = rules.Select(rule => new SarifRule
                            {
                                Id = rule.Id,
                                Name = rule.Name,
                                ShortDescription = new SarifMessage { Text = rule.Name },
                                FullDescription = new SarifMessage { Text = rule.Description },
                                DefaultConfiguration = new SarifRuleConfiguration
                                {
                                    Level = ToSarifLevel(rule.Severity)
                                },
                                Properties = new SarifRuleProperties { Tags = new[] { "TIA-Guard", "deterministic" } }
                            }).ToArray()
                        }
                    },
                    OriginalUriBaseIds = new SortedDictionary<string, SarifArtifactLocation>(StringComparer.Ordinal)
                    {
                        ["PROJECTROOT"] = new SarifArtifactLocation { Uri = "./" }
                    },
                    Results = results,
                    ColumnKind = "utf16CodeUnits",
                    Properties = new SarifRunProperties
                    {
                        ReportStatus = report.Status,
                        CaptureStatus = report.CaptureStatus,
                        SourceKind = report.SourceKind
                    }
                }
            }
        };

        return JsonSerializer.Serialize(sarif, JsonOptions) + "\n";
    }

    private static SarifPhysicalLocation ToPhysicalLocation(FindingLocation location) =>
        new SarifPhysicalLocation
        {
            ArtifactLocation = new SarifArtifactLocation
            {
                Uri = location.Artifact,
                UriBaseId = "PROJECTROOT"
            },
            Region = new SarifRegion
            {
                StartLine = location.StartLine,
                EndLine = location.EndLine
            }
        };

    private static string Fingerprint(FindingV1 finding)
    {
        var canonical = finding.RuleId + "\n" + string.Join("\n", finding.Evidence
            .Select(item => item.Kind + ":" + item.Ref)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal));
        using (var sha = SHA256.Create())
        {
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        }
    }

    private static Dictionary<string, int> CountFindings(DoctorReport report) =>
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["error"] = report.Findings.Count(finding => finding.Severity == "error"),
            ["warning"] = report.Findings.Count(finding => finding.Severity == "warning"),
            ["info"] = report.Findings.Count(finding => finding.Severity == "info")
        };

    private static string ToSarifLevel(string severity) => severity == "info" ? "note" : severity;

    private static string EscapeCell(string value) =>
        value.Replace("|", "\\|")
            .Replace("\r\n", "<br>")
            .Replace("\n", "<br>")
            .Replace("\r", "<br>");

    private static void WriteUtf8(string path, string content) =>
        File.WriteAllText(path, content, new UTF8Encoding(false));

    private sealed class JsonReportDocument
    {
        public string SchemaVersion { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public JsonProject Project { get; set; } = new JsonProject();
        public JsonCapture Capture { get; set; } = new JsonCapture();
        public string SnapshotArtifact { get; set; } = string.Empty;
        public JsonSummary Summary { get; set; } = new JsonSummary();
        public IReadOnlyList<FindingV1> Findings { get; set; } = Array.Empty<FindingV1>();
    }

    private sealed class JsonProject
    {
        public string Name { get; set; } = string.Empty;
        public string SourceKind { get; set; } = string.Empty;
        public string TiaVersion { get; set; } = string.Empty;
    }

    private sealed class JsonCapture
    {
        public string Status { get; set; } = string.Empty;
        public string CapturedAtUtc { get; set; } = string.Empty;
    }

    private sealed class JsonSummary
    {
        public int Errors { get; set; }
        public int Warnings { get; set; }
        public int Infos { get; set; }
        public int FindingCount { get; set; }
    }

    private sealed class SarifLog
    {
        [JsonPropertyName("$schema")]
        public string Schema { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public IReadOnlyList<SarifRun> Runs { get; set; } = Array.Empty<SarifRun>();
    }

    private sealed class SarifRun
    {
        public SarifTool Tool { get; set; } = new SarifTool();
        public SortedDictionary<string, SarifArtifactLocation> OriginalUriBaseIds { get; set; } =
            new SortedDictionary<string, SarifArtifactLocation>();
        public IReadOnlyList<SarifResult> Results { get; set; } = Array.Empty<SarifResult>();
        public string ColumnKind { get; set; } = string.Empty;
        public SarifRunProperties Properties { get; set; } = new SarifRunProperties();
    }

    private sealed class SarifRunProperties
    {
        public string ReportStatus { get; set; } = string.Empty;
        public string CaptureStatus { get; set; } = string.Empty;
        public string SourceKind { get; set; } = string.Empty;
    }

    private sealed class SarifTool
    {
        public SarifDriver Driver { get; set; } = new SarifDriver();
    }

    private sealed class SarifDriver
    {
        public string Name { get; set; } = string.Empty;
        public string SemanticVersion { get; set; } = string.Empty;
        public string InformationUri { get; set; } = string.Empty;
        public IReadOnlyList<SarifRule> Rules { get; set; } = Array.Empty<SarifRule>();
    }

    private sealed class SarifRule
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public SarifMessage ShortDescription { get; set; } = new SarifMessage();
        public SarifMessage FullDescription { get; set; } = new SarifMessage();
        public SarifRuleConfiguration DefaultConfiguration { get; set; } = new SarifRuleConfiguration();
        public SarifRuleProperties Properties { get; set; } = new SarifRuleProperties();
    }

    private sealed class SarifRuleConfiguration
    {
        public string Level { get; set; } = string.Empty;
    }

    private sealed class SarifRuleProperties
    {
        public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();
    }

    private sealed class SarifResult
    {
        public string RuleId { get; set; } = string.Empty;
        public int RuleIndex { get; set; }
        public string Level { get; set; } = string.Empty;
        public SarifMessage Message { get; set; } = new SarifMessage();
        public IReadOnlyList<SarifLocation> Locations { get; set; } = Array.Empty<SarifLocation>();
        public SortedDictionary<string, string> PartialFingerprints { get; set; } =
            new SortedDictionary<string, string>();
        public SarifFindingProperties Properties { get; set; } = new SarifFindingProperties();
    }

    private sealed class SarifFindingProperties
    {
        public FindingApplicability Applicability { get; set; } = null!;
        public string? ObjectRef { get; set; }
        public IReadOnlyList<FindingEvidence> Evidence { get; set; } = Array.Empty<FindingEvidence>();
    }

    private sealed class SarifMessage
    {
        public string Text { get; set; } = string.Empty;
    }

    private sealed class SarifLocation
    {
        public SarifPhysicalLocation PhysicalLocation { get; set; } = new SarifPhysicalLocation();
    }

    private sealed class SarifPhysicalLocation
    {
        public SarifArtifactLocation ArtifactLocation { get; set; } = new SarifArtifactLocation();
        public SarifRegion Region { get; set; } = new SarifRegion();
    }

    private sealed class SarifArtifactLocation
    {
        public string Uri { get; set; } = string.Empty;
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? UriBaseId { get; set; }
    }

    private sealed class SarifRegion
    {
        public int StartLine { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? EndLine { get; set; }
    }
}
