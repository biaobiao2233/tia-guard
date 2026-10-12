using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TwinCat3.Contracts;

public static class GenerationInputLoader
{
    public const string ManifestFileName = "twincat-generation.json";
    private static readonly Regex Identifier = new("^[A-Za-z_][A-Za-z0-9_]{0,127}$", RegexOptions.CultureInvariant);
    private static readonly Regex PinnedVersion = new("^[0-9]+(?:\\.[0-9]+){1,3}$", RegexOptions.CultureInvariant);

    public static FrozenGenerationInput Load(string inputDirectory)
    {
        var root = Path.GetFullPath(inputDirectory);
        if (!Directory.Exists(root)) throw new InvalidDataException("Input directory does not exist.");
        RequireNoReparseComponents(root);
        var manifestPath = Path.Combine(root, ManifestFileName);
        var manifestSource = ReadFile(root, ManifestFileName);
        using var document = JsonDocument.Parse(manifestSource.Text);
        RejectDuplicateKeys(document.RootElement);
        GenerationManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<GenerationManifest>(manifestSource.Text, ContractJson.Options)
                ?? throw new InvalidDataException("Manifest is null.");
        }
        catch (JsonException ex) { throw new InvalidDataException("Invalid generation manifest: " + ex.Message, ex); }
        ValidateManifest(manifest);
        var files = new Dictionary<string, FrozenSourceFile>(StringComparer.OrdinalIgnoreCase)
        { [ManifestFileName] = manifestSource };
        foreach (var artifact in manifest.Artifacts)
        {
            AddSource(artifact.DeclarationFile, true);
            if (artifact.ImplementationFile is { } implementation) AddSource(implementation, false);
        }
        var actual = EnumerateFilesWithoutLinks(root).Select(p => Path.GetRelativePath(root, p).Replace('\\', '/')).ToArray();
        if (actual.Distinct(StringComparer.OrdinalIgnoreCase).Count() != actual.Length)
            throw new InvalidDataException("Case-colliding source paths.");
        if (actual.Length != files.Count || actual.Any(p => !files.ContainsKey(p)))
            throw new InvalidDataException("Input file set must exactly match the manifest and referenced sources.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files.Values.OrderBy(f => f.RelativePath, StringComparer.Ordinal))
        {
            AppendLengthPrefixed(hash, Encoding.UTF8.GetBytes(file.RelativePath));
            AppendLengthPrefixed(hash, file.Bytes);
        }
        return new FrozenGenerationInput(manifest, files,
            Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), root);

        void AddSource(string relativePath, bool nonempty)
        {
            ValidateRelativePath(relativePath);
            if (string.Equals(relativePath, ManifestFileName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Manifest cannot be used as PLC source.");
            if (files.TryGetValue(relativePath, out var previous))
            {
                if (previous.RelativePath != relativePath) throw new InvalidDataException("Case-colliding source reference.");
                return;
            }
            var file = ReadFile(root, relativePath);
            if (nonempty && string.IsNullOrWhiteSpace(file.Text))
                throw new InvalidDataException("Declaration source is empty: " + relativePath);
            files.Add(relativePath, file);
        }
    }

    public static void ValidateManifest(GenerationManifest manifest)
    {
        if (manifest.SchemaVersion != "1.0") throw new InvalidDataException("Unsupported schemaVersion.");
        Name(manifest.SolutionName); Name(manifest.ConfigurationName); Name(manifest.PlcProjectName);
        if (manifest.Environment is null || manifest.Artifacts is null || manifest.Tasks is null || manifest.Libraries is null)
            throw new InvalidDataException("Environment and collections must not be null.");
        var environment = manifest.Environment;
        if (environment.Provider != "dte")
            throw new InvalidDataException("Generation v1 requires the proven DTE creation path; standalone generation is not qualified.");
        if (!Regex.IsMatch(environment.DteProgId ?? "", "^TcXaeShell\\.DTE\\.[0-9]+\\.0$"))
            throw new InvalidDataException("Generation requires an explicitly installed TcXaeShell DTE ProgID.");
        if (environment.TargetPlatform != "TwinCAT RT (x64)" || environment.BuildConfiguration is not ("Debug" or "Release"))
            throw new InvalidDataException("Unsupported platform/build configuration.");
        if (environment.PlcTemplate != "Empty PLC Template")
            throw new InvalidDataException("Generation v1 uses Empty PLC Template with explicit objects.");
        if (environment.RequestedBuild is not null && !Regex.IsMatch(environment.RequestedBuild, "^(?:3\\.1\\.)?[0-9]{4}\\.[0-9]+$"))
            throw new InvalidDataException("RequestedBuild must identify an exact TwinCAT build.");
        if (manifest.Artifacts.Count == 0 || manifest.Artifacts.Count > 256 || manifest.Tasks.Count == 0 || manifest.Tasks.Count > 32)
            throw new InvalidDataException("A bounded nonempty artifact/task set is required.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var artifact in manifest.Artifacts)
        {
            if (artifact is null) throw new InvalidDataException("Null artifact.");
            Name(artifact.Name);
            if (string.IsNullOrWhiteSpace(artifact.Id) || !Regex.IsMatch(artifact.Id, "^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$")
                || !ids.Add(artifact.Id) || !names.Add(artifact.Name))
                throw new InvalidDataException("Invalid or duplicate artifact identity/name.");
            if (!Enum.IsDefined(artifact.Kind)) throw new InvalidDataException("Unknown artifact kind.");
            ValidateRelativePath(artifact.DeclarationFile);
            var executable = artifact.Kind is ArtifactKind.Program or ArtifactKind.Function or ArtifactKind.FunctionBlock;
            if (executable && string.IsNullOrWhiteSpace(artifact.ImplementationFile))
                throw new InvalidDataException("Executable artifact requires implementationFile.");
            if (!executable && artifact.ImplementationFile is not null)
                throw new InvalidDataException("Declaration-only artifact cannot have an implementation.");
            if (artifact.ImplementationFile is not null) ValidateRelativePath(artifact.ImplementationFile);
        }
        if (!manifest.Artifacts.Any(a => a.Kind == ArtifactKind.Program && a.Name == "MAIN"))
            throw new InvalidDataException("An explicit MAIN Program is required.");
        var tasks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scheduled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var task in manifest.Tasks)
        {
            if (task is null) throw new InvalidDataException("Null task.");
            Name(task.Name);
            if (!tasks.Add(task.Name) || task.CycleTimeUs <= 0 || task.CycleTimeUs > uint.MaxValue / 10
                || task.Priority is < 1 or > 99 || task.Programs is null || task.Programs.Count != 1)
                throw new InvalidDataException("Invalid task; v1 requires one program call per task.");
            foreach (var program in task.Programs)
            {
                var referenced = manifest.Artifacts.SingleOrDefault(a => a.Name.Equals(program, StringComparison.OrdinalIgnoreCase));
                if (referenced is null || referenced.Kind != ArtifactKind.Program || !scheduled.Add(referenced.Name))
                    throw new InvalidDataException("Task must reference one unique declared Program.");
            }
        }
        if (!scheduled.Contains("MAIN")) throw new InvalidDataException("MAIN is not scheduled by a periodic task.");
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var library in manifest.Libraries)
        {
            if (library is null || string.IsNullOrWhiteSpace(library.Name) || string.IsNullOrWhiteSpace(library.Vendor)
                || !PinnedVersion.IsMatch(library.Version ?? "") || !libraries.Add(library.Name)
                || library.Name.Any(char.IsControl) || library.Vendor.Any(char.IsControl))
                throw new InvalidDataException("Libraries require unique names, explicit numeric versions and vendors.");
        }
    }

    public static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 240 || path.Contains('\\') || path.Contains(':')
            || path.StartsWith('/') || path.Any(char.IsControl)
            || path.Split('/').Any(p => p is "" or "." or ".." || p.EndsWith('.') || p.EndsWith(' ')))
            throw new InvalidDataException("Unsafe relative source path: " + path);
    }

    public static void RequireNoReparseComponents(string fullPath)
    {
        var current = new DirectoryInfo(Path.GetFullPath(fullPath));
        while (current is not null)
        {
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Reparse/symlink path is not allowed: " + current.FullName);
            current = current.Parent;
        }
        if (File.Exists(fullPath) && (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Symlink source file is not allowed.");
    }

    private static void Name(string? value)
    {
        if (value is null || !Identifier.IsMatch(value)) throw new InvalidDataException("Unsupported engineering identifier.");
    }

    private static FrozenSourceFile ReadFile(string root, string relativePath)
    {
        ValidateRelativePath(relativePath);
        var path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Source escaped input directory.");
        RequireNoReparseComponents(path);
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > 16 * 1024 * 1024) throw new InvalidDataException("Missing/oversized source: " + relativePath);
        return new FrozenSourceFile(relativePath, File.ReadAllBytes(path));
    }

    private static IEnumerable<string> EnumerateFilesWithoutLinks(string root)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked input entry is not allowed.");
            if (Directory.Exists(entry))
                foreach (var file in EnumerateFilesWithoutLinks(entry)) yield return file;
            else yield return entry;
        }
    }

    private static void RejectDuplicateKeys(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw new InvalidDataException("Duplicate manifest JSON property: " + property.Name);
                RejectDuplicateKeys(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) RejectDuplicateKeys(item);
    }

    private static void AppendLengthPrefixed(IncrementalHash hash, byte[] bytes)
    {
        Span<byte> length = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(length, bytes.LongLength);
        hash.AppendData(length); hash.AppendData(bytes);
    }
}
