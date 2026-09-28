using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Xml;

namespace TiaGuard.Openness
{
    // Validation is completed before a TIA process is started or an output folder is created.
    public sealed class RoundTripBuildInput
    {
        public string SourceRoot { get; private set; }
        public string OutputDirectory { get; private set; }
        public RoundTripManifestV1 Manifest { get; private set; }
        public RoundTripHardwareV1 Hardware { get; private set; }
        public RoundTripPlcV1 Plc { get; private set; }
        public RoundTripBlockV1 Block { get; private set; }
        public IReadOnlyList<RoundTripTagTableV1> TagTables { get; private set; }
        public string BlockSourcePath { get; private set; }

        public static RoundTripBuildInput Load(string sourceRoot, string outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(sourceRoot) || string.IsNullOrWhiteSpace(outputDirectory))
                throw new ArgumentException("Source tree and new output directory are required.");
            var root = Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar);
            var output = Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar);
            if (!Directory.Exists(root))
                throw new DirectoryNotFoundException("The canonical source tree does not exist.");
            if (Directory.Exists(output) || File.Exists(output))
                throw new IOException("The build output path already exists.");
            var outputParent = Path.GetDirectoryName(output);
            if (string.IsNullOrEmpty(outputParent) || !Directory.Exists(outputParent))
                throw new DirectoryNotFoundException("The build output parent directory does not exist.");
            RequireNoReparseAncestors(root);
            RequireNoReparseAncestors(outputParent);
            if (InsideOrEqual(output, root) || InsideOrEqual(root, output))
                throw new IOException("The build output and canonical source tree overlap.");

            var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var manifest = ReadJson<RoundTripManifestV1>(Take(root, "tia-guard.json", expected));
            Require(manifest.SchemaVersion == "1.0" && manifest.ContractStatus == "draft" &&
                    manifest.TiaVersion == "V21", "Unsupported round-trip contract or TIA version.");
            Require(manifest.RoundTripReady && manifest.Diagnostics != null &&
                    manifest.Diagnostics.Count == 0, "The canonical source is not round-trip ready.");
            Require(manifest.Project != null && SafeProjectName(manifest.Project.Name),
                "The canonical project name is missing or unsafe.");
            Require(manifest.Hardware != null && manifest.Hardware.Count == 1 &&
                    manifest.Plcs != null && manifest.Plcs.Count == 1,
                "v0.1 requires one station and one PLC.");

            var hardware = ReadJson<RoundTripHardwareV1>(Take(root, manifest.Hardware[0], expected));
            Require(hardware.SchemaVersion == "1.0" &&
                    hardware.Capability == RoundTripCapabilityStates.SupportedRoundTrip &&
                    SafeId(hardware.Id) && hardware.Name != null &&
                    hardware.DeviceTypeIdentifier == "System:Device.S71200" &&
                    hardware.EngineeringPath ==
                        Uri.EscapeDataString(hardware.Name.Normalize(NormalizationForm.FormC)) &&
                    !string.IsNullOrWhiteSpace(hardware.CreateItemName) &&
                    !string.IsNullOrWhiteSpace(hardware.CreateTypeIdentifier) &&
                    hardware.CreateTypeIdentifier.StartsWith("OrderNumber:", StringComparison.Ordinal) &&
                    manifest.Hardware[0] == "tia/hardware/" + hardware.Id + ".json",
                "The hardware descriptor is not a supported build identity.");

            var plc = ReadJson<RoundTripPlcV1>(Take(root, manifest.Plcs[0], expected));
            Require(plc.SchemaVersion == "1.0" &&
                    plc.Capability == RoundTripCapabilityStates.SupportedRoundTrip &&
                    SafeId(plc.Id) && !string.IsNullOrWhiteSpace(plc.Name) &&
                    plc.DeviceId == hardware.Id && plc.Name == hardware.CreateItemName &&
                    manifest.Plcs[0] == "tia/plc/" + plc.Id + "/plc.json" &&
                    plc.Blocks != null && plc.Blocks.Count == 1 && plc.TagTables != null,
                "The PLC descriptor is outside the v0.1 subset.");

            var block = ReadJson<RoundTripBlockV1>(Take(root, plc.Blocks[0], expected));
            Require(block.SchemaVersion == "1.0" && SafeId(block.Id) &&
                    block.Capability == RoundTripCapabilityStates.SupportedRoundTrip &&
                    block.Name == "Main" && block.Kind == "OB" && block.Number == 1 &&
                    block.Language == "LAD" && block.Source != null &&
                    block.ScopePath == hardware.EngineeringPath + "/" +
                        Uri.EscapeDataString(hardware.CreateItemName) + "/" +
                        Uri.EscapeDataString(plc.Name) + "/Program%20blocks" &&
                    block.Source.Format == "SimaticML" &&
                    block.Source.NormalizationVersion == "simaticml-v1" &&
                    plc.Blocks[0] == "tia/plc/" + plc.Id + "/blocks/" + block.Id + "/block.json" &&
                    block.Source.Artifact == "tia/plc/" + plc.Id + "/blocks/" + block.Id + "/source.xml",
                "The required Main / OB1 / LAD descriptor is invalid.");
            var sourcePath = Take(root, block.Source.Artifact, expected);
            var sourceBytes = File.ReadAllBytes(sourcePath);
            Require(sourceBytes.Length != 0 &&
                    string.Equals(Sha256(sourceBytes), block.Source.Sha256, StringComparison.Ordinal),
                "The canonical block artifact SHA-256 does not match.");
            ValidateMainXml(sourceBytes);

            var tables = new List<RoundTripTagTableV1>();
            var tagIds = new List<string>();
            var tableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var objectIds = new HashSet<string>(StringComparer.Ordinal);
            objectIds.Add(hardware.Id);
            Require(objectIds.Add(plc.Id) && objectIds.Add(block.Id),
                "Duplicate canonical object IDs.");
            foreach (var tableRef in plc.TagTables)
            {
                var table = ReadJson<RoundTripTagTableV1>(Take(root, tableRef, expected));
                Require(table.SchemaVersion == "1.0" &&
                        table.Capability == RoundTripCapabilityStates.SupportedRoundTrip &&
                        SafeId(table.Id) && !string.IsNullOrWhiteSpace(table.Name) &&
                        table.ScopePath == hardware.EngineeringPath + "/" +
                            Uri.EscapeDataString(hardware.CreateItemName) + "/" +
                            Uri.EscapeDataString(plc.Name) + "/PLC%20tags/" +
                            Uri.EscapeDataString(table.Name.Normalize(NormalizationForm.FormC)) &&
                        tableRef == "tia/plc/" + plc.Id + "/tags/" + table.Id + ".json" &&
                        tableNames.Add(table.Name) && objectIds.Add(table.Id) &&
                        table.Tags != null, "A tag table is invalid or duplicated.");
                var tagNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var tag in table.Tags)
                {
                    Require(SafeId(tag.Id) && objectIds.Add(tag.Id) &&
                            !string.IsNullOrWhiteSpace(tag.Name) && tagNames.Add(tag.Name) &&
                            !string.IsNullOrWhiteSpace(tag.DataType) &&
                            tag.Capability == RoundTripCapabilityStates.SupportedRoundTrip &&
                            ((tag.CommentStatus == "missing" && string.IsNullOrEmpty(tag.Comment)) ||
                             (tag.CommentStatus == "present" && !string.IsNullOrWhiteSpace(tag.Comment))),
                        "A tag is ambiguous or outside the single-comment v0.1 subset.");
                    tagIds.Add(tag.Id);
                }
                tables.Add(table);
            }

            var requiredCapabilities = new HashSet<string>(StringComparer.Ordinal)
            {
                "hardware:" + hardware.Id, "plc:" + plc.Id, "block:" + block.Id
            };
            foreach (var table in tables) requiredCapabilities.Add("tag-table:" + table.Id);
            foreach (var tagId in tagIds) requiredCapabilities.Add("tag:" + tagId);
            Require(manifest.Capabilities != null &&
                    manifest.Capabilities.Count == requiredCapabilities.Count &&
                    manifest.Capabilities.All(value => value != null &&
                        value.State == RoundTripCapabilityStates.SupportedRoundTrip &&
                        requiredCapabilities.Remove(value.ObjectKind + ":" + value.ObjectRef)) &&
                    requiredCapabilities.Count == 0,
                "The capability ledger does not match the canonical objects.");
            Require(expected.SetEquals(EnumerateFiles(root)),
                "The canonical source tree contains missing, extra, or ambiguous files.");

            return new RoundTripBuildInput
            {
                SourceRoot = root, OutputDirectory = output, Manifest = manifest,
                Hardware = hardware, Plc = plc, Block = block, TagTables = tables,
                BlockSourcePath = sourcePath
            };
        }

        private static T ReadJson<T>(string path)
        {
            var bytes = File.ReadAllBytes(path);
            Require(bytes.Length > 0 && bytes.Length <= 8 * 1024 * 1024 &&
                    !(bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf),
                "Canonical JSON must be nonempty UTF-8 without BOM.");
            var text = new UTF8Encoding(false, true).GetString(bytes);
            ValidateJsonShape<T>(text);
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(text)))
            {
                var value = (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
                Require(value != null && stream.Position == stream.Length,
                    "Canonical JSON is incomplete or has trailing data.");
                return value;
            }
        }

        private static void ValidateJsonShape<T>(string text)
        {
            var parser = new JavaScriptSerializer
            {
                MaxJsonLength = 8 * 1024 * 1024,
                RecursionLimit = 64
            };
            var root = Object(parser.DeserializeObject(text));
            if (typeof(T) == typeof(RoundTripManifestV1))
            {
                Keys(root, "schemaVersion", "contractStatus", "tiaVersion", "project",
                    "roundTripReady", "hardware", "plcs", "capabilities", "diagnostics");
                Keys(Object(root["project"]), "name", "projectVersion");
                foreach (var capability in Array(root["capabilities"]))
                    Keys(Object(capability), "objectKind", "objectRef", "state", "reason");
                Require(Array(root["hardware"]).All(value => value is string) &&
                        Array(root["plcs"]).All(value => value is string) &&
                        Array(root["diagnostics"]).Length == 0,
                    "The ready manifest contains invalid collections or diagnostics.");
            }
            else if (typeof(T) == typeof(RoundTripHardwareV1))
                Keys(root, "schemaVersion", "id", "name", "engineeringPath",
                    "deviceTypeIdentifier", "createTypeIdentifier", "createItemName",
                    "orderNumber", "firmware", "capability");
            else if (typeof(T) == typeof(RoundTripPlcV1))
            {
                Keys(root, "schemaVersion", "id", "name", "deviceId", "capability",
                    "tagTables", "blocks");
                Require(Array(root["tagTables"]).All(value => value is string) &&
                        Array(root["blocks"]).All(value => value is string),
                    "The PLC contains invalid descriptor references.");
            }
            else if (typeof(T) == typeof(RoundTripBlockV1))
            {
                Keys(root, "schemaVersion", "id", "name", "scopePath", "kind",
                    "number", "language", "capability", "source");
                Keys(Object(root["source"]), "format", "artifact", "sha256",
                    "normalizationVersion");
            }
            else if (typeof(T) == typeof(RoundTripTagTableV1))
            {
                Keys(root, "schemaVersion", "id", "name", "scopePath", "capability", "tags");
                foreach (var tag in Array(root["tags"]))
                    Keys(Object(tag), "id", "name", "dataType", "address",
                        "commentStatus", "comment", "capability");
            }
            else
                throw new InvalidOperationException("Unsupported canonical JSON descriptor type.");
        }

        private static IDictionary<string, object> Object(object value)
        {
            var dictionary = value as IDictionary<string, object>;
            Require(dictionary != null, "Expected a canonical JSON object.");
            return dictionary;
        }

        private static object[] Array(object value)
        {
            var array = value as object[];
            Require(array != null, "Expected a canonical JSON array.");
            return array;
        }

        private static void Keys(IDictionary<string, object> value, params string[] names)
        {
            Require(value.Count == names.Length &&
                    names.All(value.ContainsKey),
                "Canonical JSON contains missing or unexpected fields.");
        }

        private static string Take(string root, string relative, ISet<string> expected)
        {
            Require(!string.IsNullOrWhiteSpace(relative) &&
                    relative.IndexOf('\\') < 0 && relative.IndexOf(':') < 0 &&
                    !Path.IsPathRooted(relative), "An artifact path is not repository-relative.");
            var segments = relative.Split('/');
            Require(segments.All(value => value.Length != 0 && value != "." && value != ".." &&
                value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0),
                "An artifact path contains an unsafe component.");
            var full = Path.GetFullPath(Path.Combine(root,
                relative.Replace('/', Path.DirectorySeparatorChar)));
            Require(InsideOrEqual(full, root) && expected.Add(relative),
                "An artifact path escapes the source tree or is duplicated.");
            RequireNoReparseAncestors(Path.GetDirectoryName(full));
            var file = new FileInfo(full);
            Require(file.Exists && (file.Attributes & FileAttributes.ReparsePoint) == 0,
                "A referenced canonical artifact is missing or is a reparse point.");
            return full;
        }

        private static IEnumerable<string> EnumerateFiles(string root)
        {
            var files = new List<string>();
            Walk(new DirectoryInfo(root), root, files);
            return files;
        }

        private static void Walk(DirectoryInfo directory, string root, ICollection<string> files)
        {
            Require((directory.Attributes & FileAttributes.ReparsePoint) == 0,
                "The canonical tree contains a reparse point.");
            foreach (var file in directory.GetFiles())
            {
                Require((file.Attributes & FileAttributes.ReparsePoint) == 0,
                    "The canonical tree contains a reparse point.");
                files.Add(file.FullName.Substring(root.Length + 1)
                    .Replace(Path.DirectorySeparatorChar, '/'));
            }
            foreach (var child in directory.GetDirectories()) Walk(child, root, files);
        }

        private static void ValidateMainXml(byte[] bytes)
        {
            var offset = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf
                ? 3 : 0;
            var text = new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
            var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using (var reader = XmlReader.Create(new StringReader(text), settings))
                document.Load(reader);
            Require(document.DocumentElement?.Name == "Document" &&
                    document.DocumentElement.NamespaceURI.Length == 0 &&
                    document.SelectNodes("/Document/SW.Blocks.OB").Count == 1 &&
                    document.SelectSingleNode("/Document/DocumentInfo/Created")?.InnerText ==
                        "1970-01-01T00:00:00Z" &&
                    document.SelectSingleNode("/Document/SW.Blocks.OB/AttributeList/Name")?.InnerText == "Main" &&
                    document.SelectSingleNode("/Document/SW.Blocks.OB/AttributeList/Number")?.InnerText == "1" &&
                    document.SelectSingleNode("/Document/SW.Blocks.OB/AttributeList/ProgrammingLanguage")?.InnerText == "LAD",
                "The canonical SimaticML does not describe Main / OB1 / LAD.");
        }

        private static string Sha256(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes))
                    .Replace("-", string.Empty).ToLowerInvariant();
        }

        private static bool SafeId(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
                   value.IndexOfAny(new[] { '/', '\\', ':' }) < 0;
        }

        private static bool SafeProjectName(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && value == value.Trim() &&
                   value != "." && value != ".." &&
                   value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }

        private static bool InsideOrEqual(string path, string directory)
        {
            return string.Equals(path, directory, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static void RequireNoReparseAncestors(string path)
        {
            for (var item = new DirectoryInfo(path); item != null; item = item.Parent)
                if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("A source or output path contains a reparse point.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }
    }
}
