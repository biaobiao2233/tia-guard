using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
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
            Assert.False(plan.SavesOriginalProject);
            Assert.True(plan.SavesDisposableCopy);
            Assert.False(plan.Publishes);
            Assert.True(plan.MutatesDisposableOfflineCopy);
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

        [Fact]
        public void Existing_tag_address_update_is_accepted_and_declares_disposable_save()
        {
            var plan = Plan(@"{
                ""schemaVersion"": ""tia-guard.engineering-patch/v1"",
                ""operation"": ""upsert_tag"",
                ""target"": { ""table"": ""默认变量表"", ""tag"": ""StartForward"" },
                ""dataType"": ""Bool"",
                ""logicalAddress"": ""%I0.2""
            }");
            Assert.True(plan.CurrentTag.Exists);
            Assert.Equal("StartForward", plan.CurrentTag.Name);
            Assert.Equal("Bool", plan.CurrentTag.DataType);
            Assert.Equal("%I0.0", plan.CurrentTag.LogicalAddress);
            Assert.Equal("Forward start command", plan.CurrentTag.Comment);
            Assert.Equal("%I0.2", plan.ExpectedTag.LogicalAddress);
            Assert.Equal("Forward start command", plan.ExpectedTag.Comment);
            Assert.False(plan.SavesOriginalProject);
            Assert.True(plan.SavesDisposableCopy);
            Assert.False(plan.Publishes);
            Assert.False(plan.SavesProject);
        }

        [Fact]
        public void New_tag_preview_records_absence_for_rollback()
        {
            var plan = Plan(@"{
                ""schemaVersion"": ""tia-guard.engineering-patch/v1"",
                ""operation"": ""upsert_tag"",
                ""target"": { ""table"": ""默认变量表"", ""tag"": ""SafetyReady"" },
                ""dataType"": ""Bool"",
                ""logicalAddress"": ""%M0.2""
            }");
            Assert.False(plan.CurrentTag.Exists);
            Assert.True(plan.ExpectedTag.Exists);
            Assert.Equal("SafetyReady", plan.ExpectedTag.Name);
        }

        [Fact]
        public void Tag_address_change_keeps_expression_and_topology()
        {
            var before = AiEngineeringRenderer.Read(_source);
            var after = Clone(before);
            RewriteAddress(after, "StartForward", "%I0.2");
            AiEngineeringPatch.AssertTagUpdatePreservesLogic(before, after, "StartForward", "%I0.2");
            after.Blocks[0].Networks[0].Analysis.Writes[0].Expression.Op = "or";
            var error = Assert.Throws<InvalidOperationException>(() =>
                AiEngineeringPatch.AssertTagUpdatePreservesLogic(before, after, "StartForward", "%I0.2"));
            Assert.Contains("LOGIC_SEMANTICS_CHANGED", error.Message);
        }

        [Fact]
        public void Unrelated_symbol_address_change_is_rejected()
        {
            var before = AiEngineeringRenderer.Read(_source);
            var after = Clone(before);
            RewriteAddress(after, "ReverseOut", "%Q0.3");
            var error = Assert.Throws<InvalidOperationException>(() =>
                AiEngineeringPatch.AssertTagUpdatePreservesLogic(before, after, "StartForward", "%I0.0"));
            Assert.Contains("TAG_SET_CHANGED", error.Message);
        }

        [Fact]
        public void New_tag_keeps_existing_tags_and_logic()
        {
            var before = AiEngineeringRenderer.Read(_source);
            var after = Clone(before);
            after.TagTables[0].Tags.Add(new AiTag
            {
                Name = "SafetyReady",
                DataType = "Bool",
                Address = "%M0.2",
                Comment = string.Empty
            });
            AiEngineeringPatch.AssertTagUpdatePreservesLogic(before, after, "SafetyReady", "%M0.2");
            after.Blocks[0].Networks[0].Analysis.Writes[0].Expression.Op = "or";
            var error = Assert.Throws<InvalidOperationException>(() =>
                AiEngineeringPatch.AssertTagUpdatePreservesLogic(before, after, "SafetyReady", "%M0.2"));
            Assert.Contains("LOGIC_SEMANTICS_CHANGED", error.Message);
        }

        [Fact]
        public void Applied_requires_compile_export_roundtrip_and_semantics()
        {
            var applied = Verdict("upsert_tag", 0, "pass", "pass", "pass", false, true, null);
            Assert.Equal("applied", applied.Status);
            Assert.Equal("pass", applied.VerifyVerdict);
            Assert.Equal("pass", applied.CompileVerdict);
            Assert.Equal("pass", applied.ExportDeterminismVerdict);
            Assert.Equal("pass", applied.RoundTripVerifyVerdict);
            Assert.Equal("pass", applied.AiSemanticVerificationVerdict);
            Assert.True(applied.SavedDisposableCopy);
            Assert.False(applied.SavedOriginalProject);

            Assert.Equal("verification_failure", Verdict("upsert_tag", 2, "not_run", "not_run", "not_run", true, true, null).Status);
            Assert.Equal("not_run", Verdict("upsert_tag", -1, "not_run", "not_run", "fail", true, true, null).CompileVerdict);
            Assert.Equal("fail", Verdict("replace_output_condition", 0, "fail", "not_run", "not_run", true, true, null).ExportDeterminismVerdict);
            Assert.Equal("fail", Verdict("replace_output_condition", 0, "pass", "fail", "not_run", true, true, null).VerifyVerdict);
            Assert.Equal("fail", Verdict("replace_output_condition", 0, "pass", "blocked", "not_run", true, true, null).VerifyVerdict);
            var semantic = Verdict("replace_output_condition", 0, "pass", "pass", "fail", true, true, null);
            Assert.Equal("verification_failure", semantic.Status);
            Assert.Equal("pass", semantic.VerifyVerdict);
            Assert.Equal("fail", semantic.AiSemanticVerificationVerdict);
            var broken = Verdict("upsert_tag", 0, "pass", "pass", "fail", true, false, "AI_PATCH_REJECTED: ROLLBACK_FAILED: disk");
            Assert.Equal("rollback_failed", broken.Status);
            Assert.Contains("ROLLBACK_FAILED", broken.Reason);
            Assert.False(broken.SavedDisposableCopy);
        }

        private static AiPatchApplyResult Verdict(
            string operation, int errors, string exportVerdict, string roundTripVerdict, string semanticVerdict,
            bool rollbackAttempted, bool rollbackSucceeded, string rollbackError)
        {
            return AiEngineeringPatch.CreateApplyResult(
                operation, errors, 0, exportVerdict, roundTripVerdict, semanticVerdict, "rejected",
                rollbackAttempted, rollbackSucceeded, rollbackError, null, null, null, 1);
        }

        private static void RewriteAddress(AiEngineeringView view, string symbol, string address)
        {
            foreach (var tag in view.TagTables.SelectMany(table => table.Tags))
            {
                if (tag.Name == symbol) tag.Address = address;
            }
            foreach (var network in view.Blocks.SelectMany(block => block.Networks))
            {
                if (network.Analysis == null) continue;
                foreach (var read in network.Analysis.Reads)
                    if (read.Symbol == symbol) read.Address = address;
                foreach (var write in network.Analysis.Writes)
                    if (write.Target != null && write.Target.Symbol == symbol) write.Target.Address = address;
            }
        }

        private static AiEngineeringView Clone(AiEngineeringView view)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(AiEngineeringView)).WriteObject(stream, view);
                stream.Position = 0;
                return (AiEngineeringView)new DataContractJsonSerializer(typeof(AiEngineeringView)).ReadObject(stream);
            }
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
