using System.IO;

namespace TiaGuard.Openness
{
    internal static class FileSystemSafety
    {
        internal static void RequirePlainAncestors(string path)
        {
            for (var item = new DirectoryInfo(path); item != null; item = item.Parent)
                if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("A source or scratch path contains a reparse point.");
        }

        internal static void RequirePlainFile(string path)
        {
            RequirePlainAncestors(Path.GetDirectoryName(path));
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("An artifact is a reparse point.");
        }

        // Call only after the caller's ownership check. Refuse redirected cleanup,
        // including a reparse-point child, rather than traversing an unknown tree.
        internal static void DeleteOwnedTree(string path)
        {
            RequirePlainAncestors(path);
            if (!Directory.Exists(path)) return;
            CheckChildren(new DirectoryInfo(path));
            Directory.Delete(path, recursive: true);
        }

        private static void CheckChildren(DirectoryInfo directory)
        {
            foreach (var entry in directory.GetFileSystemInfos())
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Refusing cleanup of a scratch tree containing a reparse point.");
                if (entry is DirectoryInfo child) CheckChildren(child);
            }
        }
    }
}
