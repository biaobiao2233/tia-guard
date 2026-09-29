using System;
using System.Diagnostics;
using System.IO;
using TiaGuard.Gui;
using TiaGuard.Openness;
using Xunit;

namespace TiaGuard.Contracts.Tests
{
    public sealed class GitRepositoryClientTests
    {
        [Fact]
        public void ExistingRepositoryCloneAndPullUseSystemGit()
        {
            using (var fixture = new CanonicalFixture())
            {
                var gitRoot = CreateGitRoot();
                var remote = Path.Combine(gitRoot, "remote.git");
                var seed = Path.Combine(gitRoot, "seed");
                Git(gitRoot, "init", "--bare", remote);
                Git(gitRoot, "init", seed);
                Git(seed, "config", "user.name", "TIA-Guard Test");
                Git(seed, "config", "user.email", "tia-guard-test@example.invalid");
                File.WriteAllText(Path.Combine(seed, "README.md"), "one");
                Git(seed, "add", "README.md");
                Git(seed, "commit", "-m", "seed");
                Git(seed, "branch", "-M", "main");
                Git(seed, "remote", "add", "origin", RemoteUri(remote));
                Git(seed, "push", "-u", "origin", "main");
                Git(remote, "symbolic-ref", "HEAD", "refs/heads/main");

                var client = new GitRepositoryClient();
                var cache = client.PrepareRepository(RemoteUri(remote), null);
                try
                {
                    Assert.Equal("one", File.ReadAllText(Path.Combine(cache, "README.md")));

                    File.WriteAllText(Path.Combine(seed, "README.md"), "two");
                    Git(seed, "add", "README.md");
                    Git(seed, "commit", "-m", "update");
                    Git(seed, "push");

                    var reused = client.PrepareRepository(RemoteUri(remote), null);
                    Assert.Equal(cache, reused);
                    Assert.Equal("two", File.ReadAllText(Path.Combine(cache, "README.md")));
                }
                finally
                {
                    TryDeleteGitRoot(cache);
                    TryDeleteGitRoot(gitRoot);
                }
            }
        }

        [Fact]
        public void EmptyRepositoryFirstPublishPushesOnlyManagedSource()
        {
            using (var fixture = new CanonicalFixture())
            {
                var gitRoot = CreateGitRoot();
                var remote = Path.Combine(gitRoot, "empty.git");
                var verify = Path.Combine(gitRoot, "verify");
                Git(gitRoot, "init", "--bare", remote);

                var client = new GitRepositoryClient();
                var cache = client.PrepareRepository(RemoteUri(remote), null);
                try
                {
                    Git(cache, "config", "user.name", "TIA-Guard Test");
                    Git(cache, "config", "user.email", "tia-guard-test@example.invalid");
                    RepositorySourceManager.ReplaceManagedSource(cache, fixture.Root);
                    Assert.True(client.CommitManagedSource(cache, "first publish", null));
                    client.Push(cache, null);

                    Git(gitRoot, "clone", RemoteUri(remote), verify);
                    Assert.True(File.Exists(Path.Combine(
                        verify, RepositorySourceManager.ManagedDirectoryName, "tia-guard.json")));
                    Assert.False(File.Exists(Path.Combine(verify, "README.md")));
                    RoundTripBuildInput.LoadSource(Path.Combine(
                        verify, RepositorySourceManager.ManagedDirectoryName));
                }
                finally
                {
                    TryDeleteGitRoot(cache);
                    TryDeleteGitRoot(gitRoot);
                }
            }
        }

        [Fact]
        public void HttpUrlWithEmbeddedCredentialsIsRejected()
        {
            var client = new GitRepositoryClient();
            var error = Assert.Throws<GitOperationException>(() =>
                client.PrepareRepository("https://user:secret@example.invalid/repo.git", null));
            Assert.Contains("不能内嵌", error.FriendlyMessage);
        }

        private static string RemoteUri(string path)
        {
            return new Uri(Path.GetFullPath(path)).AbsoluteUri;
        }

        private static string CreateGitRoot()
        {
            var root = Path.Combine(
                Path.GetTempPath(), "TiaGuard.GitTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static void TryDeleteGitRoot(string root)
        {
            if (!Directory.Exists(root)) return;
            try
            {
                foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                foreach (var directory in Directory.GetDirectories(root, "*", SearchOption.AllDirectories))
                    File.SetAttributes(directory, FileAttributes.Directory);
                Directory.Delete(root, true);
            }
            catch
            {
                // Best-effort temp cleanup is not part of the Git workflow assertion.
            }
        }

        private static void Git(string workingDirectory, params string[] arguments)
        {
            var info = new ProcessStartInfo
            {
                FileName = "git.exe",
                Arguments = string.Join(" ", Array.ConvertAll(arguments, Quote)),
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var process = Process.Start(info))
            {
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                Assert.True(process.WaitForExit(30000), "git timed out");
                Assert.True(process.ExitCode == 0,
                    "git " + string.Join(" ", arguments) + "\n" + output + "\n" + error);
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
