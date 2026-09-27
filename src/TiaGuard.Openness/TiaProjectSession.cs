using System;
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
        public int? ProcessId { get; internal set; }
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
                ProcessId = _processId
            };
        }

        public SnapshotV1 ReadSnapshot()
        {
            ThrowIfDisposed();
            return SnapshotExtractor.Extract(_project, ReadProjectInfo());
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
