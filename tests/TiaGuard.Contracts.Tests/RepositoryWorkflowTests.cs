using System;
using System.IO;
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
    }
}
