# Run with 64-bit Windows PowerShell 5.1 (.NET Framework), not pwsh/.NET 8.
# Imports metadata from the user's installed type library; does not register COM.
param(
    [Parameter(Mandatory = $true)][string]$TypeLibraryPath,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop' -or -not [Environment]::Is64BitProcess) {
    throw 'Use 64-bit Windows PowerShell 5.1 to import the installed type library.'
}
$TypeLibraryPath = (Resolve-Path -LiteralPath $TypeLibraryPath).Path
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw 'OutputDirectory must be new; existing files are never overwritten.' }
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
public sealed class TiaGuardLocalImportSink : ITypeLibImporterNotifySink {
    [DllImport("oleaut32.dll", CharSet=CharSet.Unicode)]
    private static extern int LoadTypeLibEx(string path, int kind, out ITypeLib library);
    public void ReportEvent(ImporterEventKind kind, int code, string message) { }
    public Assembly ResolveRef(object typeLib) {
        string name, doc, help; int context;
        ((ITypeLib)typeLib).GetDocumentation(-1, out name, out doc, out context, out help);
        if (Path.GetFileName(name) != name || String.IsNullOrWhiteSpace(name) || name == "TCatSysManagerLib")
            throw new InvalidDataException("Unexpected referenced type library.");
        string file = name + ".dll";
        if (File.Exists(file)) throw new IOException("Refusing to overwrite a referenced interop file.");
        var assembly = new TypeLibConverter().ConvertTypeLibToAssembly(typeLib, file,
            TypeLibImporterFlags.ImportAsX64, this, null, null, null, null);
        assembly.Save(file);
        return assembly;
    }
    public static void Import(string path, string output) {
        string previous = Environment.CurrentDirectory;
        ITypeLib library = null;
        try {
            Environment.CurrentDirectory = output;
            // REGKIND_NONE: load type metadata without registering it.
            Marshal.ThrowExceptionForHR(LoadTypeLibEx(path, 2, out library));
            string name, doc, help; int context;
            library.GetDocumentation(-1, out name, out doc, out context, out help);
            if (name != "TCatSysManagerLib") throw new InvalidDataException("Expected the installed TCatSysManagerLib type library.");
            var assembly = new TypeLibConverter().ConvertTypeLibToAssembly(library,
                "TCatSysManagerLib.dll", TypeLibImporterFlags.ImportAsX64,
                new TiaGuardLocalImportSink(), null, null, null, null);
            assembly.Save("TCatSysManagerLib.dll");
        } finally {
            if (library != null) Marshal.ReleaseComObject(library);
            Environment.CurrentDirectory = previous;
        }
    }
}
'@
[TiaGuardLocalImportSink]::Import($TypeLibraryPath, $OutputDirectory)
$result = Join-Path $OutputDirectory 'TCatSysManagerLib.dll'
if ([Reflection.AssemblyName]::GetAssemblyName($result).Name -ne 'TCatSysManagerLib') {
    throw 'Generated interop has an unexpected assembly identity.'
}
Write-Output "TC3_INTEROP=$result"
Write-Output 'Only local interop metadata was generated; COM registration, routes and licenses were not changed.'
