using System;
using System.Collections.Generic;
using System.Linq;

namespace TiaGuard.Openness
{
    public static class AiEngineeringLadRelationships
    {
        public static AiLadRelationships Analyze(AiBlock block, IEnumerable<AiTag> symbols)
        {
            var result = new AiLadRelationships();
            if (block.NetworkInventory.Status != "extracted" || block.Networks.Any(n =>
                n.Logic.Status != "empty" && n.Analysis?.Status != "supported"))
                return Unavailable(result, "BLOCK_HAS_UNANALYZED_NETWORK", block.SourceRef);
            var writes = block.Networks.Where(n => n.Analysis != null).SelectMany(n =>
                n.Analysis.Writes.Select(w => new { Network = n, Write = w })).ToList();
            if (writes.GroupBy(w => w.Write.Target.Symbol, StringComparer.Ordinal).Any(g => g.Count() != 1))
                return Unavailable(result, "BLOCK_HAS_MULTIPLE_WRITERS", block.SourceRef);
            var tags = symbols.ToList();
            // Aliases (including wider I/Q/M declarations) invalidate symbol-only writer reasoning.
            foreach (var write in writes)
                if (tags.Any(t => t.Name != write.Write.Target.Symbol && AiEngineeringLadParser.Overlap(t.Address, write.Write.Target.Address)))
                    return Unavailable(result, "WRITE_ADDRESS_HAS_ALIAS", block.SourceRef);
            result.Status = "complete";
            var outputs = writes.Where(w => SnapshotAddressParser.Parse(w.Write.Target.Address).Area == "Q")
                .OrderBy(w => w.Write.Target.Symbol, StringComparer.Ordinal).ToList();
            for (var i = 0; i < outputs.Count; i++)
                for (var j = i + 1; j < outputs.Count; j++)
                {
                    var a = outputs[i]; var b = outputs[j];
                    if (a.Network.Id == b.Network.Id || !RequiresNot(a.Write.Expression, b.Write.Target.Symbol) ||
                        !RequiresNot(b.Write.Expression, a.Write.Target.Symbol)) continue;
                    var ar = a.Network.Analysis.Reads.First(r => r.Symbol == b.Write.Target.Symbol && r.Negated && a.Write.Path.Contains(r.NodeId));
                    var br = b.Network.Analysis.Reads.First(r => r.Symbol == a.Write.Target.Symbol && r.Negated && b.Write.Path.Contains(r.NodeId));
                    result.Interlocks.Add(new AiLadInterlock { Members = new List<string> { a.Write.Target.Symbol, b.Write.Target.Symbol },
                        Evidence = new List<AiLadInterlockEvidence> {
                            new AiLadInterlockEvidence { NetworkId = a.Network.Id, WriteNodeId = a.Write.Target.NodeId, ReadNodeId = ar.NodeId, SourceRef = a.Network.SourceRef },
                            new AiLadInterlockEvidence { NetworkId = b.Network.Id, WriteNodeId = b.Write.Target.NodeId, ReadNodeId = br.NodeId, SourceRef = b.Network.SourceRef }
                        } });
                }
            return result;
        }

        private static bool RequiresNot(AiLadExpression expression, string symbol) =>
            (expression.Op == "not" && expression.Args.Count == 1 && expression.Args[0].Op == "read" && expression.Args[0].Symbol == symbol) ||
            (expression.Op == "and" && expression.Args.Any(e => RequiresNot(e, symbol)));
        private static AiLadRelationships Unavailable(AiLadRelationships result, string code, string reference)
        {
            result.Diagnostics.Add(AiEngineeringLadParser.Diagnostic(code, reference)); return result;
        }
    }
}
