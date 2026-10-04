using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using TiaGuard.Analysis;
using TiaGuard.Reporting;
using Xunit;

namespace TiaGuard.Reporting.Tests;

public sealed class ReportWriterTests
{
    [Fact]
    public void Writes_finding_v1_json_markdown_and_sarif_deterministically()
    {
        var report = DoctorAnalyzer.Analyze(SnapshotWithMissingComment(), "snapshots/project.json");
        var json = ReportWriter.ToJson(report);
        var markdown = ReportWriter.ToMarkdown(report);
        var sarif = ReportWriter.ToSarif(report);

        Assert.Equal(json, ReportWriter.ToJson(report));
        Assert.Equal(markdown, ReportWriter.ToMarkdown(report));
        Assert.Equal(sarif, ReportWriter.ToSarif(report));

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("1.0", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("findings", root.GetProperty("status").GetString());
        Assert.Equal("offline-copy", root.GetProperty("project").GetProperty("sourceKind").GetString());
        var finding = Assert.Single(root.GetProperty("findings").EnumerateArray());
        Assert.Equal("1.0", finding.GetProperty("schemaVersion").GetString());
        Assert.Equal("TIA.TAG.COMMENT.MISSING", finding.GetProperty("ruleId").GetString());
        Assert.Equal("warning", finding.GetProperty("severity").GetString());
        Assert.True(finding.GetProperty("evidence").GetArrayLength() > 0);
        var findingLocation = finding.GetProperty("location");
        Assert.Equal("snapshots/project.json", findingLocation.GetProperty("artifact").GetString());
        Assert.True(findingLocation.GetProperty("startLine").GetInt32() > 0);
        Assert.False(findingLocation.TryGetProperty("endLine", out _));
        Assert.Contains("TIA.TAG.COMMENT.MISSING", markdown);
        Assert.Contains("Status: **findings**", markdown);
    }

    [Fact]
    public void Sarif_uses_2_1_0_rules_snapshot_locations_and_stable_fingerprints()
    {
        var report = DoctorAnalyzer.Analyze(SnapshotWithMissingComment(), "snapshots/project.json");
        var sarif = ReportWriter.ToSarif(report);
        using var document = JsonDocument.Parse(sarif);
        var root = document.RootElement;
        var run = Assert.Single(root.GetProperty("runs").EnumerateArray());
        var rules = run.GetProperty("tool").GetProperty("driver").GetProperty("rules");
        var result = Assert.Single(run.GetProperty("results").EnumerateArray());
        var location = Assert.Single(result.GetProperty("locations").EnumerateArray())
            .GetProperty("physicalLocation");

        Assert.Equal("https://json.schemastore.org/sarif-2.1.0.json", root.GetProperty("$schema").GetString());
        Assert.Equal("2.1.0", root.GetProperty("version").GetString());
        Assert.Equal(RuleCatalog.All.Count, rules.GetArrayLength());
        Assert.Equal("TIA.TAG.COMMENT.MISSING", result.GetProperty("ruleId").GetString());
        Assert.Equal("warning", result.GetProperty("level").GetString());
        Assert.Equal("snapshots/project.json",
            location.GetProperty("artifactLocation").GetProperty("uri").GetString());
        Assert.True(location.GetProperty("region").GetProperty("startLine").GetInt32() > 0);
        Assert.Equal("applicable", result.GetProperty("properties")
            .GetProperty("applicability").GetProperty("status").GetString());
        var fingerprint = result.GetProperty("partialFingerprints")
            .GetProperty("tiaGuardFindingFingerprint").GetString()!;
        Assert.Equal(64, fingerprint.Length);
        Assert.Equal(fingerprint, JsonDocument.Parse(ReportWriter.ToSarif(report)).RootElement
            .GetProperty("runs")[0].GetProperty("results")[0].GetProperty("partialFingerprints")
            .GetProperty("tiaGuardFindingFingerprint").GetString());
    }

    [Fact]
    public void Sarif_maps_info_findings_to_note_and_keeps_them_non_error()
    {
        var report = DoctorAnalyzer.Analyze(SnapshotWithMDeclaration());
        using var document = JsonDocument.Parse(ReportWriter.ToSarif(report));
        var results = document.RootElement.GetProperty("runs")[0].GetProperty("results")
            .EnumerateArray().ToArray();
        var result = Assert.Single(results, item =>
            item.GetProperty("ruleId").GetString() == "TIA.MEMORY.M_TAG_DECLARATION");

        Assert.Equal("note", result.GetProperty("level").GetString());
        Assert.DoesNotContain(results, item =>
            item.GetProperty("ruleId").GetString() == "TIA.MEMORY.M_TAG_DECLARATION" &&
            item.GetProperty("level").GetString() == "error");
    }

    [Fact]
    public void Writes_the_three_required_report_files()
    {
        var report = DoctorAnalyzer.Analyze(SnapshotWithMissingComment());
        var output = Path.Combine(Path.GetTempPath(), "tia-guard-report-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            ReportWriter.WriteAll(report, output);

            Assert.True(File.Exists(Path.Combine(output, "report.json")));
            Assert.True(File.Exists(Path.Combine(output, "report.md")));
            Assert.True(File.Exists(Path.Combine(output, "tia-guard.sarif")));
            Assert.Equal(3, Directory.GetFiles(output).Length);
            Assert.StartsWith("{", File.ReadAllText(Path.Combine(output, "report.json")).TrimStart());
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
        }
    }

    private static string SnapshotWithMissingComment() =>
        Snapshot(
            tagAddressArea: null,
            commentStatus: "missing",
            compile: new { mode = "active-compile", status = "clean", observedAtUtc = "2026-09-27T00:00:00Z", errors = (int?)0, warnings = (int?)0 });

    private static string SnapshotWithMDeclaration() =>
        Snapshot(
            tagAddressArea: "M",
            commentStatus: "present",
            compile: new { mode = "active-compile", status = "clean", observedAtUtc = "2026-09-27T00:00:00Z", errors = (int?)0, warnings = (int?)0 });

    private static string Snapshot(string? tagAddressArea, string commentStatus, object compile)
    {
        var mArea = tagAddressArea == "M";
        return JsonSerializer.Serialize(new
        {
            schemaVersion = "1.0",
            contractStatus = "draft",
            collector = new { name = "TIA-Guard", version = "0.1.0-dev", normalizationVersion = "1" },
            project = new { name = "ReportTest", sourceKind = "offline-copy", projectVersion = (string?)null, contentId = (string?)null },
            tia = new { version = "V21", build = (string?)null },
            capture = new { status = "complete", capturedAtUtc = "2026-09-27T00:00:00Z", mode = "offline-copy" },
            devices = new[]
            {
                new
                {
                    id = "device:one", parentId = (string?)null, plcId = "plc:one", name = "PLC_1",
                    type = "CPU", engineeringPath = (string?)null, orderNumber = (string?)null, firmware = (string?)null
                }
            },
            plcs = new[]
            {
                new
                {
                    id = "plc:one", name = "PLC_1", deviceId = "device:one",
                    blocks = new[]
                    {
                        new
                        {
                            id = "block:one/main", scopePath = "Program blocks", name = "Main", kind = "OB",
                            number = (int?)1, language = "LAD", protection = "none", isConsistent = (bool?)true,
                            modifiedAtUtc = "2026-09-26T23:59:00Z",
                            export = new { status = "exported", format = "xml", artifact = "blocks/main.xml", sha256 = new string('b', 64), diagnosticCode = (string?)null }
                        }
                    },
                    tags = new[]
                    {
                        new
                        {
                            id = "tag:one/value", scopePath = "PLC tags/Default", name = "Value", dataType = "Bool",
                            address = new
                            {
                                raw = mArea ? "%M10.0" : null,
                                parseStatus = mArea ? "parsed" : "missing",
                                area = tagAddressArea,
                                byteOffset = mArea ? (long?)10 : null,
                                bitOffset = mArea ? (int?)0 : null,
                                bitWidth = mArea ? (long?)1 : null
                            },
                            comment = new { status = commentStatus, text = commentStatus == "present" ? "documented" : (string?)null }
                        }
                    },
                    compile
                }
            },
            diagnostics = Array.Empty<object>()
        });
    }
}
