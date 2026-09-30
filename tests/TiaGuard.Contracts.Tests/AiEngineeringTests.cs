using System;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using System.Xml;
using TiaGuard.Cli;
using TiaGuard.Openness;
using Xunit;

namespace TiaGuard.Contracts.Tests
{
    public sealed class AiEngineeringTests
    {
        private static string Unit(string source) => "<SW.Blocks.CompileUnit><AttributeList>" + source +
            "<ProgrammingLanguage>LAD</ProgrammingLanguage></AttributeList></SW.Blocks.CompileUnit>";

        private static void Networks(CanonicalFixture f, string units)
        {
            f.Xml = f.Xml.Replace("</SW.Blocks.OB>", "<ObjectList>" + units + "</ObjectList></SW.Blocks.OB>");
            f.Save();
        }

        [Fact]
        public void RepeatedAndDeletedOutputIsByteIdenticalAndSourceUnchanged()
        {
            using (var f = new CanonicalFixture())
            {
                Networks(f, Unit("<NetworkSource />"));
                var source = Path.Combine(f.OwnedRoot, "tia-source");
                FileSystemSafety.CopyPlainTree(f.Root, source);
                var original = Tree(source);
                var output = AiEngineeringPublisher.Generate(source);
                var first = Tree(output);
                var culture = Thread.CurrentThread.CurrentCulture;
                try
                {
                    Thread.CurrentThread.CurrentCulture = new CultureInfo("ar-SA");
                    AiEngineeringPublisher.Generate(source);
                    Assert.Equal(first, Tree(output));
                }
                finally { Thread.CurrentThread.CurrentCulture = culture; }
                FileSystemSafety.DeleteOwnedTree(output);
                AiEngineeringPublisher.Generate(source);
                Assert.Equal(first, Tree(output));
                Assert.Equal(original, Tree(source));
                Assert.DoesNotContain("1970-", File.ReadAllText(Path.Combine(output, "project.json")));
            }
        }

        [Fact]
        public void MissingOrCorruptAiDoesNotAffectBuildPreflightOrVerifyAndCanBeRepaired()
        {
            using (var f = new CanonicalFixture())
            {
                var source = Path.Combine(f.OwnedRoot, "tia-source");
                FileSystemSafety.CopyPlainTree(f.Root, source);
                Assert.NotNull(RoundTripBuildInput.Load(source, Path.Combine(f.OwnedRoot, "build-a")));
                Assert.Equal("pass", RoundTripVerifier.CompareSources(f.Root, source).Verdict);
                var ai = AiEngineeringPublisher.Generate(source);
                var expected = Tree(ai);
                File.WriteAllText(Path.Combine(ai, "project.json"), "{ BROKEN JSON");
                File.WriteAllText(Path.Combine(ai, "PROJECT.md"), "pretend this PLC is different");
                Assert.NotNull(RoundTripBuildInput.Load(source, Path.Combine(f.OwnedRoot, "build-b")));
                Assert.Equal("pass", RoundTripVerifier.CompareSources(f.Root, source).Verdict);
                AiEngineeringPublisher.Generate(source);
                Assert.Equal(expected, Tree(ai));
            }
        }

        [Fact]
        public void SelectedSlotDoesNotReadOrModifySiblingAi()
        {
            using (var f = new CanonicalFixture())
            {
                RepositorySourceManager.AddProjectSource(f.OwnedRoot, "a", f.Root);
                RepositorySourceManager.AddProjectSource(f.OwnedRoot, "b", f.Root);
                var a = RepositorySourceManager.ProjectManagedSourcePath(f.OwnedRoot, "a");
                var b = RepositorySourceManager.ProjectManagedSourcePath(f.OwnedRoot, "b");
                var bai = AiEngineeringPublisher.Generate(b);
                File.WriteAllText(Path.Combine(bai, "project.json"), "corrupt sibling");
                File.WriteAllText(Path.Combine(bai, "unmanaged.txt"), "preserve me");
                var expected = Tree(Path.GetDirectoryName(b));
                AiEngineeringPublisher.Generate(a);
                f.Tag.Comment = "new comment"; f.Tag.CommentStatus = "present"; f.Save();
                RepositorySourceManager.ReplaceProjectSource(f.OwnedRoot, "a", f.Root);
                AiEngineeringPublisher.Generate(a);
                Assert.Equal(expected, Tree(Path.GetDirectoryName(b)));
                Assert.Contains("new comment", File.ReadAllText(Path.Combine(Path.GetDirectoryName(a), "ai", "symbols.md")));
            }
        }

        [Theory]
        [InlineData("<NetworkSource />", "empty")]
        [InlineData("", "incomplete")]
        [InlineData("<NetworkSource/><NetworkSource/>", "incomplete")]
        [InlineData("<NetworkSource><FlgNet xmlns='urn:unrecognized'><Parts><Part Name='Coil'/></Parts></FlgNet></NetworkSource>", "unsupported")]
        [InlineData("<NetworkSource vendor='unknown'/>", "unsupported")]
        [InlineData("<NetworkSource/><VendorExtension/>", "unsupported")]
        public void NeverConfusesUnknownOrUnsupportedLogicWithEmpty(string xml, string state)
        {
            using (var f = new CanonicalFixture())
            {
                Networks(f, Unit(xml));
                var block = AiEngineeringRenderer.Read(f.Root).Blocks.Single();
                Assert.Equal("extracted", block.NetworkInventory.Status);
                Assert.Equal(state, block.Networks.Single().Logic.Status);
                Assert.Contains("**unknown**", AiEngineeringRenderer.Render(f.Root)["programs/OB1.md"]);
            }
        }

        [Fact]
        public void MissingNetworkContainerIsIncompleteNotNoNetworks()
        {
            using (var f = new CanonicalFixture())
            {
                var block = AiEngineeringRenderer.Read(f.Root).Blocks.Single();
                Assert.Equal("incomplete", block.NetworkInventory.Status);
                Assert.Empty(block.Networks);
                Networks(f, "<VendorNetworks/>" + Unit("<NetworkSource/>"));
                Assert.Equal("incomplete", AiEngineeringRenderer.Read(f.Root).Blocks.Single().NetworkInventory.Status);
            }
        }

        [Fact]
        public void SymbolsAndNetworkOrderAndSourceReferencesAreExplicit()
        {
            using (var f = new CanonicalFixture())
            {
                f.Tag.Comment = "Start | <button>\n[link](evil)"; f.Tag.CommentStatus = "present";
                var title = "<ObjectList><MultilingualText CompositionName='Title'><ObjectList><MultilingualTextItem><AttributeList><Culture>zh-CN</Culture><Text>启动</Text></AttributeList></MultilingualTextItem></ObjectList></MultilingualText></ObjectList>";
                Networks(f, Unit("<NetworkSource/>").Replace("</SW.Blocks.CompileUnit>", title + "</SW.Blocks.CompileUnit>") + Unit("<NetworkSource><Unknown/></NetworkSource>"));
                var view = AiEngineeringRenderer.Read(f.Root);
                var tag = view.TagTables.Single().Tags.Single();
                Assert.Equal("%I0.0", tag.Address);
                Assert.Equal("Bool", tag.DataType);
                Assert.Equal(f.Tag.Comment, tag.Comment);
                Assert.Equal(CanonicalFixture.TablePath + "#/tags/0", tag.SourceRef);
                Assert.Equal(new[] { 1, 2 }, view.Blocks[0].Networks.Select(n => n.Ordinal));
                var text = view.Blocks[0].Networks[0].Titles.Single();
                var doc = new XmlDocument(); doc.LoadXml(f.Xml);
                Assert.Equal("启动", doc.SelectSingleNode(text.SourceRef.Split('#')[1]).SelectSingleNode("AttributeList/Text").InnerText);
                var markdown = AiEngineeringRenderer.Render(f.Root)["symbols.md"];
                Assert.Contains(@"Start \| &lt;button&gt;", markdown);
                Assert.DoesNotContain("[link](evil)", markdown);
            }
        }

        [Fact]
        public void MarkdownNeverDumpsXmlOrLargeBlobText()
        {
            using (var f = new CanonicalFixture())
            {
                var blob = new string('A', 2048);
                f.Tag.Comment = blob; f.Tag.CommentStatus = "present";
                Networks(f, Unit("<NetworkSource><Resource>" + blob + "</Resource></NetworkSource>"));
                var files = AiEngineeringRenderer.Render(f.Root);
                foreach (var item in files.Where(p => p.Key.EndsWith(".md")))
                {
                    Assert.DoesNotContain(blob, item.Value);
                    Assert.DoesNotContain("<Resource>", item.Value);
                }
                Assert.Contains("text omitted", files["symbols.md"]);
                Assert.Contains("unsupported", files["programs/OB1.md"]);
            }
        }

        [Fact]
        public void InvalidCanonicalOrUnmanagedOutputNeverReplacesExistingData()
        {
            using (var f = new CanonicalFixture())
            {
                var source = Path.Combine(f.OwnedRoot, "tia-source");
                FileSystemSafety.CopyPlainTree(f.Root, source);
                var ai = AiEngineeringPublisher.Generate(source);
                var before = Tree(ai);
                File.AppendAllText(Path.Combine(source, CanonicalFixture.XmlPath), "broken");
                Assert.Throws<InvalidDataException>(() => AiEngineeringPublisher.Generate(source));
                Assert.Equal(before, Tree(ai));
                FileSystemSafety.DeleteOwnedTree(source);
                FileSystemSafety.CopyPlainTree(f.Root, source);
                File.WriteAllText(Path.Combine(ai, "notes.md"), "human-owned");
                Assert.Throws<IOException>(() => AiEngineeringPublisher.Generate(source));
                Assert.Equal("human-owned", File.ReadAllText(Path.Combine(ai, "notes.md")));
                Assert.False(File.Exists(Path.Combine(f.OwnedRoot, ".ai.tia-guard.lock")));
            }
        }

        [Fact]
        public void OtherGeneratorLockAndUnsafeOutputArePreserved()
        {
            using (var f = new CanonicalFixture())
            {
                Assert.Throws<ArgumentException>(() => AiEngineeringPublisher.Generate(f.Root));
                var source = Path.Combine(f.OwnedRoot, "tia-source");
                FileSystemSafety.CopyPlainTree(f.Root, source);
                var gate = Path.Combine(f.OwnedRoot, ".ai.tia-guard.lock");
                File.WriteAllText(gate, "owned by another generator");
                Assert.Throws<IOException>(() => AiEngineeringPublisher.Generate(source));
                Assert.Equal("owned by another generator", File.ReadAllText(gate));
            }
        }

        [Fact]
        public void LockedPreviousViewSurvivesFailedPublication()
        {
            using (var f = new CanonicalFixture())
            {
                var source = Path.Combine(f.OwnedRoot, "tia-source");
                FileSystemSafety.CopyPlainTree(f.Root, source);
                var ai = AiEngineeringPublisher.Generate(source);
                var before = Tree(ai);
                using (var locked = new FileStream(Path.Combine(ai, "PROJECT.md"), FileMode.Open, FileAccess.Read, FileShare.Read))
                    Assert.Throws<IOException>(() => AiEngineeringPublisher.Generate(source));
                Assert.Equal(before, Tree(ai));
                Assert.Empty(Directory.GetFileSystemEntries(f.OwnedRoot, ".ai.tia-guard-*"));
            }
        }

        [Fact]
        public void RedirectedAiIsRejectedAndSiblingTargetPreserved()
        {
            using (var f = new CanonicalFixture())
            {
                var source = Path.Combine(f.OwnedRoot, "tia-source");
                FileSystemSafety.CopyPlainTree(f.Root, source);
                var link = Path.Combine(f.OwnedRoot, "ai");
                var before = Tree(f.Root);
                using (var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + f.Root + "\"")
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
                {
                    process.StandardOutput.ReadToEnd();
                    var error = process.StandardError.ReadToEnd();
                    Assert.True(process.WaitForExit(10000));
                    Assert.True(process.ExitCode == 0, error);
                }
                try
                {
                    Assert.Throws<IOException>(() => AiEngineeringPublisher.Generate(source));
                    Assert.Equal(before, Tree(f.Root));
                }
                finally { Directory.Delete(link); }
            }
        }

        [Fact]
        public void JsonIsParseableAndInventoryOrderingIsOrdinal()
        {
            using (var f = new CanonicalFixture())
            {
                f.Tag.Name = "Zulu";
                f.Table.Tags.Add(new RoundTripTagV1 { Id = "tag-b", Name = "Alpha", DataType = "Bool", Address = "%Q0.0", CommentStatus = "missing", Capability = "supported-round-trip" });
                f.Manifest.Capabilities.Add(new RoundTripCapabilityV1 { ObjectKind = "tag", ObjectRef = "tag-b", State = "supported-round-trip" });
                f.Save();
                var view = AiEngineeringRenderer.Read(f.Root);
                Assert.Equal(new[] { "Alpha", "Zulu" }, view.TagTables[0].Tags.Select(t => t.Name));
                Assert.EndsWith("/1", view.TagTables[0].Tags[0].SourceRef);
                Assert.NotNull(new JavaScriptSerializer().DeserializeObject(AiEngineeringRenderer.Render(f.Root)["project.json"]));
            }
        }

        [Fact]
        public void CliSelectsExactlyOneSourceWithoutOutputOverride()
        {
            Assert.True(CliCommandLine.TryParse(new[] { "ai-view", "repo/tia-projects/a/tia-source" }, out var parsed));
            Assert.Equal("ai-view", parsed.Command);
            Assert.False(CliCommandLine.TryParse(new[] { "ai-view", " " }, out _));
            Assert.False(CliCommandLine.TryParse(new[] { "ai-view", "source", "--output", "other" }, out _));
        }

        [Fact]
        public void SourceFileReferencesEscapeUriDelimiters()
        {
            using (var f = new CanonicalFixture())
            {
                f.Table.Id = "table#a";
                var path = "tia/plc/plc-a/tags/table#a.json";
                f.Plc.TagTables[0] = path;
                f.Manifest.Capabilities.Single(c => c.ObjectKind == "tag-table").ObjectRef = f.Table.Id;
                f.Save();
                File.Move(f.PathOf(CanonicalFixture.TablePath), f.PathOf(path));
                var reference = AiEngineeringRenderer.Read(f.Root).TagTables[0].SourceRef;
                Assert.Contains("table%23a.json#", reference);
                Assert.Equal(path, Uri.UnescapeDataString(reference.Split('#')[0]));
            }
        }

        private static string[] Tree(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal).Select(p => p.Substring(root.Length) + ":" + Convert.ToBase64String(File.ReadAllBytes(p))).ToArray();
    }
}
