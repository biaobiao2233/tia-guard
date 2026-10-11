#if PROJECT_GENERATION_SPEC_V1
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TiaGuard.Openness;
using Xunit;

namespace TiaGuard.ProjectGeneration.Engine.Tests
{
    public sealed class SpecBindingTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "tg45-spec-" + Guid.NewGuid().ToString("N"));
        private ProjectGenerationSpecInput Load()
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "main.scl"), "ORGANIZATION_BLOCK \"Main\"\nBEGIN\nEND_ORGANIZATION_BLOCK\n");
            File.WriteAllText(Path.Combine(root, "project-generation.json"),
                "{\"schemaVersion\":\"1.0\",\"tiaVersion\":\"V21\",\"projectName\":\"Demo\",\"plc\":{\"name\":\"PLC_1\",\"cpuTypeIdentifier\":\"OrderNumber:6ES7 212-1AE40-0XB0/V4.7\"},\"tagTables\":[],\"artifacts\":[{\"id\":\"ob\",\"kind\":\"OB\",\"name\":\"Main\",\"format\":\"scl\",\"sourcePath\":\"main.scl\",\"dependsOn\":[]}]}");
            return ProjectGenerationSpecLoader.Load(Path.Combine(root, "project-generation.json"));
        }
        private static Dictionary<string, ProjectGenerationObject> Identities() =>
            new Dictionary<string, ProjectGenerationObject> { ["ob"] = new ProjectGenerationObject
                { Kind = "OB", Name = "Main", Number = 1, Language = "SCL" } };
        [Fact]
        public void BindsExactARevisionAndSnapshotBytesWithoutRereadingLiveSource()
        {
            var input = Load(); var expected = input.GetSourceBytes("main.scl");
            var plan = ProjectGenerationBuilder.FromValidatedSpec(input, Identities(), "TEST-IDENTITIES");
            Assert.Equal(ProjectGenerationBuilder.BoundSpecRevision, plan.ContractRevision);
            File.WriteAllText(Path.Combine(root, "main.scl"), "changed after adapter");
            Assert.Equal(expected, plan.ValidatedSourceBytes["main.scl"]);
            var sourcePath = Path.Combine(root, "private-copy.scl");
            ProjectGenerationImporter.WriteValidatedBytes(plan.ValidatedSourceBytes["main.scl"], plan.Artifacts[0].Sha256, sourcePath);
            Assert.Equal(expected, File.ReadAllBytes(sourcePath));
        }
        [Fact]
        public void RejectsChangedInputBeforeAdapting()
        {
            var input = Load(); File.WriteAllText(Path.Combine(root, "main.scl"), "changed");
            Assert.Throws<InvalidDataException>(() => ProjectGenerationBuilder.FromValidatedSpec(input, Identities(), "TEST"));
        }
        [Theory]
        [InlineData("missing")]
        [InlineData("wrong-name")]
        [InlineData("no-number")]
        [InlineData("no-language")]
        [InlineData("no-revision")]
        public void MissingOrInconsistentSourceIdentitiesCannotBeGuessed(string scenario)
        {
            var input = Load(); var identities = Identities();
            if (scenario == "missing") identities.Clear();
            if (scenario == "wrong-name") identities["ob"].Name = "Other";
            if (scenario == "no-number") identities["ob"].Number = null;
            if (scenario == "no-language") identities["ob"].Language = null;
            Assert.Throws<ArgumentException>(() => ProjectGenerationBuilder.FromValidatedSpec(input, identities,
                scenario == "no-revision" ? null : "TEST"));
        }
        public void Dispose() { if (Directory.Exists(root)) FileSystemSafety.DeleteOwnedTree(root); }
    }
}
#endif
