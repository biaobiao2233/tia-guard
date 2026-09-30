using System.IO;
using System.Linq;
using TiaGuard.Openness;
using Xunit;

namespace TiaGuard.Contracts.Tests
{
    public sealed class AiEngineeringContextQueryTests
    {
        [Fact]
        public void Project_context_keeps_coverage_and_omits_raw_xml()
        {
            using (var fixture = new LadFixture())
            {
                var view = AiEngineeringRenderer.Read(fixture.Root);
                var context = AiEngineeringContextQuery.ProjectContext(view);
                var json = RoundTripJson.Serialize(context);
                Assert.Equal("ai-engineering-v2", context.SchemaVersion);
                Assert.Equal("derived-not-build-input", context.Authority);
                Assert.Contains(context.Coverage, item => item.Area == "lad-semantics" && item.Status == "partial");
                Assert.Contains(context.Blocks.Single().Networks, item => item.Ordinal == 3 && item.AnalysisStatus == "supported");
                Assert.DoesNotContain("<FlgNet", json);
                AiEngineeringContextQuery.RejectBlobLeakage(json);
            }
        }

        [Fact]
        public void Network_keeps_evidence_and_derived_semantics_separate()
        {
            using (var fixture = new LadFixture())
            {
                var view = AiEngineeringRenderer.Read(fixture.Root);
                var response = AiEngineeringContextQuery.Network(view, "OB1", 1);
                var json = RoundTripJson.Serialize(response);
                Assert.Equal("OB1", response.BlockLabel);
                Assert.NotNull(response.Network.Graph);
                Assert.NotNull(response.Network.Analysis);
                Assert.Contains("\"graph\":", json);
                Assert.Contains("\"analysis\":", json);
                Assert.DoesNotContain("\"expression\"", Slice(json, "\"graph\":", "\"analysis\":"));
                Assert.Equal("ForwardOut = StartForward AND NOT ReverseOut",
                    Assignment(response.Network));
            }
        }

        [Fact]
        public void Where_used_distinguishes_write_and_negated_read()
        {
            using (var fixture = new LadFixture())
            {
                var view = AiEngineeringRenderer.Read(fixture.Root);
                var used = AiEngineeringContextQuery.WhereUsed(view, "ForwardOut");
                Assert.Contains(used.References, item => item.Kind == "write" && item.Network == 1 && item.Block == "OB1");
                Assert.Contains(used.References, item => item.Kind == "negated-read" && item.Network == 2 && item.Negated);
                Assert.DoesNotContain(used.References, item => item.Kind == "read" && item.Network == 2);
                Assert.All(used.References, item => Assert.False(string.IsNullOrWhiteSpace(item.SourceRef)));
            }
        }

        [Fact]
        public void Unknown_block_and_network_fail_closed()
        {
            using (var fixture = new LadFixture())
            {
                var view = AiEngineeringRenderer.Read(fixture.Root);
                var block = Assert.Throws<System.ArgumentException>(() =>
                    AiEngineeringContextQuery.ProgramGraph(view, "OB99"));
                var network = Assert.Throws<System.ArgumentException>(() =>
                    AiEngineeringContextQuery.Network(view, "OB1", 9));
                Assert.Contains("UNKNOWN_BLOCK", block.Message);
                Assert.Contains("UNKNOWN_NETWORK", network.Message);
            }
        }

        [Fact]
        public void Unsupported_analysis_is_not_searched_or_completed()
        {
            var view = new AiEngineeringView
            {
                SchemaVersion = "ai-engineering-v2",
                Authority = "derived-not-build-input",
                Blocks = {
                    new AiBlock {
                        Name = "Main", Kind = "OB", Number = 1, Language = "LAD",
                        Networks = {
                            new AiNetwork {
                                Ordinal = 1, Id = "OB1/network-1",
                                Logic = new AiCoverage { Area = "logic", Status = "unsupported", Reason = "shape unknown" },
                                Analysis = new AiLadAnalysis {
                                    Status = "unavailable",
                                    Diagnostics = { new AiLadDiagnostic { Code = "INSTRUCTION_UNSUPPORTED" } }
                                }
                            }
                        }
                    }
                }
            };
            var used = AiEngineeringContextQuery.WhereUsed(view, "ForwardOut");
            Assert.Empty(used.References);
            Assert.Equal("INSTRUCTION_UNSUPPORTED", used.Unsearched.Single().Diagnostics.Single().Code);
            var context = AiEngineeringContextQuery.ProjectContext(view);
            Assert.Equal("unsupported", context.Blocks.Single().Networks.Single().LogicStatus);
            Assert.Null(context.Blocks.Single().Networks.Single().DerivedExpression);
        }

        [Fact]
        public void Read_does_not_mutate_the_fixture_tree()
        {
            var source = Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "TestData", "lad-v21-source");
            var before = File.ReadAllBytes(Directory.GetFiles(source, "source.xml", SearchOption.AllDirectories).Single());
            var view = AiEngineeringRenderer.Read(source);
            Assert.NotNull(AiEngineeringContextQuery.Network(view, "OB1", 3).Network.Graph);
            var after = File.ReadAllBytes(Directory.GetFiles(source, "source.xml", SearchOption.AllDirectories).Single());
            Assert.Equal(before, after);
        }

        private static string Assignment(AiNetwork network)
        {
            var write = network.Analysis.Writes.Single();
            return write.Target.Symbol + " = " + AiEngineeringContextQuery.FormatExpression(write.Expression);
        }

        private static string Slice(string json, string startToken, string endToken)
        {
            var start = json.IndexOf(startToken, System.StringComparison.Ordinal);
            var end = json.IndexOf(endToken, start + startToken.Length, System.StringComparison.Ordinal);
            return json.Substring(start, end - start);
        }
    }
}
