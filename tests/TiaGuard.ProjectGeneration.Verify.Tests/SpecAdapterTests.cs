#if PROJECT_GENERATION_SPEC_V1
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using TiaGuard.Openness;
using Xunit;

namespace TiaGuard.ProjectGeneration.Verify.Tests
{
    public sealed class SpecAdapterTests
    {
        [Fact]
        public void Actual_A_loader_accepts_nonempty_scl_fixture_and_adapter_keeps_interface_unknown()
        {
            var root = Path.Combine(Path.GetTempPath(), "tia-guard-47-spec-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var fixtureRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData");
                var plan = new JavaScriptSerializer().Deserialize<FixtureTests.Plan>(File.ReadAllText(Path.Combine(fixtureRoot, "verification", "fixture-plan.json")));
                var scenario = plan.scenarios.Single(s => s.id == "scl-multiblock");
                var artifacts = new List<object>();
                foreach (var item in scenario.objects)
                {
                    File.Copy(Path.Combine(fixtureRoot, item.sourcePath.Replace('/', Path.DirectorySeparatorChar)), Path.Combine(root, item.name + ".scl"));
                    artifacts.Add(new { id = item.id.Replace('-', '_'), kind = item.kind, name = item.name, format = "scl",
                        sourcePath = item.name + ".scl", dependsOn = item.dependsOn.Select(d => d.Replace('-', '_')).ToArray() });
                }
                var specPath = Path.Combine(root, "project-generation.json");
                File.WriteAllText(specPath, new JavaScriptSerializer().Serialize(new
                {
                    schemaVersion = "1.0", tiaVersion = "V21", projectName = "GenerationSclCandidate",
                    plc = new { name = "PLC_1", cpuTypeIdentifier = "OrderNumber:6ES7 212-1AE40-0XB0/V4.7" },
                    tagTables = scenario.tagTables,
                    artifacts
                }), new UTF8Encoding(false));
                var input = ProjectGenerationSpecLoader.Load(specPath);
                var expected = ProjectGenerationVerificationSpecAdapter.FromSpec(input, "fixture-plan-v1 + A@" + ProjectGenerationVerificationSpecAdapter.ProviderCommit);
                Assert.Equal(5, expected.Objects.Count);
                Assert.Equal(5, expected.TagTables.Single().Tags.Count);
                Assert.Equal(5, expected.SourceSha256.Count); // A exposes program-source hashes; its unchanged guard also covers the descriptor
                Assert.All(expected.Objects, obj => { Assert.True(obj.ContentComplete); Assert.False(obj.InterfaceComplete); });
                Assert.Null(expected.Objects.Single(o => o.Name == "LatchInstance").InstanceOfName);
                var main = expected.Objects.Single(o => o.Name == "Main").Documents[".scl"];
                File.WriteAllText(Path.Combine(root, "Main.scl"), "mutated after capture");
                var after = ProjectGenerationVerificationSpecAdapter.FromSpec(input, "snapshot-only");
                Assert.Equal(main, after.Objects.Single(o => o.Name == "Main").Documents[".scl"]);
                Assert.Throws<InvalidDataException>(() => input.ValidateUnchanged());
                var badEvidence = new Dictionary<string, ProjectGenerationVerificationObject> { ["not_declared"] = new ProjectGenerationVerificationObject() };
                Assert.Throws<ArgumentException>(() => ProjectGenerationVerificationSpecAdapter.FromSpec(input, "bad", badEvidence));
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
#endif
