using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace TiaGuard.Openness
{
    // These are the exact JSON fields in docs/contracts/snapshot-v1.schema.json.
    // Siemens types stay inside this assembly and never appear in this model.
    [DataContract]
    public sealed class SnapshotV1
    {
        [DataMember(Name = "schemaVersion", Order = 0)]
        public string SchemaVersion { get; set; } = "1.0";

        [DataMember(Name = "project", Order = 1)]
        public SnapshotProject Project { get; set; } = new SnapshotProject();

        [DataMember(Name = "tia", Order = 2)]
        public SnapshotTia Tia { get; set; } = new SnapshotTia();

        [DataMember(Name = "devices", Order = 3)]
        public List<SnapshotDevice> Devices { get; set; } = new List<SnapshotDevice>();

        [DataMember(Name = "plcs", Order = 4)]
        public List<SnapshotPlc> Plcs { get; set; } = new List<SnapshotPlc>();
    }

    [DataContract]
    public sealed class SnapshotProject
    {
        [DataMember(Name = "name", Order = 0)]
        public string Name { get; set; } = string.Empty;

        [DataMember(Name = "path", Order = 1)]
        public string Path { get; set; }
    }

    [DataContract]
    public sealed class SnapshotTia
    {
        [DataMember(Name = "version", Order = 0)]
        public string Version { get; set; } = string.Empty;

        [DataMember(Name = "processId", Order = 1)]
        public int? ProcessId { get; set; }
    }

    [DataContract]
    public sealed class SnapshotDevice
    {
        [DataMember(Name = "name", Order = 0)]
        public string Name { get; set; } = string.Empty;

        [DataMember(Name = "type", Order = 1)]
        public string Type { get; set; } = string.Empty;

        [DataMember(Name = "orderNumber", Order = 2)]
        public string OrderNumber { get; set; }

        [DataMember(Name = "firmware", Order = 3)]
        public string Firmware { get; set; }
    }

    [DataContract]
    public sealed class SnapshotPlc
    {
        [DataMember(Name = "name", Order = 0)]
        public string Name { get; set; } = string.Empty;

        [DataMember(Name = "blocks", Order = 1)]
        public List<SnapshotBlock> Blocks { get; set; } = new List<SnapshotBlock>();

        [DataMember(Name = "tags", Order = 2)]
        public List<SnapshotTag> Tags { get; set; } = new List<SnapshotTag>();

        // No compile operation is performed during extraction. Null means unknown.
        [DataMember(Name = "compile", Order = 3)]
        public SnapshotCompile Compile { get; set; }
    }

    [DataContract]
    public sealed class SnapshotBlock
    {
        [DataMember(Name = "name", Order = 0)]
        public string Name { get; set; } = string.Empty;

        [DataMember(Name = "kind", Order = 1)]
        public string Kind { get; set; } = string.Empty;

        [DataMember(Name = "language", Order = 2)]
        public string Language { get; set; }
    }

    [DataContract]
    public sealed class SnapshotTag
    {
        [DataMember(Name = "name", Order = 0)]
        public string Name { get; set; } = string.Empty;

        [DataMember(Name = "dataType", Order = 1)]
        public string DataType { get; set; } = string.Empty;

        [DataMember(Name = "address", Order = 2)]
        public string Address { get; set; }

        [DataMember(Name = "comment", Order = 3)]
        public string Comment { get; set; }
    }

    [DataContract]
    public sealed class SnapshotCompile
    {
        [DataMember(Name = "errors", Order = 0)]
        public int Errors { get; set; }

        [DataMember(Name = "warnings", Order = 1)]
        public int Warnings { get; set; }
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
