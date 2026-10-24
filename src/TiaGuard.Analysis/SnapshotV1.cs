using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TiaGuard.Analysis;

public sealed class SnapshotV1
{
    [JsonPropertyName("schemaVersion")]
    public string? SchemaVersion { get; set; }

    [JsonPropertyName("contractStatus")]
    public string? ContractStatus { get; set; }

    [JsonPropertyName("collector")]
    public SnapshotCollector? Collector { get; set; }

    [JsonPropertyName("project")]
    public SnapshotProject? Project { get; set; }

    [JsonPropertyName("tia")]
    public SnapshotTia? Tia { get; set; }

    [JsonPropertyName("capture")]
    public SnapshotCapture? Capture { get; set; }

    [JsonPropertyName("devices")]
    public List<SnapshotDevice?>? Devices { get; set; }

    [JsonPropertyName("plcs")]
    public List<SnapshotPlc?>? Plcs { get; set; }

    [JsonPropertyName("diagnostics")]
    public List<SnapshotDiagnostic?>? Diagnostics { get; set; }
}

public sealed class SnapshotCollector
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("normalizationVersion")]
    public string? NormalizationVersion { get; set; }
}

public sealed class SnapshotProject
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("sourceKind")]
    public string? SourceKind { get; set; }

    [JsonPropertyName("projectVersion")]
    public string? ProjectVersion { get; set; }

    [JsonPropertyName("contentId")]
    public string? ContentId { get; set; }
}

public sealed class SnapshotTia
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("build")]
    public string? Build { get; set; }
}

public sealed class SnapshotCapture
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("capturedAtUtc")]
    public string? CapturedAtUtc { get; set; }

    [JsonPropertyName("mode")]
    public string? Mode { get; set; }
}

public sealed class SnapshotDevice
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("parentId")]
    public string? ParentId { get; set; }

    [JsonPropertyName("plcId")]
    public string? PlcId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("engineeringPath")]
    public string? EngineeringPath { get; set; }

    [JsonPropertyName("orderNumber")]
    public string? OrderNumber { get; set; }

    [JsonPropertyName("firmware")]
    public string? Firmware { get; set; }
}

public sealed class SnapshotPlc
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("deviceId")]
    public string? DeviceId { get; set; }

    [JsonPropertyName("blocks")]
    public List<SnapshotBlock?>? Blocks { get; set; }

    [JsonPropertyName("tags")]
    public List<SnapshotTag?>? Tags { get; set; }

    [JsonPropertyName("compile")]
    public SnapshotCompile? Compile { get; set; }
}

public sealed class SnapshotBlock
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("scopePath")]
    public string? ScopePath { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("number")]
    public int? Number { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("protection")]
    public string? Protection { get; set; }

    [JsonPropertyName("isConsistent")]
    public bool? IsConsistent { get; set; }

    [JsonPropertyName("modifiedAtUtc")]
    public string? ModifiedAtUtc { get; set; }

    [JsonPropertyName("export")]
    public SnapshotBlockExport? Export { get; set; }
}

public sealed class SnapshotBlockExport
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("format")]
    public string? Format { get; set; }

    [JsonPropertyName("artifact")]
    public string? Artifact { get; set; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    [JsonPropertyName("diagnosticCode")]
    public string? DiagnosticCode { get; set; }
}

public sealed class SnapshotTag
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("scopePath")]
    public string? ScopePath { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("dataType")]
    public string? DataType { get; set; }

    [JsonPropertyName("address")]
    public SnapshotAddress? Address { get; set; }

    [JsonPropertyName("comment")]
    public SnapshotComment? Comment { get; set; }
}

public sealed class SnapshotAddress
{
    [JsonPropertyName("raw")]
    public string? Raw { get; set; }

    [JsonPropertyName("parseStatus")]
    public string? ParseStatus { get; set; }

    [JsonPropertyName("area")]
    public string? Area { get; set; }

    [JsonPropertyName("byteOffset")]
    public long? ByteOffset { get; set; }

    [JsonPropertyName("bitOffset")]
    public int? BitOffset { get; set; }

    [JsonPropertyName("bitWidth")]
    public long? BitWidth { get; set; }
}

public sealed class SnapshotComment
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

public sealed class SnapshotCompile
{
    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("observedAtUtc")]
    public string? ObservedAtUtc { get; set; }

    [JsonPropertyName("errors")]
    public int? Errors { get; set; }

    [JsonPropertyName("warnings")]
    public int? Warnings { get; set; }
}

public sealed class SnapshotDiagnostic
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("severity")]
    public string? Severity { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("objectId")]
    public string? ObjectId { get; set; }
}