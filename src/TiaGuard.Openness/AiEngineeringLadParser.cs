using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace TiaGuard.Openness
{
    // Deliberately bounded to the re-exported V21 corpus: Boolean global-symbol contacts,
    // ordinary coils, serial paths and single-driver fan-out. Joins and stateful parts fail closed.
    public static class AiEngineeringLadParser
    {
        public const string FlgNetNamespace = "http://www.siemens.com/automation/Openness/SW/NetworkSource/FlgNet/v5";

        public static void Extract(AiNetwork network, XmlElement source, IEnumerable<AiTag> symbols)
        {
            var graph = new AiLadGraph();
            network.Graph = graph;
            network.Analysis = new AiLadAnalysis();
            try { ReadGraph(graph, source, network.SourceRef); graph.Status = "complete"; }
            catch (Rejected e) { graph.Diagnostics.Add(Diagnostic(e.Code, network.SourceRef)); network.Analysis.Status = e.Status; }
            AssignIdentities(graph);
            if (graph.Status != "complete")
            {
                network.Analysis.Diagnostics.AddRange(graph.Diagnostics);
            }
            else
            {
                try { network.Analysis = Analyze(graph, symbols); }
                catch (Rejected e)
                {
                    // All-or-nothing semantics: never return a plausible expression with a lost condition.
                    network.Analysis = new AiLadAnalysis { Status = e.Status };
                    network.Analysis.Diagnostics.Add(Diagnostic(e.Code, network.SourceRef));
                }
            }
            network.Logic = new AiCoverage { Area = "logic", Status = network.Analysis.Status,
                Reason = network.Analysis.Status == "supported" ?
                    "Complete bounded contact/coil topology; derived assignments apply at this network's execution." :
                    "Expression unavailable: inspect graph/analysis diagnostics; no partial Boolean expression is asserted." };
        }

        private static void ReadGraph(AiLadGraph graph, XmlElement source, string reference)
        {
            Need(source != null && source.Name == "NetworkSource" && source.NamespaceURI == "", "NETWORK_SOURCE_SHAPE");
            Shape(source);
            var roots = Children(source);
            Need(roots.Count == 1 && roots[0].Name == "FlgNet" && roots[0].NamespaceURI == FlgNetNamespace, "FLGNET_VERSION_UNSUPPORTED");
            var root = roots[0]; Shape(root);
            Need(Children(root).Count == 2, "FLGNET_CONTAINER_SHAPE");
            var parts = One(root, "Parts"); var wires = One(root, "Wires"); Shape(parts); Shape(wires);
            Need(Children(parts).Count <= 128 && Children(wires).Count <= 256, "GRAPH_LIMIT_EXCEEDED");
            var uids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var element in Children(parts))
            {
                Need(element.NamespaceURI == FlgNetNamespace, "PART_NAMESPACE_UNSUPPORTED");
                var uid = Uid(element, uids);
                var node = new AiLadNode { Id = uid, SourceUid = uid, SourceSelector = ".//*[@UId='" + uid + "']" };
                if (element.Name == "Access")
                {
                    node.Kind = "access"; Shape(element, "Scope", "UId");
                    node.Scope = element.GetAttribute("Scope");
                    Need(Children(element).Count == 1, "ACCESS_SHAPE_UNSUPPORTED");
                    var symbol = One(element, "Symbol"); Shape(symbol);
                    Need(Children(symbol).Count == 1, "COMPOUND_OPERAND_UNSUPPORTED");
                    var component = One(symbol, "Component"); Shape(component, "Name"); Leaf(component);
                    node.Symbol = component.GetAttribute("Name");
                    Need(!string.IsNullOrWhiteSpace(node.Symbol), "OPERAND_MISSING");
                }
                else if (element.Name == "Part")
                {
                    node.Kind = "instruction"; Shape(element, "Name", "UId");
                    node.Instruction = element.GetAttribute("Name");
                    Need(Regex.IsMatch(node.Instruction, "^[A-Za-z][A-Za-z0-9_]{0,63}$"), "INSTRUCTION_NAME_UNSUPPORTED");
                    var children = Children(element);
                    Need(children.Count <= 1, "INSTRUCTION_CONTENT_UNSUPPORTED");
                    if (children.Count == 1)
                    {
                        var negated = children[0];
                        Need(negated.Name == "Negated" && negated.NamespaceURI == FlgNetNamespace, "INSTRUCTION_CONTENT_UNSUPPORTED");
                        Shape(negated, "Name"); Leaf(negated);
                        Need(negated.GetAttribute("Name") == "operand", "NEGATED_PORT_UNSUPPORTED");
                        node.Negated = true;
                    }
                }
                else throw new Rejected("PART_KIND_UNSUPPORTED");
                graph.Nodes.Add(node);
            }
            var nodes = graph.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
            foreach (var element in Children(wires))
            {
                Need(element.Name == "Wire" && element.NamespaceURI == FlgNetNamespace, "WIRE_KIND_UNSUPPORTED");
                Shape(element, "UId"); var uid = Uid(element, uids);
                var wire = new AiLadWire { Id = uid, SourceUid = uid, SourceSelector = ".//*[@UId='" + uid + "']" };
                Need(Children(element).Count >= 2 && Children(element).Count <= 128, "WIRE_ENDPOINT_COUNT");
                foreach (var connector in Children(element))
                {
                    Need(connector.NamespaceURI == FlgNetNamespace, "CONNECTOR_NAMESPACE_UNSUPPORTED"); Leaf(connector);
                    var endpoint = new AiLadEndpoint { Kind = connector.Name };
                    if (connector.Name == "Powerrail")
                    {
                        Shape(connector); endpoint.NodeId = "rail";
                        if (!nodes.ContainsKey("rail"))
                        {
                            var rail = new AiLadNode { Id = "rail", Kind = "powerrail", SourceSelector = ".//*[local-name()='Powerrail']" };
                            nodes.Add("rail", rail); graph.Nodes.Add(rail);
                        }
                    }
                    else if (connector.Name == "NameCon" || connector.Name == "IdentCon")
                    {
                        if (connector.Name == "NameCon") { Shape(connector, "UId", "Name"); endpoint.Port = connector.GetAttribute("Name"); }
                        else Shape(connector, "UId");
                        endpoint.NodeId = connector.GetAttribute("UId");
                        Need(nodes.ContainsKey(endpoint.NodeId), "DANGLING_CONNECTOR", "ambiguous");
                        Need(nodes[endpoint.NodeId].Kind == (connector.Name == "NameCon" ? "instruction" : "access"), "CONNECTOR_KIND_MISMATCH", "ambiguous");
                    }
                    else throw new Rejected("CONNECTOR_KIND_UNSUPPORTED");
                    wire.Endpoints.Add(endpoint);
                }
                graph.Wires.Add(wire);
            }
        }

        private static AiLadAnalysis Analyze(AiLadGraph graph, IEnumerable<AiTag> symbols)
        {
            var result = new AiLadAnalysis { Status = "supported" };
            var nodes = graph.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
            var parts = graph.Nodes.Where(n => n.Kind == "instruction").ToList();
            var accesses = graph.Nodes.Where(n => n.Kind == "access").ToList();
            Need(parts.Count > 0, "NO_INSTRUCTIONS", "unknown");
            Need(parts.All(n => n.Instruction == "Contact" || (n.Instruction == "Coil" && !n.Negated)), "INSTRUCTION_UNSUPPORTED");
            Need(accesses.All(n => n.Scope == "GlobalVariable"), "ACCESS_SCOPE_UNSUPPORTED");
            var tags = symbols.GroupBy(t => t.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            var bindings = new Dictionary<string, AiLadUse>(StringComparer.Ordinal);
            var ports = new HashSet<string>(StringComparer.Ordinal);
            foreach (var wire in graph.Wires)
            {
                var endpoints = wire.Endpoints;
                foreach (var endpoint in endpoints)
                {
                    if (endpoint.Kind != "NameCon") continue;
                    Need(endpoint.Port == "in" || endpoint.Port == "operand" ||
                        (endpoint.Port == "out" && nodes[endpoint.NodeId].Instruction == "Contact"), "PORT_UNSUPPORTED");
                    Need(ports.Add(endpoint.NodeId + ":" + endpoint.Port), "PORT_MULTIPLE_CONNECTIONS", "ambiguous");
                }
                if (endpoints.Any(e => e.Kind == "IdentCon" || e.Port == "operand"))
                {
                    Need(endpoints.Count == 2 && endpoints.Count(e => e.Kind == "IdentCon") == 1 &&
                        endpoints.Count(e => e.Kind == "NameCon" && e.Port == "operand") == 1, "OPERAND_WIRE_AMBIGUOUS", "ambiguous");
                    var access = nodes[endpoints.Single(e => e.Kind == "IdentCon").NodeId];
                    var part = nodes[endpoints.Single(e => e.Kind == "NameCon").NodeId];
                    Need(tags.ContainsKey(access.Symbol) && tags[access.Symbol].Count == 1 && tags[access.Symbol][0].DataType == "Bool", "OPERAND_NOT_UNIQUE_BOOL", "unknown");
                    var tag = tags[access.Symbol][0];
                    bindings.Add(part.Id, new AiLadUse { NodeId = part.Id, AccessId = access.Id,
                        OperandWireId = wire.Id, Symbol = access.Symbol, Address = tag.Address, Negated = part.Negated });
                }
                else
                {
                    var drivers = endpoints.Where(e => e.Kind == "Powerrail" || e.Port == "out").ToList();
                    var sinks = endpoints.Where(e => e.Kind == "NameCon" && e.Port == "in").ToList();
                    Need(drivers.Count == 1 && sinks.Count >= 1 && drivers.Count + sinks.Count == endpoints.Count,
                        "JOIN_OR_WIRE_DIRECTION_UNPROVEN", "ambiguous");
                    foreach (var sink in sinks) result.Flows.Add(new AiLadFlow { From = drivers[0].NodeId,
                        FromPort = drivers[0].Kind == "Powerrail" ? "power" : "out", To = sink.NodeId, ToPort = "in", WireId = wire.Id });
                }
            }
            Need(parts.All(n => bindings.ContainsKey(n.Id)), "OPERAND_UNBOUND", "unknown");
            Need(accesses.All(n => bindings.Values.Any(b => b.AccessId == n.Id)), "UNUSED_ACCESS", "unknown");
            Need(parts.All(n => result.Flows.Count(f => f.To == n.Id) == 1), "INPUT_UNCONNECTED_OR_AMBIGUOUS", "ambiguous");
            Need(parts.Where(n => n.Instruction == "Contact").All(n => result.Flows.Any(f => f.From == n.Id)), "CONTACT_OUTPUT_UNCONNECTED", "ambiguous");
            var coils = parts.Where(n => n.Instruction == "Coil").ToList();
            Need(coils.Count > 0, "NO_COIL", "unknown");
            Need(coils.Select(n => bindings[n.Id].Symbol).Distinct(StringComparer.Ordinal).Count() == coils.Count, "MULTIPLE_WRITES", "ambiguous");
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var coil in coils)
            {
                var path = new List<string> { coil.Id }; var seen = new HashSet<string>(StringComparer.Ordinal) { coil.Id };
                var cursor = coil.Id;
                while (true)
                {
                    var flow = result.Flows.Single(f => f.To == cursor);
                    Need(seen.Add(flow.From), "FLOW_CYCLE", "ambiguous"); path.Add(flow.From);
                    if (nodes[flow.From].Kind == "powerrail") break;
                    Need(nodes[flow.From].Instruction == "Contact", "UPSTREAM_INSTRUCTION_UNSUPPORTED");
                    cursor = flow.From;
                }
                path.Reverse(); foreach (var id in path) used.Add(id);
                var terms = path.Where(id => nodes[id].Instruction == "Contact").Select(id => Literal(bindings[id])).ToList();
                Need(terms.Count > 0, "UNCONDITIONAL_COIL_NOT_EVIDENCED");
                result.Writes.Add(new AiLadWrite { Target = bindings[coil.Id], Path = path,
                    Expression = terms.Count == 1 ? terms[0] : new AiLadExpression { Op = "and", Args = terms } });
            }
            Need(parts.All(n => used.Contains(n.Id)), "UNREACHABLE_INSTRUCTION", "ambiguous");
            result.Reads = parts.Where(n => n.Instruction == "Contact").Select(n => bindings[n.Id]).OrderBy(u => u.Symbol, StringComparer.Ordinal).ThenBy(u => u.NodeId, StringComparer.Ordinal).ToList();
            // Alias/feedback ordering within a multi-coil network has not been evidenced by this corpus.
            var uses = result.Reads.Concat(result.Writes.Select(w => w.Target)).ToList();
            foreach (var write in result.Writes)
                Need(!uses.Any(u => u.NodeId != write.Target.NodeId && Overlap(u.Address, write.Target.Address)), "INTRA_NETWORK_WRITE_ALIAS_OR_FEEDBACK_UNSUPPORTED");
            result.Writes = result.Writes.OrderBy(w => w.Target.Symbol, StringComparer.Ordinal).ToList();
            result.Flows = result.Flows.OrderBy(f => f.From, StringComparer.Ordinal).ThenBy(f => f.To, StringComparer.Ordinal).ToList();
            return result;
        }

        internal static bool Overlap(string left, string right)
        {
            var a = SnapshotAddressParser.Parse(left); var b = SnapshotAddressParser.Parse(right);
            if (a.ParseStatus != "parsed" || b.ParseStatus != "parsed") return true;
            var startA = a.ByteOffset.Value * 8L + (a.BitOffset ?? 0); var startB = b.ByteOffset.Value * 8L + (b.BitOffset ?? 0);
            return a.Area == b.Area && startA < startB + b.BitWidth && startB < startA + a.BitWidth;
        }

        private static AiLadExpression Literal(AiLadUse use)
        {
            var read = new AiLadExpression { Op = "read", Symbol = use.Symbol };
            return use.Negated ? new AiLadExpression { Op = "not", Args = new List<AiLadExpression> { read } } : read;
        }

        // IDs use instruction/operand/topology signatures, never UID as the primary identity.
        // Structurally indistinguishable duplicates get a deterministic occurrence suffix.
        private static void AssignIdentities(AiLadGraph graph)
        {
            var basis = graph.Nodes.ToDictionary(n => n.Id, n => Hash(n.Kind + "|" + n.Instruction + "|" + n.Scope + "|" + n.Symbol + "|" + n.Negated), StringComparer.Ordinal);
            var signatures = basis;
            for (var round = 0; round < 2; round++)
            {
                var previous = signatures;
                signatures = graph.Nodes.ToDictionary(n => n.Id, n => Hash(basis[n.Id] + "|" + string.Join("|", graph.Wires
                    .Where(w => w.Endpoints.Any(e => e.NodeId == n.Id))
                    .Select(w => string.Join(",", w.Endpoints.Select(e => e.Kind + ":" + e.Port + ":" + previous[e.NodeId]).OrderBy(s => s, StringComparer.Ordinal)))
                    .OrderBy(s => s, StringComparer.Ordinal))), StringComparer.Ordinal);
            }
            var mapping = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var group in graph.Nodes.GroupBy(n => signatures[n.Id]).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                int occurrence = 0;
                foreach (var node in group.OrderBy(n => n.SourceUid, StringComparer.Ordinal))
                {
                    var id = "n-" + group.Key + "-" + (++occurrence).ToString(CultureInfo.InvariantCulture);
                    mapping.Add(node.Id, id); node.Id = id;
                }
            }
            foreach (var wire in graph.Wires)
            {
                foreach (var endpoint in wire.Endpoints) endpoint.NodeId = mapping[endpoint.NodeId];
                wire.Endpoints = wire.Endpoints.OrderBy(e => e.NodeId, StringComparer.Ordinal).ThenBy(e => e.Port, StringComparer.Ordinal).ThenBy(e => e.Kind, StringComparer.Ordinal).ToList();
                wire.Id = "w-" + Hash(string.Join("|", wire.Endpoints.Select(e => e.Kind + ":" + e.NodeId + ":" + e.Port)));
            }
            foreach (var group in graph.Wires.GroupBy(w => w.Id).ToList())
            {
                int occurrence = 0;
                foreach (var wire in group.OrderBy(w => w.SourceUid, StringComparer.Ordinal)) wire.Id += "-" + (++occurrence).ToString(CultureInfo.InvariantCulture);
            }
            graph.Nodes = graph.Nodes.OrderBy(n => n.Id, StringComparer.Ordinal).ToList();
            graph.Wires = graph.Wires.OrderBy(w => w.Id, StringComparer.Ordinal).ToList();
        }

        private static string Hash(string text)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").Substring(0, 16).ToLowerInvariant();
        }
        private static List<XmlElement> Children(XmlElement element) => element.ChildNodes.OfType<XmlElement>().ToList();
        private static XmlElement One(XmlElement parent, string name)
        {
            var children = Children(parent).Where(e => e.Name == name && e.NamespaceURI == FlgNetNamespace).ToList();
            Need(children.Count == 1, "REQUIRED_ELEMENT_MISSING_OR_DUPLICATE"); return children[0];
        }
        private static string Uid(XmlElement element, ISet<string> seen)
        {
            var uid = element.GetAttribute("UId");
            Need(Regex.IsMatch(uid, "^[0-9]{1,10}$") && seen.Add(uid), "UID_INVALID_OR_DUPLICATE", "ambiguous"); return uid;
        }
        private static void Shape(XmlElement element, params string[] attributes)
        {
            var actual = element.Attributes.OfType<XmlAttribute>().Where(a => a.NamespaceURI != "http://www.w3.org/2000/xmlns/").ToList();
            Need(actual.Count == attributes.Length && actual.All(a => a.NamespaceURI == "" && attributes.Contains(a.Name)), "UNRECOGNIZED_ATTRIBUTES");
            Need(element.ChildNodes.Cast<XmlNode>().All(n => n is XmlElement || n.NodeType == XmlNodeType.Whitespace || n.NodeType == XmlNodeType.SignificantWhitespace ||
                n.NodeType == XmlNodeType.Comment), "UNRECOGNIZED_TEXT_OR_NODE");
        }
        private static void Leaf(XmlElement element) { Need(Children(element).Count == 0, "UNEXPECTED_CONNECTOR_OR_OPERAND_CONTENT"); }
        private static void Need(bool condition, string code, string status = "unsupported") { if (!condition) throw new Rejected(code, status); }
        internal static AiLadDiagnostic Diagnostic(string code, string reference) => new AiLadDiagnostic { Code = code, SourceRef = reference };
        private sealed class Rejected : Exception
        {
            public string Code { get; } public string Status { get; }
            public Rejected(string code, string status = "unsupported") { Code = code; Status = status; }
        }
    }
}
