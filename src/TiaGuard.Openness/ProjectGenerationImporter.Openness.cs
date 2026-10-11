// Adapted from heilingbrunner/tiaportal-mcp, MIT, Copyright (c) 2025 J.Heilingbrunner.
// Fixed source revision and full license: third-party/project-generation-imports/.
// SCL orchestration also derives from bulaofen0036-coder/TIA_Portal_Openness_MCP,
// MIT, Copyright (c) 2026 bulaofen0036-coder. See the module provenance document.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
using Siemens.Engineering.SW.Types;

namespace TiaGuard.Openness
{
    public static partial class ProjectGenerationBuilder
    {
        public static ProjectGenerationBuildResult BuildOffline(ProjectGenerationBuilderPlan plan,
            string outputDirectory, string ownedScratchRoot, string publicApiDirectory,
            Func<PlcSoftware, ProjectGenerationCompileEvidence> compile)
        {
            if (compile == null)
                return new ProjectGenerationBuildResult { Failures = { "C compile adapter is not bound." } };
            // Runtime initialization and instance creation happen only inside the preflighted factory.
            return Build(plan, outputDirectory, ownedScratchRoot, (stage, input) =>
            {
                OpennessAccess.RequireAccess();
                OpennessRuntime.Initialize(publicApiDirectory);
                return new ProjectGenerationOpennessSession(stage, input);
            }, session => compile((PlcSoftware)session.NativePlc));
        }
    }

    internal sealed class ProjectGenerationOpennessSession : IProjectGenerationSession
    {
        private TiaPortal portal;
        private Project project;
        private PlcSoftware plc;
        public object NativePlc => plc;
        public string ProjectFile { get; private set; }
        public string CpuTypeIdentifier { get; private set; }

        internal ProjectGenerationOpennessSession(string stage, ProjectGenerationBuilderPlan plan)
        {
            try
            {
                portal = new TiaPortal(TiaPortalMode.WithoutUserInterface);
                project = portal.Projects.Create(new DirectoryInfo(stage), plan.ProjectName);
                var device = project.Devices.CreateWithItem(plan.CpuTypeIdentifier, plan.PlcName, plan.PlcName + "_Station");
                plc = ProjectGenerationHardware.RequireExactPlc(device, plan.CpuTypeIdentifier);
                CpuTypeIdentifier = plan.CpuTypeIdentifier; // RequireExactPlc read and compared the created device item.
                ProjectFile = project.Path.FullName;
            }
            catch { Dispose(); throw; }
        }

        public void DeleteFreshMain()
        {
            var main = plc.BlockGroup.Blocks.Find("Main");
            if (!(main is OB) || main.Number != 1 || main.IsKnowHowProtected)
                throw new InvalidOperationException("Only the freshly observed default Main/OB1 may be removed.");
            main.Delete();
        }

        public void CreateTagTables(IReadOnlyList<ProjectGenerationEngineTagTable> tables)
        {
            if (plc.TagTableGroup.Groups.Any()) throw new InvalidOperationException("Unexpected fresh tag-table groups.");
            foreach (var existing in plc.TagTableGroup.TagTables.ToList())
            {
                if (existing.Tags.Any() || existing.UserConstants.Any())
                    throw new InvalidOperationException("Unexpected nonempty fresh tag table.");
                existing.Delete();
            }
            foreach (var wanted in tables)
            {
                var table = plc.TagTableGroup.TagTables.Create(wanted.Name);
                foreach (var tag in wanted.Tags)
                    table.Tags.Create(tag.Name, tag.DataType, tag.LogicalAddress);
            }
            var actual = plc.TagTableGroup.TagTables.ToList();
            if (actual.Count != tables.Count || plc.TagTableGroup.Groups.Any())
                throw new InvalidOperationException("Tag-table readback differs.");
            foreach (var wanted in tables)
            {
                var table = actual.Single(t => t.Name == wanted.Name);
                if (table.Tags.Count != wanted.Tags.Count || table.UserConstants.Any())
                    throw new InvalidOperationException("Tag count/constants differ.");
                foreach (var tag in wanted.Tags)
                {
                    var got = table.Tags.Find(tag.Name);
                    if (got == null || got.Name != tag.Name || got.DataTypeName != tag.DataType ||
                        !SameAddress(got.LogicalAddress, tag.LogicalAddress))
                        throw new InvalidOperationException("Tag name/type/address readback differs: " + tag.Name);
                }
            }
        }

        private static bool SameAddress(string a, string b)
        {
            var x = SnapshotAddressParser.Parse(a); var y = SnapshotAddressParser.Parse(b);
            return x.ParseStatus == "parsed" && y.ParseStatus == "parsed" &&
                x.Area == y.Area && x.ByteOffset == y.ByteOffset && x.BitOffset == y.BitOffset && x.BitWidth == y.BitWidth;
        }

        public ProjectGenerationImportResult Import(ProjectGenerationImportUnit unit, string copiedSource)
        {
            FileSystemSafety.RequirePlainFile(copiedSource);
            if (unit.Format == "scl")
            {
                // No reflection fallback to different methods/options and no KeepOnError.
                var source = plc.ExternalSourceGroup.ExternalSources.CreateFromFile(
                    "_TiaGuard_" + Guid.NewGuid().ToString("N") + ".scl", copiedSource);
                try
                {
                    var generated = source.GenerateBlocksFromSource(GenerateBlockOption.None);
                    return new ProjectGenerationImportResult
                    {
                        State = generated == null ? "Unknown" : "Success",
                        Objects = generated?.Select(Describe).ToArray()
                    };
                }
                finally { source.Delete(); }
            }
            if (unit.Format == "simatic-sd")
            {
                // Directory contains only this private .s7dcl and its explicitly declared .s7res.
                var directory = new DirectoryInfo(Path.GetDirectoryName(copiedSource));
                var stem = Path.GetFileNameWithoutExtension(copiedSource);
                if (unit.Expected.All(o => o.Kind == "UDT"))
                {
                    var imported = plc.TypeGroup.Types.ImportFromDocuments(directory, stem, ImportDocumentOptions.None);
                    return new ProjectGenerationImportResult
                    {
                        State = imported?.State.ToString() ?? "Unknown",
                        Objects = imported?.ImportedPlcTypes?.Select(t => Describe(t)).ToArray(),
                        Messages = imported?.Messages?.Select(m => m.Message).ToArray() ?? new string[0]
                    };
                }
                var blocks = plc.BlockGroup.Blocks.ImportFromDocuments(directory, stem, ImportDocumentOptions.None);
                return new ProjectGenerationImportResult
                {
                    State = blocks?.State.ToString() ?? "Unknown",
                    Objects = blocks?.ImportedPlcBlocks?.Select(b => Describe(b)).ToArray(),
                    Messages = blocks?.Messages?.Select(m => m.Message).ToArray() ?? new string[0]
                };
            }
            if (unit.Format == "simaticml")
            {
                if (unit.Expected.All(o => o.Kind == "UDT"))
                {
                    var types = plc.TypeGroup.Types.Import(new FileInfo(copiedSource), ImportOptions.None);
                    return new ProjectGenerationImportResult { State = types == null ? "Unknown" : "Success",
                        Objects = types?.Select(t => Describe(t)).ToArray() };
                }
                var blocks = plc.BlockGroup.Blocks.Import(new FileInfo(copiedSource), ImportOptions.None);
                return new ProjectGenerationImportResult { State = blocks == null ? "Unknown" : "Success",
                    Objects = blocks?.Select(b => Describe(b)).ToArray() };
            }
            throw new NotSupportedException("Unsupported import format.");
        }

        public IReadOnlyList<ProjectGenerationObject> ReadObjects()
        {
            var objects = new List<ProjectGenerationObject>();
            WalkBlocks(plc.BlockGroup, objects);
            WalkTypes(plc.TypeGroup, objects);
            return objects;
        }
        private void WalkBlocks(PlcBlockGroup group, List<ProjectGenerationObject> objects)
        {
            foreach (var block in group.Blocks) objects.Add(Describe(block));
            foreach (var child in group.Groups) WalkBlocks(child, objects);
        }
        private void WalkTypes(PlcTypeGroup group, List<ProjectGenerationObject> objects)
        {
            foreach (var type in group.Types) objects.Add(Describe(type));
            foreach (var child in group.Groups) WalkTypes(child, objects);
        }
        private ProjectGenerationObject Describe(IEngineeringObject item)
        {
            if (item is PlcBlock block)
                return new ProjectGenerationObject
                {
                    Kind = block is OB ? "OB" : block is FB ? "FB" : block is FC ? "FC" :
                        block is GlobalDB || block is InstanceDB || block is ArrayDB ? "DB" : "Unknown",
                    Name = block.Name, Number = block.Number, Language = block.ProgrammingLanguage.ToString(),
                    InstanceOfName = (block as InstanceDB)?.InstanceOfName,
                    Group = GroupPath(block.Parent, plc.BlockGroup), Protected = block.IsKnowHowProtected
                };
            if (item is PlcType type)
                return new ProjectGenerationObject { Kind = "UDT", Name = type.Name,
                    Group = GroupPath(type.Parent, plc.TypeGroup) };
            throw new InvalidOperationException("Source generated an unknown object: " + item?.GetType().FullName);
        }
        private static string GroupPath(IEngineeringObject parent, IEngineeringObject root)
        {
            var path = new List<string>();
            for (var current = parent; current != null && !current.Equals(root); current = current.Parent)
            {
                if (current is PlcBlockGroup b) path.Insert(0, b.Name);
                else if (current is PlcTypeGroup t) path.Insert(0, t.Name);
                else throw new InvalidOperationException("Object is outside its expected PLC group.");
            }
            return string.Join("/", path);
        }
        public void Save() => project.Save();
        public void Dispose()
        {
            var failures = new List<Exception>();
            if (project != null)
                try { project.Close(); project = null; } catch (Exception ex) { failures.Add(ex); }
            if (portal != null)
                try { portal.Dispose(); portal = null; } catch (Exception ex) { failures.Add(ex); }
            if (failures.Count != 0) throw new AggregateException("Failed to close the owned TIA session.", failures);
        }
    }
}
