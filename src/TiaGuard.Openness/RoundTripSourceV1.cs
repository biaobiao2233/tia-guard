using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace TiaGuard.Openness
{
    public static class RoundTripCapabilityStates
    {
        public const string SupportedRoundTrip = "supported-round-trip";
        public const string ExportOnly = "export-only";
        public const string Opaque = "opaque";
        public const string Unsupported = "unsupported";
        public const string Failed = "failed";

        public static bool IsKnown(string value)
        {
            return value == SupportedRoundTrip || value == ExportOnly || value == Opaque ||
                   value == Unsupported || value == Failed;
        }
    }

    [DataContract]
    public sealed class RoundTripManifestV1
    {
        [DataMember(Name = "schemaVersion", Order = 0)]
        public string SchemaVersion { get; set; } = "1.0";
        [DataMember(Name = "contractStatus", Order = 1)]
        public string ContractStatus { get; set; } = "draft";
        [DataMember(Name = "tiaVersion", Order = 2)]
        public string TiaVersion { get; set; } = "V21";
        [DataMember(Name = "project", Order = 3)]
        public RoundTripProjectV1 Project { get; set; } = new RoundTripProjectV1();
        [DataMember(Name = "roundTripReady", Order = 4)]
        public bool RoundTripReady { get; set; }
        [DataMember(Name = "hardware", Order = 5)]
        public List<string> Hardware { get; set; } = new List<string>();
        [DataMember(Name = "plcs", Order = 6)]
        public List<string> Plcs { get; set; } = new List<string>();
        [DataMember(Name = "capabilities", Order = 7)]
        public List<RoundTripCapabilityV1> Capabilities { get; set; } = new List<RoundTripCapabilityV1>();
        [DataMember(Name = "diagnostics", Order = 8)]
        public List<RoundTripDiagnosticV1> Diagnostics { get; set; } = new List<RoundTripDiagnosticV1>();
    }

    [DataContract]
    public sealed class RoundTripProjectV1
    {
        [DataMember(Name = "name", Order = 0)]
        public string Name { get; set; } = string.Empty;
        [DataMember(Name = "projectVersion", Order = 1)]
        public string ProjectVersion { get; set; }
    }

    [DataContract]
    public sealed class RoundTripCapabilityV1
    {
        [DataMember(Name = "objectKind", Order = 0)]
        public string ObjectKind { get; set; } = string.Empty;
        [DataMember(Name = "objectRef", Order = 1)]
        public string ObjectRef { get; set; } = string.Empty;
        [DataMember(Name = "state", Order = 2)]
        public string State { get; set; } = RoundTripCapabilityStates.Failed;
        [DataMember(Name = "reason", Order = 3)]
        public string Reason { get; set; }
    }

    [DataContract]
    public sealed class RoundTripDiagnosticV1
    {
        [DataMember(Name = "code", Order = 0)]
        public string Code { get; set; } = string.Empty;
        [DataMember(Name = "message", Order = 1)]
        public string Message { get; set; } = string.Empty;
        [DataMember(Name = "objectRef", Order = 2)]
        public string ObjectRef { get; set; }
    }

    [DataContract]
    public sealed class RoundTripHardwareV1
    {
        [DataMember(Name = "schemaVersion", Order = 0)]
        public string SchemaVersion { get; set; } = "1.0";
        [DataMember(Name = "id", Order = 1)]
        public string Id { get; set; } = string.Empty;
        [DataMember(Name = "name", Order = 2)]
        public string Name { get; set; } = string.Empty;
        [DataMember(Name = "engineeringPath", Order = 3)]
        public string EngineeringPath { get; set; }
        [DataMember(Name = "deviceTypeIdentifier", Order = 4)]
        public string DeviceTypeIdentifier { get; set; }
        [DataMember(Name = "createTypeIdentifier", Order = 5)]
        public string CreateTypeIdentifier { get; set; }
        [DataMember(Name = "createItemName", Order = 6)]
        public string CreateItemName { get; set; }
        [DataMember(Name = "orderNumber", Order = 7)]
        public string OrderNumber { get; set; }
        [DataMember(Name = "firmware", Order = 8)]
        public string Firmware { get; set; }
        [DataMember(Name = "capability", Order = 9)]
        public string Capability { get; set; } = RoundTripCapabilityStates.Failed;
    }

    [DataContract]
    public sealed class RoundTripPlcV1
    {
        [DataMember(Name = "schemaVersion", Order = 0)]
        public string SchemaVersion { get; set; } = "1.0";
        [DataMember(Name = "id", Order = 1)]
        public string Id { get; set; } = string.Empty;
        [DataMember(Name = "name", Order = 2)]
        public string Name { get; set; } = string.Empty;
        [DataMember(Name = "deviceId", Order = 3)]
        public string DeviceId { get; set; } = string.Empty;
        [DataMember(Name = "capability", Order = 4)]
        public string Capability { get; set; } = RoundTripCapabilityStates.Failed;
        [DataMember(Name = "tagTables", Order = 5)]
        public List<string> TagTables { get; set; } = new List<string>();
        [DataMember(Name = "blocks", Order = 6)]
        public List<string> Blocks { get; set; } = new List<string>();
    }

    [DataContract]
    public sealed class RoundTripTagTableV1
    {
        [DataMember(Name = "schemaVersion", Order = 0)]
        public string SchemaVersion { get; set; } = "1.0";
        [DataMember(Name = "id", Order = 1)]
        public string Id { get; set; } = string.Empty;
        [DataMember(Name = "name", Order = 2)]
        public string Name { get; set; } = string.Empty;
        [DataMember(Name = "scopePath", Order = 3)]
        public string ScopePath { get; set; } = string.Empty;
        [DataMember(Name = "capability", Order = 4)]
        public string Capability { get; set; } = RoundTripCapabilityStates.Failed;
        [DataMember(Name = "tags", Order = 5)]
        public List<RoundTripTagV1> Tags { get; set; } = new List<RoundTripTagV1>();
    }

    [DataContract]
    public sealed class RoundTripTagV1
    {
        [DataMember(Name = "id", Order = 0)]
        public string Id { get; set; } = string.Empty;
        [DataMember(Name = "name", Order = 1)]
        public string Name { get; set; } = string.Empty;
        [DataMember(Name = "dataType", Order = 2)]
        public string DataType { get; set; } = string.Empty;
        [DataMember(Name = "address", Order = 3)]
        public string Address { get; set; }
        [DataMember(Name = "commentStatus", Order = 4)]
        public string CommentStatus { get; set; } = "unavailable";
        [DataMember(Name = "comment", Order = 5)]
        public string Comment { get; set; }
        [DataMember(Name = "capability", Order = 6)]
        public string Capability { get; set; } = RoundTripCapabilityStates.Failed;
    }

    [DataContract]
    public sealed class RoundTripBlockV1
    {
        [DataMember(Name = "schemaVersion", Order = 0)]
        public string SchemaVersion { get; set; } = "1.0";
        [DataMember(Name = "id", Order = 1)]
        public string Id { get; set; } = string.Empty;
        [DataMember(Name = "name", Order = 2)]
        public string Name { get; set; } = string.Empty;
        [DataMember(Name = "scopePath", Order = 3)]
        public string ScopePath { get; set; } = string.Empty;
        [DataMember(Name = "kind", Order = 4)]
        public string Kind { get; set; } = string.Empty;
        [DataMember(Name = "number", Order = 5)]
        public int? Number { get; set; }
        [DataMember(Name = "language", Order = 6)]
        public string Language { get; set; }
        [DataMember(Name = "capability", Order = 7)]
        public string Capability { get; set; } = RoundTripCapabilityStates.Failed;
        [DataMember(Name = "source", Order = 8)]
        public RoundTripArtifactV1 Source { get; set; }
    }

    [DataContract]
    public sealed class RoundTripArtifactV1
    {
        [DataMember(Name = "format", Order = 0)]
        public string Format { get; set; } = "SimaticML";
        [DataMember(Name = "artifact", Order = 1)]
        public string Artifact { get; set; } = string.Empty;
        [DataMember(Name = "sha256", Order = 2)]
        public string Sha256 { get; set; } = string.Empty;
        [DataMember(Name = "normalizationVersion", Order = 3)]
        public string NormalizationVersion { get; set; } = "simaticml-v1";
    }

    public sealed class RoundTripHardwareBuildIdentity
    {
        public string DeviceName { get; set; }
        public string CpuItemName { get; set; }
        public string CreateTypeIdentifier { get; set; }
        public string OrderNumber { get; set; }
        public string Firmware { get; set; }
        public string State { get; set; } = RoundTripCapabilityStates.Failed;
        public string Reason { get; set; }
    }

    public sealed class RoundTripTagTableHint
    {
        public string PlcName { get; set; }
        public string ScopePath { get; set; }
        public string Name { get; set; }
    }

    public sealed class RoundTripExtractionHints
    {
        public RoundTripHardwareBuildIdentity Hardware { get; set; } = new RoundTripHardwareBuildIdentity();
        public List<RoundTripTagTableHint> TagTables { get; set; } = new List<RoundTripTagTableHint>();
        public RoundTripCompilePreparation CompilePreparation { get; set; } = new RoundTripCompilePreparation();
    }

    public sealed class RoundTripCompilePreparation
    {
        public bool Attempted { get; set; }
        public string State { get; set; } = "not-needed";
        public int? Errors { get; set; }
        public int? Warnings { get; set; }
        public string FailureType { get; set; }
    }

    public static class RoundTripJson
    {
        public static string Serialize<T>(T value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
            }
        }
    }
}
