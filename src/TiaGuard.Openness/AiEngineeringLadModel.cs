using System.Collections.Generic;
using System.Runtime.Serialization;

namespace TiaGuard.Openness
{
    [DataContract]
    public sealed class AiLadGraph
    {
        [DataMember(Name="schemaVersion", Order=0)] public string SchemaVersion = "lad-evidence-v1";
        [DataMember(Name="status", Order=1)] public string Status = "partial";
        [DataMember(Name="nodes", Order=2)] public List<AiLadNode> Nodes = new List<AiLadNode>();
        // Wires are hyperedges: keeping every endpoint prevents a fan-out being flattened into a chain.
        [DataMember(Name="wires", Order=3)] public List<AiLadWire> Wires = new List<AiLadWire>();
        [DataMember(Name="diagnostics", Order=4)] public List<AiLadDiagnostic> Diagnostics = new List<AiLadDiagnostic>();
    }
    [DataContract]
    public sealed class AiLadNode
    {
        [DataMember(Name="id", Order=0)] public string Id;
        [DataMember(Name="kind", Order=1)] public string Kind;
        [DataMember(Name="instruction", Order=2)] public string Instruction;
        [DataMember(Name="scope", Order=3)] public string Scope;
        [DataMember(Name="symbol", Order=4)] public string Symbol;
        [DataMember(Name="negated", Order=5)] public bool Negated;
        [DataMember(Name="sourceUid", Order=6)] public string SourceUid;
        [DataMember(Name="sourceSelector", Order=7)] public string SourceSelector;
    }
    [DataContract]
    public sealed class AiLadWire
    {
        [DataMember(Name="id", Order=0)] public string Id;
        [DataMember(Name="endpoints", Order=1)] public List<AiLadEndpoint> Endpoints = new List<AiLadEndpoint>();
        [DataMember(Name="sourceUid", Order=2)] public string SourceUid;
        [DataMember(Name="sourceSelector", Order=3)] public string SourceSelector;
    }
    [DataContract]
    public sealed class AiLadEndpoint
    {
        [DataMember(Name="kind", Order=0)] public string Kind;
        [DataMember(Name="nodeId", Order=1)] public string NodeId;
        [DataMember(Name="port", Order=2)] public string Port;
    }
    [DataContract]
    public sealed class AiLadDiagnostic
    {
        [DataMember(Name="code", Order=0)] public string Code;
        [DataMember(Name="sourceRef", Order=1)] public string SourceRef;
    }
    [DataContract]
    public sealed class AiLadAnalysis
    {
        [DataMember(Name="status", Order=0)] public string Status = "unavailable";
        [DataMember(Name="evaluation", Order=1)] public string Evaluation = "assignment-at-network-execution; networks execute in ordinal order; not simultaneous equations or a safety proof";
        [DataMember(Name="reads", Order=2)] public List<AiLadUse> Reads = new List<AiLadUse>();
        [DataMember(Name="writes", Order=3)] public List<AiLadWrite> Writes = new List<AiLadWrite>();
        [DataMember(Name="flows", Order=4)] public List<AiLadFlow> Flows = new List<AiLadFlow>();
        [DataMember(Name="diagnostics", Order=5)] public List<AiLadDiagnostic> Diagnostics = new List<AiLadDiagnostic>();
    }
    [DataContract]
    public sealed class AiLadUse
    {
        [DataMember(Name="nodeId", Order=0)] public string NodeId;
        [DataMember(Name="accessId", Order=1)] public string AccessId;
        [DataMember(Name="operandWireId", Order=2)] public string OperandWireId;
        [DataMember(Name="symbol", Order=3)] public string Symbol;
        [DataMember(Name="address", Order=4)] public string Address;
        [DataMember(Name="negated", Order=5)] public bool Negated;
    }
    [DataContract]
    public sealed class AiLadWrite
    {
        [DataMember(Name="target", Order=0)] public AiLadUse Target;
        [DataMember(Name="expression", Order=1)] public AiLadExpression Expression;
        [DataMember(Name="path", Order=2)] public List<string> Path = new List<string>();
    }
    [DataContract]
    public sealed class AiLadExpression
    {
        [DataMember(Name="op", Order=0)] public string Op;
        [DataMember(Name="symbol", Order=1)] public string Symbol;
        [DataMember(Name="args", Order=2)] public List<AiLadExpression> Args = new List<AiLadExpression>();
    }
    [DataContract]
    public sealed class AiLadFlow
    {
        [DataMember(Name="from", Order=0)] public string From;
        [DataMember(Name="fromPort", Order=1)] public string FromPort;
        [DataMember(Name="to", Order=2)] public string To;
        [DataMember(Name="toPort", Order=3)] public string ToPort;
        [DataMember(Name="wireId", Order=4)] public string WireId;
    }
    [DataContract]
    public sealed class AiLadRelationships
    {
        [DataMember(Name="status", Order=0)] public string Status = "unavailable";
        [DataMember(Name="interlocks", Order=1)] public List<AiLadInterlock> Interlocks = new List<AiLadInterlock>();
        [DataMember(Name="diagnostics", Order=2)] public List<AiLadDiagnostic> Diagnostics = new List<AiLadDiagnostic>();
    }
    [DataContract]
    public sealed class AiLadInterlock
    {
        [DataMember(Name="kind", Order=0)] public string Kind = "mutual-output-inhibit";
        [DataMember(Name="members", Order=1)] public List<string> Members = new List<string>();
        [DataMember(Name="evidence", Order=2)] public List<AiLadInterlockEvidence> Evidence = new List<AiLadInterlockEvidence>();
        [DataMember(Name="meaning", Order=3)] public string Meaning = "reciprocal mandatory negated reads in unique output assignments; sequential scan, no runtime/safety guarantee";
    }
    [DataContract]
    public sealed class AiLadInterlockEvidence
    {
        [DataMember(Name="networkId", Order=0)] public string NetworkId;
        [DataMember(Name="writeNodeId", Order=1)] public string WriteNodeId;
        [DataMember(Name="readNodeId", Order=2)] public string ReadNodeId;
        [DataMember(Name="sourceRef", Order=3)] public string SourceRef;
    }
}
