using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using TiaGuard.Cli;
using TiaGuard.Openness;
using Xunit;

namespace TiaGuard.ProjectGeneration.Spec.Tests
{
    public sealed class SpecTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "tia-guard-44-" + Guid.NewGuid().ToString("N"));
        private string SpecPath => Path.Combine(root, "input", ProjectGenerationSpecLoader.DefaultSpecFileName);
        public SpecTests() { Directory.CreateDirectory(root); ProjectGenerationInitializer.Initialize(Path.Combine(root, "input")); }
        public void Dispose() { FileSystemSafety.DeleteOwnedTree(root); }
        private ProjectGenerationSpecInput Load() => ProjectGenerationSpecLoader.Load(SpecPath);
        private void Edit(Action<Dictionary<string, object>> edit)
        {
            var parser = new JavaScriptSerializer(); var json = (Dictionary<string, object>)parser.DeserializeObject(File.ReadAllText(SpecPath));
            edit(json); File.WriteAllText(SpecPath, parser.Serialize(json), new UTF8Encoding(false));
        }
        private static Dictionary<string, object> Artifact(Dictionary<string, object> json) => (Dictionary<string, object>)((object[])json["artifacts"])[0];
        private void Source(string text) => File.WriteAllText(Path.Combine(root, "input", "sources", "Main.scl"), text);
        [Fact] public void InitIsSelfContainedImmutableAndRepeatRefusesOverwrite()
        {
            var input = Load(); Assert.Equal("NewProject", input.Spec.ProjectName); Assert.Single(input.Spec.Artifacts);
            Assert.Equal("main", input.OrderedArtifacts[0].Id); Assert.Single(input.SourceSha256);
            Assert.Contains(ProjectGenerationInitializer.TemplateIdentity, Encoding.UTF8.GetString(input.GetSourceBytes("sources/Main.scl")));
            var copy = input.GetSourceBytes("sources/Main.scl"); copy[0] = 0;
            Assert.NotEqual(copy, input.GetSourceBytes("sources/Main.scl")); input.ValidateUnchanged();
            Assert.Throws<IOException>(() => ProjectGenerationInitializer.Initialize(Path.GetDirectoryName(SpecPath)));
            Assert.Equal(new[] { "project-generation.json", "sources/Main.scl" }, Directory.GetFiles(Path.GetDirectoryName(SpecPath), "*", SearchOption.AllDirectories)
                .Select(x => x.Substring(Path.GetDirectoryName(SpecPath).Length + 1).Replace('\\', '/')).OrderBy(x => x).ToArray());
        }
        [Theory]
        [InlineData("schemaVersion", "2.0")]
        [InlineData("tiaVersion", "V20")]
        [InlineData("projectName", "../escape")]
        [InlineData("projectName", "CON")]
        [InlineData("projectName", "name.")]
        [InlineData("projectName", " name")]
        public void InvalidRootValues(string key, string value) { Edit(j => j[key] = value); Assert.Throws<InvalidDataException>(() => Load()); }
        [Theory]
        [InlineData("null")][InlineData("true")][InlineData("21")][InlineData("{}")] [InlineData("[]")]
        public void RejectsWrongScalarTypes(string token)
        { File.WriteAllText(SpecPath, File.ReadAllText(SpecPath).Replace("\"V21\"", token)); Assert.Throws<InvalidDataException>(() => Load()); }
        [Theory]
        [InlineData("root")][InlineData("plc")][InlineData("artifact")]
        public void RejectsUnknownFieldsAtEveryLevel(string level)
        { Edit(j => { var target = level == "root" ? j : level == "plc" ? (Dictionary<string, object>)j["plc"] : Artifact(j); target["unknown"] = "x"; }); Assert.Throws<InvalidDataException>(() => Load()); }
        [Theory]
        [InlineData("{\"schemaVersion\":\"1.0\",\"schemaVersion\":\"1.0\"}")]
        [InlineData("{\"schemaVersion\":\"1.0\",\"schema\\u0056ersion\":\"1.0\"}")]
        [InlineData("{\"x\":\"a\",}")][InlineData("{\"x\":[\"a\",]}")]
        [InlineData("{\"x\":\"\\uD800\"}")][InlineData("{} {}")][InlineData("/*x*/ {}")]
        public void RejectsAmbiguousJson(string text)
        { File.WriteAllText(SpecPath, text); Assert.Throws<InvalidDataException>(() => Load()); }
        [Fact] public void RejectsInvalidUtf8()
        { File.WriteAllBytes(SpecPath, new byte[] { 0xff }); Assert.Throws<DecoderFallbackException>(() => Load()); }
        [Fact] public void RejectsUnknownCpu()
        { Edit(j => ((Dictionary<string, object>)j["plc"])["cpuTypeIdentifier"] = "OrderNumber:unknown"); Assert.Throws<InvalidDataException>(() => Load()); }
        [Theory]
        [InlineData("format", "simatic-sd")][InlineData("format", "simaticml")][InlineData("format", "SCL")]
        [InlineData("kind", "HMI")][InlineData("name", "Other")][InlineData("id", "bad-id")]
        [InlineData("sourcePath", "../Main.scl")][InlineData("sourcePath", "C:/Main.scl")]
        [InlineData("sourcePath", "/Main.scl")][InlineData("sourcePath", "sources\\Main.scl")]
        [InlineData("sourcePath", "sources/../Main.scl")][InlineData("sourcePath", "sources/./Main.scl")]
        [InlineData("sourcePath", "sources//Main.scl")][InlineData("sourcePath", "sources/CON.scl")]
        [InlineData("sourcePath", "sources/Main.scl:extra")][InlineData("sourcePath", "sources/Main.xml")]
        public void RejectsUnsupportedOrUnsafeArtifacts(string key, string value)
        { Edit(j => Artifact(j)[key] = value); Assert.Throws<InvalidDataException>(() => Load()); }
        [Fact] public void RejectsMissingSource() { File.Delete(Path.Combine(root, "input", "sources", "Main.scl")); Assert.Throws<FileNotFoundException>(() => Load()); }
        [Fact] public void RejectsUndeclaredFile() { File.WriteAllText(Path.Combine(root, "input", "extra.scl"), "x"); Assert.Throws<InvalidDataException>(() => Load()); }
        [Fact] public void InputChangesInvalidatePinnedCandidate()
        {
            var input = Load(); Source("ORGANIZATION_BLOCK \"Main\" BEGIN // changed\nEND_ORGANIZATION_BLOCK");
            Assert.Throws<InvalidDataException>(() => input.ValidateUnchanged());
            // The consumer still has the original private bytes, not the changed import path.
            Assert.Contains("Add program logic", Encoding.UTF8.GetString(input.GetSourceBytes("sources/Main.scl")));
        }
        [Fact] public void SpecChangesInvalidatePinnedCandidate()
        { var input = Load(); Edit(j => j["projectName"] = "Changed"); Assert.Throws<InvalidDataException>(() => input.ValidateUnchanged()); }
        [Fact] public void AddedFileInvalidatesPinnedCandidate()
        { var input = Load(); File.WriteAllText(Path.Combine(root, "input", "extra"), "x"); Assert.Throws<InvalidDataException>(() => input.ValidateUnchanged()); }
        [Theory]
        [InlineData("dangling")][InlineData("main")]
        public void RejectsDanglingOrCyclicDependencies(string dependency)
        { Edit(j => Artifact(j)["dependsOn"] = new[] { dependency }); Assert.Throws<InvalidDataException>(() => Load()); }
        [Fact] public void RejectsDuplicateArtifacts()
        { Edit(j => j["artifacts"] = new object[] { Artifact(j), Artifact(j) }); Assert.Throws<InvalidDataException>(() => Load()); }
        [Theory]
        [InlineData("FUNCTION \"Other\" : Void BEGIN END_FUNCTION")]
        [InlineData("ORGANIZATION_BLOCK \"Main\" BEGIN END_FUNCTION")]
        [InlineData("ORGANIZATION_BLOCK \"Main\" BEGIN")]
        [InlineData("ORGANIZATION_BLOCK \"Main\" BEGIN END_ORGANIZATION_BLOCK DATA_BLOCK \"Hidden\" BEGIN END_DATA_BLOCK")]
        [InlineData("// ORGANIZATION_BLOCK \"Main\" END_ORGANIZATION_BLOCK")]
        [InlineData("ORGANIZATION_BLOCK \"Main\" BEGIN 'unterminated END_ORGANIZATION_BLOCK")]
        [InlineData("(* unterminated")]
        public void RejectsIdentityOrEnvelopeMismatch(string text) { Source(text); Assert.Throws<InvalidDataException>(() => Load()); }
        [Fact] public void CommentAndLiteralMarkersAreIgnored()
        { Source("(* FUNCTION \"Hidden\" END_FUNCTION *)\nORGANIZATION_BLOCK \"Main\" BEGIN 'FUNCTION \"Hidden\" END_FUNCTION'; END_ORGANIZATION_BLOCK"); Assert.Single(Load().Spec.Artifacts); }
        [Fact] public void MultipleDeclarationsPerSourceImportedOnceAndSorted()
        {
            Source("FUNCTION \"Helper\" : Void BEGIN END_FUNCTION\nORGANIZATION_BLOCK \"Main\" BEGIN END_ORGANIZATION_BLOCK");
            Edit(j => { var main = Artifact(j); main["dependsOn"] = new[] { "helper" }; j["artifacts"] = new object[] { main,
                new { id = "helper", kind = "FC", name = "Helper", format = "scl", sourcePath = "sources/Main.scl", dependsOn = new string[0] } }; });
            var input = Load(); Assert.Equal(new[] { "helper", "main" }, input.OrderedArtifacts.Select(x => x.Id)); Assert.Single(input.OrderedSourcePaths);
        }
        [Theory][InlineData("minimal")][InlineData("multi-object")]
        public void ShippedFixturesLoad(string name)
        { var input = ProjectGenerationSpecLoader.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", name, "project-generation.json")); Assert.NotEmpty(input.Spec.Artifacts); input.ValidateUnchanged(); if (name == "multi-object") { Assert.Equal(2, input.Spec.TagTables[0].Tags.Count); Assert.Equal("scale", input.OrderedArtifacts[0].Id); } }
        [Theory]
        [InlineData("Bool", "%I0.0")][InlineData("Byte", "%MB0")][InlineData("Char", "%MB0")][InlineData("SInt", "%MB0")][InlineData("USInt", "%MB0")]
        [InlineData("Word", "%MW0")][InlineData("Int", "%MW0")][InlineData("UInt", "%MW0")]
        [InlineData("DWord", "%MD0")][InlineData("DInt", "%MD0")][InlineData("UDInt", "%MD0")][InlineData("Real", "%MD0")]
        public void ReusesAllTwelveTagTypes(string type, string address)
        { Tags(new { name = "Signal", dataType = type, logicalAddress = address }); Assert.Single(Load().Spec.TagTables[0].Tags); }
        private void Tags(params object[] tags) => Edit(j => j["tagTables"] = new object[] { new { name = "Signals", tags } });
        [Theory][InlineData("Bool", "%MW0")][InlineData("Timer", "%MW0")][InlineData("Real", "%MB0")][InlineData("Bool", "%I0.8")]
        public void RejectsTagTypeAddressMismatch(string type, string address)
        { Tags(new { name = "Signal", dataType = type, logicalAddress = address }); Assert.Throws<InvalidDataException>(() => Load()); }
        [Fact] public void RejectsTagOverlaps()
        { Tags(new { name = "Wide", dataType = "Real", logicalAddress = "%MD0" }, new { name = "Bit", dataType = "Bool", logicalAddress = "%M3.7" }); Assert.Throws<InvalidDataException>(() => Load()); }
        [Fact] public void RejectsTagAndBlockNameConflict()
        { Tags(new { name = "main", dataType = "Bool", logicalAddress = "%M0.0" }); Assert.Throws<InvalidDataException>(() => Load()); }
        [Fact] public void ExistingFileAndMissingParentAreRefused()
        { var file = Path.Combine(root, "existing"); File.WriteAllText(file, "keep"); Assert.Throws<IOException>(() => ProjectGenerationInitializer.Initialize(file)); Assert.Equal("keep", File.ReadAllText(file)); Assert.Throws<DirectoryNotFoundException>(() => ProjectGenerationInitializer.Initialize(Path.Combine(root, "absent", "child"))); }
        [Fact] public void RejectsDirectoryJunctionInSourceTree()
        {
            var outside = Path.Combine(root, "outside"); Directory.CreateDirectory(outside);
            var link = Path.Combine(root, "input", "redirected");
            using (var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + outside + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
            { p.WaitForExit(); Assert.Equal(0, p.ExitCode); }
            try { Assert.Throws<InvalidDataException>(() => Load()); Assert.Throws<IOException>(() => ProjectGenerationInitializer.Initialize(Path.Combine(link, "new"))); }
            finally { Directory.Delete(link); }
        }
        [Fact] public void CliParsesInitAndPreservesOldCommands()
        {
            Assert.True(CliCommandLine.TryParse(new[] { "init", "new" }, out var init)); Assert.Equal("new", init.Target);
            foreach (var args in new[] { new[] { "doctor" }, new[] { "ai-view", "source" }, new[] { "export", "project.ap21", "repo" }, new[] { "build", "repo", "--output", "new" }, new[] { "verify", "a.ap21", "b.ap21" } }) Assert.True(CliCommandLine.TryParse(args, out _));
            foreach (var args in new[] { new[] { "init" }, new[] { "init", " " }, new[] { "init", "new", "extra" }, new[] { "INIT", "new" } }) Assert.False(CliCommandLine.TryParse(args, out _));
        }
    }
}
