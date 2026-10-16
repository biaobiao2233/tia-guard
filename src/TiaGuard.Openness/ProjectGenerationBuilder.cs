// Project orchestration adapted from bulaofen0036-coder/TIA_Portal_Openness_MCP,
// MIT, Copyright (c) 2026 bulaofen0036-coder. See third-party/project-generation-imports/.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TiaGuard.Openness
{
    public static partial class ProjectGenerationBuilder
    {
        // The factory owns a new offline TIA instance, never attaches to an existing one.
        // The caller supplies the resource allocation and version-bound A/C adapters.
        public static ProjectGenerationBuildResult Build(ProjectGenerationBuilderPlan plan,
            string outputDirectory, string ownedScratchRoot,
            Func<string, ProjectGenerationBuilderPlan, IProjectGenerationSession> factory,
            Func<IProjectGenerationSession, ProjectGenerationCompileEvidence> compile,
            Func<IProjectGenerationSession, ProjectGenerationVerificationResult> verify)
        {
            var result = new ProjectGenerationBuildResult();
            IProjectGenerationSession session = null;
            string stage = null, publish = null;
            var stageOwned = false;
            var publishOwned = false;
            try
            {
                result.Phase = "preflight";
                if (factory == null || compile == null)
                    throw new InvalidOperationException("Generation session and C compile adapter are required; no unknown-to-zero fallback.");
                if (verify == null)
                    throw new InvalidOperationException("Independent full readback verification adapter is required; compilation alone cannot publish.");
                var units = ProjectGenerationImporter.Plan(plan);
                result.ContractRevision = plan.ContractRevision;
                result.IdentityRevision = plan.IdentityRevision;
                var root = Path.GetFullPath(plan.SourceRoot).TrimEnd(Path.DirectorySeparatorChar);
                var output = Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar);
                var scratch = Path.GetFullPath(ownedScratchRoot).TrimEnd(Path.DirectorySeparatorChar);
                if (Overlaps(root, output) || Overlaps(root, scratch) || Overlaps(output, scratch))
                    throw new IOException("Source, output and owned scratch must not overlap.");
                RequireNewOutput(output);
                FileSystemSafety.RequirePlainAncestors(scratch);
                if (!Directory.Exists(scratch)) throw new DirectoryNotFoundException("Caller-owned scratch root must already exist.");
                stage = Path.Combine(scratch, "g-" + Guid.NewGuid().ToString("N"));
                if (Path.Combine(stage, plan.ProjectName).Length > 143)
                    throw new PathTooLongException("V21 project directory exceeds 143 characters; allocate a shorter owned scratch root.");
                if (Directory.Exists(stage) || File.Exists(stage)) throw new IOException("Stage collision.");
                Directory.CreateDirectory(stage); stageOwned = true;
                // Copy and digest every input before any TIA call. Importers only see private files.
                var sources = new Dictionary<ProjectGenerationImportUnit, string>();
                foreach (var unit in units)
                {
                    var folder = Path.Combine(stage, "sources", Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(folder);
                    var path = Path.Combine(folder, Path.GetFileName(unit.SourcePath));
                    if (plan.ValidatedSourceBytes != null)
                        ProjectGenerationImporter.WriteValidatedBytes(plan.ValidatedSourceBytes[unit.SourcePath], unit.Sha256, path);
                    else ProjectGenerationImporter.CopyValidatedSource(root, unit.SourcePath, unit.Sha256, path);
                    if (unit.Format == "scl")
                    {
                        // Siemens requires UTF-8 BOM for non-ANSI external sources.
                        // Digest the original snapshot first; transform only the private import copy.
                        var text = new System.Text.UTF8Encoding(false, true).GetString(File.ReadAllBytes(path)).TrimStart('\ufeff');
                        File.WriteAllText(path, text, new System.Text.UTF8Encoding(true));
                    }
                    if (unit.ResourcePath != null)
                        ProjectGenerationImporter.CopyValidatedSource(root, unit.ResourcePath, unit.ResourceSha256,
                            Path.Combine(folder, Path.GetFileName(unit.ResourcePath)));
                    sources.Add(unit, path);
                }
                result.Phase = "create";
                session = factory(stage, plan) ?? throw new InvalidOperationException("Missing created session.");
                if (session.CpuTypeIdentifier != plan.CpuTypeIdentifier)
                    throw new InvalidOperationException("Created CPU identity differs; hardware fallback is forbidden.");
                var fresh = session.ReadObjects();
                // A fresh CPU may have only its default Main / OB1. No arbitrary deletion or Override.
                if (fresh == null || fresh.Count > 1 || fresh.Any(o => o == null || o.Kind != "OB" ||
                    o.Name != "Main" || o.Number != 1 || o.Group != "" || o.Protected || o.InstanceOfName != null))
                    throw new InvalidOperationException("Unexpected objects in the fresh CPU; refusing to delete them.");
                if (fresh.Count == 1) session.DeleteFreshMain();
                ProjectGenerationInventory.RequireExact(new ProjectGenerationObject[0], session.ReadObjects(), "fresh CPU after Main removal");
                session.CreateTagTables(plan.TagTables);
                var expected = new List<ProjectGenerationObject>();
                foreach (var unit in units)
                {
                    result.Phase = "import:" + unit.SourcePath;
                    var imported = session.Import(unit, sources[unit]);
                    if (imported != null) result.Imports.Add(imported);
                    if (imported == null || imported.State != "Success")
                        throw new InvalidOperationException("Import failed/partial/unknown for " + unit.SourcePath + ": " +
                            (imported?.State ?? "no result") + "; " + string.Join("; ", imported?.Messages ?? new string[0]));
                    ProjectGenerationInventory.RequireExact(unit.Expected, imported.Objects, "generated/imported " + unit.SourcePath);
                    expected.AddRange(unit.Expected);
                    // Includes auto-created DBs/types and catches modifications to previous units.
                    ProjectGenerationInventory.RequireExact(expected, session.ReadObjects(), "readback after " + unit.SourcePath);
                }
                session.Save();
                result.Phase = "compile";
                result.Compile = compile(session);
                if (result.Compile == null || !result.Compile.AllowsPublication)
                    throw new InvalidOperationException("Compilation missing/failed/incomplete; generated project will not be published.");
                ProjectGenerationInventory.RequireExact(expected, session.ReadObjects(), "readback after compile");
                result.Phase = "verify";
                result.Verification = verify(session);
                if (result.Verification == null || result.Verification.Verdict != "PASS")
                    throw new InvalidOperationException("Full readback verification did not PASS; no output will be published.");
                session.Save();
                var expectedFile = Path.Combine(stage, plan.ProjectName, plan.ProjectName + ".ap21");
                if (!string.Equals(Path.GetFullPath(session.ProjectFile), expectedFile, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Session returned a project outside the owned stage.");
                FileSystemSafety.RequirePlainFile(expectedFile);
                session.Dispose(); session = null;
                result.Phase = "publish";
                RequireNewOutput(output);
                var parent = Path.GetDirectoryName(output);
                publish = Path.Combine(parent, "." + Path.GetFileName(output) + ".generation-" + Guid.NewGuid().ToString("N"));
                if (Directory.Exists(publish) || File.Exists(publish)) throw new IOException("Publish stage collision.");
                // CopyPlainTree can leave a partial owned tree on failure, so fence before copying.
                publishOwned = true;
                FileSystemSafety.CopyPlainTree(Path.GetDirectoryName(expectedFile), publish);
                RequireNewOutput(output);
                Directory.Move(publish, output); publishOwned = false;
                result.ProjectFile = Path.Combine(output, plan.ProjectName + ".ap21");
                result.Published = true;
                result.Phase = "complete";
            }
            catch (Exception ex) { result.Failures.Add(ex.GetType().Name + ": " + ex.Message); }
            finally
            {
                if (session != null)
                    try { session.Dispose(); } catch (Exception ex) { result.CleanupFailures.Add("session: " + ex.Message); }
                if (publishOwned) Cleanup(publish, Path.GetDirectoryName(Path.GetFullPath(outputDirectory)), result);
                if (stageOwned) Cleanup(stage, Path.GetFullPath(ownedScratchRoot), result);
            }
            return result;
        }

        private static bool Overlaps(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            b.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        private static void RequireNewOutput(string output)
        {
            FileSystemSafety.RequirePlainAncestors(output);
            var parent = Path.GetDirectoryName(output);
            if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent) ||
                Directory.Exists(output) || File.Exists(output))
                throw new IOException("Output must be a new directory in an existing plain parent.");
        }
        private static void Cleanup(string path, string parent, ProjectGenerationBuildResult result)
        {
            try
            {
                var full = Path.GetFullPath(path);
                if (!string.Equals(Path.GetDirectoryName(full), parent.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Refusing cleanup outside the owned parent.");
                FileSystemSafety.DeleteOwnedTree(full);
            }
            catch (Exception ex) { result.CleanupFailures.Add(path + ": " + ex.Message); }
        }
    }
}
