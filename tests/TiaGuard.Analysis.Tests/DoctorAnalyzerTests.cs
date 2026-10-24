using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using TiaGuard.Analysis;
using Xunit;

namespace TiaGuard.Analysis.Tests;

public sealed class DoctorAnalyzerTests
{
    [Fact]
    public void Reads_the_current_fixture_without_a_tia_runtime_and_never_calls_its_coverage_complete()
    {
        var fixturePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "examples", "snapshot-fixture", "snapshot.json"));
        var report = DoctorAnalyzer.Analyze(
            File.ReadAllText(fixturePath),
            "examples/snapshot-fixture/snapshot.json");

        Assert.Equal("S7-1200-Motor-Reversing-Control", report.ProjectName);
        Assert.Equal("offline-copy", report.SourceKind);
        Assert.Equal("incomplete", report.Status);
        Assert.Contains(report.Findings, finding => finding.RuleId == "TIA.PLC.COMPILE.UNVERIFIED");
        Assert.Contains(report.Findings, finding => finding.RuleId == "TIA.BLOCK.CONSISTENCY.UNVERIFIED");
        Assert.DoesNotContain(report.Findings, finding => finding.RuleId is
            "TIA.IO.ADDRESS.DUPLICATE" or "TIA.IO.ADDRESS.OVERLAP");
    }

    [Fact]
    public void Reports_duplicate_and_partial_overlapping_structured_io_ranges_as_warnings()
    {
        var report = Analyze(
            Tag("Word", "PLC tags/Default", "I", 0, 16),
            Tag("ByteOverlap", "PLC tags/Default", "I", 1, 8),
            Tag("WordDuplicateA", "PLC tags/Default", "Q", 4, 16),
            Tag("WordDuplicateB", "PLC tags/Default", "Q", 4, 16));

        var duplicate = Assert.Single(report.Findings, finding =>
            finding.RuleId == "TIA.IO.ADDRESS.DUPLICATE");
        var overlap = Assert.Single(report.Findings, finding =>
            finding.RuleId == "TIA.IO.ADDRESS.OVERLAP");
        Assert.Equal("warning", duplicate.Severity);
        Assert.Equal("warning", overlap.Severity);
        Assert.Equal("applicable", overlap.Applicability.Status);
        Assert.Equal(2, duplicate.Evidence.Count);
        Assert.Contains("not classified as an engineering error", overlap.Message);
        Assert.DoesNotContain(report.Findings, finding =>
            (finding.RuleId == "TIA.IO.ADDRESS.DUPLICATE" || finding.RuleId == "TIA.IO.ADDRESS.OVERLAP") &&
            finding.Severity == "error");
    }

    [Fact]
    public void Does_not_infer_overlap_from_unsupported_raw_addresses()
    {
        var report = Analyze(
            Tag("Unsupported", "PLC tags/Default", "I", null, null, parseStatus: "unsupported", raw: "%IW0"),
            Tag("Known", "PLC tags/Default", "I", 0, 16));

        var finding = Assert.Single(report.Findings, item => item.RuleId == "TIA.IO.ADDRESS.UNSUPPORTED");
        Assert.Equal("info", finding.Severity);
        Assert.Equal("partial", finding.Applicability.Status);
        Assert.DoesNotContain(report.Findings, item =>
            item.RuleId is "TIA.IO.ADDRESS.DUPLICATE" or "TIA.IO.ADDRESS.OVERLAP");
    }

    [Fact]
    public void Duplicate_names_are_checked_within_plc_and_scope_only()
    {
        var report = Analyze(
            Tag("Shared", "PLC tags/Default"),
            Tag("shared", "PLC tags/Default"),
            Tag("Shared", "PLC tags/Other"));

        var finding = Assert.Single(report.Findings, item => item.RuleId == "TIA.TAG.NAME.DUPLICATE");
        Assert.Equal("error", finding.Severity);
        Assert.Contains("PLC tags/Default", finding.Message);
        Assert.Equal(2, finding.Evidence.Count);
    }

    [Fact]
    public void Distinguishes_explicitly_missing_unavailable_and_read_failed_comments()
    {
        var report = Analyze(
            Tag("Missing", "PLC tags/Default", commentStatus: "missing"),
            Tag("Failed", "PLC tags/Default", commentStatus: "read-failed"),
            Tag("Unavailable", "PLC tags/Default", commentStatus: "unavailable"),
            Tag("Present", "PLC tags/Default", commentStatus: "present"));

        Assert.Single(report.Findings, item => item.RuleId == "TIA.TAG.COMMENT.MISSING");
        Assert.Single(report.Findings, item => item.RuleId == "TIA.TAG.COMMENT.READ_FAILED");
        Assert.Single(report.Findings, item => item.RuleId == "TIA.TAG.COMMENT.UNAVAILABLE");
        Assert.DoesNotContain(report.Findings, item => item.Message.Contains("'Present'"));
        Assert.Equal("applicable", Assert.Single(report.Findings,
            item => item.RuleId == "TIA.TAG.COMMENT.MISSING").Applicability.Status);
        Assert.Equal("partial", Assert.Single(report.Findings,
            item => item.RuleId == "TIA.TAG.COMMENT.READ_FAILED").Applicability.Status);
    }

    [Fact]
    public void M_area_is_only_a_tag_declaration_inventory()
    {
        var report = Analyze(Tag("Marker", "PLC tags/Default", "M", 10, 1));
        var finding = Assert.Single(report.Findings,
            item => item.RuleId == "TIA.MEMORY.M_TAG_DECLARATION");

        Assert.Equal("info", finding.Severity);
        Assert.Contains("declared", finding.Message);
        Assert.Contains("does not establish program use", finding.Message);
        Assert.DoesNotContain("uses direct", finding.Message);
    }

    [Fact]
    public void Propagates_compile_errors_and_warnings_only_from_timestamped_observation()
    {
        var snapshot = Snapshot(
            new[] { Tag("Input", "PLC tags/Default") },
            compile: Compile("active-compile", "issues", "2026-09-27T00:00:00Z", 2, 3));
        var report = DoctorAnalyzer.Analyze(snapshot);

        Assert.Equal(2, GetCount(report, "TIA.PLC.COMPILE.ERRORS"));
        Assert.Equal(3, GetCount(report, "TIA.PLC.COMPILE.WARNINGS"));
        Assert.DoesNotContain(report.Findings, item => item.RuleId == "TIA.PLC.COMPILE.UNVERIFIED");
        Assert.Equal("incomplete", report.Status);
    }

    [Fact]
    public void Compile_counts_without_freshness_do_not_become_compile_findings()
    {
        var report = DoctorAnalyzer.Analyze(Snapshot(
            new[] { Tag("Input", "PLC tags/Default") },
            compile: Compile("not-observed", "unknown", null, null, null)));

        Assert.Contains(report.Findings, item => item.RuleId == "TIA.PLC.COMPILE.UNVERIFIED");
        Assert.DoesNotContain(report.Findings, item =>
            item.RuleId is "TIA.PLC.COMPILE.ERRORS" or "TIA.PLC.COMPILE.WARNINGS");
    }

    [Fact]
    public void Block_consistency_requires_fresh_export_hash_and_modification_time()
    {
        var unverified = DoctorAnalyzer.Analyze(Snapshot(new[] { Tag("Input", "PLC tags/Default") }));
        Assert.Contains(unverified.Findings, item => item.RuleId == "TIA.BLOCK.CONSISTENCY.UNVERIFIED");

        var block = Block(
            isConsistent: false,
            modifiedAtUtc: "2026-09-26T23:59:00Z",
            exportStatus: "exported",
            artifact: "blocks/main.xml",
            sha256: new string('a', 64));
        var verified = DoctorAnalyzer.Analyze(Snapshot(new[] { Tag("Input", "PLC tags/Default") }, block: block));
        var finding = Assert.Single(verified.Findings,
            item => item.RuleId == "TIA.BLOCK.CONSISTENCY.INCONSISTENT");
        Assert.Equal("warning", finding.Severity);
        Assert.DoesNotContain(verified.Findings, item =>
            item.RuleId == "TIA.BLOCK.CONSISTENCY.UNVERIFIED");
    }

    [Fact]
    public void Partial_capture_is_never_a_clean_pass()
    {
        var report = DoctorAnalyzer.Analyze(Snapshot(
            new[] { Tag("Input", "PLC tags/Default") },
            captureStatus: "partial"));

        Assert.NotEqual("pass", report.Status);
        Assert.Equal("incomplete", report.Status);
        Assert.Contains(report.Findings, item => item.RuleId == "TIA.SNAPSHOT.COLLECTION_INCOMPLETE");
    }

    [Fact]
    public void Rejects_unknown_fields_and_missing_contract_fields()
    {
        var json = Snapshot(new[] { Tag("Input", "PLC tags/Default") });
        Assert.Throws<JsonException>(() => SnapshotReader.Parse(
            json.Replace("\"schemaVersion\":\"1.0\"", "\"schemaVersion\":\"1.0\",\"unexpected\":true")));
        Assert.Throws<InvalidDataException>(() => SnapshotReader.Parse(
            json.Replace(",\"diagnostics\":[]", string.Empty)));
    }

    [Fact]
    public void Findings_carry_stable_ids_severity_applicability_and_snapshot_evidence()
    {
        var report = DoctorAnalyzer.Analyze(Snapshot(
            new[] { Tag("MissingComment", "PLC tags/Default", commentStatus: "missing") }),
            "snapshots/project.json");
        var finding = Assert.Single(report.Findings, item => item.RuleId == "TIA.TAG.COMMENT.MISSING");

        Assert.Matches("^TIA\\.[A-Z0-9_.-]+$", finding.RuleId);
        Assert.Equal("1.0", finding.SchemaVersion);
        Assert.Equal("warning", finding.Severity);
        Assert.NotEmpty(finding.Message);
        Assert.Equal("applicable", finding.Applicability.Status);
        Assert.NotEmpty(finding.Evidence);
        Assert.Equal("snapshot-object", finding.Evidence[0].Kind);
        Assert.Equal("snapshots/project.json", finding.Location!.Artifact);
        Assert.True(finding.Location.StartLine > 0);
    }

    [Fact]
    public void Finding_location_points_to_the_snapshot_tag_object_line()
    {
        var compact = Snapshot(
            new[] { Tag("MissingComment", "PLC tags/Default", commentStatus: "missing") });
        using var parsed = JsonDocument.Parse(compact);
        var pretty = JsonSerializer.Serialize(parsed.RootElement, new JsonSerializerOptions { WriteIndented = true });
        var lines = pretty.Split('\n');
        var report = DoctorAnalyzer.Analyze(pretty, "snapshots/line-test.json");
        var finding = Assert.Single(report.Findings,
            item => item.RuleId == "TIA.TAG.COMMENT.MISSING");

        Assert.Equal("{", lines[finding.Location!.StartLine - 1].Trim());
        Assert.Contains("tag:plc_1/0/missingcomment", lines[finding.Location.StartLine]);
    }

    private static int GetCount(DoctorReport report, string ruleId)
    {
        var evidence = Assert.Single(report.Findings, item => item.RuleId == ruleId).Evidence[0].Detail!;
        var number = int.Parse(evidence.Split('=').Last(), System.Globalization.CultureInfo.InvariantCulture);
        return number;
    }

    private static DoctorReport Analyze(params TagInput[] tags) =>
        DoctorAnalyzer.Analyze(Snapshot(tags));

    private static TagInput Tag(
        string name,
        string scopePath,
        string? area = null,
        long? byteOffset = null,
        long? bitWidth = null,
        string? parseStatus = null,
        string? raw = null,
        string commentStatus = "present")
    {
        return new TagInput(name, scopePath, area, byteOffset, null, bitWidth,
            parseStatus ?? (area == null ? "missing" : "parsed"), raw, commentStatus);
    }

    private static string Snapshot(
        TagInput[] tags,
        object? compile = null,
        object? block = null,
        string captureStatus = "complete")
    {
        var tagJson = tags.Select((tag, index) => (object)new
        {
            id = "tag:plc_1/" + index + "/" + tag.Name.ToLowerInvariant(),
            scopePath = tag.ScopePath,
            name = tag.Name,
            dataType = "Bool",
            address = new
            {
                raw = tag.Raw,
                parseStatus = tag.ParseStatus,
                area = tag.Area,
                byteOffset = tag.ByteOffset,
                bitOffset = tag.BitOffset,
                bitWidth = tag.BitWidth
            },
            comment = new
            {
                status = tag.CommentStatus,
                text = tag.CommentStatus == "present" ? "documented" : (string?)null
            }
        }).ToArray();
        var selectedCompile = compile ?? Compile("not-observed", "unknown", null, null, null);
        var selectedBlock = block ?? Block(null, null, "not-attempted", null, null);
        return JsonSerializer.Serialize(new
        {
            schemaVersion = "1.0",
            contractStatus = "draft",
            collector = new { name = "TIA-Guard", version = "0.1.0-dev", normalizationVersion = "1" },
            project = new { name = "TestProject", sourceKind = "offline-copy", projectVersion = (string?)null, contentId = (string?)null },
            tia = new { version = "V21", build = (string?)null },
            capture = new { status = captureStatus, capturedAtUtc = "2026-09-27T00:00:00Z", mode = "offline-copy" },
            devices = new[]
            {
                new
                {
                    id = "device:plc_1", parentId = (string?)null, plcId = "plc:plc_1",
                    name = "PLC_1", type = "CPU", engineeringPath = (string?)null,
                    orderNumber = (string?)null, firmware = (string?)null
                }
            },
            plcs = new[]
            {
                new
                {
                    id = "plc:plc_1", name = "PLC_1", deviceId = "device:plc_1",
                    blocks = new[] { selectedBlock },
                    tags = tagJson,
                    compile = selectedCompile
                }
            },
            diagnostics = Array.Empty<object>()
        });
    }

    private static object Compile(string mode, string status, string? observedAtUtc, int? errors, int? warnings) =>
        new { mode, status, observedAtUtc, errors, warnings };

    private static object Block(
        bool? isConsistent,
        string? modifiedAtUtc,
        string exportStatus,
        string? artifact,
        string? sha256) =>
        new
        {
            id = "block:plc_1/main",
            scopePath = "Program blocks",
            name = "Main",
            kind = "OB",
            number = (int?)1,
            language = "LAD",
            protection = "none",
            isConsistent,
            modifiedAtUtc,
            export = new { status = exportStatus, format = "xml", artifact, sha256, diagnosticCode = (string?)null }
        };

    private sealed record TagInput(
        string Name,
        string ScopePath,
        string? Area,
        long? ByteOffset,
        int? BitOffset,
        long? BitWidth,
        string ParseStatus,
        string? Raw,
        string CommentStatus);
}
