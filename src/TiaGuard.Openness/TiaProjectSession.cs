using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Siemens.Engineering;

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
            if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
        }
    }
}
