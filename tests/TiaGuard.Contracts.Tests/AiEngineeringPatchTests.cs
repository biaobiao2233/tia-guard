using System;
using System.IO;
using System.Linq;
using TiaGuard.Openness;
using Xunit;

namespace TiaGuard.Contracts.Tests
{
    public sealed class AiEngineeringPatchTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "TiaGuard.PatchTests", Guid.NewGuid().ToString("N"));
        private readonly string _source;

        public AiEngineeringPatchTests()
        {
            _source = Path.Combine(_root, "source");
            FileSystemSafety.CopyPlainTree(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", "lad-v21-source"),
                _source);
        }

        [Fact]
        public void Serial_and_patch_round_trips_through_the_existing_renderer()
        {
            var beforeXml = File.ReadAllText(BlockXml());
            var plan = Plan(LogicPatch(ExtraRead("StartReverse")));
            var write = plan.ExpectedNetwork.Analysis.Writes.Single();
            Assert.Equal("accepted", plan.Validation.Status);
            Assert.Equal("ForwardOut", write.Target.Symbol);
            Assert.Equal("StartForward AND NOT ReverseOut AND StartReverse",
                AiEngineeringContextQuery.FormatExpression(write.Expression));
            Assert.NotNull(plan.CurrentNetwork.Graph);
            Assert.NotNull(plan.ExpectedNetwork.Graph);
            Assert.NotNull(plan.ExpectedNetwork.Analysis);
            Assert.Equal(beforeXml, File.ReadAllText(BlockXml()));
            Assert.False(plan.SavesProject);
            Assert.False(plan.Publishes);
        }

        [Fact]
        public void Parallel_network_is_not_rewritten()
        {
            var error = Assert.Throws<InvalidOperationException>(() => Plan(@"{
                ""schemaVersion"": ""tia-guard.engineering-patch/v1"",
                ""operation"": ""replace_output_condition"",
                ""target"": { ""block"": ""OB1"", ""network"": 3 },
                ""output"": ""BranchA"",
                ""expression"": { ""op"": ""read"", ""symbol"": ""StartForward"" }
            }"));
            Assert.Contains("PARALLEL_TOPOLOGY_NOT_REVERSE_MAPPED", error.Message);
        }

        [Fact]
        public void Unknown_symbol_operator_block_and_network_are_rejected()
        {
            Assert.Contains("UNKNOWN_SYMBOL", Reject(LogicPatch(ExtraRead("SafetyReady"))));
            Assert.Contains("OPERATOR_UNSUPPORTED", Reject(LogicPatch(@"{ ""op"": ""or"", ""args"": [ { ""op"": ""read"", ""symbol"": ""StartForward"" } ] }")));
            Assert.Contains("UNKNOWN_BLOCK", Reject(LogicPatch(ExtraRead("StartReverse"), "OB2", 1)));
            Assert.Contains("UNKNOWN_NETWORK", Reject(LogicPatch(ExtraRead("StartReverse"), "OB1", 8)));
        }

        [Fact]
        public void Tag_patch_does_not_rewrite_logic_and_rejects_a_bad_address()
        {
            var plan = Plan(@"{
                ""schemaVersion"": ""tia-guard.engineering-patch/v1"",
                ""operation"": ""upsert_tag"",
                ""target"": { ""table"": ""默认变量表"", ""tag"": ""SafetyReady"" },
                ""dataType"": ""Bool"",
                ""logicalAddress"": ""%M0.2""
            }");
            Assert.False(plan.CurrentTag.Exists);
            Assert.Equal("SafetyReady", plan.ExpectedTag.Name);
            Assert.Equal("%M0.2", plan.ExpectedTag.LogicalAddress);
            Assert.Contains("UNKNOWN_TAG_TABLE", Reject(@"{
                ""schemaVersion"": ""tia-guard.engineering-patch/v1"",
                ""operation"": ""upsert_tag"",
                ""target"": { ""table"": ""Missing"", ""tag"": ""SafetyReady"" },
                ""dataType"": ""Bool"",
                ""logicalAddress"": ""%M0.2""
            }"));
        }

        private AiPatchPlan Plan(string patch)
        {
            var preview = Path.Combine(_root, "preview-" + Guid.NewGuid().ToString("N"));
            try
            {
                return AiEngineeringPatch.Plan(_source, patch, preview);
            }
            finally
            {
                AiEngineeringPatch.DeleteOwnedDirectory(preview);
            }
        }

        private string Reject(string patch)
        {
            var error = Assert.Throws<InvalidOperationException>(() => Plan(patch));
            return error.Message;
        }

        private string BlockXml()
        {
            return Directory.GetFiles(_source, "source.xml", SearchOption.AllDirectories).Single();
        }

        private static string ExtraRead(string symbol)
        {
            return "{ \"op\": \"read\", \"symbol\": \"" + symbol + "\" }";
        }

        private static string LogicPatch(string extraTerm, string block = "OB1", int network = 1)
        {
            return "{" +
                "\"schemaVersion\":\"tia-guard.engineering-patch/v1\"," +
                "\"operation\":\"replace_output_condition\"," +
                "\"target\":{\"block\":\"" + block + "\",\"network\":" + network + "}," +
                "\"output\":\"ForwardOut\"," +
                "\"expression\":{\"op\":\"and\",\"args\":[" +
                    "{\"op\":\"read\",\"symbol\":\"StartForward\"}," +
                    "{\"op\":\"not\",\"args\":[{\"op\":\"read\",\"symbol\":\"ReverseOut\"}]}," +
                    extraTerm +
                "]}}";
        }

        public void Dispose()
        {
            AiEngineeringPatch.DeleteOwnedDirectory(_root);
        }
    }
}
