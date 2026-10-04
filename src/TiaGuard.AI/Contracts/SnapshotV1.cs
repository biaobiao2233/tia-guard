namespace TiaGuard.AI.Contracts;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// The Snapshot v1 contract as consumed by the AI review layer.
/// </summary>
public sealed record SnapshotV1(
    string SchemaVersion,
    SnapshotProject Project,
    SnapshotTia Tia,
    IReadOnlyList<SnapshotDevice> Devices,
    IReadOnlyList<SnapshotPlc> Plcs);

public sealed record SnapshotProject(string Name, string? Path);

public sealed record SnapshotTia(string Version, int? ProcessId);

public sealed record SnapshotDevice(
    string Name,
    string Type,
    string? OrderNumber,
    string? Firmware);

public sealed record SnapshotPlc(
    string Name,
    IReadOnlyList<SnapshotBlock> Blocks,
    IReadOnlyList<SnapshotTag> Tags,
    SnapshotCompile? Compile);

public sealed record SnapshotBlock(string Name, string Kind, string? Language);

public sealed record SnapshotTag(
    string Name,
    string DataType,
    string? Address,
    string? Comment);

public sealed record SnapshotCompile(int Errors, int Warnings);

/// <summary>
/// Optional exported source for a block. Source text is treated as untrusted input.
/// </summary>
public sealed record BlockSourceText(
    string PlcName,
    string BlockName,
    string? Language,
    string SourceText);

/// <summary>
/// Reads the repository's camel-case Snapshot v1 JSON contract.
/// </summary>
public static class SnapshotV1Json
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static SnapshotV1 Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var snapshot = JsonSerializer.Deserialize<SnapshotV1>(json, JsonOptions)
            ?? throw new JsonException("Snapshot v1 JSON must contain an object.");
        SnapshotV1Validator.Validate(snapshot);
        return snapshot;
    }
}

internal static class SnapshotV1Validator
{
    public static void Validate(SnapshotV1 snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        RequireValue(snapshot.SchemaVersion, "schemaVersion");
        if (!string.Equals(snapshot.SchemaVersion, "1.0", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Unsupported Snapshot schema version '{snapshot.SchemaVersion}'. Expected '1.0'.", nameof(snapshot));
        }

        ArgumentNullException.ThrowIfNull(snapshot.Project);
        ArgumentNullException.ThrowIfNull(snapshot.Tia);
        RequireValue(snapshot.Project.Name, "project.name");
        RequireValue(snapshot.Tia.Version, "tia.version");
        ArgumentNullException.ThrowIfNull(snapshot.Devices);
        ArgumentNullException.ThrowIfNull(snapshot.Plcs);

        foreach (var device in snapshot.Devices)
        {
            ArgumentNullException.ThrowIfNull(device);
            RequireValue(device.Name, "device.name");
            RequireValue(device.Type, "device.type");
        }

        foreach (var plc in snapshot.Plcs)
        {
            ArgumentNullException.ThrowIfNull(plc);
            RequireValue(plc.Name, "plc.name");
            ArgumentNullException.ThrowIfNull(plc.Blocks);
            ArgumentNullException.ThrowIfNull(plc.Tags);

            foreach (var block in plc.Blocks)
            {
                ArgumentNullException.ThrowIfNull(block);
                RequireValue(block.Name, "block.name");
                RequireValue(block.Kind, "block.kind");
            }

            foreach (var tag in plc.Tags)
            {
                ArgumentNullException.ThrowIfNull(tag);
                RequireValue(tag.Name, "tag.name");
                RequireValue(tag.DataType, "tag.dataType");
            }

            if (plc.Compile is { Errors: < 0 } or { Warnings: < 0 })
            {
                throw new ArgumentException("Snapshot compile counts cannot be negative.", nameof(snapshot));
            }
        }
    }

    private static void RequireValue(string? value, string fieldName)
    {
        if (value is null)
        {
            throw new ArgumentException($"Snapshot v1 field '{fieldName}' is required.");
        }
    }
}
