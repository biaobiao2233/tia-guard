#if TWINCAT_DTE && TWINCAT_COM
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using EnvDTE;
using EnvDTE80;
using TCatSysManagerLib;
using C = TwinCat3.Contracts;

namespace TiaGuard.TwinCat.Validation;

internal static class NativeReadback
{
    public static C.ProjectCapture Capture(C.FrozenGenerationInput input, C.NativeProjectHandle handle,
        C.NativeEngineeringContext context, CancellationToken cancellationToken)
    {
        var failures = new List<string>();
        var unsupported = new List<string>();
        var artifacts = new List<C.CapturedArtifact>();
        var tasks = new List<C.CapturedTask>();
        var libraries = new List<C.CapturedLibrary>();
        var plcTaskItems = new List<ITcSmTreeItem>();
        ITcSmTreeItem? references = null;
        var evidence = new List<string>();
        var saved = false; var closed = false; var reopened = false;
        C.MotionSpec? motion = null; bool motionComplete = false;
        var objectComplete = false; var tasksComplete = false; var librariesComplete = false;
        string? solutionName = null, configurationName = null, plcName = null, toolBuild = null, platform = null, projectIdentity = null;
        var evidenceRoot = Path.Combine(handle.StageRoot, "_readback", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidenceRoot);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dte = context.Dte as DTE2 ?? throw new InvalidOperationException("DTE2 is unavailable.");
            if (!string.Equals(Path.GetFullPath(dte.Solution.FullName), Path.GetFullPath(handle.SolutionPath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Different solution is open in the owned session.");
            dte.ExecuteCommand("File.SaveAll", "");
            dte.Solution.SaveAs(handle.SolutionPath);
            saved = true;
            dte.Solution.Close(true);
            closed = !dte.Solution.IsOpen;
            if (!closed) throw new InvalidOperationException("Owned solution did not close.");
            context.TwinCatProject = null;
            context.SystemManager = null;
            dte.Solution.Open(handle.SolutionPath);
            reopened = dte.Solution.IsOpen && string.Equals(Path.GetFullPath(dte.Solution.FullName), Path.GetFullPath(handle.SolutionPath), StringComparison.OrdinalIgnoreCase);
            if (!reopened) throw new InvalidOperationException("Owned solution did not reopen.");
            solutionName = Path.GetFileNameWithoutExtension(dte.Solution.FullName);
            var projects = dte.Solution.Projects;
            if (projects.Count != 1) unsupported.Add("Expected one TwinCAT configuration project; extra or missing solution projects.");
            Project? project = null;
            for (var i = 1; i <= projects.Count; i++)
            {
                var current = projects.Item(i);
                if (string.Equals(Path.GetFullPath(current.FullName), Path.GetFullPath(handle.TwinCatProjectPath), StringComparison.OrdinalIgnoreCase)) project = current;
                else unsupported.Add("Unexpected solution project: " + current.Name);
            }
            if (project == null) throw new InvalidOperationException("Reopened solution does not contain the bound TwinCAT project.");
            configurationName = project.Name;
            var manager = (ITcSysManager)project.Object;
            context.TwinCatProject = project;
            context.SystemManager = manager;
            var tipc = manager.LookupTreeItem("TIPC");
            var plcRoots = Children(tipc);
            if (plcRoots.Count != 1) unsupported.Add("Expected one PLC project; extra or missing PLC roots.");
            var plcRoot = plcRoots.Single(x => x.Name == handle.PlcProjectName);
            plcName = plcRoot.Name;
            projectIdentity = C.NativeProjectHandle.ComputeIdentity(project.FullName, configurationName, plcName);
            if (projectIdentity != handle.ProjectIdentity) throw new InvalidOperationException("Reopened actual project identity changed.");
            context.BoundProjectIdentity = projectIdentity;
            context.BoundProjectPath = project.FullName;
            platform = ((SolutionConfiguration2)dte.Solution.SolutionBuild.ActiveConfiguration).PlatformName;
            var actualTargetPlatform = ((ITcConfigManager)((ITcSysManager7)manager).ConfigurationManager).ActiveTargetPlatform;
            if (platform != actualTargetPlatform) failures.Add("VS build platform differs from actual TwinCAT target platform.");
            toolBuild = ReadXml(handle.TwinCatProjectPath).Root?.Attribute("TcVersion")?.Value;
            if (string.IsNullOrWhiteSpace(toolBuild)) failures.Add("Actual project TcVersion could not be captured.");
            var actualConfiguration = C.MotionConfiguration.Read(handle.TwinCatProjectPath);
            motion = C.MotionConfiguration.Capture(actualConfiguration, handle.PlcProjectName);
            var ncInventory = Children(manager.LookupTreeItem("TINC"));
            if ((motion is null && ncInventory.Count != 0) || (motion is not null && ncInventory.Count != 1))
                throw new InvalidDataException("Saved NC configuration and reopened native inventory differ.");
            if (motion is not null)
            {
                var actualAxis = manager.LookupTreeItem("TINC^" + motion.TaskName + " SAF^Axes^" + motion.AxisName);
                SaveXml("nc-axis", actualAxis.ProduceXml());
                SaveXml("nc-task", ncInventory.Single().ProduceXml());
                SaveXml("nc-configuration", actualConfiguration.Root!.Element("Project")!.Element("Motion")!.ToString());
                SaveXml("nc-mappings", actualConfiguration.Root.Element("Mappings")!.ToString());
            }
            motionComplete = true;
            var plcProject = (ITcSmTreeItem)((ITcProjectRoot)plcRoot).NestedProject;
            SaveXml("plc-project", plcProject.ProduceXml());
            var systemTasks = Children(manager.LookupTreeItem("TIRT"));
            var taskMap = systemTasks.ToDictionary(x => x.Name, StringComparer.Ordinal);
            Walk(plcProject, 0);
            objectComplete = true;
            foreach (var task in plcTaskItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var linked = ((ITcPlcTaskReference)task).LinkedTask;
                if (string.IsNullOrEmpty(linked) || !linked.StartsWith("TIRT^", StringComparison.Ordinal))
                    throw new NotSupportedException("Unproven PLC task system-link form: " + linked);
                var systemName = linked[5..];
                if (!taskMap.TryGetValue(systemName, out var systemTask)) throw new InvalidDataException("Linked system task is absent: " + linked);
                var systemXml = ParseXml(systemTask.ProduceXml());
                SaveXml("system-task-" + systemTask.Name, systemXml.ToString());
                var taskProjection = TaskXmlProjection.Read(systemXml.ToString());
                unsupported.AddRange(taskProjection.Unknowns.Select(x => systemTask.PathName + ": " + x));
                if (!taskProjection.CycleTimeUs.HasValue || !taskProjection.Priority.HasValue) throw new InvalidDataException("Task cycle/priority projection is incomplete.");
                var programs = new List<string>();
                foreach (var child in Children(task))
                {
                    if (child.ItemType != (int)TREEITEMTYPES.TREEITEMTYPE_PLCPROGREF)
                    { SaveXml("unknown-task-child-" + task.Name + "-" + child.Name, child.ProduceXml()); unsupported.Add("Unexpected PLC task child: " + child.PathName + ":" + child.ItemType); continue; }
                    // Read the native reference name in COM child order, never populate from input.
                    programs.Add(child.Name);
                    SaveXml("task-program-" + task.Name + "-" + child.Name, child.ProduceXml());
                }
                if (programs.Count == 0) failures.Add("Task has no captured program references: " + task.PathName);
                tasks.Add(new C.CapturedTask { Name = task.Name, CycleTimeUs = taskProjection.CycleTimeUs.Value, Priority = taskProjection.Priority.Value, Programs = programs, LinkedSystemTask = linked });
                SaveXml("plc-task-" + task.Name, task.ProduceXml());
            }
            foreach (var systemTask in systemTasks)
                if (!tasks.Any(t => t.LinkedSystemTask == "TIRT^" + systemTask.Name)) unsupported.Add("Unbound additional system task: " + systemTask.Name);
            tasksComplete = !unsupported.Any(x => x.Contains("TaskDef", StringComparison.Ordinal) || x.Contains("task metadata", StringComparison.Ordinal) || x.Contains("task child", StringComparison.Ordinal));
            if (references == null) throw new InvalidDataException("PLC library References node is absent.");
            var referencesXml = ParseXml(references.ProduceXml());
            SaveXml("references", referencesXml.ToString());
            var nativeReferences = ((ITcPlcLibraryManager)references).References;
            var referenceCount = nativeReferences.Count;
            if (referenceCount < 0 || referenceCount > 10000) throw new InvalidDataException("Invalid or oversized library reference collection.");
            // ITcPlcReferences uses a documented zero-based indexer, unlike ErrorItems/tree children.
            for (var index = 0; index < referenceCount; index++)
            {
                var reference = nativeReferences[index];
                if (reference is ITcPlcPlaceholderRef placeholder)
                {
                    // Build 4026 injects this virtual system library. Validate its actual
                    // resolved identity; it is not an explicit user library from the manifest.
                    if (reference.Name == "#Tc3_GlobalTypes" && placeholder.PlaceholderName == "Tc3_GlobalTypes"
                        && GeneratedLibraryProjection.IsSystemGlobalTypes(referencesXml.ToString())) continue;
                    unsupported.Add("Placeholder library is outside this supported slice: " + reference.Name);
                    continue;
                }
                if (reference is not ITcPlcLibrary library) throw new NotSupportedException("Library reference does not expose its resolved library identity: " + reference.Name);
                libraries.Add(new C.CapturedLibrary { Name = library.Name, Version = library.Version,
                    Vendor = library.Distributor, ResolvedVersion = library.Version, IsPlaceholder = false });
            }
            if (nativeReferences.Count != referenceCount) throw new InvalidOperationException("Library references changed during enumeration.");
            librariesComplete = true;
        }
        catch (Exception ex) { failures.Add($"Native independent readback failed: {ex.GetType().Name}: {ex.Message}"); }
        return new C.ProjectCapture
        {
            ProjectIdentity = projectIdentity, InputHash = input.InputHash,
            SolutionName = solutionName, ConfigurationName = configurationName, PlcProjectName = plcName,
            ToolBuild = toolBuild, Platform = platform, Saved = saved, Closed = closed, Reopened = reopened,
            ObjectsComplete = objectComplete, TasksComplete = tasksComplete, LibrariesComplete = librariesComplete,
            Artifacts = artifacts, Tasks = tasks, Libraries = libraries, Motion = motion, MotionComplete = motionComplete,
            Unsupported = unsupported, Failures = failures, RawEvidencePaths = evidence
        };

        void Walk(ITcSmTreeItem item, int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (depth > 20 || artifacts.Count > 10000) throw new InvalidDataException("PLC inventory exceeded a bounded capture limit.");
            foreach (var child in Children(item))
            {
                var type = child.ItemType;
                if (type == 601) { Walk(child, depth + 1); continue; }
                if (type == 617)
                {
                    if (references != null) throw new InvalidDataException("Duplicate References nodes.");
                    references = child;
                    continue;
                }
                if (type == 621) { plcTaskItems.Add(child); continue; }
                if (type == 653)
                {
                    var tmcXml = ParseXml(child.ProduceXml());
                    SaveXml("generated-tmc-" + child.Name, tmcXml.ToString());
                    var file = tmcXml.Root?.Element("TmcFileDef")?.Element("FileName")?.Value;
                    var generated = tmcXml.Root?.Element("TmcFileDef")?.Element("Document")?.Element("TcModuleClass");
                    var expected = Path.Combine(Path.GetDirectoryName(handle.TwinCatProjectPath)!, handle.PlcProjectName, handle.PlcProjectName + ".tmc");
                    if (child.Name != handle.PlcProjectName + ".tmc" || Children(child).Count != 0 || file == null
                        || !Path.GetFullPath(file).Equals(expected, StringComparison.OrdinalIgnoreCase) || !File.Exists(file)
                        || generated?.Attribute("GeneratedBy")?.Value != "TwinCAT XAE Plc"
                        || string.IsNullOrEmpty(generated.Attribute("Hash")?.Value)
                        || generated.Attribute("Hash")?.Value != ReadXml(file).Root?.Attribute("Hash")?.Value)
                        unsupported.Add("Unproven generated TMC identity: " + child.PathName);
                    continue;
                }
                var kind = type switch { 602 => C.ArtifactKind.Program, 603 => C.ArtifactKind.Function,
                    604 => C.ArtifactKind.FunctionBlock, 615 => C.ArtifactKind.GlobalVariableList,
                    606 => C.ArtifactKind.Struct, 605 => C.ArtifactKind.Enum, _ => (C.ArtifactKind?)null };
                if (kind == null) { SaveXml("unknown-item-" + child.Name, child.ProduceXml()); unsupported.Add("Unsupported PLC inventory: " + child.PathName + ":" + type); continue; }
                var declaration = ((ITcPlcDeclaration)child).DeclarationText;
                string? implementation = null;
                var opaque = false;
                if (kind is C.ArtifactKind.Program or C.ArtifactKind.Function or C.ArtifactKind.FunctionBlock)
                {
                    var implementationItem = (ITcPlcImplementation)child;
                    if ((int)implementationItem.Language != 1) { unsupported.Add("Non-ST PLC object: " + child.PathName); opaque = true; }
                    implementation = implementationItem.ImplementationText;
                }
                if (Children(child).Count != 0) { unsupported.Add("Nested object members are outside this slice: " + child.PathName); opaque = true; }
                artifacts.Add(new C.CapturedArtifact { Name = child.Name, Kind = kind.Value,
                    DeclarationText = declaration, ImplementationText = implementation, TreePath = child.PathName, NativeItemType = type, Opaque = opaque });
                SaveXml("object-" + child.Name, child.ProduceXml());
            }
        }
        void SaveXml(string name, string xml)
        {
            var safeName = string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
            var path = Path.Combine(evidenceRoot, evidence.Count.ToString("D4", CultureInfo.InvariantCulture) + "-" + safeName + ".xml");
            File.WriteAllText(path, xml, new UTF8Encoding(false));
            evidence.Add(path);
        }
    }

    private static IReadOnlyList<ITcSmTreeItem> Children(ITcSmTreeItem parent)
    {
        var count = parent.ChildCount;
        if (count < 0 || count > 10000) throw new InvalidDataException("Invalid or oversized tree collection.");
        var children = new List<ITcSmTreeItem>();
        for (var index = 1; index <= count; index++) children.Add(parent.get_Child(index) ?? throw new InvalidDataException("Missing tree child at index " + index));
        if (parent.ChildCount != count) throw new InvalidOperationException("Tree collection changed during capture.");
        return children;
    }
    private static XDocument ParseXml(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 20000000 });
        return XDocument.Load(reader);
    }
    private static XDocument ReadXml(string path) => ParseXml(File.ReadAllText(path));
}
#endif
