using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TiaGuard.Openness
{
    public static class AiEngineeringPublisher
    {
        // The caller chooses exactly one canonical slot, never an arbitrary output path.
        // Canonical validation and rendering finish before any ai directory is touched.
        public static string Generate(string sourceRoot)
        {
            var source = Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(Path.GetFileName(source), "tia-source", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("AI generation requires a directory named tia-source; output is its ai sibling.");
            var files = AiEngineeringRenderer.Render(source);
            var parent = Path.GetDirectoryName(source);
            FileSystemSafety.RequirePlainAncestors(parent);
            var target = Path.Combine(parent, "ai");
            var lockPath = Path.Combine(parent, ".ai.tia-guard.lock");
            // CreateNew means another generator (or an unknown lock) is never overwritten/deleted.
            using (var gate = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                1, FileOptions.DeleteOnClose))
            {
                CheckExisting(target, new HashSet<string>(files.Keys, StringComparer.Ordinal));
                var token = Guid.NewGuid().ToString("N");
                var incoming = Path.Combine(parent, ".ai.tia-guard-new-" + token);
                var backup = Path.Combine(parent, ".ai.tia-guard-old-" + token);
                var moved = false;
                try
                {
                    Directory.CreateDirectory(incoming);
                    foreach (var file in files)
                    {
                        var path = Path.Combine(incoming, file.Key.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        File.WriteAllText(path, file.Value, new UTF8Encoding(false));
                    }
                    if (Directory.Exists(target)) { Directory.Move(target, backup); moved = true; }
                    Directory.Move(incoming, target);
                }
                catch
                {
                    if (moved && !Directory.Exists(target)) Directory.Move(backup, target);
                    FileSystemSafety.DeleteOwnedTree(incoming);
                    throw;
                }
                // After publication, never roll back from a potentially partially cleaned backup.
                if (moved) FileSystemSafety.DeleteOwnedTree(backup);
            }
            return target;
        }

        private static void CheckExisting(string target, ISet<string> managed)
        {
            FileSystemSafety.RequirePlainAncestors(target);
            if (File.Exists(target)) throw new IOException("ai exists as a file.");
            if (!Directory.Exists(target)) return;
            CheckDirectory(new DirectoryInfo(target), target, managed);
        }

        private static void CheckDirectory(DirectoryInfo directory, string root, ISet<string> managed)
        {
            foreach (var entry in directory.GetFileSystemInfos())
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("AI output contains a reparse point.");
                var relative = entry.FullName.Substring(root.Length + 1).Replace(Path.DirectorySeparatorChar, '/');
                if (entry is DirectoryInfo child)
                {
                    if (!managed.Any(name => name.StartsWith(relative + "/", StringComparison.Ordinal)))
                        throw new IOException("AI output contains an unmanaged directory; preserve it before regeneration.");
                    CheckDirectory(child, root, managed);
                }
                else if (!managed.Contains(relative))
                    throw new IOException("AI output contains an unmanaged file; preserve it before regeneration.");
            }
        }
    }
}
