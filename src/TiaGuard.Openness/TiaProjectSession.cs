using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;

namespace TiaGuard.Openness
{
    public sealed class ProjectInfo
    {
        public string Name { get; internal set; }
        public string Path { get; internal set; }
        public string TiaVersion { get; internal set; }
        public string TiaBuild { get; internal set; }
        public string ProjectVersion { get; internal set; }
        public string SourceKind { get; internal set; }
        public int? ProcessId { get; internal set; }
    }

    public sealed class SnapshotCollectionOptions
    {
        // Optional and explicit. Null leaves all block exports not attempted.
        public string BlockExportDirectory { get; set; }
    }

    public sealed class PlcCompileObservation
    {
        public int Errors { get; internal set; }
        public int Warnings { get; internal set; }
    }

    public sealed class BridgeTagState
    {
        public string TableName { get; internal set; }
        public bool TableIsDefault { get; internal set; }
        public bool Exists { get; internal set; }
        public string Name { get; internal set; }
        public string DataType { get; internal set; }
        public string LogicalAddress { get; internal set; }
    }

    public sealed class BridgePublishResult
    {
        public string ProjectDirectory { get; internal set; }
        public string ProjectFile { get; internal set; }
        public int CompileErrors { get; internal set; }
        public int CompileWarnings { get; internal set; }
    }

    public sealed class TiaProjectSession : IDisposable
    {
        private readonly TiaPortal _portal;
        private readonly Project _project;
        private readonly string _sourcePath;
        private readonly string _scratchDirectory;
        private readonly string _version;
        private readonly int? _processId;
        private bool _disposed;

        private TiaProjectSession(TiaPortal portal, Project project, string sourcePath,
            string scratchDirectory, string version, int? processId)
        {
            _portal = portal;
            _project = project;
            _sourcePath = sourcePath;
            _scratchDirectory = scratchDirectory;
            _version = version;
            _processId = processId;
        }

        // Attaches to exactly one V21 process with an open project. Supply the PID when
        // more than one such process exists; the adapter never guesses between projects.
        public static TiaProjectSession Attach(int? processId = null)
        {
            OpennessAccess.RequireAccess();
            OpennessRuntime.Initialize();
            return AttachCore(processId);
        }

        private static TiaProjectSession AttachCore(int? processId)
        {
            var candidates = TiaPortal.GetProcesses()
                .Where(IsV21)
                .Where(process => process.ProjectPath != null)
                .Where(process => !processId.HasValue || process.Id == processId.Value)
                .ToList();
            if (candidates.Count == 0)
                throw new InvalidOperationException("No TIA Portal V21 process with an open project matches the requested PID.");
            if (candidates.Count > 1)
                throw new InvalidOperationException("More than one TIA Portal V21 process has an open project; specify a PID.");

            var selected = candidates[0];
            var portal = selected.Attach();
            try
            {
                var projects = portal.Projects.ToList();
                var matching = projects.Where(project =>
                    string.Equals(project.Path?.FullName, selected.ProjectPath.FullName,
                        StringComparison.OrdinalIgnoreCase)).ToList();
                var projectToRead = matching.Count == 1 ? matching[0]
                    : projects.Count == 1 ? projects[0] : null;
                if (projectToRead == null)
                    throw new InvalidOperationException("The attached process does not identify one unambiguous open project.");
                return new TiaProjectSession(portal, projectToRead, null, null,
                    GetVersion(selected), selected.Id);
            }
            catch
            {
                portal.Dispose();
                throw;
            }
        }

        // Siemens V21 has Primary/Secondary open modes, not a read-only mode.
        // Copy the complete offline project folder before opening, so TIA never opens
        // the supplied original. The copy is removed when this session is disposed.
        public static TiaProjectSession OpenOfflineCopy(string sourceProjectFile)
        {
            OpennessAccess.RequireAccess();
            OpennessRuntime.Initialize();
            return OpenOfflineCopyCore(sourceProjectFile);
        }

        private static TiaProjectSession OpenOfflineCopyCore(string sourceProjectFile)
        {
            if (string.IsNullOrWhiteSpace(sourceProjectFile))
                throw new ArgumentException("An offline .ap21 project file is required.", nameof(sourceProjectFile));
            var sourcePath = Path.GetFullPath(sourceProjectFile);
            if (!string.Equals(Path.GetExtension(sourcePath), ".ap21", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Only TIA Portal V21 .ap21 projects are supported.", nameof(sourceProjectFile));
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException("The offline project file does not exist.", sourcePath);

            var sourceFolder = Path.GetDirectoryName(sourcePath);
            var scratchParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "TiaGuard.Openness"));
            RequireNoReparseAncestors(sourceFolder);
            RequireNoReparseAncestors(scratchParent);
            if (scratchParent.StartsWith(sourceFolder.TrimEnd(Path.DirectorySeparatorChar) +
                    Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(scratchParent, sourceFolder, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The source project folder encloses the scratch location; use a dedicated offline project folder.");
            var scratchDirectory = Path.Combine(scratchParent,
                Guid.NewGuid().ToString("N"));
            TiaPortal portal = null;
            try
            {
                CopyDirectory(sourceFolder, scratchDirectory);
                portal = new TiaPortal(TiaPortalMode.WithoutUserInterface);
                var copiedProject = new FileInfo(Path.Combine(scratchDirectory, Path.GetFileName(sourcePath)));
                var project = portal.Projects.Open(copiedProject);
                return new TiaProjectSession(portal, project, sourcePath, scratchDirectory, "V21", null);
            }
            catch
            {
                try { portal?.Dispose(); }
                finally { DeleteOwnedScratchDirectory(scratchDirectory); }
                throw;
            }
        }

        public ProjectInfo ReadProjectInfo()
        {
            ThrowIfDisposed();
            return new ProjectInfo
            {
                Name = _project.Name,
                Path = _sourcePath ?? _project.Path?.FullName,
                TiaVersion = _version,
                TiaBuild = GetApiBuild(),
                ProjectVersion = ReadProjectVersion(),
                SourceKind = _scratchDirectory == null ? "attached-session" : "offline-copy",
                ProcessId = _processId
            };
        }

        public SnapshotV1 ReadSnapshot(SnapshotCollectionOptions options = null)
        {
            ThrowIfDisposed();
            options = options ?? new SnapshotCollectionOptions();
            if (options.BlockExportDirectory != null)
            {
                var target = Path.GetFullPath(options.BlockExportDirectory);
                var sourceFolder = Path.GetDirectoryName(Path.GetFullPath(_sourcePath ?? _project.Path.FullName));
                RequireNoReparseAncestors(target);
                RequireNoReparseAncestors(sourceFolder);
                if (IsInsideOrEqual(target, sourceFolder) ||
                    (_scratchDirectory != null && IsInsideOrEqual(target, _scratchDirectory)))
                    throw new InvalidOperationException("Block export output must be outside the TIA project folder.");
                options = new SnapshotCollectionOptions { BlockExportDirectory = target };
            }
            return SnapshotExtractor.Extract(_project, ReadProjectInfo(), options);
        }

        public RoundTripManifestV1 ExportRoundTripSource(
            string outputDirectory,
            Action<string> progress = null)
        {
            ThrowIfDisposed();
            if (_scratchDirectory == null)
                throw new InvalidOperationException("Round-trip export requires an offline/disposable project copy.");
            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new ArgumentException("A round-trip output directory is required.", nameof(outputDirectory));

            var target = Path.GetFullPath(outputDirectory);
            var sourceFolder = Path.GetDirectoryName(Path.GetFullPath(_sourcePath ?? _project.Path.FullName));
            RequireNoReparseAncestors(target);
            RequireNoReparseAncestors(sourceFolder);
            if (IsInsideOrEqual(target, sourceFolder) ||
                (_scratchDirectory != null && IsInsideOrEqual(target, _scratchDirectory)))
                throw new InvalidOperationException("Round-trip output must be outside the TIA project folder.");

            return RoundTripSourceExporter.Export(_project, ReadProjectInfo(), target, progress);
        }

        // Bridge v0 write scope is intentionally narrow: root tag tables in a
        // disposable offline project copy only. Attached user projects remain
        // read-only until a separate live-engineering write contract is accepted.
        public BridgeTagState ReadRootTagStateForBridge(string tableName, string tagName)
        {
            ThrowIfDisposed();
            RequireBridgeTagName(tableName, nameof(tableName));
            RequireBridgeTagName(tagName, nameof(tagName));

            var plc = RequireSinglePlcSoftwareForBridge();
            var table = plc.TagTableGroup.TagTables.Find(tableName);
            if (table == null)
                throw new InvalidOperationException(
                    "The root PLC tag table '" + tableName + "' was not found.");

            var tag = table.Tags.Find(tagName);
            return new BridgeTagState
            {
                TableName = table.Name,
                TableIsDefault = table.IsDefault,
                Exists = tag != null,
                Name = tag?.Name,
                DataType = tag?.DataTypeName,
                LogicalAddress = tag?.LogicalAddress
            };
        }

        public BridgeTagState UpsertRootTagForBridge(
            string tableName,
            string tagName,
            string dataType,
            string logicalAddress)
        {
            ThrowIfDisposed();
            if (_scratchDirectory == null)
                throw new InvalidOperationException(
                    "Bridge engineering writes are currently allowed only on an offline disposable project copy.");

            RequireBridgeTagName(tableName, nameof(tableName));
            RequireBridgeTagName(tagName, nameof(tagName));
            RequireBridgeTagName(dataType, nameof(dataType));
            if (logicalAddress == null)
                throw new ArgumentNullException(nameof(logicalAddress));

            var plc = RequireSinglePlcSoftwareForBridge();
            var table = plc.TagTableGroup.TagTables.Find(tableName);
            if (table == null)
                throw new InvalidOperationException(
                    "The root PLC tag table '" + tableName + "' was not found.");

            var tag = table.Tags.Find(tagName);
            if (tag == null)
                tag = table.Tags.Create(tagName, dataType, logicalAddress);
            else
            {
                tag.DataTypeName = dataType;
                tag.LogicalAddress = logicalAddress;
            }

            return new BridgeTagState
            {
                TableName = table.Name,
                TableIsDefault = table.IsDefault,
                Exists = true,
                Name = tag.Name,
                DataType = tag.DataTypeName,
                LogicalAddress = tag.LogicalAddress
            };
        }

        public void RestoreRootTagForBridge(
            string tableName,
            string tagName,
            bool existed,
            string dataType,
            string logicalAddress)
        {
            ThrowIfDisposed();
            if (_scratchDirectory == null)
                throw new InvalidOperationException(
                    "Bridge engineering writes are currently allowed only on an offline disposable project copy.");
            RequireBridgeTagName(tableName, nameof(tableName));
            RequireBridgeTagName(tagName, nameof(tagName));

            var plc = RequireSinglePlcSoftwareForBridge();
            var table = plc.TagTableGroup.TagTables.Find(tableName);
            if (table == null)
                throw new InvalidOperationException(
                    "The root PLC tag table '" + tableName + "' was not found.");
            var tag = table.Tags.Find(tagName);
            if (!existed)
            {
                if (tag != null)
                    tag.Delete();
                return;
            }

            if (string.IsNullOrWhiteSpace(dataType) || logicalAddress == null)
                throw new InvalidOperationException("The prior tag state required for rollback is incomplete.");
            if (tag == null)
                table.Tags.Create(tagName, dataType, logicalAddress);
            else
            {
                tag.DataTypeName = dataType;
                tag.LogicalAddress = logicalAddress;
            }
        }

        public BridgePublishResult PublishOfflineCopyForBridge(
            string outputDirectory,
            string outputName)
        {
            ThrowIfDisposed();
            if (_scratchDirectory == null)
                throw new InvalidOperationException(
                    "Bridge publishing is allowed only from a disposable offline project copy.");
            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new ArgumentException(
                    "An output directory is required.", nameof(outputDirectory));
            RequireBridgeOutputName(outputName);

            var outputRoot = Path.GetFullPath(outputDirectory);
            if (!Directory.Exists(outputRoot))
                throw new DirectoryNotFoundException(
                    "The Bridge publish output directory does not exist: " + outputRoot);

            var publishDirectory = Path.GetFullPath(Path.Combine(outputRoot, outputName));
            var sourceFolder = Path.GetDirectoryName(Path.GetFullPath(_sourcePath));
            RequireNoReparseAncestors(outputRoot);
            RequireNoReparseAncestors(sourceFolder);
            RequireNoReparseAncestors(_scratchDirectory);

            if (IsInsideOrEqual(publishDirectory, _scratchDirectory))
                throw new InvalidOperationException(
                    "Bridge publish output must be outside the disposable TIA scratch directory.");
            if (IsInsideOrEqual(publishDirectory, sourceFolder))
                throw new InvalidOperationException(
                    "Bridge publish output must be outside the original project folder.");
            if (Directory.Exists(publishDirectory) || File.Exists(publishDirectory))
                throw new InvalidOperationException(
                    "Bridge publish refuses to overwrite an existing output path: " +
                    publishDirectory);

            var compile = CompilePlcForVerification();
            if (compile.Errors != 0)
                throw new InvalidOperationException(
                    "Bridge publish is blocked because the disposable project has compile errors: " +
                    compile.Errors);

            _project.SaveAs(new DirectoryInfo(publishDirectory));

            var activeProjectPath = _project.Path?.FullName;
            var discovered = Directory.Exists(publishDirectory)
                ? Directory.GetFiles(
                    publishDirectory, "*.ap21", SearchOption.AllDirectories)
                : Array.Empty<string>();
            var matching = string.IsNullOrWhiteSpace(activeProjectPath)
                ? Array.Empty<string>()
                : discovered
                    .Where(path => string.Equals(
                        Path.GetFullPath(path),
                        Path.GetFullPath(activeProjectPath),
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray();

            if (matching.Length != 1)
                throw new InvalidOperationException(
                    "TIA Portal SaveAs completed but the Bridge could not prove that the active " +
                    "project is exactly one newly published .ap21 file. Inspect the output before retrying.");

            var publishedFile = matching[0];

            // TIA Portal still owns the SaveAs result at this point and may hold an
            // exclusive file handle. The worker disposes this session immediately
            // after returning the publish metadata; the host computes SHA-256 only
            // after that worker response, when the TIA handle has been released.
            return new BridgePublishResult
            {
                ProjectDirectory = publishDirectory,
                ProjectFile = publishedFile,
                CompileErrors = compile.Errors,
                CompileWarnings = compile.Warnings
            };
        }

        private PlcSoftware RequireSinglePlcSoftwareForBridge()
        {
            var softwares = RoundTripSourceExporter.FindPlcSoftware(_project);
            if (softwares.Count != 1)
                throw new InvalidOperationException(
                    "Bridge tag operations require exactly one PLC software object.");
            return softwares[0];
        }

        private static void RequireBridgeOutputName(
            string value,
            string parameterName = "outputName")
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException(
                    "A non-empty Bridge output name is required.", parameterName);
            if (value == "." || value == ".." ||
                value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                value.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
                value.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
                throw new ArgumentException(
                    "Bridge output name must be one safe directory name.", parameterName);
        }

        private static void RequireBridgeTagName(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty value is required.", parameterName);
            if (value.IndexOfAny(new[] { '/', '\\' }) >= 0)
                throw new ArgumentException(
                    "Bridge v0 accepts root tag-table and tag names, not paths.", parameterName);
        }

        public void ImportMainBlockForBridge(string simaticMlPath)
        {
            ThrowIfDisposed();
            if (_scratchDirectory == null)
                throw new InvalidOperationException(
                    "Bridge block import is allowed only on an offline disposable project copy.");
            if (string.IsNullOrWhiteSpace(simaticMlPath) || !File.Exists(simaticMlPath))
                throw new FileNotFoundException(
                    "The SimaticML block to import does not exist.", simaticMlPath);

            var plc = RequireSinglePlcSoftwareForBridge();
            var imported = plc.BlockGroup.Blocks.Import(
                new FileInfo(simaticMlPath), ImportOptions.Override);
            if (imported == null || imported.Count != 1 || imported[0].Name != "Main")
                throw new InvalidOperationException(
                    "Bridge block import did not produce exactly Main / OB1.");
        }

        public void SaveDisposableCopyForBridge()
        {
            ThrowIfDisposed();
            if (_scratchDirectory == null)
                throw new InvalidOperationException(
                    "Bridge save is allowed only on an offline disposable project copy.");
            _project.Save();
        }

        // Verification actively compiles only its owned disposable project copy.
        public PlcCompileObservation CompilePlcForVerification()
        {
            ThrowIfDisposed();
            if (_scratchDirectory == null)
                throw new InvalidOperationException("Verification compile requires an offline project copy.");
            var softwares = RoundTripSourceExporter.FindPlcSoftware(_project);
            if (softwares.Count != 1)
                throw new InvalidOperationException("Verification requires exactly one PLC software object.");
            var compiler = softwares[0].GetService<ICompilable>();
            if (compiler == null)
                throw new InvalidOperationException("PLC compile service is unavailable.");
            var result = compiler.Compile();
            return new PlcCompileObservation
            {
                Errors = result.ErrorCount,
                Warnings = result.WarningCount
            };
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                try
                {
                    if (_scratchDirectory != null) _project.Close();
                }
                finally { _portal.Dispose(); }
            }
            finally
            {
                if (_scratchDirectory != null)
                    DeleteOwnedScratchDirectory(_scratchDirectory);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(TiaProjectSession));
        }

        private static bool IsV21(TiaPortalProcess process)
        {
            return (process.Path?.FullName.IndexOf("Portal V21", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                || process.InstalledSoftware.Any(product => IsVersion21(product.Version));
        }

        private static string GetVersion(TiaPortalProcess process)
        {
            return process.InstalledSoftware.Select(product => product.Version)
                .FirstOrDefault(IsVersion21) ?? "V21";
        }

        private string ReadProjectVersion()
        {
            try { return _project.Version; }
            catch (EngineeringNotSupportedException) { return null; }
        }

        private static string GetApiBuild()
        {
            var file = Path.Combine(OpennessRuntime.DefaultPublicApiDirectory, "Siemens.Engineering.Base.dll");
            return File.Exists(file) ? FileVersionInfo.GetVersionInfo(file).FileVersion : null;
        }

        private static bool IsInsideOrEqual(string path, string directory)
        {
            var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            var fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
            return string.Equals(fullPath, fullDirectory, StringComparison.OrdinalIgnoreCase) ||
                fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static void RequireNoReparseAncestors(string path)
        {
            for (var directory = new DirectoryInfo(path); directory != null; directory = directory.Parent)
                if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("A project or output path contains a reparse point.");
        }

        private static bool IsVersion21(string version)
        {
            return !string.IsNullOrEmpty(version) &&
                (version.StartsWith("21", StringComparison.OrdinalIgnoreCase) ||
                 version.StartsWith("V21", StringComparison.OrdinalIgnoreCase));
        }

        private static void CopyDirectory(string source, string destination)
        {
            var directory = new DirectoryInfo(source);
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The project folder contains a reparse point; refusing to follow it.");
            Directory.CreateDirectory(destination);
            foreach (var file in directory.GetFiles())
            {
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("The project folder contains a reparse point; refusing to follow it.");
                file.CopyTo(Path.Combine(destination, file.Name));
            }
            foreach (var child in directory.GetDirectories())
                CopyDirectory(child.FullName, Path.Combine(destination, child.Name));
        }

        private static void DeleteOwnedScratchDirectory(string path)
        {
            var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "TiaGuard.Openness"))
                .TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(Path.GetDirectoryName(full), parent, StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParseExact(Path.GetFileName(full), "N", out _))
                throw new InvalidOperationException("Refusing to remove an unexpected scratch directory.");
            FileSystemSafety.DeleteOwnedTree(full);
        }
    }
}
