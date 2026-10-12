using System.Text.RegularExpressions;

namespace TwinCat3.Engineering;

/// <summary>Checks every existing ancestor; a new output alone is not a safe path.</summary>
public static partial class EngineeringPaths
{
    private static readonly StringComparison Comparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static string RequireUnder(string path, string allocation, bool mustExist = false)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(allocation)
            || !Path.IsPathFullyQualified(path) || !Path.IsPathFullyQualified(allocation))
            throw new ArgumentException("An absolute path and allocated root are required.");
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || allocation.StartsWith(@"\\", StringComparison.Ordinal))
            throw new ArgumentException("Network paths are outside the offline allocation.");
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(allocation));
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("The allocated root does not exist.");
        if (full.Equals(root, Comparison)
            || !full.StartsWith(root + Path.DirectorySeparatorChar, Comparison))
            throw new ArgumentException("The path must be below, and distinct from, the allocated root.");
        CheckAncestors(root);
        CheckAncestors(full);
        if (mustExist && !Directory.Exists(full) && !File.Exists(full))
            throw new FileNotFoundException("The allocated path does not exist.", full);
        return full;
    }

    public static string RequireNewOutput(string path, string allocation)
    {
        string full = RequireUnder(path, allocation);
        if (Directory.Exists(full) || File.Exists(full))
            throw new IOException("The output must be a new destination.");
        string? parent = Path.GetDirectoryName(full);
        if (parent is null || !Directory.Exists(parent))
            throw new DirectoryNotFoundException("The output parent must already exist.");
        return full;
    }

    public static string RequireSourceFile(string relativePath, string sourceDirectory)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)
            || relativePath.Contains(':') || relativePath.Contains('\\'))
            throw new ArgumentException("Source paths must be relative POSIX paths.");
        string[] parts = relativePath.Split('/');
        if (parts.Any(p => p is "" or "." or ".." || p.EndsWith(' ') || p.EndsWith('.')))
            throw new ArgumentException("Source paths cannot contain traversal or ambiguous segments.");
        string full = RequireUnder(Path.Combine(sourceDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)),
            sourceDirectory, true);
        if (!File.Exists(full)) throw new FileNotFoundException("A regular source file is required.", full);
        return full;
    }

    public static void RequireDistinctTrees(params string[] directories)
    {
        for (int i = 0; i < directories.Length; i++)
            for (int j = i + 1; j < directories.Length; j++)
            {
                string a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directories[i]));
                string b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directories[j]));
                if (a.Equals(b, Comparison) || a.StartsWith(b + Path.DirectorySeparatorChar, Comparison)
                    || b.StartsWith(a + Path.DirectorySeparatorChar, Comparison))
                    throw new ArgumentException("Input, staging and output trees must be separate.");
            }
    }

    public static void CheckTree(string directory)
    {
        CheckAncestors(directory);
        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
        {
            CheckAncestors(path);
            if (Directory.Exists(path)) CheckTree(path);
        }
    }

    public static string RequireIdentifier(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 64 || !IdentifierRegex().IsMatch(value)
            || ReservedNames.Contains(value))
            throw new ArgumentException("A non-reserved IEC identifier of at most 64 characters is required.");
        return value;
    }

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        "PROGRAM", "FUNCTION", "FUNCTION_BLOCK", "METHOD", "TYPE", "END_TYPE", "STRUCT", "END_STRUCT",
        "VAR", "VAR_GLOBAL", "END_VAR", "IF", "THEN", "ELSE", "END_IF", "TRUE", "FALSE"
    };

    private static void CheckAncestors(string path)
    {
        string? current = Path.GetFullPath(path);
        while (current is not null)
        {
            if (File.Exists(current) || Directory.Exists(current))
            {
                FileAttributes attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Reparse points and links are outside the allocation.");
            }
            current = Path.GetDirectoryName(current);
        }
    }

    [GeneratedRegex(@"\A[A-Za-z_][A-Za-z0-9_]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();
}
