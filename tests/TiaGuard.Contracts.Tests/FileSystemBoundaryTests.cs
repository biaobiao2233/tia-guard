using System;
using System.Diagnostics;
using System.IO;
using TiaGuard.Openness;
using Xunit;

namespace TiaGuard.Contracts.Tests
{
    public sealed class FileSystemBoundaryTests
    {
        private static void Junction(string path, string target)
        {
            using (var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + path + "\" \"" + target + "\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
            {
                process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                Assert.True(process.WaitForExit(10000), "Junction creation timed out.");
                Assert.True(process.ExitCode == 0, error);
            }
        }

        [Fact]
        public void RootAndOutputAncestorJunctionsAreRejected()
        {
            using (var f = new CanonicalFixture())
            {
                var link = Path.Combine(f.OwnedRoot, "link");
                Junction(link, f.Root);
                try
                {
                    Assert.Throws<IOException>(() => RoundTripBuildInput.LoadSource(link));
                    Assert.Throws<IOException>(() => RoundTripBuildInput.Load(f.Root, Path.Combine(link, "new")));
                }
                finally { Directory.Delete(link); }
            }
        }

        [Fact]
        public void CleanupRefusesChildJunctionAndPreservesTarget()
        {
            using (var f = new CanonicalFixture())
            using (var outside = new CanonicalFixture())
            {
                var link = Path.Combine(f.Root, "link");
                Junction(link, outside.Root);
                try
                {
                    Assert.Throws<InvalidDataException>(() => RoundTripBuildInput.LoadSource(f.Root));
                    Assert.Throws<IOException>(() => FileSystemSafety.DeleteOwnedTree(f.Root));
                    Assert.True(File.Exists(outside.PathOf("tia-guard.json")));
                }
                finally { Directory.Delete(link); }
            }
        }

        [Fact]
        public void ReadyMaterializerRejectsRedirectedRawArtifact()
        {
            using (var f = new CanonicalFixture())
            {
                // The production public entry rejects reparse ancestors before writing.
                var link = Path.Combine(f.OwnedRoot, "linked-raw");
                Junction(link, f.Root);
                try
                {
                    Assert.Throws<IOException>(() => RoundTripSourceMaterializer.Write(new SnapshotV1(),
                        new RoundTripExtractionHints(), link, Path.Combine(f.OwnedRoot, "export")));
                    Assert.False(Directory.Exists(Path.Combine(f.OwnedRoot, "export")));
                }
                finally { Directory.Delete(link); }
            }
        }
    }
}
