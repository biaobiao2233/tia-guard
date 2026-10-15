using Microsoft.Win32;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using TwinCat3.Contracts;

namespace TwinCat3.EnvironmentHost;

public static class EnvironmentDoctor
{
    public static EnvironmentReport Inspect(string? twinCatInteropAssemblyPath = null)
    {
        if (!OperatingSystem.IsWindows()) return new EnvironmentReport
        { IsWindows = false, ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(), Issues = new[] { "Windows COM environment is required." } };
        var issues = new List<string>();
        var assemblies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var registrations = new List<ComRegistration>();
        string? installation = null;
        using (var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
        using (var version = machine.OpenSubKey(@"SOFTWARE\Beckhoff\TwinCAT3\3.1"))
            installation = version?.GetValue("InstallDir") as string;
        if (installation is null) issues.Add("TwinCAT 3.1 installation directory was not found in the installation registry.");
        var builds = installation is not null && Directory.Exists(Path.Combine(installation, "Components", "Base"))
            ? Directory.EnumerateDirectories(Path.Combine(installation, "Components", "Base"), "Build_*")
                .Select(Path.GetFileName).Select(n => n!["Build_".Length..]).Order(StringComparer.Ordinal).ToArray()
            : Array.Empty<string>();
        var template = installation is null ? null : Path.Combine(installation, "Components", "Base", "PrjTemplate", "TwinCAT Project.tsproj");
        if (template is not null && !File.Exists(template)) { issues.Add("TwinCAT configuration project template is missing."); template = null; }
        foreach (var progId in new[] { "TcXaeShell.DTE.17.0", "TcXaeShell.DTE.15.0", "TcSysManagerRM" })
        {
            using var clsidKey = Registry.ClassesRoot.OpenSubKey(progId + @"\CLSID");
            var classId = clsidKey?.GetValue("") as string;
            string? server = null;
            if (classId is not null)
            {
                using var localServer = Registry.ClassesRoot.OpenSubKey(@"CLSID\" + classId + @"\LocalServer32");
                using var inprocServer = Registry.ClassesRoot.OpenSubKey(@"CLSID\" + classId + @"\InprocServer32");
                server = localServer?.GetValue("") as string ?? inprocServer?.GetValue("") as string;
            }
            registrations.Add(new ComRegistration { ProgId = progId, ClassId = classId, ServerPath = server, Registered = classId is not null });
        }
        var publicCandidates = new[]
        {
            Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles), "Beckhoff", "TcXaeShell", "Common7", "IDE", "PublicAssemblies"),
            Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFilesX86), "Beckhoff", "TcXaeShell", "Common7", "IDE", "PublicAssemblies")
        };
        var publicRoot = publicCandidates.FirstOrDefault(p => File.Exists(Path.Combine(p, "envdte80.dll")));
        if (publicRoot is not null)
            foreach (var name in new[] { "envdte", "envdte80", "Microsoft.VisualStudio.Interop", "stdole" })
            {
                var path = Path.Combine(publicRoot, name + ".dll");
                if (File.Exists(path)) assemblies[AssemblyName.GetAssemblyName(path).Name!] = path;
            }
        else issues.Add("XAE DTE public interop assemblies are unavailable.");
        if (twinCatInteropAssemblyPath is not null)
        {
            var path = Path.GetFullPath(twinCatInteropAssemblyPath);
            GenerationInputLoader.RequireNoReparseComponents(path);
            if (!File.Exists(path) || AssemblyName.GetAssemblyName(path).Name != "TCatSysManagerLib")
                throw new InvalidDataException("TwinCatInteropAssemblyPath must be a local TCatSysManagerLib interop assembly.");
            assemblies["TCatSysManagerLib"] = path;
            var standardOle = Path.Combine(Path.GetDirectoryName(path)!, "stdole.dll");
            if (File.Exists(standardOle)) assemblies["stdole"] = standardOle;
        }
        var capabilities = new Dictionary<string, CapabilityObservation>
        {
            ["dteRegistration"] = new() { Available = registrations.Any(r => r.ProgId.StartsWith("TcXaeShell.") && r.Registered), State = "detected", Reason = "Registry observation; no IDE session was instantiated." },
            ["standaloneRegistration"] = new() { Available = registrations.Any(r => r.ProgId == "TcSysManagerRM" && r.Registered), State = "detected", Reason = "Registry observation, not a functional COM/build qualification." },
            ["configurationTemplate"] = new() { Available = template is not null, State = "detected", Reason = "Installed template file and hash." },
            ["ownedDteSession"] = new() { State = "unprobed", Reason = "Requires an explicit fresh owned session probe." },
            ["standaloneBuild16"] = new() { State = "unprobed", Reason = "ITcSysManager16 must be queried and its build/diagnostics tested separately." },
            ["plcPlusPlusCli"] = new() { State = "unprobed", Reason = "Official product announces CLI; no installed executable/command has been qualified." },
            ["generation"] = new() { State = "unqualified", Reason = "Creation, compilation and independent readback have not been qualified by Doctor." },
            ["runtimeWrites"] = new() { Available = false, State = "disabled", Reason = "This adapter phase is offline only." }
        };
        using var identity = WindowsIdentity.GetCurrent();
        return new EnvironmentReport
        {
            IsWindows = true, ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            CurrentTokenAdministrator = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator),
            InstallDirectory = installation, InstalledBuilds = builds, ComRegistrations = registrations,
            ProjectTemplatePath = template, ProjectTemplateHash = template is null ? null : Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(template))).ToLowerInvariant(),
            PlcTemplateNames = new[] { "Empty PLC Template" }, InteropAssemblyPaths = assemblies, Capabilities = capabilities, Issues = issues
        };
    }
}
