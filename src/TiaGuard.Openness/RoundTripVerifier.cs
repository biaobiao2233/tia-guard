using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace TiaGuard.Openness
{
    [DataContract]
    public sealed class RoundTripDifference
    {
        [DataMember(Name = "objectRef", Order = 0)]
        public string ObjectRef { get; set; }

        [DataMember(Name = "field", Order = 1)]
        public string Field { get; set; }
    }

    [DataContract]
    public sealed class RoundTripVerifyResult
    {
        [DataMember(Name = "contractVersion", Order = 0)]
        public string ContractVersion { get; set; } = "roundtrip-source-v1";

        [DataMember(Name = "verdict", Order = 1)]
        public string Verdict { get; set; } = "blocked";

        [DataMember(Name = "blockedCode", Order = 2)]
        public string BlockedCode { get; set; }

        [DataMember(Name = "blockedType", Order = 3)]
        public string BlockedType { get; set; }

        [DataMember(Name = "rebuiltCompileErrors", Order = 4)]
        public int? RebuiltCompileErrors { get; set; }

        [DataMember(Name = "rebuiltCompileWarnings", Order = 5)]
        public int? RebuiltCompileWarnings { get; set; }

        [DataMember(Name = "differences", Order = 6)]
        public List<RoundTripDifference> Differences { get; set; } =
            new List<RoundTripDifference>();

        [DataMember(Name = "differencesTruncated", Order = 7)]
        public bool DifferencesTruncated { get; set; }
    }

    public static class RoundTripVerifier
    {
        private const int MaxDifferences = 64;

        // Also used by the focused tests. The same strict source validation gates Build.
        public static RoundTripVerifyResult CompareSources(string originalRoot, string rebuiltRoot)
        {
            RoundTripBuildInput original;
            RoundTripBuildInput rebuilt;
            try { original = RoundTripBuildInput.LoadSource(originalRoot); }
            catch (Exception error) { return Blocked("ORIGINAL_SOURCE_INVALID", error); }
            try { rebuilt = RoundTripBuildInput.LoadSource(rebuiltRoot); }
            catch (Exception error) { return Blocked("REBUILT_SOURCE_INVALID", error); }
            return CompareValidated(original, rebuilt);
        }

        public static RoundTripVerifyResult VerifyProjects(
            string originalProjectFile, string rebuiltProjectFile, Action<string> progress = null)
        {
            string originalPath;
            string rebuiltPath;
            try
            {
                originalPath = Path.GetFullPath(originalProjectFile);
                rebuiltPath = Path.GetFullPath(rebuiltProjectFile);
                if (!string.Equals(Path.GetExtension(originalPath), ".ap21",
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(Path.GetExtension(rebuiltPath), ".ap21",
                        StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(originalPath) || !File.Exists(rebuiltPath))
                    return Blocked("PROJECT_INPUT_INVALID");
                if (string.Equals(originalPath, rebuiltPath, StringComparison.OrdinalIgnoreCase))
                    return Blocked("PROJECT_INPUTS_IDENTICAL");
            }
            catch (Exception error) { return Blocked("PROJECT_INPUT_INVALID", error); }

            string scratchParent;
            string scratch;
            try
            {
                scratchParent = Path.Combine(Path.GetTempPath(), "TiaGuard.Verify");
                scratch = Path.Combine(scratchParent, Guid.NewGuid().ToString("N"));
            }
            catch (Exception error) { return Blocked("VERIFY_SCRATCH_FAILED", error); }

            PlcCompileObservation observedCompile = null;
            RoundTripVerifyResult result;
            var failureCode = "VERIFY_SCRATCH_FAILED";
            try
            {
                Directory.CreateDirectory(scratch);
                failureCode = "VERIFY_INTERNAL_FAILED";
                result = VerifyInScratch(originalPath, rebuiltPath, scratch,
                    progress, compile => observedCompile = compile);
            }
            catch (Exception error) { result = Blocked(failureCode, error); }
            try { DeleteOwnedScratch(scratch, scratchParent); }
            catch (Exception error) { result = Blocked("VERIFY_CLEANUP_FAILED", error); }
            if (observedCompile != null)
            {
                result.RebuiltCompileErrors = observedCompile.Errors;
                result.RebuiltCompileWarnings = observedCompile.Warnings;
            }
            return result;
        }

        private static RoundTripVerifyResult VerifyInScratch(
            string originalPath, string rebuiltPath, string scratch,
            Action<string> progress, Action<PlcCompileObservation> observeCompile)
        {
            var originalRoot = Path.Combine(scratch, "original");
            var rebuiltRoot = Path.Combine(scratch, "rebuilt");
            try
            {
                progress?.Invoke("export-original");
                using (var session = TiaProjectSession.OpenOfflineCopy(originalPath))
                    session.ExportRoundTripSource(originalRoot);
            }
            catch (OpennessAccessException error) { return Blocked("OPENNESS_ACCESS_BLOCKED", error); }
            catch (Exception error) { return Blocked("ORIGINAL_EXPORT_FAILED", error); }

            try { RoundTripBuildInput.LoadSource(originalRoot); }
            catch (Exception error) { return Blocked("ORIGINAL_SOURCE_INVALID", error); }

            PlcCompileObservation compile;
            try
            {
                progress?.Invoke("compile-rebuilt-copy");
                using (var session = TiaProjectSession.OpenOfflineCopy(rebuiltPath))
                {
                    compile = session.CompilePlcForVerification();
                    observeCompile(compile);
                    if (compile.Errors == 0)
                    {
                        progress?.Invoke("export-rebuilt");
                        session.ExportRoundTripSource(rebuiltRoot);
                    }
                }
            }
            catch (OpennessAccessException error) { return Blocked("OPENNESS_ACCESS_BLOCKED", error); }
            catch (Exception error) { return Blocked("REBUILT_EXPORT_OR_COMPILE_FAILED", error); }

            if (compile.Errors != 0)
                return Blocked("REBUILT_COMPILE_ERRORS");

            progress?.Invoke("compare");
            var result = CompareSources(originalRoot, rebuiltRoot);
            progress?.Invoke("done");
            return result;
        }

        private static RoundTripVerifyResult CompareValidated(
            RoundTripBuildInput original, RoundTripBuildInput rebuilt)
        {
            var result = new RoundTripVerifyResult { Verdict = "pass" };
            Check(result, "project", "name", original.Manifest.Project.Name,
                rebuilt.Manifest.Project.Name);
            Check(result, "project", "tiaVersion", original.Manifest.TiaVersion,
                rebuilt.Manifest.TiaVersion);
            var a = original.Hardware;
            var b = rebuilt.Hardware;
            Check(result, "hardware", "id", a.Id, b.Id);
            Check(result, "hardware", "name", a.Name, b.Name);
            Check(result, "hardware", "engineeringPath", a.EngineeringPath, b.EngineeringPath);
            Check(result, "hardware", "deviceTypeIdentifier", a.DeviceTypeIdentifier,
                b.DeviceTypeIdentifier);
            Check(result, "hardware", "createTypeIdentifier", a.CreateTypeIdentifier,
                b.CreateTypeIdentifier);
            Check(result, "hardware", "createItemName", a.CreateItemName, b.CreateItemName);
            Check(result, "hardware", "orderNumber", a.OrderNumber, b.OrderNumber);
            Check(result, "hardware", "firmware", a.Firmware, b.Firmware);

            var pa = original.Plc;
            var pb = rebuilt.Plc;
            Check(result, "plc", "id", pa.Id, pb.Id);
            Check(result, "plc", "name", pa.Name, pb.Name);
            Check(result, "plc", "deviceId", pa.DeviceId, pb.DeviceId);
            CheckList(result, "plc", "tagTables", pa.TagTables, pb.TagTables);
            CheckList(result, "plc", "blocks", pa.Blocks, pb.Blocks);

            if (original.TagTables.Count != rebuilt.TagTables.Count)
                Add(result, "plc", "tagTables.count");
            for (var i = 0; i < Math.Min(original.TagTables.Count, rebuilt.TagTables.Count); i++)
            {
                var ta = original.TagTables[i];
                var tb = rebuilt.TagTables[i];
                var reference = "tag-table[" + i + "]";
                Check(result, reference, "id", ta.Id, tb.Id);
                Check(result, reference, "name", ta.Name, tb.Name);
                Check(result, reference, "scopePath", ta.ScopePath, tb.ScopePath);
                if (ta.Tags.Count != tb.Tags.Count) Add(result, reference, "tags.count");
                for (var j = 0; j < Math.Min(ta.Tags.Count, tb.Tags.Count); j++)
                {
                    var taga = ta.Tags[j];
                    var tagb = tb.Tags[j];
                    var tagRef = reference + "/tag[" + j + "]";
                    Check(result, tagRef, "id", taga.Id, tagb.Id);
                    Check(result, tagRef, "name", taga.Name, tagb.Name);
                    Check(result, tagRef, "dataType", taga.DataType, tagb.DataType);
                    Check(result, tagRef, "address", taga.Address, tagb.Address);
                    Check(result, tagRef, "commentStatus", taga.CommentStatus, tagb.CommentStatus);
                    Check(result, tagRef, "comment", taga.Comment, tagb.Comment);
                }
            }

            var ba = original.Block;
            var bb = rebuilt.Block;
            Check(result, "block", "id", ba.Id, bb.Id);
            Check(result, "block", "name", ba.Name, bb.Name);
            Check(result, "block", "scopePath", ba.ScopePath, bb.ScopePath);
            Check(result, "block", "kind", ba.Kind, bb.Kind);
            Check(result, "block", "number", ba.Number, bb.Number);
            Check(result, "block", "language", ba.Language, bb.Language);
            Check(result, "block", "source.format", ba.Source.Format, bb.Source.Format);
            Check(result, "block", "source.normalizationVersion",
                ba.Source.NormalizationVersion, bb.Source.NormalizationVersion);
            // Each artifact was hash-checked during LoadSource. The canonicalizer
            // removes only its versioned, explicitly classified volatile field.
            Check(result, "block", "source.sha256", ba.Source.Sha256, bb.Source.Sha256);

            if (result.Differences.Count != 0) result.Verdict = "mismatch";
            return result;
        }

        private static void Check<T>(RoundTripVerifyResult result, string reference,
            string field, T original, T rebuilt)
        {
            if (!EqualityComparer<T>.Default.Equals(original, rebuilt)) Add(result, reference, field);
        }

        private static void CheckList(RoundTripVerifyResult result, string reference,
            string field, IEnumerable<string> original, IEnumerable<string> rebuilt)
        {
            if (!original.SequenceEqual(rebuilt, StringComparer.Ordinal)) Add(result, reference, field);
        }

        private static void Add(RoundTripVerifyResult result, string reference, string field)
        {
            if (result.Differences.Count == MaxDifferences)
            {
                result.DifferencesTruncated = true;
                return;
            }
            result.Differences.Add(new RoundTripDifference
            {
                ObjectRef = reference, Field = field
            });
        }

        private static RoundTripVerifyResult Blocked(string code, Exception error = null)
        {
            return new RoundTripVerifyResult
            {
                BlockedCode = code,
                BlockedType = error?.GetType().Name
            };
        }

        private static void DeleteOwnedScratch(string scratch, string parent)
        {
            var full = Path.GetFullPath(scratch).TrimEnd(Path.DirectorySeparatorChar);
            var expectedParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(Path.GetDirectoryName(full), expectedParent,
                    StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParseExact(Path.GetFileName(full), "N", out _))
                throw new InvalidOperationException("Refusing to remove an unowned verify scratch directory.");
            if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
        }
    }
}
