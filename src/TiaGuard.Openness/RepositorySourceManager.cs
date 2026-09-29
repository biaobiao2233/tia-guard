using System;
using System.IO;

namespace TiaGuard.Openness
{
    public static class RepositorySourceManager
    {
        public const string ManagedDirectoryName = "tia-source";

        public static string ManagedSourcePath(string repositoryRoot)
        {
            if (string.IsNullOrWhiteSpace(repositoryRoot))
                throw new ArgumentException("A Git repository directory is required.", nameof(repositoryRoot));
            var root = Path.GetFullPath(repositoryRoot).TrimEnd(Path.DirectorySeparatorChar);
            if (!Directory.Exists(root))
                throw new DirectoryNotFoundException("The Git repository directory does not exist.");
            FileSystemSafety.RequirePlainAncestors(root);
            return Path.Combine(root, ManagedDirectoryName);
        }

        public static void ValidateExistingManagedSource(string repositoryRoot)
        {
            var managed = ManagedSourcePath(repositoryRoot);
            if (File.Exists(managed))
                throw new InvalidDataException("tia-source exists as a file; refusing to replace it.");
            if (!Directory.Exists(managed)) return;

            // Exact-tree validation rejects unknown/extra files and incomplete sources.
            RoundTripBuildInput.LoadSource(managed);
        }

        public static void ReplaceManagedSource(string repositoryRoot, string stagedSourceRoot)
        {
            var managed = ManagedSourcePath(repositoryRoot);
            var repo = Path.GetDirectoryName(managed);
            RoundTripBuildInput.LoadSource(stagedSourceRoot);
            ValidateExistingManagedSource(repositoryRoot);

            var id = Guid.NewGuid().ToString("N");
            var incoming = Path.Combine(repo, ".tia-source.tia-guard-new-" + id);
            var backup = Path.Combine(repo, ".tia-source.tia-guard-old-" + id);
            var hadExisting = Directory.Exists(managed);
            var movedExisting = false;
            var published = false;

            try
            {
                FileSystemSafety.CopyPlainTree(stagedSourceRoot, incoming);
                if (hadExisting)
                {
                    Directory.Move(managed, backup);
                    movedExisting = true;
                }
                Directory.Move(incoming, managed);
                published = true;

                if (movedExisting)
                    FileSystemSafety.DeleteOwnedTree(backup);
            }
            catch
            {
                try
                {
                    if (published && Directory.Exists(managed))
                        FileSystemSafety.DeleteOwnedTree(managed);
                    if (movedExisting && Directory.Exists(backup) && !Directory.Exists(managed))
                        Directory.Move(backup, managed);
                    if (Directory.Exists(incoming))
                        FileSystemSafety.DeleteOwnedTree(incoming);
                }
                catch
                {
                    throw new IOException(
                        "Updating tia-source failed and automatic rollback could not be completed safely.");
                }
                throw;
            }
        }
    }
}
