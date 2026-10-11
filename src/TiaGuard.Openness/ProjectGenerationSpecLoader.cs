using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TiaGuard.Openness
{
    public sealed class ProjectGenerationSpecInput
    {
        public string SourceRoot { get; }
        public ProjectGenerationSpec Spec { get; }
        public IReadOnlyList<ProjectGenerationArtifact> OrderedArtifacts { get; }
        // Import each path ONCE, in this order. All declarations sharing a file are a group.
        public IReadOnlyList<string> OrderedSourcePaths { get; }
        public IReadOnlyDictionary<string, string> SourceSha256 { get; }
        private readonly Dictionary<string, byte[]> snapshots;
        internal ProjectGenerationSpecInput(string root, string file, ProjectGenerationSpec spec,
            List<ProjectGenerationArtifact> ordered, List<string> paths, Dictionary<string, byte[]> bytes)
        {
            SourceRoot = root; Spec = spec; OrderedArtifacts = ordered.AsReadOnly();
            OrderedSourcePaths = paths.AsReadOnly(); snapshots = bytes;
            SourceSha256 = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
                paths.ToDictionary(x => x, x => Hash(bytes[x]), StringComparer.Ordinal));
        }
        public byte[] GetSourceBytes(string sourcePath)
        {
            if (!SourceSha256.ContainsKey(sourcePath)) throw new ArgumentException("Undeclared source path.");
            return (byte[])snapshots[sourcePath].Clone();
        }
        // B must use snapshot bytes for imports and D must bind its verdict to these digests.
        // Re-reading source paths after this check is not a race-free import protocol.
        public void ValidateUnchanged()
        {
            FileSystemSafety.RequirePlainAncestors(SourceRoot);
            ProjectGenerationSpecLoader.CheckFiles(SourceRoot, snapshots.Keys);
            foreach (var pair in snapshots)
                if (!pair.Value.SequenceEqual(ProjectGenerationSpecLoader.Read(SourceRoot, pair.Key)))
                    throw new InvalidDataException("Generation input changed: " + pair.Key);
        }
        private static string Hash(byte[] bytes)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    }
    public static class ProjectGenerationSpecLoader
    {
        public const string DefaultSpecFileName = "project-generation.json";
        internal static bool Identifier(string value) => value != null && Regex.IsMatch(value, @"\A[A-Za-z_][A-Za-z0-9_]{0,127}\z");
        internal static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
        public static ProjectGenerationSpecInput Load(string specPath)
        {
            if (string.IsNullOrWhiteSpace(specPath)) throw new ArgumentException("A spec file is required.");
            var full = Path.GetFullPath(specPath); var root = Path.GetDirectoryName(full); var file = Path.GetFileName(full);
            FileSystemSafety.RequirePlainAncestors(root);
            var bytes = new Dictionary<string, byte[]>(StringComparer.Ordinal) { [file] = Read(root, file) };
            var json = Object(ProjectGenerationSpecJson.Parse(new UTF8Encoding(false, true).GetString(bytes[file])));
            Keys(json, "schemaVersion", "tiaVersion", "projectName", "plc", "tagTables", "artifacts");
            Require(String(json, "schemaVersion") == "1.0" && String(json, "tiaVersion") == "V21", "Unsupported schema/TIA version.");
            var name = String(json, "projectName"); Require(RoundTripProfile.SafeFileName(name) && name.Length <= 64, "Unsafe project name.");
            var p = Object(json["plc"]); Keys(p, "name", "cpuTypeIdentifier");
            var plc = new ProjectGenerationPlc(Name(p), String(p, "cpuTypeIdentifier"));
            Require(RoundTripProfile.SupportsCpu(plc.CpuTypeIdentifier), "Unsupported exact CPU identity.");
            var tables = new List<ProjectGenerationTagTable>(); var tableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var addresses = new List<Tuple<string, long, long>>();
            foreach (var value in Array(json["tagTables"]))
            {
                var t = Object(value); Keys(t, "name", "tags"); var tableName = Name(t);
                Require(tableNames.Add(tableName), "Duplicate tag table."); var tags = new List<ProjectGenerationTag>();
                foreach (var tagValue in Array(t["tags"]))
                {
                    var tag = Object(tagValue); Keys(tag, "name", "dataType", "logicalAddress"); var tagName = Name(tag);
                    var type = String(tag, "dataType"); var address = String(tag, "logicalAddress");
                    Require(symbols.Add(tagName) && RoundTripProfile.SupportsTag(type, address), "Conflicting/unsupported tag.");
                    var parsed = SnapshotAddressParser.Parse(address); var start = (long)parsed.ByteOffset.Value * 8 + parsed.BitOffset.Value;
                    var finish = start + parsed.BitWidth.Value;
                    Require(!addresses.Any(x => x.Item1 == parsed.Area && start < x.Item3 && x.Item2 < finish), "Overlapping tag addresses.");
                    addresses.Add(Tuple.Create(parsed.Area, start, finish)); tags.Add(new ProjectGenerationTag(tagName, type, address));
                }
                tables.Add(new ProjectGenerationTagTable(tableName, tags));
            }
            var artifacts = new List<ProjectGenerationArtifact>(); var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var typeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pathCase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in Array(json["artifacts"]))
            {
                var a = Object(value); Keys(a, "id", "kind", "name", "format", "sourcePath", "dependsOn");
                var id = String(a, "id"); var kind = String(a, "kind"); var format = String(a, "format"); var objectName = Name(a);
                Require(Identifier(id) && ids.Add(id), "Duplicate/invalid artifact id.");
                Require(ProjectGenerationCapabilities.Supports("V21", plc.CpuTypeIdentifier, kind, format), "Unsupported object/format capability.");
                Require((kind == "UDT" ? typeNames : symbols).Add(objectName), "Conflicting object name in PLC scope.");
                var path = String(a, "sourcePath"); CheckRelative(path);
                Require(path.EndsWith(".scl", StringComparison.Ordinal) && !string.Equals(path, file, StringComparison.OrdinalIgnoreCase), "Invalid source extension/spec alias.");
                if (pathCase.TryGetValue(path, out var previous)) Require(previous == path, "Ambiguous path casing."); else pathCase.Add(path, path);
                var deps = Array(a["dependsOn"]).Select(x => x as string).ToList();
                Require(deps.All(Identifier) && deps.Distinct(StringComparer.OrdinalIgnoreCase).Count() == deps.Count, "Invalid/duplicate dependency.");
                artifacts.Add(new ProjectGenerationArtifact(id, kind, objectName, format, path, deps));
            }
            Require(artifacts.Count > 0, "At least one declared artifact is required.");
            var ordered = Sort(artifacts, x => x.Id, x => x.DependsOn);
            var groups = artifacts.GroupBy(x => x.SourcePath).ToDictionary(x => x.Key, x => x.ToList(), StringComparer.Ordinal);
            var byId = artifacts.ToDictionary(x => x.Id, StringComparer.Ordinal);
            var paths = Sort(groups.Keys.ToList(), x => x, x => groups[x].SelectMany(a => a.DependsOn)
                .Select(d => byId[d].SourcePath).Where(d => d != x).Distinct().ToList());
            foreach (var path in paths)
            { bytes.Add(path, Read(root, path)); ProjectGenerationSpecSource.Validate(bytes[path], groups[path]); }
            CheckFiles(root, bytes.Keys);
            var spec = new ProjectGenerationSpec(name, plc, tables, artifacts);
            return new ProjectGenerationSpecInput(root, file, spec, ordered, paths, bytes);
        }
        private static List<T> Sort<T>(List<T> values, Func<T, string> key, Func<T, IReadOnlyList<string>> deps)
        {
            var map = values.ToDictionary(key, StringComparer.Ordinal); var done = new HashSet<string>(StringComparer.Ordinal); var result = new List<T>();
            Require(values.All(x => deps(x).All(map.ContainsKey)), "Dangling dependency.");
            while (result.Count != values.Count)
            {
                var ready = values.Where(x => !done.Contains(key(x)) && deps(x).All(done.Contains)).ToList();
                Require(ready.Count > 0, "Cyclic artifact/source dependency.");
                foreach (var item in ready) { done.Add(key(item)); result.Add(item); }
            }
            return result;
        }
        internal static void CheckRelative(string path)
        {
            Require(!string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path) && path.IndexOfAny(new[] { '\\', ':' }) < 0 &&
                path.Split('/').All(x => x != "." && x != ".." && RoundTripProfile.SafeFileName(x)), "Unsafe source path.");
        }
        internal static byte[] Read(string root, string relative)
        {
            CheckRelative(relative); var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            FileSystemSafety.RequirePlainFile(path); var info = new FileInfo(path);
            Require(info.Length > 0 && info.Length <= 8 * 1024 * 1024, "Input file size outside limit.");
            return File.ReadAllBytes(path);
        }
        internal static void CheckFiles(string root, IEnumerable<string> expected)
        {
            var actual = new List<string>(); Walk(new DirectoryInfo(root), "", actual);
            Require(new HashSet<string>(expected, StringComparer.Ordinal).SetEquals(actual), "Undeclared or missing input files.");
        }
        private static void Walk(DirectoryInfo dir, string prefix, List<string> files)
        {
            foreach (var child in dir.GetFileSystemInfos())
            {
                Require((child.Attributes & FileAttributes.ReparsePoint) == 0, "Input tree contains a reparse point.");
                if (child is DirectoryInfo nested) Walk(nested, prefix + child.Name + "/", files);
                else files.Add(prefix + child.Name);
            }
        }
        private static Dictionary<string, object> Object(object value)
        { var result = value as Dictionary<string, object>; Require(result != null, "Expected JSON object."); return result; }
        private static List<object> Array(object value)
        { var result = value as List<object>; Require(result != null, "Expected JSON array."); return result; }
        private static string String(Dictionary<string, object> obj, string key)
        { var result = obj[key] as string; Require(result != null && result.Length > 0, "Expected nonempty JSON string: " + key); return result; }
        private static string Name(Dictionary<string, object> obj)
        { var name = String(obj, "name"); Require(Identifier(name), "Object names require an ASCII PLC identifier."); return name; }
        private static void Keys(Dictionary<string, object> obj, params string[] names)
        { Require(obj.Count == names.Length && names.All(obj.ContainsKey), "Unknown or missing JSON fields."); }
    }
}
