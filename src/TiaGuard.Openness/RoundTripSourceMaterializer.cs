using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace TiaGuard.Openness
{
    public static class RoundTripSourceMaterializer
    {
        public static RoundTripManifestV1 Write(
            SnapshotV1 snapshot,
            RoundTripExtractionHints hints,
            string blockExportDirectory,
            string outputDirectory)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (hints == null) throw new ArgumentNullException(nameof(hints));
            if (string.IsNullOrWhiteSpace(blockExportDirectory))
                throw new ArgumentException("A block export directory is required.", nameof(blockExportDirectory));
            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new ArgumentException("An output directory is required.", nameof(outputDirectory));

            var outputRoot = Path.GetFullPath(outputDirectory);
            if (Directory.Exists(outputRoot) || File.Exists(outputRoot))
                throw new IOException("Round-trip output already exists; refusing to overwrite it.");

            var parent = Path.GetDirectoryName(outputRoot);
            if (string.IsNullOrWhiteSpace(parent))
                throw new IOException("Round-trip output must have a parent directory.");
            Directory.CreateDirectory(parent);

            var stagingRoot = Path.Combine(parent,
                "." + Path.GetFileName(outputRoot) + ".tia-guard-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingRoot);
            try
            {
                var manifest = Build(snapshot, hints, Path.GetFullPath(blockExportDirectory), stagingRoot);
                WriteJson(Path.Combine(stagingRoot, "tia-guard.json"), manifest);
                Directory.Move(stagingRoot, outputRoot);
                return manifest;
            }
            catch
            {
                if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, recursive: true);
                throw;
            }
        }

        private static RoundTripManifestV1 Build(
            SnapshotV1 snapshot,
            RoundTripExtractionHints hints,
            string blockExportRoot,
            string outputRoot)
        {
            var manifest = new RoundTripManifestV1
            {
                TiaVersion = snapshot.Tia?.Version ?? "unknown",
                Project = new RoundTripProjectV1
                {
                    Name = snapshot.Project?.Name ?? string.Empty,
                    ProjectVersion = snapshot.Project?.ProjectVersion
                }
            };

            var supportedTiaVersion = string.Equals(manifest.TiaVersion, "V21", StringComparison.Ordinal);
            if (!supportedTiaVersion)
                AddDiagnostic(manifest, "TIA_VERSION_UNSUPPORTED",
                    "v0.1 round-trip source requires TIA Portal V21.", "project");

            if (snapshot.Capture?.Status != "complete")
                AddDiagnostic(manifest, "SOURCE_CAPTURE_INCOMPLETE",
                    "The source Snapshot is not complete; round-trip readiness is blocked.", "project");

            var compileReady = true;
            if (hints.CompilePreparation != null)
            {
                if (hints.CompilePreparation.State == "blocked-attached-session")
                {
                    compileReady = false;
                    AddDiagnostic(manifest, "BLOCK_EXPORT_REQUIRES_CONSISTENT_COPY",
                        "The supported block is not consistent. Attached user projects are never compiled by export; use an offline copy.",
                        "project");
                }
                else if (hints.CompilePreparation.Attempted &&
                         hints.CompilePreparation.State != "succeeded")
                {
                    compileReady = false;
                    var failure = string.IsNullOrWhiteSpace(hints.CompilePreparation.FailureType)
                        ? "unknown"
                        : hints.CompilePreparation.FailureType;
                    AddDiagnostic(manifest, "OFFLINE_COPY_COMPILE_FAILED",
                        "The disposable offline copy could not be compiled for block export (" + failure + ").",
                        "project");
                }
            }

            var station = MaterializeHardware(snapshot, hints.Hardware, manifest, outputRoot);
            var plc = MaterializePlc(snapshot, hints.TagTables, manifest, outputRoot, blockExportRoot);

            manifest.Hardware = station == null
                ? new List<string>()
                : new List<string> { station.Item1 };
            manifest.Plcs = plc == null
                ? new List<string>()
                : new List<string> { plc.Item1 };

            manifest.Capabilities = manifest.Capabilities
                .OrderBy(value => value.ObjectKind, StringComparer.Ordinal)
                .ThenBy(value => value.ObjectRef, StringComparer.Ordinal)
                .ToList();
            manifest.Diagnostics = manifest.Diagnostics
                .OrderBy(value => value.ObjectRef, StringComparer.Ordinal)
                .ThenBy(value => value.Code, StringComparer.Ordinal)
                .ToList();

            manifest.RoundTripReady =
                supportedTiaVersion &&
                compileReady &&
                snapshot.Capture?.Status == "complete" &&
                station != null &&
                plc != null &&
                manifest.Capabilities.Count > 0 &&
                manifest.Capabilities.All(value => value.State == RoundTripCapabilityStates.SupportedRoundTrip);

            return manifest;
        }

        private static Tuple<string, RoundTripHardwareV1> MaterializeHardware(
            SnapshotV1 snapshot,
            RoundTripHardwareBuildIdentity identity,
            RoundTripManifestV1 manifest,
            string outputRoot)
        {
            var devices = snapshot.Devices ?? new List<SnapshotDevice>();
            if (devices.Count != 1)
            {
                AddDiagnostic(manifest, "HARDWARE_DEVICE_COUNT_UNSUPPORTED",
                    "v0.1 requires exactly one device/station; observed " + devices.Count + ".", "hardware");
                foreach (var extraDevice in devices.OrderBy(value => value.Id, StringComparer.Ordinal))
                    AddCapability(manifest, "hardware", extraDevice.Id, RoundTripCapabilityStates.Unsupported,
                        "v0.1 supports exactly one root station.");
                return null;
            }

            var device = devices[0];
            var pathSegments = (device.EngineeringPath ?? string.Empty).Split(new[] { '/' },
                StringSplitOptions.RemoveEmptyEntries);
            var isRoot = pathSegments.Length <= 1;
            var state = identity?.State ?? RoundTripCapabilityStates.Failed;
            var reason = identity?.Reason;

            if (!isRoot)
            {
                state = RoundTripCapabilityStates.Unsupported;
                reason = "v0.1 requires the supported S7-1200 station to be a root project device.";
            }
            if (!string.Equals(device.Type, "System:Device.S71200", StringComparison.Ordinal))
            {
                state = RoundTripCapabilityStates.Unsupported;
                reason = "v0.1 supports only a verified S7-1200 root station type.";
            }
            if (identity == null || !string.Equals(identity.DeviceName, device.Name, StringComparison.Ordinal))
            {
                state = RoundTripCapabilityStates.Failed;
                reason = "The build identity could not be bound unambiguously to the exported station.";
            }
            if (state == RoundTripCapabilityStates.SupportedRoundTrip &&
                (string.IsNullOrWhiteSpace(identity.CreateTypeIdentifier) ||
                 !identity.CreateTypeIdentifier.StartsWith("OrderNumber:", StringComparison.Ordinal)))
            {
                state = RoundTripCapabilityStates.Failed;
                reason = "The CPU DeviceItem does not expose a build-grade OrderNumber TypeIdentifier.";
            }

            var id = SafeId("station", device.Id);
            var relative = "tia/hardware/" + id + ".json";
            var descriptor = new RoundTripHardwareV1
            {
                Id = id,
                Name = device.Name,
                EngineeringPath = device.EngineeringPath,
                DeviceTypeIdentifier = device.Type,
                CreateTypeIdentifier = identity?.CreateTypeIdentifier,
                CreateItemName = identity?.CpuItemName,
                OrderNumber = identity?.OrderNumber ?? device.OrderNumber,
                Firmware = identity?.Firmware ?? device.Firmware,
                Capability = state
            };
            WriteJson(Path.Combine(outputRoot, ToSystemPath(relative)), descriptor);
            AddCapability(manifest, "hardware", id, state, reason);
            if (state != RoundTripCapabilityStates.SupportedRoundTrip)
                AddDiagnostic(manifest, "HARDWARE_BUILD_IDENTITY_BLOCKED",
                    reason ?? "Build-grade hardware identity is unavailable.", id);
            return Tuple.Create(relative, descriptor);
        }

        private static Tuple<string, RoundTripPlcV1> MaterializePlc(
            SnapshotV1 snapshot,
            IReadOnlyList<RoundTripTagTableHint> tableHints,
            RoundTripManifestV1 manifest,
            string outputRoot,
            string blockExportRoot)
        {
            var plcs = snapshot.Plcs ?? new List<SnapshotPlc>();
            if (plcs.Count != 1)
            {
                AddDiagnostic(manifest, "PLC_COUNT_UNSUPPORTED",
                    "v0.1 requires exactly one PLC software object; observed " + plcs.Count + ".", "plc");
                foreach (var item in plcs.OrderBy(value => value.Id, StringComparer.Ordinal))
                    AddCapability(manifest, "plc", item.Id, RoundTripCapabilityStates.Unsupported,
                        "v0.1 supports exactly one PLC software object.");
                return null;
            }

            var plc = plcs[0];
            var plcId = SafeId("plc", plc.Id);
            var baseRelative = "tia/plc/" + plcId;
            var plcRelative = baseRelative + "/plc.json";
            var descriptor = new RoundTripPlcV1
            {
                Id = plcId,
                Name = plc.Name,
                DeviceId = SafeId("station", plc.DeviceId),
                Capability = RoundTripCapabilityStates.SupportedRoundTrip
            };

            var blockStates = new List<string>();
            foreach (var block in plc.Blocks.OrderBy(value => value.Id, StringComparer.Ordinal))
            {
                var materialized = MaterializeBlock(block, plcId, manifest, outputRoot, blockExportRoot);
                descriptor.Blocks.Add(materialized.Item1);
                blockStates.Add(materialized.Item2);
            }

            var hints = (tableHints ?? Array.Empty<RoundTripTagTableHint>())
                .Where(value => string.Equals(value.PlcName, plc.Name, StringComparison.Ordinal))
                .OrderBy(value => value.ScopePath, StringComparer.Ordinal)
                .ToList();
            var tagsByScope = plc.Tags
                .GroupBy(value => value.ScopePath ?? string.Empty, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.OrderBy(value => value.Id, StringComparer.Ordinal).ToList(),
                    StringComparer.Ordinal);
            var scopes = new SortedSet<string>(tagsByScope.Keys, StringComparer.Ordinal);
            foreach (var hint in hints) scopes.Add(hint.ScopePath ?? string.Empty);

            foreach (var scope in scopes)
            {
                var hint = hints.FirstOrDefault(value => string.Equals(value.ScopePath, scope, StringComparison.Ordinal));
                var tags = tagsByScope.TryGetValue(scope, out var values) ? values : new List<SnapshotTag>();
                var table = MaterializeTagTable(plcId, scope, hint?.Name, tags, manifest, outputRoot);
                descriptor.TagTables.Add(table.Item1);
                blockStates.Add(table.Item2);
            }

            if (plc.Blocks.Count != 1 ||
                plc.Blocks[0].Number != 1 ||
                !string.Equals(plc.Blocks[0].Language, "LAD", StringComparison.OrdinalIgnoreCase))
            {
                descriptor.Capability = RoundTripCapabilityStates.Unsupported;
                AddDiagnostic(manifest, "PLC_BLOCK_SUBSET_UNSUPPORTED",
                    "v0.1 requires exactly one OB1/LAD block.", plcId);
            }
            else if (blockStates.Any(value => value != RoundTripCapabilityStates.SupportedRoundTrip))
                descriptor.Capability = RoundTripCapabilityStates.Failed;

            WriteJson(Path.Combine(outputRoot, ToSystemPath(plcRelative)), descriptor);
            AddCapability(manifest, "plc", plcId, descriptor.Capability,
                descriptor.Capability == RoundTripCapabilityStates.SupportedRoundTrip
                    ? null
                    : "One or more required PLC children are not round-trip capable.");
            return Tuple.Create(plcRelative, descriptor);
        }

        private static Tuple<string, string> MaterializeBlock(
            SnapshotBlock block,
            string plcId,
            RoundTripManifestV1 manifest,
            string outputRoot,
            string blockExportRoot)
        {
            var blockId = SafeId("block", block.Id);
            var baseRelative = "tia/plc/" + plcId + "/blocks/" + blockId;
            var descriptorRelative = baseRelative + "/block.json";
            var sourceRelative = baseRelative + "/source.xml";
            var supportedShape =
                string.Equals(block.Name, "Main", StringComparison.Ordinal) &&
                block.Number == 1 &&
                string.Equals(block.Language, "LAD", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(block.Kind, "OB", StringComparison.OrdinalIgnoreCase) ||
                 (block.Kind?.EndsWith("OB", StringComparison.OrdinalIgnoreCase) ?? false));

            var state = RoundTripCapabilityStates.Unsupported;
            string reason;
            string canonicalSha256 = null;
            if (!supportedShape)
            {
                reason = "v0.1 supports only Main / OB1 in LAD.";
                if (block.Export?.Status == "exported") state = RoundTripCapabilityStates.ExportOnly;
            }
            else if (block.Export == null)
            {
                state = RoundTripCapabilityStates.Failed;
                reason = "Block export evidence is missing.";
            }
            else if (block.Export.Status == "protected")
            {
                state = RoundTripCapabilityStates.Opaque;
                reason = "The block is know-how protected.";
            }
            else if (block.Export.Status == "unsupported")
            {
                state = RoundTripCapabilityStates.Unsupported;
                reason = "Openness does not support exporting this block.";
            }
            else if (block.Export.Status == "failed")
            {
                state = RoundTripCapabilityStates.Failed;
                reason = "Block export failed.";
            }
            else if (block.Export.Status != "exported" ||
                     string.IsNullOrWhiteSpace(block.Export.Artifact) ||
                     string.IsNullOrWhiteSpace(block.Export.Sha256))
            {
                state = RoundTripCapabilityStates.Failed;
                reason = "A full SimaticML artifact is required for the supported block.";
            }
            else
            {
                var source = ResolveInside(blockExportRoot, block.Export.Artifact);
                if (!File.Exists(source))
                {
                    state = RoundTripCapabilityStates.Failed;
                    reason = "The claimed block artifact does not exist.";
                }
                else
                {
                    var actual = Sha256File(source);
                    if (!string.Equals(actual, block.Export.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        state = RoundTripCapabilityStates.Failed;
                        reason = "The block artifact hash does not match Snapshot evidence.";
                    }
                    else
                    {
                        var destination = Path.Combine(outputRoot, ToSystemPath(sourceRelative));
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        try
                        {
                            canonicalSha256 = NormalizeSimaticMlV1(source, destination);
                            state = supportedShape
                                ? RoundTripCapabilityStates.SupportedRoundTrip
                                : RoundTripCapabilityStates.ExportOnly;
                            reason = supportedShape ? null : "The artifact is preserved but its block shape is outside v0.1.";
                        }
                        catch (InvalidDataException error)
                        {
                            state = RoundTripCapabilityStates.Failed;
                            reason = "SimaticML v1 normalization rejected source: " + error.Message;
                        }
                    }
                }
            }

            var descriptor = new RoundTripBlockV1
            {
                Id = blockId,
                Name = block.Name,
                ScopePath = block.ScopePath ?? string.Empty,
                Kind = block.Kind,
                Number = block.Number,
                Language = block.Language,
                Capability = state,
                Source = state == RoundTripCapabilityStates.SupportedRoundTrip ||
                         state == RoundTripCapabilityStates.ExportOnly
                    ? new RoundTripArtifactV1
                    {
                        Artifact = sourceRelative,
                        Sha256 = canonicalSha256,
                        Format = block.Export.Format ?? "SimaticML",
                        NormalizationVersion = "simaticml-v1"
                    }
                    : null
            };
            WriteJson(Path.Combine(outputRoot, ToSystemPath(descriptorRelative)), descriptor);
            AddCapability(manifest, "block", blockId, state, reason);
            if (state != RoundTripCapabilityStates.SupportedRoundTrip)
                AddDiagnostic(manifest, "BLOCK_ROUND_TRIP_BLOCKED", reason ?? "Block is not round-trip capable.", blockId);
            return Tuple.Create(descriptorRelative, state);
        }

        private static Tuple<string, string> MaterializeTagTable(
            string plcId,
            string scopePath,
            string hintedName,
            IReadOnlyList<SnapshotTag> tags,
            RoundTripManifestV1 manifest,
            string outputRoot)
        {
            var sourceIdentity = string.IsNullOrEmpty(scopePath) ? hintedName ?? "unnamed-table" : scopePath;
            var tableId = SafeId("tag-table", sourceIdentity);
            var relative = "tia/plc/" + plcId + "/tags/" + tableId + ".json";
            var table = new RoundTripTagTableV1
            {
                Id = tableId,
                Name = hintedName ?? LastPathSegment(scopePath),
                ScopePath = scopePath ?? string.Empty,
                Capability = RoundTripCapabilityStates.SupportedRoundTrip
            };

            foreach (var sourceTag in tags.OrderBy(value => value.Id, StringComparer.Ordinal))
            {
                var state = DetermineTagState(sourceTag, out var reason);
                var tagId = SafeId("tag", sourceTag.Id);
                table.Tags.Add(new RoundTripTagV1
                {
                    Id = tagId,
                    Name = sourceTag.Name,
                    DataType = sourceTag.DataType,
                    Address = sourceTag.Address?.Raw,
                    CommentStatus = sourceTag.Comment?.Status ?? "unavailable",
                    Comment = sourceTag.Comment?.Text,
                    Capability = state
                });
                AddCapability(manifest, "tag", tagId, state, reason);
                if (state != RoundTripCapabilityStates.SupportedRoundTrip)
                    table.Capability = RoundTripCapabilityStates.Failed;
            }

            WriteJson(Path.Combine(outputRoot, ToSystemPath(relative)), table);
            AddCapability(manifest, "tag-table", tableId, table.Capability,
                table.Capability == RoundTripCapabilityStates.SupportedRoundTrip
                    ? null
                    : "One or more tags cannot be reconstructed without guessing.");
            return Tuple.Create(relative, table.Capability);
        }

        private static string DetermineTagState(SnapshotTag tag, out string reason)
        {
            if (tag == null || string.IsNullOrWhiteSpace(tag.Name) || string.IsNullOrWhiteSpace(tag.DataType) ||
                string.Equals(tag.DataType, "unknown", StringComparison.OrdinalIgnoreCase))
            {
                reason = "Tag identity/data type is incomplete.";
                return RoundTripCapabilityStates.Failed;
            }
            if (tag.Address == null || string.IsNullOrWhiteSpace(tag.Address.Raw))
            {
                reason = "v0.1 requires the raw PLC tag address.";
                return RoundTripCapabilityStates.Unsupported;
            }
            if (tag.Comment == null || tag.Comment.Status == "read-failed")
            {
                reason = "Tag comment evidence could not be read.";
                return RoundTripCapabilityStates.Failed;
            }
            if (tag.Comment.Status == "unavailable")
            {
                reason = "Tag comment semantics are unavailable.";
                return RoundTripCapabilityStates.Opaque;
            }
            reason = null;
            return RoundTripCapabilityStates.SupportedRoundTrip;
        }

        private static void AddCapability(
            RoundTripManifestV1 manifest,
            string kind,
            string objectRef,
            string state,
            string reason)
        {
            if (!RoundTripCapabilityStates.IsKnown(state))
                throw new InvalidOperationException("Unknown round-trip capability state: " + state);
            if (manifest.Capabilities.Any(value => string.Equals(value.ObjectRef, objectRef, StringComparison.Ordinal)))
                throw new InvalidOperationException("Duplicate capability objectRef: " + objectRef);
            manifest.Capabilities.Add(new RoundTripCapabilityV1
            {
                ObjectKind = kind,
                ObjectRef = objectRef,
                State = state,
                Reason = reason
            });
        }

        private static void AddDiagnostic(RoundTripManifestV1 manifest, string code, string message, string objectRef)
        {
            manifest.Diagnostics.Add(new RoundTripDiagnosticV1
            {
                Code = code,
                Message = message,
                ObjectRef = objectRef
            });
        }

        private static string SafeId(string prefix, string identity)
        {
            if (string.IsNullOrWhiteSpace(identity)) identity = prefix;
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(identity.Normalize(NormalizationForm.FormC)));
                var hex = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
                return prefix + "-" + hex.Substring(0, 16);
            }
        }

        private static string LastPathSegment(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "unnamed-table";
            var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            return segments.Length == 0 ? "unnamed-table" : Uri.UnescapeDataString(segments[segments.Length - 1]);
        }

        private static string ResolveInside(string root, string relative)
        {
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var full = Path.GetFullPath(Path.Combine(fullRoot, ToSystemPath(relative ?? string.Empty)));
            if (!full.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Artifact path escapes the block export directory.");
            return full;
        }

        private static string ToSystemPath(string relative)
        {
            return (relative ?? string.Empty).Replace('/', Path.DirectorySeparatorChar);
        }

        private static string NormalizeSimaticMlV1(string source, string destination)
        {
            var bytes = File.ReadAllBytes(source);
            var hasUtf8Bom = bytes.Length >= 3 &&
                             bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf;
            var offset = hasUtf8Bom ? 3 : 0;
            string text;
            try
            {
                text = new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
                var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
                using (var reader = XmlReader.Create(new StringReader(text), settings))
                    document.Load(reader);
                var root = document.DocumentElement;
                if (root == null || root.Name != "Document" || root.NamespaceURI.Length != 0)
                    throw new InvalidDataException("Unexpected SimaticML document root.");

                XmlElement documentInfo = null;
                foreach (XmlNode child in root.ChildNodes)
                {
                    if (child.NodeType != XmlNodeType.Element || child.Name != "DocumentInfo") continue;
                    if (documentInfo != null)
                        throw new InvalidDataException("Multiple root DocumentInfo elements.");
                    documentInfo = (XmlElement)child;
                }
                if (documentInfo == null || documentInfo.HasAttributes)
                    throw new InvalidDataException("Unexpected root DocumentInfo shape.");
                XmlElement created = null;
                foreach (XmlNode child in documentInfo.ChildNodes)
                {
                    if (child.NodeType != XmlNodeType.Element) continue;
                    if (created == null && child.Name != "Created")
                        throw new InvalidDataException("Created must be the first DocumentInfo element.");
                    if (child.Name != "Created") continue;
                    if (created != null)
                        throw new InvalidDataException("Multiple root Created elements.");
                    created = (XmlElement)child;
                }
                if (created == null || created.HasAttributes || created.ChildNodes.Count != 1 ||
                    created.FirstChild.NodeType != XmlNodeType.Text)
                    throw new InvalidDataException("Unexpected root Created shape.");
            }
            catch (XmlException error)
            {
                throw new InvalidDataException("Invalid SimaticML XML.", error);
            }
            catch (DecoderFallbackException error)
            {
                throw new InvalidDataException("Invalid SimaticML UTF-8.", error);
            }
            const string pattern = @"(<DocumentInfo>\s*<Created>)[^<]*(</Created>)";
            var matches = Regex.Matches(text, pattern, RegexOptions.CultureInvariant);
            if (matches.Count != 1)
                throw new InvalidDataException("Unexpected SimaticML DocumentInfo/Created shape.");

            var match = matches[0];
            const string canonicalCreated = "1970-01-01T00:00:00Z";
            var normalized = text.Substring(0, match.Groups[1].Index + match.Groups[1].Length) +
                             canonicalCreated +
                             text.Substring(match.Groups[2].Index);
            var payload = Encoding.UTF8.GetBytes(normalized);
            if (hasUtf8Bom)
            {
                var preamble = Encoding.UTF8.GetPreamble();
                var canonical = new byte[preamble.Length + payload.Length];
                Buffer.BlockCopy(preamble, 0, canonical, 0, preamble.Length);
                Buffer.BlockCopy(payload, 0, canonical, preamble.Length, payload.Length);
                File.WriteAllBytes(destination, canonical);
            }
            else
            {
                File.WriteAllBytes(destination, payload);
            }
            return Sha256File(destination);
        }

        private static string Sha256File(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static void WriteJson<T>(string path, T value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, RoundTripJson.Serialize(value), new UTF8Encoding(false));
        }
    }
}
