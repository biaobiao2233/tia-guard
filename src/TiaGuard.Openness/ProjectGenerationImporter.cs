using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace TiaGuard.Openness
{
    public sealed class ProjectGenerationEngineTag
    {
        public string Name { get; set; }
        public string DataType { get; set; }
        public string LogicalAddress { get; set; }
    }
    public sealed class ProjectGenerationEngineTagTable
    {
        public string Name { get; set; }
        public IReadOnlyList<ProjectGenerationEngineTag> Tags { get; set; } = new ProjectGenerationEngineTag[0];
    }
    public sealed class ProjectGenerationEngineArtifact
    {
        public string Id { get; set; }
        public string Format { get; set; }
        public string SourcePath { get; set; }
        public string Sha256 { get; set; }
        public string ResourcePath { get; set; }
        public string ResourceSha256 { get; set; }
        public IReadOnlyList<string> DependsOn { get; set; } = new string[0];
        public ProjectGenerationObject Object { get; set; }
    }
    public sealed class ProjectGenerationBuilderPlan
    {
        public string ContractRevision { get; set; }
        public string IdentityRevision { get; set; }
        public string SourceRoot { get; set; }
        public string ProjectName { get; set; }
        public string PlcName { get; set; }
        public string CpuTypeIdentifier { get; set; }
        public IReadOnlyList<ProjectGenerationEngineTagTable> TagTables { get; set; } = new ProjectGenerationEngineTagTable[0];
        public IReadOnlyList<ProjectGenerationEngineArtifact> Artifacts { get; set; } = new ProjectGenerationEngineArtifact[0];
        internal IReadOnlyDictionary<string, byte[]> ValidatedSourceBytes { get; set; }
    }
    public sealed class ProjectGenerationImportUnit
    {
        public string Format { get; internal set; }
        public string SourcePath { get; internal set; }
        public string Sha256 { get; internal set; }
        public string ResourcePath { get; internal set; }
        public string ResourceSha256 { get; internal set; }
        public IReadOnlyList<ProjectGenerationObject> Expected { get; internal set; }
        internal HashSet<string> Ids { get; set; }
        internal HashSet<string> Dependencies { get; set; }
    }

    public static class ProjectGenerationImporter
    {
        internal static bool KnownKind(string kind) =>
            kind == "OB" || kind == "FB" || kind == "FC" || kind == "DB" || kind == "UDT";

        // A validated #44 input must be adapted to this plan; there is no JSON loader here.
        public static IReadOnlyList<ProjectGenerationImportUnit> Plan(ProjectGenerationBuilderPlan plan)
        {
            if (plan == null || string.IsNullOrWhiteSpace(plan.ContractRevision) ||
                !RoundTripProfile.SafeFileName(plan.ProjectName) || !RoundTripProfile.SafeFileName(plan.PlcName) ||
                !RoundTripProfile.SupportsCpu(plan.CpuTypeIdentifier) || plan.Artifacts == null || plan.TagTables == null)
                throw new ArgumentException("A version-bound, validated V21/1212C plan is required.");
            FileSystemSafety.RequirePlainAncestors(Path.GetFullPath(plan.SourceRoot));
            if (!Directory.Exists(plan.SourceRoot)) throw new DirectoryNotFoundException("Source root is missing.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var numbers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var a in plan.Artifacts)
            {
                var o = a?.Object;
                if (a == null || string.IsNullOrWhiteSpace(a.Id) || !ids.Add(a.Id) || o == null ||
                    !KnownKind(o.Kind) || !RoundTripProfile.SafeFileName(o.Name) || o.Protected || o.Group != "" ||
                    !names.Add(o.Key) || a.DependsOn == null || a.DependsOn.Distinct().Count() != a.DependsOn.Count)
                    throw new ArgumentException("Invalid/duplicate artifact or unsupported scope/protection.");
                if (o.Kind == "UDT" ? o.Number.HasValue || o.Language != null || o.InstanceOfName != null :
                    !o.Number.HasValue || o.Number.Value < 1 || !numbers.Add(o.Kind + ":" + o.Number.Value))
                    throw new ArgumentException("Invalid/duplicate block number or UDT identity.");
                if (o.Kind != "UDT" && o.Language != "SCL" && o.Language != "LAD" &&
                    o.Language != "FBD" && o.Language != "DB")
                    throw new ArgumentException("Unsupported language.");
                if (o.Kind == "DB" && o.Language != "DB" || o.Kind != "DB" && o.Language == "DB" ||
                    o.InstanceOfName != null && (o.Kind != "DB" || string.IsNullOrWhiteSpace(o.InstanceOfName)))
                    throw new ArgumentException("Invalid data block/instance identity.");
                var extension = a.Format == "scl" ? ".scl" : a.Format == "simatic-sd" ? ".s7dcl" :
                    a.Format == "simaticml" ? ".xml" : null;
                if (extension == null || !string.Equals(Path.GetExtension(a.SourcePath), extension, StringComparison.OrdinalIgnoreCase) ||
                    a.Format == "scl" && o.Kind != "UDT" && o.Language != "SCL" && o.Language != "DB")
                    throw new ArgumentException("Unsupported object/format/language or source extension.");
                RequireHash(a.Sha256);
                ResolveSource(plan.SourceRoot, a.SourcePath);
                if (a.ResourcePath != null)
                {
                    if (a.Format != "simatic-sd" || Path.ChangeExtension(a.SourcePath, ".s7res") != a.ResourcePath)
                        throw new ArgumentException(".s7res must accompany its declared .s7dcl, not be a program source.");
                    RequireHash(a.ResourceSha256);
                    ResolveSource(plan.SourceRoot, a.ResourcePath);
                }
                else if (a.ResourceSha256 != null) throw new ArgumentException("Orphan resource hash.");
            }
            foreach (var a in plan.Artifacts)
            {
                if (a.DependsOn.Any(d => d == a.Id || !ids.Contains(d))) throw new ArgumentException("Dangling/self dependency.");
                if (a.Object.InstanceOfName != null && !plan.Artifacts.Any(f => f.Object.Kind == "FB" &&
                    f.Object.Name == a.Object.InstanceOfName && a.DependsOn.Contains(f.Id)))
                    throw new ArgumentException("An instance DB must explicitly depend on its declared FB.");
            }
            // Check the object graph even when several objects share a single source unit.
            var visited = new HashSet<string>();
            var pending = plan.Artifacts.ToList();
            while (pending.Count != 0)
            {
                var ready = pending.Where(a => a.DependsOn.All(visited.Contains)).ToList();
                if (ready.Count == 0) throw new ArgumentException("Cyclic object dependencies.");
                foreach (var a in ready) { visited.Add(a.Id); pending.Remove(a); }
            }
            if (plan.Artifacts.Count(a => a.Object.Kind == "OB" && a.Object.Number == 1) != 1)
                throw new ArgumentException("Declare exactly one OB1; the fresh Main is replaced explicitly.");
            ValidateTags(plan.TagTables);
            var units = plan.Artifacts.GroupBy(a => a.SourcePath, StringComparer.OrdinalIgnoreCase).Select(g =>
            {
                var first = g.First();
                if (g.Any(a => a.Format != first.Format || a.Sha256 != first.Sha256 ||
                    a.ResourcePath != first.ResourcePath || a.ResourceSha256 != first.ResourceSha256) ||
                    first.Format != "scl" && g.Select(a => a.Object.Kind == "UDT").Distinct().Count() > 1)
                    throw new ArgumentException("Inconsistent shared source declarations.");
                return new ProjectGenerationImportUnit
                {
                    Format = first.Format, SourcePath = first.SourcePath, Sha256 = first.Sha256,
                    ResourcePath = first.ResourcePath, ResourceSha256 = first.ResourceSha256,
                    Expected = g.Select(a => a.Object).ToArray(), Ids = new HashSet<string>(g.Select(a => a.Id)),
                    Dependencies = new HashSet<string>(g.SelectMany(a => a.DependsOn).Except(g.Select(a => a.Id)))
                };
            }).ToList();
            var ordered = new List<ProjectGenerationImportUnit>();
            visited.Clear();
            while (units.Count != 0)
            {
                // Explicit dependencies win; independent types precede blocks, OBs last.
                var next = units.Where(u => u.Dependencies.All(visited.Contains))
                    .OrderBy(u => u.Expected.Any(o => o.Kind == "OB") ? 2 : u.Expected.All(o => o.Kind == "UDT") ? 0 : 1)
                    .ThenBy(u => u.SourcePath, StringComparer.Ordinal).FirstOrDefault();
                if (next == null) throw new ArgumentException("Source units cannot satisfy the declared dependency graph.");
                ordered.Add(next); visited.UnionWith(next.Ids); units.Remove(next);
            }
            return ordered;
        }

        private static void ValidateTags(IReadOnlyList<ProjectGenerationEngineTagTable> tables)
        {
            var tableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var tagNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in tables)
            {
                if (t == null || !RoundTripProfile.SafeFileName(t.Name) || !tableNames.Add(t.Name) || t.Tags == null)
                    throw new ArgumentException("Invalid/duplicate tag table.");
                foreach (var tag in t.Tags)
                    if (tag == null || !RoundTripProfile.SafeFileName(tag.Name) || !tagNames.Add(tag.Name) ||
                        !RoundTripProfile.SupportsTag(tag.DataType, tag.LogicalAddress))
                        throw new ArgumentException("Invalid/duplicate tag or unsupported type/address.");
            }
        }
        private static void RequireHash(string hash)
        {
            if (hash == null || !Regex.IsMatch(hash, "\\A[0-9a-f]{64}\\z"))
                throw new ArgumentException("A lowercase SHA-256 binding is required.");
        }
        internal static string ResolveSource(string root, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.IndexOf(':') >= 0 ||
                relative.Split('/', '\\').Any(s => s == ".." || s == "." || s.Length == 0))
                throw new IOException("Invalid relative source path.");
            var parent = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(Path.Combine(root, relative));
            if (!full.StartsWith(parent, StringComparison.OrdinalIgnoreCase)) throw new IOException("Source escaped its root.");
            FileSystemSafety.RequirePlainFile(full);
            return full;
        }
        internal static void CopyValidatedSource(string root, string relative, string hash, string target)
        {
            var full = ResolveSource(root, relative);
            // Hold the source against writes/deletion until the private copy and digest are complete.
            using (var source = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var copy = new FileStream(target, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var sha = SHA256.Create())
            {
                source.CopyTo(copy); copy.Position = 0;
                var actual = BitConverter.ToString(sha.ComputeHash(copy)).Replace("-", "").ToLowerInvariant();
                if (actual != hash) throw new IOException("Source changed since validation: " + relative);
            }
        }
        internal static void WriteValidatedBytes(byte[] bytes, string hash, string target)
        {
            using (var sha = SHA256.Create())
            {
                var actual = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                if (actual != hash) throw new IOException("Input snapshot does not match the validated digest.");
            }
            using (var copy = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                copy.Write(bytes, 0, bytes.Length);
        }
    }
}
