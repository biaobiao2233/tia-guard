using System;
using System.IO;
using System.Linq;
using TiaGuard.Openness;
using Xunit;

namespace TiaGuard.Contracts.Tests
{
    public sealed class RepositoryWorkflowTests
    {
        [Fact]
        public void LongUserOutputDoesNotControlTiaStagingPath()
        {
            using (var fixture = new CanonicalFixture())
            {
                var longParent = Path.Combine(
                    fixture.OwnedRoot,
                    new string('a', 48),
                    new string('b', 48));
                Directory.CreateDirectory(longParent);
                var output = Path.Combine(longParent, "rebuilt-project");

                var input = RoundTripBuildInput.Load(fixture.Root, output);
                var layout = RoundTripBuildLayout.Plan(
                    input.Manifest.Project.Name,
                    "0123456789abcdef0123456789abcdef");

                Assert.Equal(Path.GetFullPath(output), input.OutputDirectory);
                Assert.True(layout.StagedProjectDirectory.Length <=
                    RoundTripBuildLayout.TiaV21MaximumProjectDirectoryLength);
                Assert.DoesNotContain(longParent, layout.StagedProjectDirectory,
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void ExistingManagedSourceWithUnknownContentFailsClosed()
        {
            using (var fixture = new CanonicalFixture())
            {
                var repo = Path.Combine(fixture.OwnedRoot, "repo");
                var managed = Path.Combine(repo, RepositorySourceManager.ManagedDirectoryName);
                Directory.CreateDirectory(managed);
                File.WriteAllText(Path.Combine(managed, "foreign.txt"), "do not overwrite");

                Assert.ThrowsAny<Exception>(() =>
                    RepositorySourceManager.ValidateExistingManagedSource(repo));
                Assert.Equal("do not overwrite",
                    File.ReadAllText(Path.Combine(managed, "foreign.txt")));
            }
        }

        [Fact]
        public void ManagedSourceReplacementTouchesOnlyTiaSource()
        {
            using (var existing = new CanonicalFixture())
            using (var replacement = new CanonicalFixture())
            {
                replacement.Tag.CommentStatus = "present";
                replacement.Tag.Comment = "updated";
                replacement.Save();

                var repo = Path.Combine(existing.OwnedRoot, "repo");
                Directory.CreateDirectory(repo);
                File.WriteAllText(Path.Combine(repo, "README.md"), "keep me");
                FileSystemSafety.CopyPlainTree(existing.Root,
                    Path.Combine(repo, RepositorySourceManager.ManagedDirectoryName));

                RepositorySourceManager.ReplaceManagedSource(repo, replacement.Root);

                Assert.Equal("keep me", File.ReadAllText(Path.Combine(repo, "README.md")));
                var loaded = RoundTripBuildInput.LoadSource(
                    Path.Combine(repo, RepositorySourceManager.ManagedDirectoryName));
                Assert.Equal("updated", loaded.TagTables[0].Tags[0].Comment);
            }
        }

        [Fact]
        public void LegacyAndMultiProjectLayoutsCanCoexist()
        {
            using (var legacy = new CanonicalFixture())
            using (var second = new CanonicalFixture())
            {
                second.Manifest.Project.Name = "Second";
                second.Save();

                var repo = Path.Combine(legacy.OwnedRoot, "repo");
                Directory.CreateDirectory(repo);
                FileSystemSafety.CopyPlainTree(
                    legacy.Root,
                    Path.Combine(repo, RepositorySourceManager.ManagedDirectoryName));
                RepositorySourceManager.AddProjectSource(repo, "second", second.Root);

                var catalog = RepositorySourceManager.DiscoverProjects(repo);

                Assert.Equal(2, catalog.Projects.Count);
                Assert.Empty(catalog.Problems);
                Assert.Single(catalog.Projects, value => value.IsLegacy);
                Assert.Contains(catalog.Projects,
                    value => value.Slot == "second" &&
                             value.Manifest.Project.Name == "Second");
            }
        }

        [Fact]
        public void UpdatingOneProjectSlotDoesNotTouchSiblingProjectOrRepoFiles()
        {
            using (var first = new CanonicalFixture())
            using (var second = new CanonicalFixture())
            using (var replacement = new CanonicalFixture())
            {
                second.Manifest.Project.Name = "Second";
                second.Save();
                replacement.Tag.CommentStatus = "present";
                replacement.Tag.Comment = "updated";
                replacement.Save();

                var repo = Path.Combine(first.OwnedRoot, "repo");
                Directory.CreateDirectory(repo);
                File.WriteAllText(Path.Combine(repo, "README.md"), "keep me");
                RepositorySourceManager.AddProjectSource(repo, "first", first.Root);
                RepositorySourceManager.AddProjectSource(repo, "second", second.Root);

                RepositorySourceManager.ReplaceProjectSource(
                    repo, "first", replacement.Root);

                Assert.Equal("keep me", File.ReadAllText(Path.Combine(repo, "README.md")));
                var updated = RoundTripBuildInput.LoadSource(
                    RepositorySourceManager.ProjectManagedSourcePath(repo, "first"));
                var untouched = RoundTripBuildInput.LoadSource(
                    RepositorySourceManager.ProjectManagedSourcePath(repo, "second"));
                Assert.Equal("updated", updated.TagTables[0].Tags[0].Comment);
                Assert.Equal("Second", untouched.Manifest.Project.Name);
            }
        }

        [Fact]
        public void BrokenProjectSlotDoesNotHideValidProjects()
        {
            using (var valid = new CanonicalFixture())
            {
                var repo = Path.Combine(valid.OwnedRoot, "repo");
                Directory.CreateDirectory(repo);
                RepositorySourceManager.AddProjectSource(repo, "valid", valid.Root);
                Directory.CreateDirectory(Path.Combine(
                    repo, RepositorySourceManager.ProjectsDirectoryName, "broken"));

                var catalog = RepositorySourceManager.DiscoverProjects(repo);

                Assert.Single(catalog.Projects);
                Assert.Equal("valid", catalog.Projects[0].Slot);
                Assert.Single(catalog.Problems);
                Assert.Equal("broken", catalog.Problems[0].Slot);
                Assert.Equal("ProjectSourceMissing", catalog.Problems[0].ProblemType);
            }
        }

        [Fact]
        public void ExistingProjectIdentityMismatchFailsClosed()
        {
            using (var existing = new CanonicalFixture())
            using (var candidate = new CanonicalFixture())
            {
                candidate.Manifest.Project.Name = "Different";
                candidate.Save();

                Assert.Throws<InvalidDataException>(() =>
                    RepositorySourceManager.EnsureSameProjectIdentity(
                        existing.Root, candidate.Root));
            }
        }

        [Theory]
        [InlineData("Motor Control.ap21", "motor-control")]
        [InlineData("泵站控制.ap21", "泵站控制")]
        public void SuggestedProjectSlotIsStableAndSafe(string input, string expected)
        {
            var slot = RepositorySourceManager.SuggestProjectSlot(input);
            Assert.Equal(expected, slot);
            Assert.True(RepositorySourceManager.IsSafeProjectSlot(slot));
        }
    }
}
