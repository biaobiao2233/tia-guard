using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace TiaGuard.Analysis;

public static class DoctorAnalyzer
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static DoctorReport Analyze(string snapshotJson, string artifactUri = "snapshot.json") =>
        Analyze(SnapshotReader.Parse(snapshotJson, artifactUri));

    public static DoctorReport Analyze(SnapshotDocument document)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));

        var snapshot = document.Snapshot;
        var findings = new List<FindingV1>();

        for (var plcIndex = 0; plcIndex < snapshot.Plcs!.Count; plcIndex++)
        {
            var plc = snapshot.Plcs[plcIndex]!;
            var tags = plc.Tags!.Select((tag, tagIndex) =>
                new TagReference(plcIndex, tagIndex, plc, tag!)).ToArray();

            AddAddressFindings(document, tags, findings);
            AddCommentFindings(document, tags, findings);
            AddDuplicateNames(document, tags, findings);
            AddMemoryInventory(document, tags, findings);
            AddCompileFindings(document, plcIndex, plc, findings);
            AddBlockConsistencyFindings(document, plcIndex, plc, findings);
        }

        AddCaptureFinding(document, findings);
        AddDiagnosticFindings(document, findings);

        var stableFindings = findings
            .OrderBy(finding => finding.RuleId, StringComparer.Ordinal)
            .ThenBy(StableFindingKey, StringComparer.Ordinal)
            .ToArray();

        var incomplete = snapshot.Capture!.Status != "complete" ||
                         stableFindings.Any(finding => finding.Applicability.Status == "partial");
        var hasIssues = stableFindings.Any(finding =>
            finding.Severity == "error" || finding.Severity == "warning");
        var status = snapshot.Capture.Status == "failed"
            ? "failed"
            : incomplete
                ? "incomplete"
                : hasIssues
                    ? "findings"
                    : "pass";

        return new DoctorReport(
            status,
            snapshot.Project!.Name!,
            snapshot.Project.SourceKind!,
            snapshot.Tia!.Version!,
            snapshot.Capture.Status!,
            snapshot.Capture.CapturedAtUtc!,
            document.ArtifactUri,
            stableFindings);
    }

    private static void AddAddressFindings(
        SnapshotDocument document,
        IReadOnlyList<TagReference> tags,
        ICollection<FindingV1> findings)
    {
        var supported = new List<AddressReference>();
        foreach (var tag in tags)
        {
            var address = tag.Tag.Address!;
            if (address.ParseStatus == "missing") continue;
            if (address.ParseStatus == "unsupported")
            {
                if (LooksLikeIo(address.Raw) || IsIoArea(address.Area))
                {
                    AddUnsupportedAddress(document, tag, findings);
                }
                continue;
            }

            var area = address.Area?.ToUpperInvariant();
            if (area != "I" && area != "Q")
            {
                if (LooksLikeIo(address.Raw)) AddUnsupportedAddress(document, tag, findings);
                continue;
            }
            if (!TryGetRange(address, out var range))
            {
                AddUnsupportedAddress(document, tag, findings);
                continue;
            }
            supported.Add(new AddressReference(tag, area, range.Start, range.End));
        }

        var ordered = supported
            .OrderBy(item => item.Area, StringComparer.Ordinal)
            .ThenBy(item => item.Start)
            .ThenBy(item => item.End)
            .ThenBy(item => item.Tag.Tag.ScopePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Tag.Tag.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Tag.Tag.Id, StringComparer.Ordinal)
            .ToArray();

        var exactGroups = ordered
            .GroupBy(item => new { item.Area, item.Start, item.End })
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.Key.Area, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Start)
            .ThenBy(group => group.Key.End);

        foreach (var group in exactGroups)
        {
            var members = group.ToArray();
            findings.Add(CreateFinding(
                "TIA.IO.ADDRESS.DUPLICATE",
                "warning",
                "PLC '" + members[0].Tag.Plc.Name + "' has " + members.Length.ToString(CultureInfo.InvariantCulture) +
                " tag declarations with the same parsed " + group.Key.Area + " I/O range.",
                "applicable",
                null,
                members.Select(member => TagEvidence(member.Tag, "area=" + member.Area +
                    "; startBit=" + member.Start.ToString(CultureInfo.InvariantCulture) +
                    "; endBit=" + member.End.ToString(CultureInfo.InvariantCulture))),
                document.GetTagLocation(members[0].Tag.PlcIndex, members[0].Tag.TagIndex)));
        }

        for (var i = 0; i < ordered.Length; i++)
        {
            var left = ordered[i];
            for (var j = i + 1; j < ordered.Length; j++)
            {
                var right = ordered[j];
                if (right.Area != left.Area) break;
                if (right.Start >= left.End) break;
                if (right.End <= left.Start || (right.Start == left.Start && right.End == left.End)) continue;

                findings.Add(CreateFinding(
                    "TIA.IO.ADDRESS.OVERLAP",
                    "warning",
                    "PLC '" + left.Tag.Plc.Name + "' has partially overlapping parsed " + left.Area +
                    " I/O ranges for tag declarations '" + left.Tag.Tag.Name + "' and '" + right.Tag.Tag.Name +
                    "'. This overlap is reported as evidence and is not classified as an engineering error.",
                    "applicable",
                    null,
                    new[]
                    {
                        TagEvidence(left.Tag, RangeDetail(left)),
                        TagEvidence(right.Tag, RangeDetail(right))
                    },
                    document.GetTagLocation(left.Tag.PlcIndex, left.Tag.TagIndex)));
            }
        }
    }

    private static void AddUnsupportedAddress(
        SnapshotDocument document,
        TagReference tag,
        ICollection<FindingV1> findings)
    {
        findings.Add(CreateFinding(
            "TIA.IO.ADDRESS.UNSUPPORTED",
            "info",
            "PLC '" + tag.Plc.Name + "' tag '" + tag.Tag.Name +
            "' has an I/O address that cannot be evaluated with the supported parsed address fields.",
            "partial",
            "Address parse semantics are unsupported or incomplete.",
            new[] { TagEvidence(tag, tag.Tag.Address!.Raw ?? "raw address is absent") },
            document.GetTagLocation(tag.PlcIndex, tag.TagIndex)));
    }

    private static bool LooksLikeIo(string? raw)
    {
        if (raw == null) return false;
        var value = raw.TrimStart();
        return value.StartsWith("%I", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("%Q", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIoArea(string? area) =>
        string.Equals(area, "I", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(area, "Q", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetRange(SnapshotAddress address, out BitRange range)
    {
        range = default;
        if (!address.ByteOffset.HasValue || address.ByteOffset.Value < 0 ||
            !address.BitWidth.HasValue || address.BitWidth.Value <= 0)
        {
            return false;
        }

        var bitOffset = address.BitOffset ?? 0;
        if (bitOffset < 0 || bitOffset > 7) return false;
        var byteOffset = (ulong)address.ByteOffset.Value;
        var width = (ulong)address.BitWidth.Value;
        if (byteOffset > (ulong.MaxValue - (ulong)bitOffset) / 8UL) return false;
        var start = byteOffset * 8UL + (ulong)bitOffset;
        if (width > ulong.MaxValue - start) return false;
        range = new BitRange(start, start + width);
        return true;
    }

    private static string RangeDetail(AddressReference address) =>
        "area=" + address.Area + "; startBit=" + address.Start.ToString(CultureInfo.InvariantCulture) +
        "; endBit=" + address.End.ToString(CultureInfo.InvariantCulture);

    private static void AddCommentFindings(
        SnapshotDocument document,
        IReadOnlyList<TagReference> tags,
        ICollection<FindingV1> findings)
    {
        foreach (var reference in tags.OrderBy(tag => tag.Tag.Id, StringComparer.Ordinal))
        {
            var comment = reference.Tag.Comment!;
            var rule = comment.Status switch
            {
                "missing" => ("TIA.TAG.COMMENT.MISSING", "warning", "applicable", (string?)null,
                    "PLC '" + reference.Plc.Name + "' tag '" + reference.Tag.Name + "' has no tag comment."),
                "read-failed" => ("TIA.TAG.COMMENT.READ_FAILED", "warning", "partial",
                    "The comment read failed, so absence cannot be concluded.",
                    "PLC '" + reference.Plc.Name + "' tag '" + reference.Tag.Name + "' comment could not be read."),
                "unavailable" => ("TIA.TAG.COMMENT.UNAVAILABLE", "info", "partial",
                    "The comment is unavailable, so this check could not be completed.",
                    "PLC '" + reference.Plc.Name + "' tag '" + reference.Tag.Name + "' comment is unavailable."),
                _ => default
            };

            if (rule.Item1 == null) continue;
            findings.Add(CreateFinding(
                rule.Item1,
                rule.Item2,
                rule.Item5,
                rule.Item3,
                rule.Item4,
                new[] { TagEvidence(reference, "commentStatus=" + comment.Status) },
                document.GetTagLocation(reference.PlcIndex, reference.TagIndex)));
        }
    }

    private static void AddDuplicateNames(
        SnapshotDocument document,
        IReadOnlyList<TagReference> tags,
        ICollection<FindingV1> findings)
    {
        foreach (var scope in tags.GroupBy(tag => tag.Tag.ScopePath!, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(group => group.Key, StringComparer.Ordinal))
        {
            var nameGroups = scope
                .GroupBy(tag => tag.Tag.Name!.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ThenBy(group => group.Key, StringComparer.Ordinal);
            foreach (var group in nameGroups)
            {
                var duplicates = group.OrderBy(tag => tag.Tag.Id, StringComparer.Ordinal).ToArray();
                findings.Add(CreateFinding(
                    "TIA.TAG.NAME.DUPLICATE",
                    "error",
                    "PLC '" + duplicates[0].Plc.Name + "' scope '" + scope.Key +
                    "' declares symbolic name '" + group.Key + "' more than once.",
                    "applicable",
                    null,
                    duplicates.Select(tag => TagEvidence(tag, "scopePath=" + tag.Tag.ScopePath +
                        "; name=" + tag.Tag.Name)),
                    document.GetTagLocation(duplicates[0].PlcIndex, duplicates[0].TagIndex)));
            }
        }
    }

    private static void AddMemoryInventory(
        SnapshotDocument document,
        IReadOnlyList<TagReference> tags,
        ICollection<FindingV1> findings)
    {
        foreach (var tag in tags
                     .Where(reference => reference.Tag.Address!.ParseStatus != "missing" &&
                         string.Equals(reference.Tag.Address.Area, "M", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(reference => reference.Tag.Id, StringComparer.Ordinal))
        {
            findings.Add(CreateFinding(
                "TIA.MEMORY.M_TAG_DECLARATION",
                "info",
                "PLC '" + tag.Plc.Name + "' tag '" + tag.Tag.Name +
                "' is declared in the M area. This inventory does not establish program use.",
                "applicable",
                null,
                new[] { TagEvidence(tag, "M-area tag declaration; no program-use claim") },
                document.GetTagLocation(tag.PlcIndex, tag.TagIndex)));
        }
    }

    private static void AddCompileFindings(
        SnapshotDocument document,
        int plcIndex,
        SnapshotPlc plc,
        ICollection<FindingV1> findings)
    {
        var compile = plc.Compile!;
        var captured = ParseDate(document.Snapshot.Capture!.CapturedAtUtc);
        var observed = ParseDate(compile.ObservedAtUtc);
        var hasFreshSource = compile.Mode != "not-observed" &&
                             compile.ObservedAtUtc != null &&
                             observed.HasValue && captured.HasValue && observed.Value <= captured.Value;
        var completeCounts = compile.Errors.HasValue && compile.Warnings.HasValue;
        var consistentStatus = (compile.Status == "clean" && compile.Errors == 0 && compile.Warnings == 0) ||
                               ((compile.Status == "issues" || compile.Status == "failed") &&
                                (compile.Errors.GetValueOrDefault() > 0 || compile.Warnings.GetValueOrDefault() > 0));

        if (!hasFreshSource || !completeCounts || !consistentStatus)
        {
            findings.Add(CreateFinding(
                "TIA.PLC.COMPILE.UNVERIFIED",
                "info",
                "PLC '" + plc.Name + "' compile evidence is not sufficient to propagate a current result.",
                "partial",
                "Requires an observed compile source, timestamp no later than capture, complete counts, and a consistent clean/issues status.",
                new[] { CompileEvidence(plc, compile, "compile evidence cannot support a current pass/fail conclusion") },
                document.GetCompileLocation(plcIndex)));
            return;
        }

        if (compile.Errors!.Value > 0)
        {
            findings.Add(CreateFinding(
                "TIA.PLC.COMPILE.ERRORS",
                "error",
                "PLC '" + plc.Name + "' compile evidence reports " +
                compile.Errors.Value.ToString(CultureInfo.InvariantCulture) + " error(s).",
                "applicable",
                null,
                new[] { CompileEvidence(plc, compile, "errors=" + compile.Errors.Value.ToString(CultureInfo.InvariantCulture)) },
                document.GetCompileLocation(plcIndex)));
        }

        if (compile.Warnings!.Value > 0)
        {
            findings.Add(CreateFinding(
                "TIA.PLC.COMPILE.WARNINGS",
                "warning",
                "PLC '" + plc.Name + "' compile evidence reports " +
                compile.Warnings.Value.ToString(CultureInfo.InvariantCulture) + " warning(s).",
                "applicable",
                null,
                new[] { CompileEvidence(plc, compile, "warnings=" + compile.Warnings.Value.ToString(CultureInfo.InvariantCulture)) },
                document.GetCompileLocation(plcIndex)));
        }
    }

    private static void AddBlockConsistencyFindings(
        SnapshotDocument document,
        int plcIndex,
        SnapshotPlc plc,
        ICollection<FindingV1> findings)
    {
        var captured = ParseDate(document.Snapshot.Capture!.CapturedAtUtc);
        for (var blockIndex = 0; blockIndex < plc.Blocks!.Count; blockIndex++)
        {
            var block = plc.Blocks[blockIndex]!;
            var export = block.Export!;
            var modified = ParseDate(block.ModifiedAtUtc);
            var freshArtifact = export.Status == "exported" &&
                               !string.IsNullOrWhiteSpace(export.Artifact) &&
                               IsSha256(export.Sha256) &&
                               modified.HasValue && captured.HasValue &&
                               modified.Value <= captured.Value;
            if (!freshArtifact || !block.IsConsistent.HasValue)
            {
                findings.Add(CreateFinding(
                    "TIA.BLOCK.CONSISTENCY.UNVERIFIED",
                    "info",
                    "PLC '" + plc.Name + "' block '" + block.Name +
                    "' has no consistency result tied to a fresh exported artifact.",
                    "partial",
                    "Requires exported artifact, SHA-256, block modification time, capture time, and an explicit consistency value.",
                    new[] { BlockEvidence(plc, block, "consistency evidence is incomplete or stale") },
                    document.GetBlockLocation(plcIndex, blockIndex)));
                continue;
            }

            if (block.IsConsistent == false)
            {
                findings.Add(CreateFinding(
                    "TIA.BLOCK.CONSISTENCY.INCONSISTENT",
                    "warning",
                    "PLC '" + plc.Name + "' block '" + block.Name +
                    "' is marked inconsistent in fresh exported-block evidence.",
                    "applicable",
                    null,
                    new[] { BlockEvidence(plc, block, "isConsistent=false; sha256=" + export.Sha256) },
                    document.GetBlockLocation(plcIndex, blockIndex)));
            }
        }
    }

    private static void AddCaptureFinding(SnapshotDocument document, ICollection<FindingV1> findings)
    {
        var capture = document.Snapshot.Capture!;
        if (capture.Status == "complete") return;

        var failed = capture.Status == "failed";
        findings.Add(CreateFinding(
            "TIA.SNAPSHOT.COLLECTION_INCOMPLETE",
            failed ? "error" : "warning",
            "Snapshot collection status is '" + capture.Status + "'; the report cannot claim complete coverage.",
            "partial",
            "The collector did not mark this capture complete.",
            new[]
            {
                new FindingEvidence("snapshot-object", "capture",
                    "status=" + capture.Status + "; mode=" + capture.Mode)
            },
            document.GetCaptureLocation()));
    }

    private static void AddDiagnosticFindings(SnapshotDocument document, ICollection<FindingV1> findings)
    {
        for (var i = 0; i < document.Snapshot.Diagnostics!.Count; i++)
        {
            var diagnostic = document.Snapshot.Diagnostics[i]!;
            findings.Add(CreateFinding(
                "TIA.SNAPSHOT.COLLECTOR_DIAGNOSTIC",
                diagnostic.Severity!,
                "[" + diagnostic.Code + "] " + diagnostic.Message,
                diagnostic.Severity == "info" ? "applicable" : "partial",
                diagnostic.Severity == "info" ? null : "Collector diagnostic may indicate incomplete evidence.",
                new[]
                {
                new FindingEvidence("diagnostic", "diagnostics/" + i.ToString(CultureInfo.InvariantCulture),
                        "code=" + diagnostic.Code + "; " +
                        (diagnostic.ObjectId == null ? diagnostic.Message : "objectId=" + diagnostic.ObjectId))
                },
                document.GetDiagnosticLocation(i)));
        }
    }

    private static FindingV1 CreateFinding(
        string ruleId,
        string severity,
        string message,
        string applicability,
        string? reason,
        IEnumerable<FindingEvidence> evidence,
        FindingLocation location)
    {
        var orderedEvidence = evidence
            .OrderBy(item => item.Kind, StringComparer.Ordinal)
            .ThenBy(item => item.Ref, StringComparer.Ordinal)
            .ThenBy(item => item.Detail, StringComparer.Ordinal)
            .ToArray();
        return new FindingV1(
            ruleId,
            severity,
            message,
            new FindingApplicability(applicability, reason),
            orderedEvidence.FirstOrDefault(item => item.Kind == "snapshot-object")?.Ref,
            orderedEvidence,
            location);
    }

    private static FindingEvidence TagEvidence(TagReference reference, string detail) =>
        new FindingEvidence("snapshot-object", reference.Tag.Id!, detail);

    private static FindingEvidence CompileEvidence(SnapshotPlc plc, SnapshotCompile compile, string detail) =>
        new FindingEvidence("compile", plc.Id!,
            "mode=" + compile.Mode + "; status=" + compile.Status +
            "; observedAtUtc=" + (compile.ObservedAtUtc ?? "null") + "; " + detail);

    private static FindingEvidence BlockEvidence(SnapshotPlc plc, SnapshotBlock block, string detail) =>
        new FindingEvidence("snapshot-object", block.Id!,
            "plcId=" + plc.Id + "; exportStatus=" + block.Export!.Status +
            "; artifact=" + (block.Export.Artifact ?? "null") +
            "; sha256=" + (block.Export.Sha256 ?? "null") + "; " + detail);

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    private static bool IsSha256(string? value)
    {
        if (value == null || value.Length != 64) return false;
        foreach (var character in value)
        {
            if (!Uri.IsHexDigit(character)) return false;
        }
        return true;
    }

    private static string StableFindingKey(FindingV1 finding) =>
        JsonSerializer.Serialize(new
        {
            finding.ObjectRef,
            Evidence = finding.Evidence.Select(item => new { item.Kind, item.Ref, item.Detail })
        }, CanonicalJsonOptions);

    private readonly struct BitRange
    {
        public BitRange(ulong start, ulong end)
        {
            Start = start;
            End = end;
        }

        public ulong Start { get; }
        public ulong End { get; }
    }

    private sealed class TagReference
    {
        public TagReference(int plcIndex, int tagIndex, SnapshotPlc plc, SnapshotTag tag)
        {
            PlcIndex = plcIndex;
            TagIndex = tagIndex;
            Plc = plc;
            Tag = tag;
        }

        public int PlcIndex { get; }
        public int TagIndex { get; }
        public SnapshotPlc Plc { get; }
        public SnapshotTag Tag { get; }
    }

    private sealed class AddressReference
    {
        public AddressReference(TagReference tag, string area, ulong start, ulong end)
        {
            Tag = tag;
            Area = area;
            Start = start;
            End = end;
        }

        public TagReference Tag { get; }
        public string Area { get; }
        public ulong Start { get; }
        public ulong End { get; }
    }
}
