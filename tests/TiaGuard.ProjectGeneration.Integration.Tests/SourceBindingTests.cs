using System;
using System.IO;
using System.Linq;
using TiaGuard.Openness;
using Xunit;

namespace TiaGuard.ProjectGeneration.Integration.Tests
{
    public sealed class SourceBindingTests
    {
        [Fact]
        public void S71500PreviewReportsSelectedCpuAndBindsItToApprovalHash()
        {
            var root = Path.Combine(Path.GetTempPath(), "cpu-preview-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var source = Path.Combine(root, "source");
                ProjectGenerationInitializer.Initialize(source, ProjectGenerationCpuProfiles.S71500ProfileId);
                var first = ProjectGenerationWorkflow.Inspect(source);
                Assert.Equal(ProjectGenerationCpuProfiles.S71500CpuTypeIdentifier, first.CpuTypeIdentifier);
                Assert.Equal(ProjectGenerationCpuProfiles.S71500ProfileId, first.CpuProfileId);
                var manifest = Path.Combine(source, ProjectGenerationSpecLoader.DefaultSpecFileName);
                File.WriteAllText(manifest, File.ReadAllText(manifest).Replace(ProjectGenerationCpuProfiles.S71500CpuTypeIdentifier,
                    ProjectGenerationCpuProfiles.DefaultCpuTypeIdentifier));
                var changed = ProjectGenerationWorkflow.Inspect(source);
                Assert.Equal(ProjectGenerationCpuProfiles.DefaultProfileId, changed.CpuProfileId);
                Assert.NotEqual(first.InputHash, changed.InputHash);
            }
            finally { FileSystemSafety.DeleteOwnedTree(root); }
        }

        [Fact]
        public void InitCanBeInspectedWithoutSiemensAndApprovalIncludesSourceAndManifestBytes()
        {
            var root = Path.Combine(Path.GetTempPath(), "tg43-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var source = Path.Combine(root, "source"); ProjectGenerationInitializer.Initialize(source);
                var first = ProjectGenerationWorkflow.Inspect(source);
                Assert.Equal(1, first.ArtifactCount); Assert.Equal("NewProject", first.ProjectName);
                Assert.Equal(first.InputHash, ProjectGenerationWorkflow.Inspect(source).InputHash);
                File.AppendAllText(Path.Combine(source, "sources/Main.scl"), "// source changed\n");
                var changed = ProjectGenerationWorkflow.Inspect(source);
                Assert.NotEqual(first.InputHash, changed.InputHash);
                File.AppendAllText(Path.Combine(source, "project-generation.json"), "\n");
                Assert.NotEqual(changed.InputHash, ProjectGenerationWorkflow.Inspect(source).InputHash);
            }
            finally { FileSystemSafety.DeleteOwnedTree(root); }
        }
        [Fact]
        public void GroupedSourcePreservesIndependentObjectSlicesAndInterfaces()
        {
            var objects = ProjectGenerationSclProjection.Parse("TYPE \"State\"\nVERSION : 0.1\nSTRUCT\nReady : Bool;\nEND_STRUCT;\nEND_TYPE\nFUNCTION_BLOCK \"Controller\"\nVERSION : 0.1\nVAR_INPUT\nEnable : Bool;\nEND_VAR\nBEGIN\nEND_FUNCTION_BLOCK\nDATA_BLOCK \"Instance\"\nVERSION : 0.1\n\"Controller\"\nBEGIN\nEND_DATA_BLOCK");
            Assert.Equal(new[] { "UDT", "FB", "DB" }, objects.Select(o => o.Kind));
            Assert.Equal("Controller", objects[2].InstanceOfName);
            Assert.DoesNotContain("FUNCTION_BLOCK", objects[0].Text);
            Assert.DoesNotContain("DATA_BLOCK", objects[1].Text);
            Assert.Equal(ProjectGenerationSclProjection.XmlInterface("<Interface><Sections><Section Name=\"Input\"><Member Name=\"Enable\" Datatype=\"Bool\" /></Section><Section Name=\"Output\" /></Sections></Interface>"), objects[1].Interface);
        }
        [Fact]
        public void GlobalDbHasNoInstanceAndNestedStructMatchesObservedDeclarationShape()
        {
            var source = ProjectGenerationSclProjection.Parse("DATA_BLOCK \"Config\"\nVERSION : 0.1\nVAR\nState : STRUCT\nReady : Bool;\nEND_STRUCT;\nEND_VAR\nBEGIN\nEND_DATA_BLOCK")[0];
            Assert.Null(source.InstanceOfName);
            var xml = "<Interface><Sections><Section Name=\"Static\"><Member Name=\"State\" Datatype=\"Struct\"><Sections><Section Name=\"None\"><Member Name=\"Ready\" Datatype=\"Bool\" /></Section></Sections></Member></Section></Sections></Interface>";
            Assert.Equal(source.Interface, ProjectGenerationSclProjection.XmlInterface(xml));
            Assert.NotEqual(source.Interface, ProjectGenerationSclProjection.XmlInterface(xml.Replace("Bool", "Int")));
        }
        [Theory]
        [InlineData("VAR\na,b : Bool;\nEND_VAR")]
        [InlineData("VAR\na : Bool\nEND_VAR")]
        [InlineData("VAR\na : ;\nEND_VAR")]
        public void UnsupportedOrIncompleteInterfaceFailsClosed(string declaration)
        {
            Assert.ThrowsAny<Exception>(() => ProjectGenerationSclProjection.Parse("FUNCTION_BLOCK \"F\"\nVERSION : 0.1\n" + declaration + "\nBEGIN\nEND_FUNCTION_BLOCK"));
        }
        [Fact]
        public void FormattingAndNestedCommentsAreOnlyTransportDifferences()
        {
            Assert.Equal(ProjectGenerationSclProjection.Normalize("IF #run THEN #x := 'a // b'; END_IF;"),
                ProjectGenerationSclProjection.Normalize("if (* outer (* nested *) end *) #run then\n#x:='a // b';end_if; // tail"));
        }
        [Fact]
        public void V21QualificationIsLimitedToDeclaredLocalReferences()
        {
            const string head = "FUNCTION_BLOCK \"Controller\"\nVERSION : 0.1\nVAR_INPUT\nRun : Bool;\nEND_VAR\nVAR\nActive : Bool;\nEND_VAR\nBEGIN\n";
            var source = head + "Active := Run; END_FUNCTION_BLOCK";
            var exported = head + "#Active := #Run; END_FUNCTION_BLOCK";
            Assert.Equal(ProjectGenerationSclProjection.Normalize(source), ProjectGenerationSclProjection.Normalize(exported));
            Assert.NotEqual(ProjectGenerationSclProjection.Normalize(source), ProjectGenerationSclProjection.Normalize(head + "#Active := \"Run\"; END_FUNCTION_BLOCK"));
        }
        [Theory]
        [InlineData("IF #run THEN #x := 'a'; END_IF;", "IF NOT #run THEN #x := 'a'; END_IF;")]
        [InlineData("#x := 'a // b';", "#x := 'a';")]
        [InlineData("#x := 1;", "#x := 2;")]
        [InlineData("{ S7_Optimized_Access := 'TRUE' }", "{ S7_Optimized_Access := 'FALSE' }")]
        [InlineData("#x := A >= B;", "#x := A > B;")]
        public void SourceProjectionRetainsControlRelevantDifferences(string first, string second)
        { Assert.NotEqual(ProjectGenerationSclProjection.Normalize(first), ProjectGenerationSclProjection.Normalize(second)); }
        [Theory]
        [InlineData("(* missing end")]
        [InlineData("#x := 'missing end")]
        [InlineData("#x := \"missing end")]
        public void BrokenExportCannotBeNormalizedIntoPass(string text)
        { Assert.Throws<InvalidDataException>(() => ProjectGenerationSclProjection.Normalize(text)); }
    }
}
