using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

namespace TiaGuard.Openness
{
    // Queries an already-built AI Engineering v2 view. This is not a second LAD parser.
    public static class AiEngineeringContextQuery
    {
        public static AiProjectContext ProjectContext(AiEngineeringView view)
        {
            RequireView(view);
            var context = new AiProjectContext
            {
                SchemaVersion = view.SchemaVersion,
                Authority = view.Authority,
                Project = view.Project,
                Hardware = view.Hardware,
                Plc = view.Plc,
                Coverage = view.Coverage,
                Relationships = view.Blocks.Count == 0 ? null : view.Blocks[0].Relationships
            };
            foreach (var table in view.TagTables)
            {
                var summary = new AiTagTableSummary
                {
                    Name = table.Name,
                    SourceRef = table.SourceRef
                };
                foreach (var tag in table.Tags)
                    summary.Tags.Add(new AiTagSummary
                    {
                        Name = tag.Name,
                        DataType = tag.DataType,
                        Address = tag.Address,
                        SourceRef = tag.SourceRef
                    });
                context.TagTables.Add(summary);
            }
            foreach (var block in view.Blocks)
            {
                var summary = new AiBlockSummary
                {
                    Name = block.Name,
                    Kind = block.Kind,
                    Number = block.Number,
                    Language = block.Language,
                    SourceRef = block.SourceRef,
                    XmlSourceRef = block.XmlSourceRef,
                    NetworkInventory = block.NetworkInventory,
                    RelationshipStatus = block.Relationships == null ? null : block.Relationships.Status
                };
                foreach (var network in block.Networks)
                    summary.Networks.Add(SummarizeNetwork(network));
                context.Blocks.Add(summary);
            }
            context.ProgramSummary = context.Blocks.Count == 0
                ? "No block was present in the derived view."
                : string.Join("; ", context.Blocks.Select(block =>
                    BlockLabel(block.Kind, block.Number, block.Name) + " / " +
                    block.Language + " / " +
                    block.Networks.Count.ToString(CultureInfo.InvariantCulture) + " networks"));
            return context;
        }

        public static AiProgramGraphResponse ProgramGraph(AiEngineeringView view, string block)
        {
            RequireView(view);
            var resolved = ResolveBlock(view, block);
            return new AiProgramGraphResponse
            {
                SchemaVersion = view.SchemaVersion,
                Authority = view.Authority,
                BlockLabel = BlockLabel(resolved.Kind, resolved.Number, resolved.Name),
                Block = resolved,
                Coverage = view.Coverage
            };
        }

        public static AiNetworkResponse Network(AiEngineeringView view, string block, int network)
        {
            RequireView(view);
            var resolved = ResolveBlock(view, block);
            var match = ResolveNetwork(resolved, network);
            return new AiNetworkResponse
            {
                SchemaVersion = view.SchemaVersion,
                Authority = view.Authority,
                BlockLabel = BlockLabel(resolved.Kind, resolved.Number, resolved.Name),
                BlockName = resolved.Name,
                Network = match
            };
        }

        public static AiWhereUsedResult WhereUsed(AiEngineeringView view, string symbol)
        {
            RequireView(view);
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("AI_CONTEXT_REJECTED: SYMBOL_REQUIRED");
            var result = new AiWhereUsedResult { Symbol = symbol };
            foreach (var block in view.Blocks)
            {
                var label = BlockLabel(block.Kind, block.Number, block.Name);
                foreach (var network in block.Networks)
                {
                    if (network.Analysis == null || network.Analysis.Status != "supported")
                    {
                        result.Unsearched.Add(new AiWhereUsedGap
                        {
                            Block = label,
                            BlockName = block.Name,
                            Network = network.Ordinal,
                            NetworkId = network.Id,
                            SourceRef = network.SourceRef,
                            AnalysisStatus = network.Analysis == null ? "missing" : network.Analysis.Status,
                            Diagnostics = network.Analysis == null
                                ? new List<AiLadDiagnostic>()
                                : network.Analysis.Diagnostics
                        });
                        continue;
                    }
                    foreach (var read in network.Analysis.Reads.Where(item =>
                                 string.Equals(item.Symbol, symbol, StringComparison.Ordinal)))
                    {
                        result.References.Add(Hit(
                            read.Negated ? "negated-read" : "read",
                            label, block.Name, network, read.NodeId, read));
                    }
                    foreach (var write in network.Analysis.Writes.Where(item =>
                                 item.Target != null &&
                                 string.Equals(item.Target.Symbol, symbol, StringComparison.Ordinal)))
                    {
                        result.References.Add(Hit(
                            "write", label, block.Name, network, write.Target.NodeId, write.Target));
                    }
                }
            }
            result.References = result.References
                .OrderBy(item => item.Network)
                .ThenBy(item => item.Kind, StringComparer.Ordinal)
                .ThenBy(item => item.NodeId, StringComparer.Ordinal)
                .ToList();
            return result;
        }

        public static AiBlock ResolveBlock(AiEngineeringView view, string block)
        {
            if (string.IsNullOrWhiteSpace(block))
                throw new ArgumentException("AI_CONTEXT_REJECTED: BLOCK_REQUIRED");
            var matches = view.Blocks.Where(item =>
                string.Equals(item.Name, block, StringComparison.Ordinal) ||
                string.Equals(BlockLabel(item.Kind, item.Number, item.Name), block, StringComparison.Ordinal))
                .ToList();
            if (matches.Count != 1)
                throw new ArgumentException("AI_CONTEXT_REJECTED: UNKNOWN_BLOCK: " + block);
            return matches[0];
        }

        public static AiNetwork ResolveNetwork(AiBlock block, int network)
        {
            if (network < 1)
                throw new ArgumentException("AI_CONTEXT_REJECTED: UNKNOWN_NETWORK: " +
                    network.ToString(CultureInfo.InvariantCulture));
            var match = block.Networks.SingleOrDefault(item => item.Ordinal == network);
            if (match == null)
                throw new ArgumentException("AI_CONTEXT_REJECTED: UNKNOWN_NETWORK: " +
                    network.ToString(CultureInfo.InvariantCulture));
            return match;
        }

        public static string BlockLabel(string kind, int number, string name)
        {
            if (!string.IsNullOrWhiteSpace(kind) && number > 0)
                return kind + number.ToString(CultureInfo.InvariantCulture);
            return name ?? string.Empty;
        }

        public static void RejectBlobLeakage(string json)
        {
            if (string.IsNullOrEmpty(json))
                return;
            if (json.IndexOf("base64", StringComparison.OrdinalIgnoreCase) >= 0)
                throw new InvalidOperationException("AI_CONTEXT_REJECTED: BLOB_LEAKAGE");
            if (Regex.IsMatch(json, "[A-Za-z0-9+/=]{180,}"))
                throw new InvalidOperationException("AI_CONTEXT_REJECTED: BLOB_LEAKAGE");
        }

        private static AiNetworkSummary SummarizeNetwork(AiNetwork network)
        {
            string expression = null;
            if (network.Analysis != null && network.Analysis.Status == "supported" &&
                network.Analysis.Writes.Count == 1 && network.Analysis.Writes[0].Expression != null)
                expression = FormatExpression(network.Analysis.Writes[0].Expression);
            return new AiNetworkSummary
            {
                Id = network.Id,
                Ordinal = network.Ordinal,
                Language = network.Language,
                SourceRef = network.SourceRef,
                LogicStatus = network.Logic == null ? null : network.Logic.Status,
                GraphStatus = network.Graph == null ? null : network.Graph.Status,
                AnalysisStatus = network.Analysis == null ? null : network.Analysis.Status,
                DerivedExpression = expression,
                GraphDiagnosticCount = network.Graph == null ? 0 : network.Graph.Diagnostics.Count,
                AnalysisDiagnosticCount = network.Analysis == null ? 0 : network.Analysis.Diagnostics.Count
            };
        }

        internal static string FormatExpression(AiLadExpression expression)
        {
            if (expression == null) return string.Empty;
            if (expression.Op == "read") return expression.Symbol ?? string.Empty;
            if (expression.Op == "not")
                return "NOT " + FormatExpression(expression.Args == null || expression.Args.Count == 0
                    ? null : expression.Args[0]);
            if (expression.Op == "and")
                return string.Join(" AND ", (expression.Args ?? new List<AiLadExpression>()).Select(FormatExpression));
            return expression.Op ?? string.Empty;
        }

        private static AiWhereUsedHit Hit(
            string kind, string label, string blockName, AiNetwork network, string nodeId, AiLadUse use)
        {
            return new AiWhereUsedHit
            {
                Kind = kind,
                Block = label,
                BlockName = blockName,
                Network = network.Ordinal,
                NetworkId = network.Id,
                SourceRef = network.SourceRef,
                NodeId = nodeId,
                Symbol = use == null ? null : use.Symbol,
                Negated = use != null && use.Negated
            };
        }

        private static void RequireView(AiEngineeringView view)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (view.SchemaVersion != "ai-engineering-v2")
                throw new InvalidOperationException("AI_CONTEXT_REJECTED: SCHEMA_VERSION");
        }
    }

    [DataContract]
    public sealed class AiProjectContext
    {
        [DataMember(Name = "schemaVersion", Order = 0)] public string SchemaVersion;
        [DataMember(Name = "authority", Order = 1)] public string Authority;
        [DataMember(Name = "project", Order = 2)] public AiProject Project;
        [DataMember(Name = "hardware", Order = 3)] public AiHardware Hardware;
        [DataMember(Name = "plc", Order = 4)] public AiPlc Plc;
        [DataMember(Name = "blocks", Order = 5)] public List<AiBlockSummary> Blocks = new List<AiBlockSummary>();
        [DataMember(Name = "tagTables", Order = 6)] public List<AiTagTableSummary> TagTables = new List<AiTagTableSummary>();
        [DataMember(Name = "programSummary", Order = 7)] public string ProgramSummary;
        [DataMember(Name = "coverage", Order = 8)] public List<AiCoverage> Coverage = new List<AiCoverage>();
        [DataMember(Name = "relationships", Order = 9)] public AiLadRelationships Relationships;
    }

    [DataContract]
    public sealed class AiBlockSummary
    {
        [DataMember(Name = "name", Order = 0)] public string Name;
        [DataMember(Name = "kind", Order = 1)] public string Kind;
        [DataMember(Name = "number", Order = 2)] public int Number;
        [DataMember(Name = "language", Order = 3)] public string Language;
        [DataMember(Name = "sourceRef", Order = 4)] public string SourceRef;
        [DataMember(Name = "xmlSourceRef", Order = 5)] public string XmlSourceRef;
        [DataMember(Name = "networkInventory", Order = 6)] public AiCoverage NetworkInventory;
        [DataMember(Name = "relationshipStatus", Order = 7)] public string RelationshipStatus;
        [DataMember(Name = "networks", Order = 8)] public List<AiNetworkSummary> Networks = new List<AiNetworkSummary>();
    }

    [DataContract]
    public sealed class AiNetworkSummary
    {
        [DataMember(Name = "id", Order = 0)] public string Id;
        [DataMember(Name = "ordinal", Order = 1)] public int Ordinal;
        [DataMember(Name = "language", Order = 2)] public string Language;
        [DataMember(Name = "sourceRef", Order = 3)] public string SourceRef;
        [DataMember(Name = "logicStatus", Order = 4)] public string LogicStatus;
        [DataMember(Name = "graphStatus", Order = 5)] public string GraphStatus;
        [DataMember(Name = "analysisStatus", Order = 6)] public string AnalysisStatus;
        [DataMember(Name = "derivedExpression", Order = 7)] public string DerivedExpression;
        [DataMember(Name = "graphDiagnosticCount", Order = 8)] public int GraphDiagnosticCount;
        [DataMember(Name = "analysisDiagnosticCount", Order = 9)] public int AnalysisDiagnosticCount;
    }

    [DataContract]
    public sealed class AiTagTableSummary
    {
        [DataMember(Name = "name", Order = 0)] public string Name;
        [DataMember(Name = "sourceRef", Order = 1)] public string SourceRef;
        [DataMember(Name = "tags", Order = 2)] public List<AiTagSummary> Tags = new List<AiTagSummary>();
    }

    [DataContract]
    public sealed class AiTagSummary
    {
        [DataMember(Name = "name", Order = 0)] public string Name;
        [DataMember(Name = "dataType", Order = 1)] public string DataType;
        [DataMember(Name = "address", Order = 2)] public string Address;
        [DataMember(Name = "sourceRef", Order = 3)] public string SourceRef;
    }

    [DataContract]
    public sealed class AiProgramGraphResponse
    {
        [DataMember(Name = "schemaVersion", Order = 0)] public string SchemaVersion;
        [DataMember(Name = "authority", Order = 1)] public string Authority;
        [DataMember(Name = "blockLabel", Order = 2)] public string BlockLabel;
        [DataMember(Name = "block", Order = 3)] public AiBlock Block;
        [DataMember(Name = "coverage", Order = 4)] public List<AiCoverage> Coverage;
    }

    [DataContract]
    public sealed class AiNetworkResponse
    {
        [DataMember(Name = "schemaVersion", Order = 0)] public string SchemaVersion;
        [DataMember(Name = "authority", Order = 1)] public string Authority;
        [DataMember(Name = "blockLabel", Order = 2)] public string BlockLabel;
        [DataMember(Name = "blockName", Order = 3)] public string BlockName;
        [DataMember(Name = "network", Order = 4)] public AiNetwork Network;
    }

    [DataContract]
    public sealed class AiWhereUsedResult
    {
        [DataMember(Name = "symbol", Order = 0)] public string Symbol;
        [DataMember(Name = "references", Order = 1)] public List<AiWhereUsedHit> References = new List<AiWhereUsedHit>();
        [DataMember(Name = "unsearched", Order = 2)] public List<AiWhereUsedGap> Unsearched = new List<AiWhereUsedGap>();
    }

    [DataContract]
    public sealed class AiWhereUsedHit
    {
        [DataMember(Name = "kind", Order = 0)] public string Kind;
        [DataMember(Name = "block", Order = 1)] public string Block;
        [DataMember(Name = "blockName", Order = 2)] public string BlockName;
        [DataMember(Name = "network", Order = 3)] public int Network;
        [DataMember(Name = "networkId", Order = 4)] public string NetworkId;
        [DataMember(Name = "sourceRef", Order = 5)] public string SourceRef;
        [DataMember(Name = "nodeId", Order = 6)] public string NodeId;
        [DataMember(Name = "symbol", Order = 7)] public string Symbol;
        [DataMember(Name = "negated", Order = 8)] public bool Negated;
    }

    [DataContract]
    public sealed class AiWhereUsedGap
    {
        [DataMember(Name = "block", Order = 0)] public string Block;
        [DataMember(Name = "blockName", Order = 1)] public string BlockName;
        [DataMember(Name = "network", Order = 2)] public int Network;
        [DataMember(Name = "networkId", Order = 3)] public string NetworkId;
        [DataMember(Name = "sourceRef", Order = 4)] public string SourceRef;
        [DataMember(Name = "analysisStatus", Order = 5)] public string AnalysisStatus;
        [DataMember(Name = "diagnostics", Order = 6)] public List<AiLadDiagnostic> Diagnostics = new List<AiLadDiagnostic>();
    }
}
