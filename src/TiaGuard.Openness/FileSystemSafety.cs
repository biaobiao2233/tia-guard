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

        internal static void CopyPlainTree(string sourcePath, string destinationPath)
        {
            var source = Path.GetFullPath(sourcePath).TrimEnd(Path.DirectorySeparatorChar);
            var destination = Path.GetFullPath(destinationPath).TrimEnd(Path.DirectorySeparatorChar);
            if (!Directory.Exists(source))
                throw new DirectoryNotFoundException("The source directory does not exist.");
            if (Directory.Exists(destination) || File.Exists(destination))
                throw new IOException("The destination path already exists.");

            RequirePlainAncestors(source);
            RequirePlainAncestors(Path.GetDirectoryName(destination));
            CopyDirectory(new DirectoryInfo(source), new DirectoryInfo(destination));
        }

        private static void CopyDirectory(DirectoryInfo source, DirectoryInfo destination)
        {
            if ((source.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Refusing to copy a directory through a reparse point.");
            destination.Create();
            foreach (var file in source.GetFiles())
            {
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Refusing to copy a file through a reparse point.");
                file.CopyTo(Path.Combine(destination.FullName, file.Name), false);
            }
            foreach (var child in source.GetDirectories())
                CopyDirectory(child, new DirectoryInfo(Path.Combine(destination.FullName, child.Name)));
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
