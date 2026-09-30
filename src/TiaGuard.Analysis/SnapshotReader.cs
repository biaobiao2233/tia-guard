using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TiaGuard.Analysis;

public static class SnapshotReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = false,
        MaxDepth = 64,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static SnapshotDocument Parse(string json, string artifactUri = "snapshot.json")
    {
        if (json == null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        if (string.IsNullOrWhiteSpace(artifactUri))
        {
            throw new ArgumentException("A repository-relative Snapshot URI is required.", nameof(artifactUri));
        }

        ValidateRequiredJsonMembers(json);
        var snapshot = JsonSerializer.Deserialize<SnapshotV1>(json, SerializerOptions);
        Validate(snapshot);

        var normalizedUri = artifactUri.Trim().Replace('\\', '/').TrimStart('/');
        while (normalizedUri.StartsWith("./", StringComparison.Ordinal))
        {
            normalizedUri = normalizedUri.Substring(2);
        }

        var uriSegments = normalizedUri.Split('/');
        if (normalizedUri.Length == 0 ||
            Uri.TryCreate(normalizedUri, UriKind.Absolute, out _) ||
            Array.Exists(uriSegments, segment => segment == ".."))
        {
            throw new ArgumentException("Snapshot URI must be repository-relative.", nameof(artifactUri));
        }

        return new SnapshotDocument(snapshot!, normalizedUri, IndexObjectLines(json));
    }

    private static void Validate(SnapshotV1? snapshot)
    {
        if (snapshot == null)
        {
            throw new InvalidDataException("Snapshot JSON must contain an object.");
        }

        Require(snapshot.SchemaVersion == "1.0", "Snapshot schemaVersion must be '1.0'.");
        Require(snapshot.ContractStatus == "draft", "Snapshot contractStatus must be 'draft'.");
        Require(snapshot.Collector?.Name == "TIA-Guard" &&
                NonEmpty(snapshot.Collector.Version) && NonEmpty(snapshot.Collector.NormalizationVersion),
            "Snapshot collector identity and versions are required.");
        Require(NonEmpty(snapshot.Project?.Name) &&
                OneOf(snapshot.Project?.SourceKind, "offline-copy", "attached-session"),
            "Snapshot project name and sourceKind are required.");
        Require(NonEmpty(snapshot.Tia?.Version), "Snapshot TIA version is required.");
        Require(OneOf(snapshot.Capture?.Status, "complete", "partial", "failed") &&
                OneOf(snapshot.Capture?.Mode, "offline-copy", "attached-session") &&
                IsDate(snapshot.Capture?.CapturedAtUtc),
            "Snapshot capture status, mode, and capturedAtUtc are required.");
        Require(snapshot.Devices != null && snapshot.Plcs != null && snapshot.Diagnostics != null,
            "Snapshot devices, plcs, and diagnostics arrays are required.");

        foreach (var device in snapshot.Devices!)
        {
            Require(device != null && NonEmpty(device.Id) && NonEmpty(device.Name) && NonEmpty(device.Type),
                "Each Snapshot device requires id, name, and type.");
        }

        foreach (var plc in snapshot.Plcs!)
        {
            Require(plc != null && NonEmpty(plc.Id) && NonEmpty(plc.Name) &&
                    NonEmpty(plc.DeviceId) && plc.Blocks != null && plc.Tags != null && plc.Compile != null,
                "Each Snapshot PLC requires id, name, deviceId, blocks, tags, and compile.");

            foreach (var block in plc!.Blocks!)
            {
                Require(block != null && NonEmpty(block.Id) && NonEmpty(block.Name) && NonEmpty(block.Kind) &&
                        OneOf(block.Protection, "none", "know-how", "unknown") && block.Export != null &&
                        OneOf(block.Export.Status, "exported", "protected", "unsupported", "failed", "not-attempted"),
                    "Each Snapshot block requires identity, protection, and export status.");
                Require(!block!.Number.HasValue || block.Number.Value >= 0,
                    "Snapshot block numbers must be non-negative.");
                Require(block.ModifiedAtUtc == null || IsDate(block.ModifiedAtUtc),
                    "Snapshot block modifiedAtUtc must be a date-time when present.");
                Require(block.Export!.Sha256 == null || IsSha256(block.Export.Sha256),
                    "Snapshot block SHA-256 must contain 64 hexadecimal characters.");
            }

            foreach (var tag in plc.Tags!)
            {
                Require(tag != null && NonEmpty(tag.Id) && NonEmpty(tag.ScopePath) &&
                        NonEmpty(tag.Name) && NonEmpty(tag.DataType) && tag.Address != null && tag.Comment != null,
                    "Each Snapshot tag requires identity, scope, name, dataType, address, and comment.");
                Require(OneOf(tag!.Address!.ParseStatus, "parsed", "unsupported", "missing"),
                    "Tag address parseStatus must be parsed, unsupported, or missing.");
                Require(NonNegativeOrNull(tag.Address.ByteOffset) &&
                        (!tag.Address.BitOffset.HasValue ||
                         (tag.Address.BitOffset.Value >= 0 && tag.Address.BitOffset.Value <= 7)) &&
                        (!tag.Address.BitWidth.HasValue || tag.Address.BitWidth.Value >= 1),
                    "Snapshot parsed address fields are outside their allowed ranges.");
                Require(OneOf(tag.Comment!.Status, "present", "missing", "unavailable", "read-failed"),
                    "Tag comment status is invalid.");
            }

            var compile = plc.Compile!;
            Require(OneOf(compile.Mode, "not-observed", "consistency-only", "active-compile") &&
                    OneOf(compile.Status, "unknown", "clean", "issues", "failed") &&
                    NonNegativeOrNull(compile.Errors) && NonNegativeOrNull(compile.Warnings) &&
                    (compile.ObservedAtUtc == null || IsDate(compile.ObservedAtUtc)),
                "Snapshot compile evidence is invalid.");
        }

        foreach (var diagnostic in snapshot.Diagnostics!)
        {
            Require(diagnostic != null && NonEmpty(diagnostic.Code) && NonEmpty(diagnostic.Message) &&
                    OneOf(diagnostic.Severity, "info", "warning", "error"),
                "Each Snapshot diagnostic requires code, severity, and message.");
        }
    }

    private static void ValidateRequiredJsonMembers(string json)
    {
        using (var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 }))
        {
            var root = document.RootElement;
            RequireMembers(root, "schemaVersion", "contractStatus", "collector", "project", "tia",
                "capture", "devices", "plcs", "diagnostics");

            var collector = RequiredObject(root, "collector");
            RequireMembers(collector, "name", "version", "normalizationVersion");
            var project = RequiredObject(root, "project");
            RequireMembers(project, "name", "sourceKind");
            RequireMembers(RequiredObject(root, "tia"), "version");
            RequireMembers(RequiredObject(root, "capture"), "status", "capturedAtUtc", "mode");

            foreach (var device in RequiredArray(root, "devices").EnumerateArray())
            {
                RequireMembers(device, "id", "name", "type");
            }

            foreach (var plc in RequiredArray(root, "plcs").EnumerateArray())
            {
                RequireMembers(plc, "id", "name", "deviceId", "blocks", "tags", "compile");
                foreach (var block in RequiredArray(plc, "blocks").EnumerateArray())
                {
                    RequireMembers(block, "id", "name", "kind", "protection", "export");
                    RequireMembers(RequiredObject(block, "export"), "status");
                }
                foreach (var tag in RequiredArray(plc, "tags").EnumerateArray())
                {
                    RequireMembers(tag, "id", "scopePath", "name", "dataType", "address", "comment");
                    RequireMembers(RequiredObject(tag, "address"), "raw", "parseStatus");
                    RequireMembers(RequiredObject(tag, "comment"), "status", "text");
                }
                RequireMembers(RequiredObject(plc, "compile"), "mode", "status", "errors", "warnings");
            }

            foreach (var diagnostic in RequiredArray(root, "diagnostics").EnumerateArray())
            {
                RequireMembers(diagnostic, "code", "severity", "message");
            }

            RejectDuplicateProperties(root);
        }
    }

    private static JsonElement RequiredObject(JsonElement owner, string propertyName)
    {
        Require(owner.TryGetProperty(propertyName, out var value) &&
                value.ValueKind == JsonValueKind.Object,
            "Snapshot property '" + propertyName + "' must be an object.");
        return value;
    }

    private static JsonElement RequiredArray(JsonElement owner, string propertyName)
    {
        Require(owner.TryGetProperty(propertyName, out var value) &&
                value.ValueKind == JsonValueKind.Array,
            "Snapshot property '" + propertyName + "' must be an array.");
        return value;
    }

    private static void RequireMembers(JsonElement value, params string[] propertyNames)
    {
        Require(value.ValueKind == JsonValueKind.Object,
            "Snapshot contract values must be objects.");
        foreach (var propertyName in propertyNames)
        {
            Require(value.TryGetProperty(propertyName, out _),
                "Snapshot object is missing required property '" + propertyName + "'.");
        }
    }

    private static void RejectDuplicateProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                Require(names.Add(property.Name),
                    "Snapshot JSON contains duplicate property '" + property.Name + "'.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray()) RejectDuplicateProperties(item);
        }
    }

    private static Dictionary<string, int> IndexObjectLines(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var lineStarts = new List<long> { 0 };
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == (byte)'\n')
            {
                lineStarts.Add(i + 1L);
            }
        }

        var objectLines = new Dictionary<string, int>(StringComparer.Ordinal);
        var frames = new List<JsonPathFrame>();
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 64 });
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    if (frames.Count == 0 || frames[frames.Count - 1].IsArray)
                    {
                        throw new InvalidDataException("Invalid JSON object path in Snapshot.");
                    }
                    frames[frames.Count - 1].PendingProperty = reader.GetString();
                    break;

                case JsonTokenType.StartObject:
                case JsonTokenType.StartArray:
                    var path = ConsumeValuePath(frames);
                    if (reader.TokenType == JsonTokenType.StartObject && IsEvidenceObject(path))
                    {
                        objectLines[path] = FindLine(lineStarts, reader.TokenStartIndex);
                    }
                    frames.Add(new JsonPathFrame(path, reader.TokenType == JsonTokenType.StartArray));
                    break;

                case JsonTokenType.EndObject:
                case JsonTokenType.EndArray:
                    if (frames.Count == 0)
                    {
                        throw new InvalidDataException("Invalid JSON container in Snapshot.");
                    }
                    frames.RemoveAt(frames.Count - 1);
                    break;

                default:
                    ConsumeValuePath(frames);
                    break;
            }
        }

        return objectLines;
    }

    private static bool IsEvidenceObject(string path)
    {
        var parts = path.Split('/');
        if (parts.Length == 5 &&
            parts[1] == "plcs" &&
            int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out _) &&
            (parts[3] == "tags" || parts[3] == "blocks") &&
            int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            return true;
        }

        if (parts.Length == 4 && parts[1] == "plcs" &&
            int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out _) &&
            parts[3] == "compile")
        {
            return true;
        }

        if (parts.Length == 3 && parts[1] == "diagnostics" &&
            int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            return true;
        }

        return path == "/capture";
    }

    private static string ConsumeValuePath(List<JsonPathFrame> frames)
    {
        if (frames.Count == 0)
        {
            return string.Empty;
        }

        var frame = frames[frames.Count - 1];
        if (frame.IsArray)
        {
            return frame.Path + "/" + frame.NextArrayIndex++.ToString(CultureInfo.InvariantCulture);
        }

        if (frame.PendingProperty == null)
        {
            throw new InvalidDataException("Invalid JSON object path in Snapshot.");
        }

        var property = frame.PendingProperty.Replace("~", "~0").Replace("/", "~1");
        frame.PendingProperty = null;
        return frame.Path + "/" + property;
    }

    private static int FindLine(List<long> lineStarts, long byteOffset)
    {
        var low = 0;
        var high = lineStarts.Count - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (lineStarts[middle] <= byteOffset)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return high + 1;
    }

    private static bool NonEmpty(string? value) => !string.IsNullOrWhiteSpace(value);

    private static bool NonNegativeOrNull(int? value) => !value.HasValue || value.Value >= 0;
    private static bool NonNegativeOrNull(long? value) => !value.HasValue || value.Value >= 0;

    private static bool IsSha256(string? value)
    {
        if (value == null || value.Length != 64) return false;
        foreach (var c in value)
        {
            if (!Uri.IsHexDigit(c)) return false;
        }
        return true;
    }

    private static bool OneOf(string? value, params string[] values)
    {
        foreach (var candidate in values)
        {
            if (string.Equals(value, candidate, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static bool IsDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var dateTime = value!;
        var timeStart = dateTime.IndexOf('T');
        if (timeStart < 0) return false;
        var hasExplicitOffset = dateTime.EndsWith("Z", StringComparison.OrdinalIgnoreCase) ||
                                dateTime.IndexOf('+', timeStart) >= 0 ||
                                dateTime.LastIndexOf('-', dateTime.Length - 1) > timeStart;
        return hasExplicitOffset &&
               DateTimeOffset.TryParse(dateTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    private sealed class JsonPathFrame
    {
        public JsonPathFrame(string path, bool isArray)
        {
            Path = path;
            IsArray = isArray;
        }

        public string Path { get; }
        public bool IsArray { get; }
        public int NextArrayIndex { get; set; }
        public string? PendingProperty { get; set; }
    }
}
