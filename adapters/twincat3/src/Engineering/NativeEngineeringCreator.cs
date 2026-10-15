// Offline API sequence adapted from Beckhoff TC_AI_DOTNET_Samples (0BSD),
// rassrollers TcAutomationInterface (MIT), and Edge-JB TwinCAT-XAE-MCP (MIT).
// Exact pins, source paths, changes and full notices: ../../THIRD_PARTY.Engineering.md.
using System.Security.Cryptography;
using TwinCat3.Contracts;
#if TWINCAT_NATIVE_INTEROP
using TCatSysManagerLib;
using EnvDTE80;
#endif

namespace TwinCat3.Engineering;

/// <summary>Creates only a checked, empty stage in a fresh, owned DTE session.</summary>
public sealed class NativeEngineeringCreator : IEngineeringCreator
{
    private readonly EngineeringAllocation allocation;
    private readonly StageAuthorizationRegistry authorizations;

    public NativeEngineeringCreator(EngineeringAllocation allocation, StageAuthorizationRegistry authorizations)
    {
        this.allocation = allocation ?? throw new ArgumentNullException(nameof(allocation));
        this.authorizations = authorizations ?? throw new ArgumentNullException(nameof(authorizations));
    }

    public Task<NativeProjectHandle> CreateAsync(FrozenGenerationInput input, string stageRoot,
        IOwnedEngineeringSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(session);
        string stage = allocation.CheckStage(stageRoot);
        authorizations.Require(stage, session.SessionId, input.InputHash);
        if (Directory.EnumerateFileSystemEntries(stage).Any())
            throw new IOException("Native creation requires an empty owned stage.");
        foreach (string name in new[] { input.Manifest.SolutionName, input.Manifest.ConfigurationName, input.Manifest.PlcProjectName })
            EngineeringPaths.RequireIdentifier(name);
        if (input.Manifest.Environment.Provider != "dte" || input.Manifest.Environment.PlcTemplate != "Empty PLC Template")
            throw new NotSupportedException("The first native authoring provider requires DTE and Empty PLC Template.");
        if (input.Manifest.Tasks.Any(t => t.Programs.Count != 1))
            throw new NotSupportedException("Each task must have exactly one program call in v1.");
#if TWINCAT_NATIVE_INTEROP
        return session.InvokeAsync(context => CreateOnSta(input, stage, context), cancellationToken);
#else
        throw new NotSupportedException("Native authoring was built without the machine-local TwinCAT interop assembly.");
#endif
    }

#if TWINCAT_NATIVE_INTEROP
    private static NativeProjectHandle CreateOnSta(FrozenGenerationInput input, string stage, NativeEngineeringContext context)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("Native engineering must execute on the owned STA.");
        if (context.Dte is null || context.BoundProjectPath is not null || context.SystemManager is not null)
            throw new InvalidOperationException("A fresh, unbound owned DTE session is required.");
        GenerationManifest manifest = input.Manifest;
        string template = context.Environment.ProjectTemplatePath
            ?? throw new InvalidOperationException("The detected TwinCAT project template is unavailable.");
        string expectedTemplateHash = context.Environment.ProjectTemplateHash
            ?? throw new InvalidOperationException("The detected project template has no identity hash.");
        string actualTemplateHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(template))).ToLowerInvariant();
        if (actualTemplateHash != expectedTemplateHash)
            throw new InvalidOperationException("The detected project template changed before creation.");
        if (!context.Environment.PlcTemplateNames.Contains(manifest.Environment.PlcTemplate, StringComparer.Ordinal))
            throw new InvalidOperationException("The requested PLC template was not detected in the owned environment.");

        dynamic dte = context.Dte;
        dynamic solution = dte.Solution;
        if ((int)solution.Projects.Count != 0 || !string.IsNullOrEmpty((string)solution.FullName))
            throw new InvalidOperationException("The owned DTE unexpectedly has an existing solution.");
        string solutionPath = Path.Combine(stage, manifest.SolutionName + ".sln");
        solution.Create(stage, manifest.SolutionName);
        solution.SaveAs(solutionPath);
        string configurationDirectory = Path.Combine(stage, manifest.ConfigurationName);
        dynamic project = solution.AddFromTemplate(template, configurationDirectory, manifest.ConfigurationName);
        if (project is null) throw new InvalidOperationException("AddFromTemplate did not return the new TwinCAT project.");
        string projectPath = Path.GetFullPath((string)project.FullName);
        string expectedPath = Path.Combine(configurationDirectory, manifest.ConfigurationName + ".tsproj");
        if (!projectPath.Equals(expectedPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The created TwinCAT project path does not match the owned allocation.");
        if ((string)project.Name != manifest.ConfigurationName)
            throw new InvalidOperationException("The created configuration name differs from the frozen input.");

        var manager = (ITcSysManager)project.Object;
        context.TwinCatProject = (object)project;
        context.SystemManager = manager;
        context.BoundProjectPath = projectPath;
        context.BoundProjectIdentity = NativeProjectHandle.ComputeIdentity(projectPath, manifest.ConfigurationName, manifest.PlcProjectName);
        ITcSmTreeItem plcRoot = manager.LookupTreeItem("TIPC").CreateChild(manifest.PlcProjectName, 0, "", manifest.Environment.PlcTemplate);
        AssertChild(plcRoot, manifest.PlcProjectName, "TIPC");
        ITcSmTreeItem plcProject = (ITcSmTreeItem)((ITcProjectRoot)plcRoot).NestedProject;
        if (!plcProject.PathName.StartsWith(plcRoot.PathName + "^", StringComparison.Ordinal))
            throw new InvalidOperationException("The new PLC nested project is outside the owned PLC root.");

        ITcSmTreeItem pous = GetOrCreateFolder(plcProject, "POUs");
        ITcSmTreeItem gvls = GetOrCreateFolder(plcProject, "GVLs");
        ITcSmTreeItem duts = GetOrCreateFolder(plcProject, "DUTs");
        // Create all independent types before declarations referencing them; source order is otherwise retained.
        foreach (ArtifactSpec artifact in manifest.Artifacts.OrderBy(a => a.Kind is ArtifactKind.Struct or ArtifactKind.Enum ? 0 : 1))
        {
            EngineeringPaths.RequireIdentifier(artifact.Name);
            string declaration = input.Files[artifact.DeclarationFile].Text;
            int subtype = artifact.Kind switch
            {
                ArtifactKind.Program => 602, ArtifactKind.Function => 603, ArtifactKind.FunctionBlock => 604,
                ArtifactKind.GlobalVariableList => 615, ArtifactKind.Struct => 606, ArtifactKind.Enum => 605,
                _ => throw new NotSupportedException("The artifact kind is not allowed for offline authoring.")
            };
            ITcSmTreeItem parent = artifact.Kind switch
            {
                ArtifactKind.Struct or ArtifactKind.Enum => duts,
                ArtifactKind.GlobalVariableList => gvls,
                _ => pous
            };
            if (Children(parent).Any(child => child.Name.Equals(artifact.Name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("The new template already contains a requested object; it will not be overwritten.");
            object info = artifact.Kind switch
            {
                ArtifactKind.Program or ArtifactKind.FunctionBlock => new object[] { 1 },
                ArtifactKind.Function => new object[] { 1, FunctionReturnType(declaration, artifact.Name) },
                _ => declaration
            };
            ITcSmTreeItem item = parent.CreateChild(artifact.Name, subtype, "", info);
            AssertChild(item, artifact.Name, parent.PathName);
            ((ITcPlcDeclaration)item).DeclarationText = declaration;
            if (artifact.ImplementationFile is not null)
                ((ITcPlcImplementation)item).ImplementationText = input.Files[artifact.ImplementationFile].Text;
        }

        ITcSmTreeItem taskCollection = manager.LookupTreeItem("TIRT");
        foreach (TaskSpec task in manifest.Tasks)
        {
            EngineeringPaths.RequireIdentifier(task.Name);
            if (Children(taskCollection).Any(child => child.Name.Equals(task.Name, StringComparison.OrdinalIgnoreCase))
                || Children(plcProject).Any(child => child.Name.Equals(task.Name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("A stock task collides with the requested task; it will not be overwritten.");
            ITcSmTreeItem systemTask = taskCollection.CreateChild(task.Name, 1, "", null);
            AssertChild(systemTask, task.Name, "TIRT");
            long ticks = checked(task.CycleTimeUs * 10);
            systemTask.ConsumeXml($"<TreeItem><TaskDef><CycleTime>{ticks}</CycleTime><Priority>{task.Priority}</Priority></TaskDef></TreeItem>");
            ITcSmTreeItem plcTask = plcProject.CreateChild(task.Name, 621, "", task.Programs[0]);
            AssertChild(plcTask, task.Name, plcProject.PathName);
            ((ITcPlcTaskReference)plcTask).LinkedTask = systemTask.PathName;
        }

        ITcSmTreeItem references = manager.LookupTreeItem(plcProject.PathName + "^References");
        var libraries = (ITcPlcLibraryManager)references;
        foreach (LibrarySpec library in manifest.Libraries)
        {
            if (string.IsNullOrWhiteSpace(library.Version) || library.Version.Contains('*'))
                throw new InvalidOperationException("A fixed installed library version is required.");
            libraries.AddLibrary(library.Name, library.Version, library.Vendor);
        }
        if (manifest.Motion is { } motion)
        {
            var ncRoot = manager.LookupTreeItem("TINC");
            if (ncRoot.ChildCount != 0 || manager.LookupTreeItem("TIID").ChildCount != 0)
                throw new InvalidOperationException("Motion creation requires an empty motion/I/O inventory.");
            var ncTask = ncRoot.CreateChild(motion.TaskName, 1, "", null);
            if (ncTask.PathName != "TINC^" + motion.TaskName + " SAF")
                throw new InvalidOperationException("Unexpected NC task identity.");
            ncTask.ConsumeXml($"<TreeItem><TaskDef><CycleTime>{checked(motion.CycleTimeUs * 10)}</CycleTime><Priority>4</Priority></TaskDef></TreeItem>");
            var axis = manager.LookupTreeItem(ncTask.PathName + "^Axes").CreateChild(motion.AxisName, 1, "", null);
            AssertChild(axis, motion.AxisName, ncTask.PathName + "^Axes");
            axis.ConsumeXml(MotionConfiguration.AxisXml(motion));
        }
        // Offline build selection; this never selects or activates a live target device.
        var configurationManager = (ITcConfigManager)((ITcSysManager7)manager).ConfigurationManager;
        configurationManager.ActiveTargetPlatform = manifest.Environment.TargetPlatform;
        if (configurationManager.ActiveTargetPlatform != manifest.Environment.TargetPlatform)
            throw new InvalidOperationException("The owned configuration did not select the requested target platform.");
        dynamic build = solution.SolutionBuild;
        dynamic configurations = build.SolutionConfigurations;
        bool selected = false;
        var available = new List<string>();
        for (int index = 1; index <= (int)configurations.Count; index++)
        {
            var configuration = (SolutionConfiguration2)configurations.Item(index);
            string name = configuration.Name;
            string platform = configuration.PlatformName;
            available.Add(name + "|" + platform);
            if (name == manifest.Environment.BuildConfiguration && platform == manifest.Environment.TargetPlatform)
            { configuration.Activate(); selected = true; }
        }
        if (!selected) throw new NotSupportedException("The requested build configuration/platform is absent: " + string.Join(", ", available));
        if ((string)build.ActiveConfiguration.Name != manifest.Environment.BuildConfiguration
            || ((SolutionConfiguration2)build.ActiveConfiguration).PlatformName != manifest.Environment.TargetPlatform)
            throw new InvalidOperationException("The active build configuration differs from the frozen request.");
        // Save only: never generate boot projects, activate configuration or invoke a live target.
        dte.ExecuteCommand("File.SaveAll");
        solution.SaveAs(solutionPath);
        if (!File.Exists(solutionPath) || !File.Exists(projectPath)
            || !Directory.EnumerateFiles(stage, "*.plcproj", SearchOption.AllDirectories).Any())
            throw new IOException("XAE did not save the complete native solution/configuration/PLC project.");
        // Reload the saved native project so the newly added PLC's build configurations
        // and library references are initialized from disk before a compiler is invoked.
        var typedDte = (DTE2)context.Dte;
        typedDte.Solution.Close(true);
        if (typedDte.Solution.IsOpen) throw new InvalidOperationException("The new owned solution did not close before compiler initialization.");
        context.TwinCatProject = null;
        context.SystemManager = null;
        typedDte.Solution.Open(solutionPath);
        if (!typedDte.Solution.IsOpen || typedDte.Solution.Projects.Count != 1)
            throw new InvalidOperationException("The new owned solution did not reload completely.");
        var reloaded = typedDte.Solution.Projects.Item(1);
        if (!Path.GetFullPath(reloaded.FullName).Equals(projectPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A different configuration reloaded before compilation.");
        context.TwinCatProject = reloaded;
        context.SystemManager = (ITcSysManager)reloaded.Object;
        EngineeringPaths.CheckTree(stage);
        return new NativeProjectHandle
        {
            SolutionPath = solutionPath, TwinCatProjectPath = projectPath,
            PlcProjectName = manifest.PlcProjectName, StageRoot = stage,
            ProjectIdentity = context.BoundProjectIdentity, InputHash = input.InputHash
        };
    }

    private static ITcSmTreeItem GetOrCreateFolder(ITcSmTreeItem parent, string name)
    {
        ITcSmTreeItem? existing = Children(parent).SingleOrDefault(child => child.Name == name);
        if (existing is not null) return existing;
        ITcSmTreeItem item = parent.CreateChild(name, 601, "", null);
        AssertChild(item, name, parent.PathName);
        return item;
    }

    private static IEnumerable<ITcSmTreeItem> Children(ITcSmTreeItem parent)
    {
        int count = parent.ChildCount;
        for (int i = 1; i <= count; i++) yield return parent.Child[i];
    }

    private static void AssertChild(ITcSmTreeItem child, string name, string parentPath)
    {
        if (child is null || child.Name != name || child.PathName != parentPath + "^" + name)
            throw new InvalidOperationException("Native creation returned a malformed or foreign child.");
    }
#endif

    internal static string FunctionReturnType(string declaration, string name)
    {
        var match = System.Text.RegularExpressions.Regex.Match(declaration,
            @"\AFUNCTION\s+" + System.Text.RegularExpressions.Regex.Escape(name) + @"\s*:\s*([A-Za-z_][A-Za-z0-9_.]*)\s*(?:\r?\n|\z)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (!match.Success) throw new NotSupportedException("Function declarations require an explicit simple return type.");
        return match.Groups[1].Value;
    }
}
