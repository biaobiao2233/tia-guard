using System;
using System.IO;

namespace TiaGuard.Openness
{
    public static partial class RoundTripVerifier
    {
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
                FileSystemSafety.RequirePlainAncestors(scratch);
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

        private static void DeleteOwnedScratch(string scratch, string parent)
        {
            var full = Path.GetFullPath(scratch).TrimEnd(Path.DirectorySeparatorChar);
            var expectedParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(Path.GetDirectoryName(full), expectedParent,
                    StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParseExact(Path.GetFileName(full), "N", out _))
                throw new InvalidOperationException("Refusing to remove an unowned verify scratch directory.");
            FileSystemSafety.DeleteOwnedTree(full);
        }
    }
}
