using System.Collections.Generic;
using System.Runtime.Serialization;

namespace TiaGuard.Openness
{
    [DataContract]
    public sealed class AiEngineeringView
    {
        [DataMember(Name = "schemaVersion", Order = 0)] public string SchemaVersion = "ai-engineering-v2";
        [DataMember(Name = "authority", Order = 1)] public string Authority = "derived-not-build-input";
        [DataMember(Name = "project", Order = 2)] public AiProject Project;
        [DataMember(Name = "hardware", Order = 3)] public AiHardware Hardware;
        [DataMember(Name = "plc", Order = 4)] public AiPlc Plc;
        [DataMember(Name = "tagTables", Order = 5)] public List<AiTagTable> TagTables = new List<AiTagTable>();
        [DataMember(Name = "blocks", Order = 6)] public List<AiBlock> Blocks = new List<AiBlock>();
        [DataMember(Name = "coverage", Order = 7)] public List<AiCoverage> Coverage = new List<AiCoverage>();
    }

    [DataContract]
    public sealed class AiProject
    {
        [DataMember(Name = "name", Order = 0)] public string Name;
        [DataMember(Name = "tiaVersion", Order = 1)] public string TiaVersion;
        [DataMember(Name = "projectVersion", Order = 2)] public string ProjectVersion;
        [DataMember(Name = "sourceRef", Order = 3)] public string SourceRef;
    }

    [DataContract]
    public sealed class AiHardware
    {
        [DataMember(Name = "station", Order = 0)] public string Station;
        [DataMember(Name = "family", Order = 1)] public string Family = "S7-1200";
        [DataMember(Name = "cpuCreateIdentity", Order = 2)] public string CpuCreateIdentity;
        [DataMember(Name = "orderNumber", Order = 3)] public string OrderNumber;
        [DataMember(Name = "firmware", Order = 4)] public string Firmware;
        [DataMember(Name = "sourceRef", Order = 5)] public string SourceRef;
    }

    [DataContract]
    public sealed class AiPlc
    {
        [DataMember(Name = "name", Order = 0)] public string Name;
        [DataMember(Name = "sourceRef", Order = 1)] public string SourceRef;
    }

    [DataContract]
    public sealed class AiTagTable
    {
        [DataMember(Name = "name", Order = 0)] public string Name;
        [DataMember(Name = "sourceRef", Order = 1)] public string SourceRef;
        [DataMember(Name = "tags", Order = 2)] public List<AiTag> Tags = new List<AiTag>();
    }

    [DataContract]
    public sealed class AiTag
    {
        [DataMember(Name = "name", Order = 0)] public string Name;
        [DataMember(Name = "dataType", Order = 1)] public string DataType;
        [DataMember(Name = "address", Order = 2)] public string Address;
        [DataMember(Name = "commentStatus", Order = 3)] public string CommentStatus;
        [DataMember(Name = "comment", Order = 4)] public string Comment;
        [DataMember(Name = "sourceRef", Order = 5)] public string SourceRef;
    }

    [DataContract]
    public sealed class AiBlock
    {
        [DataMember(Name = "name", Order = 0)] public string Name;
        [DataMember(Name = "kind", Order = 1)] public string Kind;
        [DataMember(Name = "number", Order = 2)] public int Number;
        [DataMember(Name = "language", Order = 3)] public string Language;
        [DataMember(Name = "sourceRef", Order = 4)] public string SourceRef;
        [DataMember(Name = "xmlSourceRef", Order = 5)] public string XmlSourceRef;
        [DataMember(Name = "networkInventory", Order = 6)] public AiCoverage NetworkInventory;
        [DataMember(Name = "networks", Order = 7)] public List<AiNetwork> Networks = new List<AiNetwork>();
        [DataMember(Name = "relationships", Order = 8)] public AiLadRelationships Relationships;
    }

    [DataContract]
    public sealed class AiNetwork
    {
        [DataMember(Name = "ordinal", Order = 0)] public int Ordinal;
        [DataMember(Name = "language", Order = 1)] public string Language;
        [DataMember(Name = "sourceRef", Order = 2)] public string SourceRef;
        [DataMember(Name = "titles", Order = 3)] public List<AiText> Titles = new List<AiText>();
        [DataMember(Name = "comments", Order = 4)] public List<AiText> Comments = new List<AiText>();
        [DataMember(Name = "logic", Order = 5)] public AiCoverage Logic;
        [DataMember(Name = "id", Order = 6)] public string Id;
        [DataMember(Name = "graph", Order = 7)] public AiLadGraph Graph;
        [DataMember(Name = "analysis", Order = 8)] public AiLadAnalysis Analysis;
    }

    [DataContract]
    public sealed class AiText
    {
        [DataMember(Name = "culture", Order = 0)] public string Culture;
        [DataMember(Name = "text", Order = 1)] public string Text;
        [DataMember(Name = "sourceRef", Order = 2)] public string SourceRef;
    }

    [DataContract]
    public sealed class AiCoverage
    {
        [DataMember(Name = "area", Order = 0)] public string Area;
        [DataMember(Name = "status", Order = 1)] public string Status;
        [DataMember(Name = "reason", Order = 2)] public string Reason;
    }
}
