using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using TiaGuard.Openness;
using Xunit;

namespace TiaGuard.Contracts.Tests
{
    public sealed class LadGraphTests
    {
        [Fact]
        public void RealReexportHasExactSerialTopologyOperandsNegationAndAssignments()
        {
            using (var f = new LadFixture())
            {
                var block = f.View().Blocks.Single();
                Assert.Equal(3, block.Networks.Count);
                var n = block.Networks[0];
                Assert.Equal("complete", n.Graph.Status); Assert.Equal("supported", n.Analysis.Status);
                Assert.Equal(7, n.Graph.Nodes.Count); Assert.Equal(6, n.Graph.Wires.Count);
                var contacts = n.Graph.Nodes.Where(x => x.Instruction == "Contact").ToList();
                Assert.Equal(2, contacts.Count); Assert.Single(contacts, c => c.Negated);
                Assert.Single(n.Graph.Nodes, x => x.Instruction == "Coil");
                Assert.Single(n.Graph.Nodes, x => x.Kind == "powerrail");
                Assert.Equal(new[] { "ReverseOut", "StartForward" }, n.Analysis.Reads.Select(x => x.Symbol));
                var write = Assert.Single(n.Analysis.Writes);
                Assert.Equal("ForwardOut", write.Target.Symbol); Assert.Equal("%Q0.0", write.Target.Address);
                Assert.Equal("and", write.Expression.Op); Assert.Equal(2, write.Expression.Args.Count);
                Assert.Equal("StartForward", write.Expression.Args[0].Symbol);
                Assert.Equal("not", write.Expression.Args[1].Op);
                Assert.Equal("ReverseOut", Assert.Single(write.Expression.Args[1].Args).Symbol);
                Assert.Equal(4, write.Path.Count);
                var source = n.Graph.Nodes.ToDictionary(x => x.Id);
                Assert.Equal(new[] { "powerrail", "instruction", "instruction", "instruction" }, write.Path.Select(id => source[id].Kind));
                Assert.Equal(new[] { "Contact", "Contact", "Coil" }, write.Path.Skip(1).Select(id => source[id].Instruction));
                foreach (var read in n.Analysis.Reads.Append(write.Target))
                {
                    var wire = n.Graph.Wires.Single(w => w.Id == read.OperandWireId);
                    Assert.Contains(wire.Endpoints, e => e.Kind == "IdentCon" && e.NodeId == read.AccessId);
                    Assert.Contains(wire.Endpoints, e => e.Kind == "NameCon" && e.NodeId == read.NodeId && e.Port == "operand");
                    Assert.Equal(read.Symbol, source[read.AccessId].Symbol);
                }
                foreach (var node in n.Graph.Nodes) Assert.NotEmpty(f.At(LadFixture.Network1).SelectNodes(node.SourceSelector).Cast<XmlNode>());
            }
        }

        [Fact]
        public void RealExpressionsMatchTruthTablesAndSeparateParallelOutputs()
        {
            using (var f = new LadFixture())
            {
                var networks = f.View().Blocks[0].Networks;
                for (var mask = 0; mask < 16; mask++)
                {
                    var values = new Dictionary<string, bool> { ["StartForward"] = (mask & 1) != 0, ["StartReverse"] = (mask & 2) != 0,
                        ["ForwardOut"] = (mask & 4) != 0, ["ReverseOut"] = (mask & 8) != 0 };
                    Assert.Equal(values["StartForward"] && !values["ReverseOut"], Evaluate(networks[0].Analysis.Writes[0].Expression, values));
                    Assert.Equal(values["StartReverse"] && !values["ForwardOut"], Evaluate(networks[1].Analysis.Writes[0].Expression, values));
                    Assert.Equal(values["StartForward"], Evaluate(networks[2].Analysis.Writes.Single(w => w.Target.Symbol == "BranchA").Expression, values));
                    Assert.Equal(values["StartReverse"], Evaluate(networks[2].Analysis.Writes.Single(w => w.Target.Symbol == "BranchB").Expression, values));
                }
                var branch = networks[2];
                var rail = branch.Graph.Nodes.Single(x => x.Kind == "powerrail");
                var fork = Assert.Single(branch.Graph.Wires, w => w.Endpoints.Any(e => e.NodeId == rail.Id));
                Assert.Equal(3, fork.Endpoints.Count);
                Assert.Equal(2, fork.Endpoints.Count(e => e.Kind == "NameCon" && e.Port == "in"));
                Assert.All(branch.Analysis.Writes, w => Assert.Equal("read", w.Expression.Op));
                Assert.Single(branch.Analysis.Writes[0].Path.Intersect(branch.Analysis.Writes[1].Path));
            }
        }

        [Fact]
        public void MutualInhibitRequiresBothExactSupportedAssignmentsAndTracesReadWriteNodes()
        {
            using (var f = new LadFixture())
            {
                var block = f.View().Blocks[0];
                Assert.Equal("complete", block.Relationships.Status);
                var relation = Assert.Single(block.Relationships.Interlocks);
                Assert.Equal("mutual-output-inhibit", relation.Kind);
                Assert.Equal(new[] { "ForwardOut", "ReverseOut" }, relation.Members);
                Assert.Equal(2, relation.Evidence.Count);
                foreach (var e in relation.Evidence)
                {
                    var network = block.Networks.Single(n => n.Id == e.NetworkId);
                    var write = network.Analysis.Writes.Single(w => w.Target.NodeId == e.WriteNodeId);
                    var read = network.Analysis.Reads.Single(r => r.NodeId == e.ReadNodeId);
                    Assert.True(read.Negated); Assert.Contains(read.NodeId, write.Path);
                    Assert.NotEqual(write.Target.Symbol, read.Symbol); Assert.Contains(read.Symbol, relation.Members);
                }
                f.At(LadFixture.Network2 + "//f:Negated").ParentNode.RemoveChild(f.At(LadFixture.Network2 + "//f:Negated"));
                Assert.Empty(f.View().Blocks[0].Relationships.Interlocks);
            }
        }

        [Fact]
        public void ReorderingXmlAndRenumberingUidsCannotChangeTopologyOrSemantics()
        {
            using (var f = new LadFixture())
            {
                var before = f.View();
                foreach (var container in f.Document.SelectNodes("//f:Parts|//f:Wires|//f:Wire", f.Ns).OfType<XmlElement>().ToList())
                    foreach (var child in container.ChildNodes.OfType<XmlElement>().Reverse().ToList()) container.AppendChild(child);
                foreach (var attr in f.Document.SelectNodes("//@UId").OfType<XmlAttribute>()) attr.Value = (int.Parse(attr.Value) + 1000).ToString(System.Globalization.CultureInfo.InvariantCulture);
                var after = f.View();
                for (var i = 0; i < 3; i++)
                {
                    Assert.Equal(before.Blocks[0].Networks[i].Graph.Nodes.Select(n => n.Id), after.Blocks[0].Networks[i].Graph.Nodes.Select(n => n.Id));
                    Assert.Equal(before.Blocks[0].Networks[i].Graph.Wires.Select(w => w.Id), after.Blocks[0].Networks[i].Graph.Wires.Select(w => w.Id));
                    Assert.Equal(RoundTripJson.Serialize(before.Blocks[0].Networks[i].Analysis), RoundTripJson.Serialize(after.Blocks[0].Networks[i].Analysis));
                }
            }
        }

        [Theory]
        [InlineData("unknown-part")]
        [InlineData("set-coil")]
        [InlineData("negated-coil")]
        [InlineData("dangling")]
        [InlineData("duplicate-uid")]
        [InlineData("unknown-connector")]
        [InlineData("unknown-port")]
        [InlineData("unknown-scope")]
        [InlineData("unknown-symbol")]
        [InlineData("missing-operand-wire")]
        [InlineData("extra-access")]
        [InlineData("join")]
        [InlineData("disconnected")]
        [InlineData("duplicate-wire")]
        [InlineData("cycle")]
        [InlineData("extension-attribute")]
        [InlineData("operand-child")]
        [InlineData("blob")]
        [InlineData("namespace")]
        public void UnsupportedOrAmbiguousNetworkNeverLeaksACompleteExpression(string mutation)
        {
            using (var f = new LadFixture())
            {
                switch (mutation)
                {
                    case "unknown-part": f.Part(24).SetAttribute("Name", "TON"); break;
                    case "set-coil": f.Part(26).SetAttribute("Name", "SCoil"); break;
                    case "negated-coil": f.Part(26).AppendChild(f.At(LadFixture.Network1 + "//f:Negated").CloneNode(true)); break;
                    case "dangling": f.Wire(27).ChildNodes.OfType<XmlElement>().Last().SetAttribute("UId", "9999"); break;
                    case "duplicate-uid": f.Part(26).SetAttribute("UId", "24"); break;
                    case "unknown-connector": f.Wire(27).AppendChild(f.Document.CreateElement("OpenCon", AiEngineeringLadParser.FlgNetNamespace)); break;
                    case "unknown-port": f.At(LadFixture.Network1 + "//f:NameCon[@UId='24'][@Name='out']").SetAttribute("Name", "unproven"); break;
                    case "unknown-scope": f.Access(21).SetAttribute("Scope", "LocalVariable"); break;
                    case "unknown-symbol": f.At(LadFixture.Network1 + "//f:Access[@UId='21']//f:Component").SetAttribute("Name", "NotDeclared"); break;
                    case "missing-operand-wire": f.Wire(28).ParentNode.RemoveChild(f.Wire(28)); break;
                    case "extra-access": var a = (XmlElement)f.Access(21).CloneNode(true); a.SetAttribute("UId", "500"); f.Access(21).ParentNode.AppendChild(a); break;
                    case "join": var e = f.Document.CreateElement("NameCon", AiEngineeringLadParser.FlgNetNamespace); e.SetAttribute("UId", "24"); e.SetAttribute("Name", "out"); f.Wire(31).AppendChild(e); break;
                    case "disconnected": f.Wire(27).ParentNode.RemoveChild(f.Wire(27)); break;
                    case "duplicate-wire": var w = (XmlElement)f.Wire(28).CloneNode(true); w.SetAttribute("UId", "500"); f.Wire(28).ParentNode.AppendChild(w); break;
                    case "cycle": var rail = f.At(LadFixture.Network1 + "//f:Powerrail"); var c = f.Document.CreateElement("NameCon", AiEngineeringLadParser.FlgNetNamespace); c.SetAttribute("UId", "25"); c.SetAttribute("Name", "out"); rail.ParentNode.ReplaceChild(c, rail); break;
                    case "extension-attribute": f.Part(24).SetAttribute("VendorBehavior", "invert"); break;
                    case "operand-child": f.At(LadFixture.Network1 + "//f:Access[@UId='21']//f:Component").AppendChild(f.Document.CreateElement("Unknown", AiEngineeringLadParser.FlgNetNamespace)); break;
                    case "blob": f.Part(24).InnerText = new string('A', 2000); break;
                    case "namespace": f.At(LadFixture.Network1 + "/AttributeList/NetworkSource").InnerXml = "<FlgNet xmlns='urn:other'><Parts/><Wires/></FlgNet>"; break;
                }
                var block = f.View().Blocks[0]; var network = block.Networks[0];
                Assert.NotEqual("supported", network.Analysis.Status);
                Assert.Empty(network.Analysis.Writes); Assert.Empty(network.Analysis.Reads); Assert.Empty(network.Analysis.Flows);
                Assert.NotEmpty(network.Analysis.Diagnostics);
                Assert.Equal("unavailable", block.Relationships.Status); Assert.Empty(block.Relationships.Interlocks);
            }
        }

        [Theory]
        [InlineData("partial-third")]
        [InlineData("duplicate-writer")]
        [InlineData("alias")]
        [InlineData("wide-alias")]
        [InlineData("hidden-unit-attribute")]
        [InlineData("missing-composition")]
        public void WholeBlockCompletenessAndUniqueAddressWritersGateInterlocks(string mutation)
        {
            using (var f = new LadFixture())
            {
                if (mutation == "partial-third") f.At(LadFixture.Network3 + "//f:Part[@UId='25']").SetAttribute("Name", "TON");
                if (mutation == "duplicate-writer") f.At(LadFixture.Network3 + "//f:Access[@UId='23']//f:Component").SetAttribute("Name", "ForwardOut");
                if (mutation == "hidden-unit-attribute") f.At(LadFixture.Network3).SetAttribute("UnknownBehavior", "yes");
                if (mutation == "missing-composition") f.At(LadFixture.Network3).RemoveAttribute("CompositionName");
                if (mutation == "alias" || mutation == "wide-alias")
                {
                    var table = f.Input.TagTables.Single(); var tag = table.Tags.Single(t => t.Name == "BranchA");
                    tag.Address = mutation == "alias" ? "%Q0.0" : "%QW0";
                    if (mutation == "wide-alias") tag.DataType = "Word";
                    File.WriteAllText(Path.Combine(f.Root, f.Input.Plc.TagTables[0]), RoundTripJson.Serialize(table), new UTF8Encoding(false));
                }
                var block = f.View().Blocks[0];
                Assert.Empty(block.Relationships.Interlocks); Assert.Equal("unavailable", block.Relationships.Status);
            }
        }

        [Theory]
        [InlineData("O")]
        [InlineData("RCoil")]
        [InlineData("PContact")]
        [InlineData("NContact")]
        [InlineData("CTU")]
        [InlineData("TON")]
        [InlineData("Move")]
        public void ExternalInstructionFamiliesRemainUnsupported(string instruction)
        {
            using (var f = new LadFixture())
            {
                // Independently constructed mutations, not copied third-party program source.
                f.Part(24).SetAttribute("Name", instruction);
                var block = f.View().Blocks.Single();
                Assert.Equal("complete", block.Networks[0].Graph.Status);
                Assert.Equal("unsupported", block.Networks[0].Analysis.Status);
                Assert.Empty(block.Networks[0].Analysis.Writes);
                Assert.Empty(block.Relationships.Interlocks);
            }
        }

        [Fact]
        public void NonemptyGraphRegenerationAndSiblingIsolationPreserveCanonicalBuildAndVerify()
        {
            using (var f = new LadFixture())
            {
                var ai = AiEngineeringPublisher.Generate(f.Root);
                var first = File.ReadAllBytes(Path.Combine(ai, "project.json"));
                AiEngineeringPublisher.Generate(f.Root); Assert.Equal(first, File.ReadAllBytes(Path.Combine(ai, "project.json")));
                FileSystemSafety.DeleteOwnedTree(ai);
                Assert.NotNull(RoundTripBuildInput.Load(f.Root, Path.Combine(f.OwnedRoot, "build")));
                AiEngineeringPublisher.Generate(f.Root); Assert.Equal(first, File.ReadAllBytes(Path.Combine(ai, "project.json")));
                var sibling = Path.Combine(f.OwnedRoot, "sibling"); Directory.CreateDirectory(sibling);
                FileSystemSafety.CopyPlainTree(f.Root, Path.Combine(sibling, "tia-source"));
                var siblingAi = AiEngineeringPublisher.Generate(Path.Combine(sibling, "tia-source"));
                File.WriteAllText(Path.Combine(ai, "project.json"), "corrupt");
                Assert.Equal("pass", RoundTripVerifier.CompareSources(f.Root, Path.Combine(sibling, "tia-source")).Verdict);
                f.Part(24).SetAttribute("Name", "Unknown"); f.Save(); AiEngineeringPublisher.Generate(f.Root);
                Assert.Equal(first, File.ReadAllBytes(Path.Combine(siblingAi, "project.json")));
            }
        }

        private static bool Evaluate(AiLadExpression expression, IDictionary<string, bool> variables)
        {
            switch (expression.Op)
            {
                case "read": return variables[expression.Symbol];
                case "not": return !Evaluate(Assert.Single(expression.Args), variables);
                case "and": return expression.Args.All(e => Evaluate(e, variables));
                default: throw new InvalidOperationException("Unknown expression in test oracle");
            }
        }
    }
}
