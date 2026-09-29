using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TiaGuard.Openness
{
    public sealed class RepositoryProjectSource
    {
        public string Slot { get; internal set; }
        public string SourcePath { get; internal set; }
        public string RelativeSourcePath { get; internal set; }
        public bool IsLegacy { get; internal set; }
        public RoundTripManifestV1 Manifest { get; internal set; }
    }

    public sealed class RepositoryProjectProblem
    {
        public string Slot { get; internal set; }
        public string RelativePath { get; internal set; }
        public string ProblemType { get; internal set; }
    }

    public sealed class RepositoryProjectCatalog
    {
        public List<RepositoryProjectSource> Projects { get; } =
            new List<RepositoryProjectSource>();
        public List<RepositoryProjectProblem> Problems { get; } =
            new List<RepositoryProjectProblem>();
    }

    public static class RepositorySourceManager
    {
        public const string ManagedDirectoryName = "tia-source";
        public const string ProjectsDirectoryName = "tia-projects";

        // Legacy single-project layout: repo/tia-source/.
        public static string ManagedSourcePath(string repositoryRoot)
        {
            return Path.Combine(RequireRepositoryRoot(repositoryRoot), ManagedDirectoryName);
        }

        // Multi-project layout: repo/tia-projects/<slot>/tia-source/.
        public static string ProjectManagedSourcePath(string repositoryRoot, string slot)
        {
            var root = RequireRepositoryRoot(repositoryRoot);
            RequireSafeProjectSlot(slot);
            return Path.Combine(root, ProjectsDirectoryName, slot, ManagedDirectoryName);
        }

        public static string RelativeManagedSourcePath(string slot)
        {
            RequireSafeProjectSlot(slot);
            return ProjectsDirectoryName + "/" + slot + "/" + ManagedDirectoryName;
        }

        public static string SuggestProjectSlot(string fileNameOrProjectName)
        {
            var value = fileNameOrProjectName ?? string.Empty;
            try
            {
                var file = Path.GetFileName(value);
                value = string.Equals(Path.GetExtension(file), ".ap21",
                    StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFileNameWithoutExtension(file)
                    : file;
            }
            catch
            {
                value = string.Empty;
            }

            var builder = new StringBuilder();
            var pendingSeparator = false;
            foreach (var ch in value.Normalize(NormalizationForm.FormC))
            {
                if (char.IsLetterOrDigit(ch))
                {
                    if (pendingSeparator && builder.Length != 0)
                        builder.Append('-');
                    builder.Append(char.ToLowerInvariant(ch));
                    pendingSeparator = false;
                }
                else
                {
                    pendingSeparator = builder.Length != 0;
                }

                if (builder.Length >= 64) break;
            }

            var candidate = builder.ToString().Trim('-');
            if (candidate.Length == 0) candidate = "tia-project";
            if (!IsSafeProjectSlot(candidate))
                candidate = "tia-project-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            return candidate;
        }

        public static bool IsSafeProjectSlot(string slot)
        {
            return !string.IsNullOrWhiteSpace(slot) &&
                   slot.Length <= 80 &&
                   !slot.StartsWith(".", StringComparison.Ordinal) &&
                   !string.Equals(slot, ManagedDirectoryName, StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(slot, ProjectsDirectoryName, StringComparison.OrdinalIgnoreCase) &&
                   RoundTripProfile.SafeFileName(slot);
        }

        public static RepositoryProjectCatalog DiscoverProjects(string repositoryRoot)
        {
            var root = RequireRepositoryRoot(repositoryRoot);
            var result = new RepositoryProjectCatalog();

            var legacy = Path.Combine(root, ManagedDirectoryName);
            if (File.Exists(legacy))
            {
                AddProblem(result, null, ManagedDirectoryName, "LegacySourceIsFile");
            }
            else if (Directory.Exists(legacy))
            {
                TryAddProject(result, null, legacy, ManagedDirectoryName, true);
            }

            var projectsRoot = Path.Combine(root, ProjectsDirectoryName);
            if (File.Exists(projectsRoot))
            {
                AddProblem(result, null, ProjectsDirectoryName, "ProjectsRootIsFile");
                return result;
            }
            if (!Directory.Exists(projectsRoot)) return result;

            var rootInfo = new DirectoryInfo(projectsRoot);
            if ((rootInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                AddProblem(result, null, ProjectsDirectoryName, "ProjectsRootIsReparsePoint");
                return result;
            }

            foreach (var slotDirectory in rootInfo.GetDirectories()
                .OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase))
            {
                var slot = slotDirectory.Name;
                var relative = ProjectsDirectoryName + "/" + slot + "/" + ManagedDirectoryName;
                if ((slotDirectory.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    AddProblem(result, slot, relative, "ProjectSlotIsReparsePoint");
                    continue;
                }
                if (!IsSafeProjectSlot(slot))
                {
                    AddProblem(result, slot, relative, "ProjectSlotNameIsUnsafe");
                    continue;
                }

                var source = Path.Combine(slotDirectory.FullName, ManagedDirectoryName);
                if (File.Exists(source))
                {
                    AddProblem(result, slot, relative, "ProjectSourceIsFile");
                    continue;
                }
                if (!Directory.Exists(source))
                {
                    AddProblem(result, slot, relative, "ProjectSourceMissing");
                    continue;
                }

                TryAddProject(result, slot, source, relative, false);
            }

            return result;
        }

        public static void ValidateExistingManagedSource(string repositoryRoot)
        {
            var managed = ManagedSourcePath(repositoryRoot);
            ValidateSourceIfPresent(managed);
        }

        public static void ReplaceManagedSource(string repositoryRoot, string stagedSourceRoot)
        {
            var managed = ManagedSourcePath(repositoryRoot);
            ReplaceSourceAtPath(managed, stagedSourceRoot, false);
        }

        public static void ReplaceProjectSource(
            string repositoryRoot, string slot, string stagedSourceRoot)
        {
            var managed = ProjectManagedSourcePath(repositoryRoot, slot);
            if (!Directory.Exists(managed))
                throw new DirectoryNotFoundException(
                    "The selected TIA-Guard project slot does not contain tia-source.");
            ReplaceSourceAtPath(managed, stagedSourceRoot, true);
        }

        public static void AddProjectSource(
            string repositoryRoot, string slot, string stagedSourceRoot)
        {
            var root = RequireRepositoryRoot(repositoryRoot);
            RequireSafeProjectSlot(slot);
            RoundTripBuildInput.LoadSource(stagedSourceRoot);

            var projectsRoot = Path.Combine(root, ProjectsDirectoryName);
            if (File.Exists(projectsRoot))
                throw new InvalidDataException(
                    "tia-projects exists as a file; refusing to create a project slot.");

            if (Directory.Exists(projectsRoot))
            {
                var projectsInfo = new DirectoryInfo(projectsRoot);
                if ((projectsInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("tia-projects cannot be a reparse point.");
            }
            else
            {
                Directory.CreateDirectory(projectsRoot);
            }

            var slotRoot = Path.Combine(projectsRoot, slot);
            if (Directory.Exists(slotRoot) || File.Exists(slotRoot))
                throw new IOException(
                    "The requested project slot already exists; choose it as an update target or use another slot.");

            Directory.CreateDirectory(slotRoot);
            try
            {
                ReplaceSourceAtPath(
                    Path.Combine(slotRoot, ManagedDirectoryName),
                    stagedSourceRoot,
                    false);
            }
            catch
            {
                TryDeleteEmptyDirectory(slotRoot);
                TryDeleteEmptyDirectory(projectsRoot);
                throw;
            }
        }

        public static bool IsSameProjectIdentity(
            string existingSourceRoot, string candidateSourceRoot)
        {
            var existing = RoundTripBuildInput.LoadSource(existingSourceRoot);
            var candidate = RoundTripBuildInput.LoadSource(candidateSourceRoot);
            return IsSameProjectIdentity(existing.Manifest.Project, candidate.Manifest.Project);
        }

        public static void EnsureSameProjectIdentity(
            string existingSourceRoot, string candidateSourceRoot)
        {
            if (!IsSameProjectIdentity(existingSourceRoot, candidateSourceRoot))
                throw new InvalidDataException(
                    "The selected repository project does not match the .ap21 being published. " +
                    "Choose the correct existing project or add it as a new project.");
        }

        private static bool IsSameProjectIdentity(
            RoundTripProjectV1 existing, RoundTripProjectV1 candidate)
        {
            if (existing == null || candidate == null ||
                !string.Equals(existing.Name, candidate.Name, StringComparison.Ordinal))
                return false;

            if (!string.IsNullOrWhiteSpace(existing.OriginalFileName) &&
                !string.Equals(existing.OriginalFileName, candidate.OriginalFileName,
                    StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        private static void TryAddProject(
            RepositoryProjectCatalog catalog,
            string slot,
            string sourcePath,
            string relativePath,
            bool legacy)
        {
            try
            {
                var input = RoundTripBuildInput.LoadSource(sourcePath);
                catalog.Projects.Add(new RepositoryProjectSource
                {
                    Slot = slot,
                    SourcePath = sourcePath,
                    RelativeSourcePath = relativePath,
                    IsLegacy = legacy,
                    Manifest = input.Manifest
                });
            }
            catch (Exception error)
            {
                AddProblem(catalog, slot, relativePath, error.GetType().Name);
            }
        }

        private static void AddProblem(
            RepositoryProjectCatalog catalog,
            string slot,
            string relativePath,
            string problemType)
        {
            catalog.Problems.Add(new RepositoryProjectProblem
            {
                Slot = slot,
                RelativePath = relativePath,
                ProblemType = problemType
            });
        }

        private static string RequireRepositoryRoot(string repositoryRoot)
        {
            if (string.IsNullOrWhiteSpace(repositoryRoot))
                throw new ArgumentException(
                    "A Git repository directory is required.", nameof(repositoryRoot));
            var root = Path.GetFullPath(repositoryRoot).TrimEnd(Path.DirectorySeparatorChar);
            if (!Directory.Exists(root))
                throw new DirectoryNotFoundException(
                    "The Git repository directory does not exist.");
            FileSystemSafety.RequirePlainAncestors(root);
            return root;
        }

        private static void RequireSafeProjectSlot(string slot)
        {
            if (!IsSafeProjectSlot(slot))
                throw new ArgumentException(
                    "The project slot name is empty or unsafe.", nameof(slot));
        }

        private static void ValidateSourceIfPresent(string managed)
        {
            if (File.Exists(managed))
                throw new InvalidDataException(
                    "tia-source exists as a file; refusing to replace it.");
            if (!Directory.Exists(managed)) return;
            RoundTripBuildInput.LoadSource(managed);
        }

        private static void ReplaceSourceAtPath(
            string managed, string stagedSourceRoot, bool requireExisting)
        {
            RoundTripBuildInput.LoadSource(stagedSourceRoot);
            if (File.Exists(managed))
                throw new InvalidDataException(
                    "tia-source exists as a file; refusing to replace it.");
            if (requireExisting && !Directory.Exists(managed))
                throw new DirectoryNotFoundException(
                    "The selected TIA-Guard project source does not exist.");
            if (Directory.Exists(managed))
                RoundTripBuildInput.LoadSource(managed);

            var parent = Path.GetDirectoryName(managed);
            if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
                throw new DirectoryNotFoundException(
                    "The managed source parent directory does not exist.");
            FileSystemSafety.RequirePlainAncestors(parent);

            var id = Guid.NewGuid().ToString("N");
            var incoming = Path.Combine(parent, ".tia-source.tia-guard-new-" + id);
            var backup = Path.Combine(parent, ".tia-source.tia-guard-old-" + id);
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
                    if (movedExisting && Directory.Exists(backup) &&
                        !Directory.Exists(managed))
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

        private static void TryDeleteEmptyDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path) &&
                    !Directory.EnumerateFileSystemEntries(path).Any())
                    Directory.Delete(path);
            }
            catch
            {
                // Best-effort cleanup only; the managed source transaction already failed safely.
            }
        }
    }
}
