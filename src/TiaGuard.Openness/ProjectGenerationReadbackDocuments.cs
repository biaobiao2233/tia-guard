// Adapted from heilingbrunner/tiaportal-mcp Portal.Documents.cs / Portal.Sources.cs
// at 6824e4d671b403993c35d98dddf104686a950f4e. Copyright (c) 2025 J.Heilingbrunner.
// MIT license retained in third-party/project-generation-verification/heilingbrunner-LICENSE.txt.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TiaGuard.Openness
{
    public static class ProjectGenerationReadbackDocuments
    {
        // An object owns a fresh directory. Reported and observed files must agree;
        // a successful API return alone cannot establish content completeness.
        public static void Read(string directory, IEnumerable<string> reportedFiles,
            ProjectGenerationVerificationObject target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            target.ContentComplete = false;
            target.Documents.Clear();
            target.RawSha256.Clear();
            var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            FileSystemSafety.RequirePlainAncestors(root);
            if (reportedFiles == null) throw new InvalidDataException("Missing export file ledger.");
            var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in reportedFiles)
            {
                if (string.IsNullOrWhiteSpace(file)) throw new InvalidDataException("Empty reported export path.");
                var full = Path.GetFullPath(Path.IsPathRooted(file) ? file : Path.Combine(root, file));
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(Path.GetDirectoryName(full) + Path.DirectorySeparatorChar, root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Export reported a file outside its object directory.");
                if (!reported.Add(full)) throw new InvalidDataException("Duplicate reported file.");
            }
            if (Directory.GetDirectories(root).Length != 0) throw new InvalidDataException("Unexpected export subdirectory.");
            var observed = new HashSet<string>(Directory.GetFiles(root), StringComparer.OrdinalIgnoreCase);
            if (observed.Count == 0 || !observed.SetEquals(reported))
                throw new InvalidDataException("Reported and observed export files differ or are empty.");
            foreach (var file in observed.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                FileSystemSafety.RequirePlainFile(file);
                var extension = Path.GetExtension(file).ToLowerInvariant();
                if (!Allowed(target.ContentFormat, extension) || target.Documents.ContainsKey(extension))
                    throw new InvalidDataException("Unknown or duplicate export document extension.");
                var bytes = File.ReadAllBytes(file);
                // V21 writes UTF-8. Reject replacement decoding and empty/truncated files.
                var text = new UTF8Encoding(false, true).GetString(bytes);
                if (string.IsNullOrWhiteSpace(ProjectGenerationVerifier.NormalizeTransportText(text)))
                    throw new InvalidDataException("Empty exported document.");
                target.Documents.Add(extension, text);
                using (var sha = SHA256.Create())
                    target.RawSha256.Add(extension, BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant());
            }
            if (target.ContentFormat == "simatic-sd" && !target.Documents.ContainsKey(".s7dcl"))
                throw new InvalidDataException("SIMATIC SD code document missing.");
            target.ContentComplete = true;
        }

        private static bool Allowed(string format, string extension)
        {
            return format == "simatic-sd" ? extension == ".s7dcl" || extension == ".s7res"
                : format == "scl" ? extension == ".scl" || extension == ".db" || extension == ".udt"
                : format == "simaticml" && extension == ".xml";
        }
    }
}
