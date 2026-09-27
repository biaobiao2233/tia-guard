using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace TiaGuard.Openness
{
    // DTOs match the coordinator-owned draft Snapshot v1 schema. Siemens API
    // objects never cross this assembly boundary.
    [DataContract]
    public sealed class SnapshotV1
    {
        [DataMember(Name = "schemaVersion", Order = 0)]
        public string SchemaVersion { get; set; } = "1.0";
        [DataMember(Name = "contractStatus", Order = 1)]
        public string ContractStatus { get; set; } = "draft";
        [DataMember(Name = "collector", Order = 2)]
        public SnapshotCollector Collector { get; set; } = new SnapshotCollector();
        [DataMember(Name = "project", Order = 3)]
        public SnapshotProject Project { get; set; } = new SnapshotProject();
        [DataMember(Name = "tia", Order = 4)]
        public SnapshotTia Tia { get; set; } = new SnapshotTia();
        [DataMember(Name = "capture", Order = 5)]
        public SnapshotCapture Capture { get; set; } = new SnapshotCapture();
        [DataMember(Name = "devices", Order = 6)]
        public List<SnapshotDevice> Devices { get; set; } = new List<SnapshotDevice>();
        [DataMember(Name = "plcs", Order = 7)]
        public List<SnapshotPlc> Plcs { get; set; } = new List<SnapshotPlc>();
        [DataMember(Name = "diagnostics", Order = 8)]
        public List<SnapshotDiagnostic> Diagnostics { get; set; } = new List<SnapshotDiagnostic>();
    }

    [DataContract]
    public sealed class SnapshotCollector
    {
        [DataMember(Name = "name", Order = 0)]
        public string Name { get; set; } = "TIA-Guard";
        [DataMember(Name = "version", Order = 1)]
        public string Version { get; set; } = "0.1.0-dev";
        [DataMember(Name = "normalizationVersion", Order = 2)]
        public string NormalizationVersion { get; set; } = "1";
    }

    [DataContract]
    public sealed class SnapshotProject
    {
        [DataMember(Name = "name", Order = 0)]
        public string Name { get; set; } = string.Empty;
        [DataMember(Name = "sourceKind", Order = 1)]
        public string SourceKind { get; set; } = "offline-copy";
        [DataMember(Name = "projectVersion", Order = 2)]
        public string ProjectVersion { get; set; }
        [DataMember(Name = "contentId", Order = 3)]
        public string ContentId { get; set; }
    }

    [DataContract]
    public sealed class SnapshotTia
    {
        [DataMember(Name = "version", Order = 0)]
        public string Version { get; set; } = "V21";
        [DataMember(Name = "build", Order = 1)]
        public string Build { get; set; }
    }

    [DataContract]
    public sealed class SnapshotCapture
    {
        [DataMember(Name = "status", Order = 0)]
        public string Status { get; set; } = "failed";
        [DataMember(Name = "capturedAtUtc", Order = 1)]
        public string CapturedAtUtc { get; set; } = string.Empty;
        [DataMember(Name = "mode", Order = 2)]
        public string Mode { get; set; } = "offline-copy";
    }

    [DataContract]
    public sealed class SnapshotDevice
    {
        [DataMember(Name = "id", Order = 0)]
        public string Id { get; set; } = string.Empty;
        [DataMember(Name = "parentId", Order = 1)]
        public string ParentId { get; set; }
        [DataMember(Name = "plcId", Order = 2)]
        public string PlcId { get; set; }
        [DataMember(Name = "name", Order = 3)]
        public string Name { get; set; } = string.Empty;
        [DataMember(Name = "type", Order = 4)]
        public string Type { get; set; } = string.Empty;
        [DataMember(Name = "engineeringPath", Order = 5)]
        public string EngineeringPath { get; set; }
        [DataMember(Name = "orderNumber", Order = 6)]
        public string OrderNumber { get; set; }
        [DataMember(Name = "firmware", Order = 7)]
        public string Firmware { get; set; }
    }

    [DataContract]
    public sealed class SnapshotPlc
    {
        [DataMember(Name = "id", Order = 0)]
        public string Id { get; set; } = string.Empty;
        [DataMember(Name = "name", Order = 1)]
        public string Name { get; set; } = string.Empty;
        [DataMember(Name = "deviceId", Order = 2)]
        public string DeviceId { get; set; } = string.Empty;
        [DataMember(Name = "blocks", Order = 3)]
        public List<SnapshotBlock> Blocks { get; set; } = new List<SnapshotBlock>();
        [DataMember(Name = "tags", Order = 4)]
        public List<SnapshotTag> Tags { get; set; } = new List<SnapshotTag>();
        [DataMember(Name = "compile", Order = 5)]
        public SnapshotCompile Compile { get; set; } = new SnapshotCompile();
    }

    [DataContract]
    public sealed class SnapshotBlock
    {
        [DataMember(Name = "id", Order = 0)]
        public string Id { get; set; } = string.Empty;
        [DataMember(Name = "scopePath", Order = 1)]
        public string ScopePath { get; set; }
        [DataMember(Name = "name", Order = 2)]
        public string Name { get; set; } = string.Empty;
        [DataMember(Name = "kind", Order = 3)]
        public string Kind { get; set; } = string.Empty;
        [DataMember(Name = "number", Order = 4)]
        public int? Number { get; set; }
        [DataMember(Name = "language", Order = 5)]
        public string Language { get; set; }
        [DataMember(Name = "protection", Order = 6)]
        public string Protection { get; set; } = "unknown";
        [DataMember(Name = "isConsistent", Order = 7)]
        public bool? IsConsistent { get; set; }
        [DataMember(Name = "modifiedAtUtc", Order = 8)]
        public string ModifiedAtUtc { get; set; }
        [DataMember(Name = "export", Order = 9)]
        public SnapshotExport Export { get; set; } = new SnapshotExport();
    }

    [DataContract]
    public sealed class SnapshotExport
    {
        [DataMember(Name = "status", Order = 0)]
        public string Status { get; set; } = "not-attempted";
        [DataMember(Name = "format", Order = 1)]
        public string Format { get; set; }
        [DataMember(Name = "artifact", Order = 2)]
        public string Artifact { get; set; }
        [DataMember(Name = "sha256", Order = 3)]
        public string Sha256 { get; set; }
        [DataMember(Name = "diagnosticCode", Order = 4)]
        public string DiagnosticCode { get; set; }
    }

    [DataContract]
    public sealed class SnapshotTag
    {
        [DataMember(Name = "id", Order = 0)]
        public string Id { get; set; } = string.Empty;
        [DataMember(Name = "scopePath", Order = 1)]
        public string ScopePath { get; set; } = string.Empty;
        [DataMember(Name = "name", Order = 2)]
        public string Name { get; set; } = string.Empty;
        [DataMember(Name = "dataType", Order = 3)]
        public string DataType { get; set; } = string.Empty;
        [DataMember(Name = "address", Order = 4)]
        public SnapshotAddress Address { get; set; } = new SnapshotAddress();
        [DataMember(Name = "comment", Order = 5)]
        public SnapshotComment Comment { get; set; } = new SnapshotComment();
    }

    [DataContract]
    public sealed class SnapshotAddress
    {
        [DataMember(Name = "raw", Order = 0)]
        public string Raw { get; set; }
        [DataMember(Name = "parseStatus", Order = 1)]
        public string ParseStatus { get; set; } = "missing";
        [DataMember(Name = "area", Order = 2)]
        public string Area { get; set; }
        [DataMember(Name = "byteOffset", Order = 3)]
        public int? ByteOffset { get; set; }
        [DataMember(Name = "bitOffset", Order = 4)]
        public int? BitOffset { get; set; }
        [DataMember(Name = "bitWidth", Order = 5)]
        public int? BitWidth { get; set; }
    }

    [DataContract]
    public sealed class SnapshotComment
    {
        [DataMember(Name = "status", Order = 0)]
        public string Status { get; set; } = "unavailable";
        [DataMember(Name = "text", Order = 1)]
        public string Text { get; set; }
    }

    [DataContract]
    public sealed class SnapshotCompile
    {
        [DataMember(Name = "mode", Order = 0)]
        public string Mode { get; set; } = "not-observed";
        [DataMember(Name = "status", Order = 1)]
        public string Status { get; set; } = "unknown";
        [DataMember(Name = "observedAtUtc", Order = 2)]
        public string ObservedAtUtc { get; set; }
        [DataMember(Name = "errors", Order = 3)]
        public int? Errors { get; set; }
        [DataMember(Name = "warnings", Order = 4)]
        public int? Warnings { get; set; }
    }

    [DataContract]
    public sealed class SnapshotDiagnostic
    {
        [DataMember(Name = "code", Order = 0)]
        public string Code { get; set; } = string.Empty;
        [DataMember(Name = "severity", Order = 1)]
        public string Severity { get; set; } = "warning";
        [DataMember(Name = "message", Order = 2)]
        public string Message { get; set; } = string.Empty;
        [DataMember(Name = "objectId", Order = 3)]
        public string ObjectId { get; set; }
    }

    public static class SnapshotV1Json
    {
        public static string Serialize(SnapshotV1 snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(SnapshotV1)).WriteObject(stream, snapshot);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }
}
