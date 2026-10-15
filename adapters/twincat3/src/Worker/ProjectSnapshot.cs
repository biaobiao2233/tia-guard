using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using TwinCat3.Engineering;

namespace TwinCat3.Worker;

/// <summary>A bounded complete project-tree snapshot; no original file is opened through COM.</summary>
public sealed class ProjectSnapshot
{
    private readonly Dictionary<string, byte[]> files;
    public string Root { get; }
    public string SolutionRelativePath { get; }
    public string ProjectRelativePath { get; }
    public string TreeHash { get; }
    public int FileCount => files.Count;
    public long ByteCount => files.Values.Sum(f => (long)f.Length);
    private ProjectSnapshot(string root, string solution, string project, Dictionary<string, byte[]> files, string hash)
    { Root = root; SolutionRelativePath = solution; ProjectRelativePath = project; this.files = files; TreeHash = hash; }

    public static ProjectSnapshot Capture(string projectPath, EngineeringAllocation allocation)
    {
        string path = EngineeringPaths.RequireUnder(projectPath, allocation.WorkspaceRoot, true);
        if (!File.Exists(path)) throw new InvalidDataException("An existing solution or TwinCAT project file is required.");
        string? solution = null;
        if (Path.GetExtension(path).Equals(".sln", StringComparison.OrdinalIgnoreCase)) solution = path;
        else if (Path.GetExtension(path).Equals(".tsproj", StringComparison.OrdinalIgnoreCase))
        {
            string? directory = Path.GetDirectoryName(path);
            for (int depth = 0; directory is not null && depth < 3; depth++, directory = Path.GetDirectoryName(directory))
            {
                if (directory.Equals(allocation.WorkspaceRoot, StringComparison.OrdinalIgnoreCase)) break;
                EngineeringPaths.RequireUnder(directory, allocation.WorkspaceRoot, true);
                var candidates = Directory.EnumerateFiles(directory, "*.sln").ToArray();
                if (candidates.Length > 1) throw new InvalidDataException("Ambiguous solution root.");
                if (candidates.Length == 1) { solution = candidates[0]; break; }
            }
        }
        if (solution is null) throw new InvalidDataException("A local solution root is required for an owned copy.");
        string root = Path.GetDirectoryName(solution)!;
        EngineeringPaths.RequireDistinctTrees(root, allocation.ScratchRoot);
        EngineeringPaths.CheckTree(root);
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(f => Path.GetRelativePath(root, f).Replace('\\', '/'), StringComparer.Ordinal))
        {
            EngineeringPaths.RequireUnder(file, root, true);
            if (files.Count >= 10000 || new FileInfo(file).Length > 32 * 1024 * 1024)
                throw new InvalidDataException("Owned project snapshot limit exceeded.");
            byte[] bytes = File.ReadAllBytes(file);
            total += bytes.LongLength;
            if (total > 128 * 1024 * 1024) throw new InvalidDataException("Owned project snapshot limit exceeded.");
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (!files.TryAdd(relative, bytes)) throw new InvalidDataException("Ambiguous project filename.");
            byte[] name = Encoding.UTF8.GetBytes(relative);
            byte[] length = new byte[8];
            System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(length, name.LongLength);
            hash.AppendData(length); hash.AppendData(name); hash.AppendData(SHA256.HashData(bytes));
            if (Path.GetExtension(file).Equals(".tsproj", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(file).Equals(".plcproj", StringComparison.OrdinalIgnoreCase)) CheckXmlReferences(file, bytes, root);
        }
        string solutionText = new UTF8Encoding(false, true).GetString(files[Path.GetRelativePath(root, solution).Replace('\\', '/')]);
        var projects = Regex.Matches(solutionText, "(?m)^Project\\([^\\r\\n]*\\)\\s*=\\s*\"[^\"]*\",\\s*\"([^\"]+)\"")
            .Select(m => ResolveReference(solution, m.Groups[1].Value, root)).ToArray();
        if (projects.Length != 1 || !Path.GetExtension(projects[0]).Equals(".tsproj", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The owned-copy slice requires exactly one local TwinCAT configuration project.");
        if (Path.GetExtension(path).Equals(".tsproj", StringComparison.OrdinalIgnoreCase) && !path.Equals(projects[0], StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The selected project is not the solution project.");
        string project = Path.GetRelativePath(root, projects[0]).Replace('\\', '/');
        if (!files.ContainsKey(project)) throw new InvalidDataException("The selected project is outside the frozen tree.");
        return new(root, Path.GetRelativePath(root, solution).Replace('\\', '/'), project, files,
            Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    private static void CheckXmlReferences(string file, byte[] bytes, string root)
    {
        using var reader = XmlReader.Create(new MemoryStream(bytes), new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 32 * 1024 * 1024 });
        var document = XDocument.Load(reader);
        var referenceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Include", "File", "FileName", "PrjFilePath", "ProjectFile", "ProjectPath", "SourceFile", "ImportFile" };
        foreach (var element in document.Descendants())
        {
            foreach (var attribute in element.Attributes())
                if ((referenceNames.Contains(attribute.Name.LocalName) || attribute.Name.LocalName.EndsWith("FilePath", StringComparison.OrdinalIgnoreCase)) && LooksLikeFile(attribute.Value))
                    ResolveReference(file, attribute.Value, root);
            if ((referenceNames.Contains(element.Name.LocalName) || element.Name.LocalName.EndsWith("FilePath", StringComparison.OrdinalIgnoreCase)) && !element.HasElements && LooksLikeFile(element.Value))
                ResolveReference(file, element.Value, root);
            if (element.Name.LocalName == "Import")
                throw new InvalidDataException("Custom MSBuild imports are outside the owned-copy slice.");
        }
    }
    private static bool LooksLikeFile(string value) => value.Contains('/') || value.Contains('\\') || value.Contains(':') ||
        Regex.IsMatch(value, @"\.(?:tsproj|plcproj|TcPOU|TcGVL|TcDUT|TcTTO|TcIO|tmc|tmi|xml|dll|exe|targets|props)\z", RegexOptions.IgnoreCase);
    private static string ResolveReference(string file, string value, string root)
    {
        if (value.Contains("$(", StringComparison.Ordinal) || value.Contains('*') || Path.IsPathRooted(value) || value.Contains(':'))
            throw new InvalidDataException("External, wildcard or evaluated project references are outside the owned-copy slice.");
        string path = EngineeringPaths.RequireUnder(Path.Combine(Path.GetDirectoryName(file)!, value.Replace('\\', Path.DirectorySeparatorChar)), root, true);
        return path;
    }

    public void CopyTo(string destination, EngineeringAllocation allocation)
    {
        EngineeringPaths.RequireUnder(destination, allocation.ScratchRoot);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("The owned copy must be new.");
        Directory.CreateDirectory(destination);
        foreach (var (relative, bytes) in files)
        {
            string target = EngineeringPaths.RequireUnder(Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar)), destination);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(bytes);
        }
        if (EngineeringPipeline.HashTree(destination) != TreeHash) throw new InvalidDataException("The owned copy does not match the frozen project tree.");
    }
}
