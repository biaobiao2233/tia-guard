using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;

namespace TiaGuard.Openness
{
    public sealed class RoundTripBuildResult
    {
        public string ProjectFile { get; internal set; }
        public int CompileErrors { get; internal set; }
        public int CompileWarnings { get; internal set; }
    }

    public static class RoundTripBuilder
    {
        public static RoundTripBuildResult Build(
            string sourceRoot, string outputDirectory, Action<string> progress = null)
        {
            progress?.Invoke("validate-source");
            var input = RoundTripBuildInput.Load(sourceRoot, outputDirectory);
            OpennessAccess.RequireAccess();
            OpennessRuntime.Initialize();
            return BuildCore(input, progress);
        }

        private static RoundTripBuildResult BuildCore(
            RoundTripBuildInput input, Action<string> progress)
        {
            // TIA Portal V21 applies its 143-character limit to the project directory
            // used during creation. Keep that internal path short and independent from
            // the user's Git clone/output path, then publish only after compile succeeds.
            var layout = RoundTripBuildLayout.Plan(input.Manifest.Project.Name);
            FileSystemSafety.RequirePlainAncestors(layout.WorkingRoot);
            Directory.CreateDirectory(layout.WorkingRoot);
            Directory.CreateDirectory(layout.StageDirectory);
            TiaPortal portal = null;
            Project project = null;
            try
            {
                progress?.Invoke("create-project");
                portal = new TiaPortal(TiaPortalMode.WithoutUserInterface);
                project = portal.Projects.Create(
                    new DirectoryInfo(layout.StageDirectory), input.Manifest.Project.Name);

                progress?.Invoke("create-device");
                var device = project.Devices.CreateWithItem(
                    input.Hardware.CreateTypeIdentifier,
                    input.Hardware.CreateItemName,
                    input.Hardware.Name);
                var softwares = new List<PlcSoftware>();
                foreach (var item in device.DeviceItems)
                    CollectPlcSoftware(item, softwares);
                if (softwares.Count != 1)
                    throw new InvalidOperationException(
                        "The created device does not contain one PLC software object.");
                var plc = softwares[0];

                progress?.Invoke("create-tags");
                CreateTagTables(plc, input.TagTables);

                progress?.Invoke("import-ob1");
                string importPath;
                using (var source = input.OpenValidatedBlockSource(layout.StageDirectory))
                {
                    importPath = source.Name;
                    var imported = plc.BlockGroup.Blocks.Import(new FileInfo(importPath), ImportOptions.Override);
                    if (imported == null || imported.Count != 1 || imported[0].Name != input.Block.Name)
                        throw new InvalidOperationException("SimaticML import did not produce exactly Main / OB1.");
                }
                File.Delete(importPath);

                progress?.Invoke("save");
                project.Save();
                progress?.Invoke("compile");
                var compiler = plc.GetService<ICompilable>();
                if (compiler == null)
                    throw new InvalidOperationException("PLC compile service is unavailable.");
                var compileResult = compiler.Compile();
                var compileErrors = compileResult.ErrorCount;
                var compileWarnings = compileResult.WarningCount;
                if (compileErrors != 0)
                    throw new InvalidOperationException("Rebuilt PLC has " +
                        compileErrors + " compile errors.");
                project.Save();

                var stagedFolder = layout.StagedProjectDirectory;
                var stagedProject = Path.Combine(stagedFolder,
                    input.Manifest.Project.Name + ".ap21");
                if (!File.Exists(stagedProject))
                    throw new FileNotFoundException(
                        "TIA did not save the expected V21 project file.", stagedProject);
                project.Close();
                project = null;
                portal.Dispose();
                portal = null;
                progress?.Invoke("publish");
                var publishedFileName = string.IsNullOrWhiteSpace(
                    input.Manifest.Project.OriginalFileName)
                    ? input.Manifest.Project.Name + ".ap21"
                    : input.Manifest.Project.OriginalFileName;
                PublishBuiltProject(
                    stagedFolder,
                    input.OutputDirectory,
                    input.Manifest.Project.Name + ".ap21",
                    publishedFileName);
                DeleteOwnedStage(layout.StageDirectory, layout.WorkingRoot);
                progress?.Invoke("done");
                return new RoundTripBuildResult
                {
                    ProjectFile = Path.Combine(input.OutputDirectory, publishedFileName),
                    CompileErrors = compileErrors,
                    CompileWarnings = compileWarnings
                };
            }
            catch
            {
                try { project?.Close(); } catch { }
                try { portal?.Dispose(); } catch { }
                DeleteOwnedStage(layout.StageDirectory, layout.WorkingRoot);
                throw;
            }
        }

        private static void PublishBuiltProject(
            string stagedFolder,
            string outputDirectory,
            string generatedProjectFileName,
            string publishedProjectFileName)
        {
            var output = Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar);
            var parent = Path.GetDirectoryName(output);
            var outputName = Path.GetFileName(output);
            var publishName = "." + outputName + ".tia-guard-publish-" +
                Guid.NewGuid().ToString("N");
            var publish = Path.Combine(parent, publishName);

            FileSystemSafety.RequirePlainAncestors(parent);
            if (Directory.Exists(output) || File.Exists(output))
                throw new IOException("The build output path already exists.");

            try
            {
                FileSystemSafety.CopyPlainTree(stagedFolder, publish);
                if (!string.Equals(generatedProjectFileName, publishedProjectFileName,
                        StringComparison.Ordinal))
                {
                    var generated = Path.Combine(publish, generatedProjectFileName);
                    var desired = Path.Combine(publish, publishedProjectFileName);
                    if (!File.Exists(generated) || File.Exists(desired))
                        throw new IOException(
                            "The rebuilt project file cannot be renamed to the original file name safely.");
                    File.Move(generated, desired);
                }
                if (Directory.Exists(output) || File.Exists(output))
                    throw new IOException("The build output path appeared while publishing.");
                Directory.Move(publish, output);
            }
            catch
            {
                DeleteOwnedPublish(publish, parent, outputName);
                throw;
            }
        }

        private static void CollectPlcSoftware(DeviceItem item, ICollection<PlcSoftware> result)
        {
            var software = item.GetService<SoftwareContainer>()?.Software as PlcSoftware;
            if (software != null) result.Add(software);
            foreach (var child in item.DeviceItems) CollectPlcSoftware(child, result);
        }

        private static void CreateTagTables(
            PlcSoftware plc, IReadOnlyList<RoundTripTagTableV1> wanted)
        {
            var desired = new HashSet<string>(wanted.Select(value => value.Name),
                StringComparer.OrdinalIgnoreCase);
            foreach (var existing in plc.TagTableGroup.TagTables.ToList())
            {
                if (desired.Contains(existing.Name)) continue;
                if (existing.Tags.Any() || existing.UserConstants.Any())
                    throw new InvalidOperationException(
                        "The fresh CPU contains a nonempty extra tag table.");
                existing.Delete();
            }
            foreach (var descriptor in wanted)
            {
                var table = plc.TagTableGroup.TagTables.Find(descriptor.Name) ??
                    plc.TagTableGroup.TagTables.Create(descriptor.Name);
                if (table.Tags.Any() || table.UserConstants.Any())
                    throw new InvalidOperationException(
                        "An existing tag table is not empty before reconstruction.");
                foreach (var tagDescriptor in descriptor.Tags)
                {
                    var tag = table.Tags.Create(tagDescriptor.Name,
                        tagDescriptor.DataType, tagDescriptor.Address ?? string.Empty);
                    if (tagDescriptor.CommentStatus != "present") continue;
                    var comment = tag.Comment?.Items.FirstOrDefault();
                    if (comment == null)
                        throw new InvalidOperationException(
                            "No active project language is available for a tag comment.");
                    comment.Text = tagDescriptor.Comment;
                }
            }
        }

        private static void DeleteOwnedStage(string stage, string workingRoot)
        {
            var full = Path.GetFullPath(stage).TrimEnd(Path.DirectorySeparatorChar);
            var expectedParent = Path.GetFullPath(workingRoot).TrimEnd(Path.DirectorySeparatorChar);
            const string prefix = "b-";
            var name = Path.GetFileName(full);
            if (!string.Equals(Path.GetDirectoryName(full), expectedParent,
                    StringComparison.OrdinalIgnoreCase) ||
                !name.StartsWith(prefix, StringComparison.Ordinal) ||
                !Guid.TryParseExact(name.Substring(prefix.Length), "N", out _))
                throw new InvalidOperationException("Refusing to remove an unowned build stage.");
            FileSystemSafety.DeleteOwnedTree(full);
        }

        private static void DeleteOwnedPublish(string publish, string parent, string outputName)
        {
            var full = Path.GetFullPath(publish).TrimEnd(Path.DirectorySeparatorChar);
            var expectedParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar);
            var prefix = "." + outputName + ".tia-guard-publish-";
            var name = Path.GetFileName(full);
            if (!string.Equals(Path.GetDirectoryName(full), expectedParent,
                    StringComparison.OrdinalIgnoreCase) ||
                !name.StartsWith(prefix, StringComparison.Ordinal) ||
                !Guid.TryParseExact(name.Substring(prefix.Length), "N", out _))
                throw new InvalidOperationException("Refusing to remove an unowned publish stage.");
            FileSystemSafety.DeleteOwnedTree(full);
        }
    }
}
