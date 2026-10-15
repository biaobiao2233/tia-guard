using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TiaGuard.Analysis;

public sealed class DoctorReport
{
    internal DoctorReport(
        string status,
        string projectName,
        string sourceKind,
        string tiaVersion,
        string captureStatus,
        string capturedAtUtc,
        string snapshotUri,
        IReadOnlyList<FindingV1> findings)
    {
        Status = status;
        ProjectName = projectName;
        SourceKind = sourceKind;
        TiaVersion = tiaVersion;
        CaptureStatus = captureStatus;
        CapturedAtUtc = capturedAtUtc;
        SnapshotUri = snapshotUri;
        Findings = findings;
    }

    public string Status { get; }
    public string ProjectName { get; }
    public string SourceKind { get; }
    public string TiaVersion { get; }
    public string CaptureStatus { get; }
    public string CapturedAtUtc { get; }
    public string SnapshotUri { get; }
    public IReadOnlyList<FindingV1> Findings { get; }
}

public sealed class FindingV1
{
    internal FindingV1(
        string ruleId,
        string severity,
        string message,
        FindingApplicability applicability,
        string? objectRef,
        IReadOnlyList<FindingEvidence> evidence,
        FindingLocation? location)
    {
        RuleId = ruleId;
        Severity = severity;
        Message = message;
        Applicability = applicability;
        ObjectRef = objectRef;
        Evidence = evidence;
        Location = location;
    }

    public string SchemaVersion => "1.0";
    public string RuleId { get; }
    public string Severity { get; }
    public string Message { get; }
    public FindingApplicability Applicability { get; }
    public string? ObjectRef { get; }
    public IReadOnlyList<FindingEvidence> Evidence { get; }
    public FindingLocation? Location { get; }
}

public sealed class FindingApplicability
{
    internal FindingApplicability(string status, string? reason)
    {
        Status = status;
        Reason = reason;
    }

    public string Status { get; }
    public string? Reason { get; }
}

public sealed class FindingEvidence
{
    internal FindingEvidence(string kind, string reference, string? detail)
    {
        Kind = kind;
        Ref = reference;
        Detail = detail;
    }

    public string Kind { get; }
    public string Ref { get; }
    public string? Detail { get; }
}

public sealed class FindingLocation
{
    internal FindingLocation(string artifact, int startLine, int? endLine = null)
    {
        Artifact = artifact;
        StartLine = startLine;
        EndLine = endLine;
    }

    public string Artifact { get; }
    public int StartLine { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? EndLine { get; }
}

public sealed class RuleDefinition
{
    internal RuleDefinition(string id, string name, string description, string severity)
    {
        Id = id;
        Name = name;
        Description = description;
        Severity = severity;
    }

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public string Severity { get; }
}

public static class RuleCatalog
{
    private static readonly IReadOnlyList<RuleDefinition> RuleDefinitions = new[]
    {
        new RuleDefinition("TIA.IO.ADDRESS.DUPLICATE", "Duplicate I/O address", "Two declarations in one PLC use the same supported parsed I/O range.", "warning"),
        new RuleDefinition("TIA.IO.ADDRESS.OVERLAP", "Overlapping I/O address", "Two declarations in one PLC have partially overlapping supported parsed I/O ranges.", "warning"),
        new RuleDefinition("TIA.IO.ADDRESS.UNSUPPORTED", "Unsupported I/O address", "The declared I/O address cannot be evaluated using the supported parsed address fields.", "info"),
        new RuleDefinition("TIA.TAG.COMMENT.MISSING", "Missing tag comment", "A tag comment is explicitly marked missing.", "warning"),
        new RuleDefinition("TIA.TAG.COMMENT.READ_FAILED", "Tag comment read failed", "The collector could not read the tag comment.", "warning"),
        new RuleDefinition("TIA.TAG.COMMENT.UNAVAILABLE", "Tag comment unavailable", "The tag comment is unavailable and cannot be evaluated.", "info"),
        new RuleDefinition("TIA.TAG.NAME.DUPLICATE", "Duplicate symbolic name", "The same symbolic name is declared more than once in one explicit PLC scope.", "error"),
        new RuleDefinition("TIA.MEMORY.M_TAG_DECLARATION", "M-area tag declaration", "A tag declaration is in the M area; this inventory does not prove that program logic uses it.", "info"),
        new RuleDefinition("TIA.PLC.COMPILE.ERRORS", "Compile errors", "Fresh, timestamped compile evidence reports one or more errors.", "error"),
        new RuleDefinition("TIA.PLC.COMPILE.WARNINGS", "Compile warnings", "Fresh, timestamped compile evidence reports one or more warnings.", "warning"),
        new RuleDefinition("TIA.PLC.COMPILE.UNVERIFIED", "Compile evidence unverified", "Compile counts lack a supported source, timestamp, or complete status.", "info"),
        new RuleDefinition("TIA.BLOCK.CONSISTENCY.INCONSISTENT", "Block consistency", "Fresh exported block evidence reports that the block is inconsistent.", "warning"),
        new RuleDefinition("TIA.BLOCK.CONSISTENCY.UNVERIFIED", "Block consistency unverified", "Consistency cannot be tied to a fresh exported block artifact.", "info"),
        new RuleDefinition("TIA.SNAPSHOT.COLLECTION_INCOMPLETE", "Snapshot collection incomplete", "The Snapshot capture is partial or failed.", "warning"),
        new RuleDefinition("TIA.SNAPSHOT.COLLECTOR_DIAGNOSTIC", "Snapshot collector diagnostic", "The Snapshot contains a collector diagnostic.", "info")
    };

    public static IReadOnlyList<RuleDefinition> All => RuleDefinitions;
}
