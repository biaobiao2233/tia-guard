param(
    [string]$Configuration = 'Release',
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$TwinCatInteropAssemblyPath,
    [Parameter(Mandatory = $true)][string]$TwinCatVsPublicAssemblies,
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version = '0.2.0-alpha.1'
)
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$adapter = Join-Path $repoRoot 'adapters\twincat3'
$TwinCatInteropAssemblyPath = (Resolve-Path -LiteralPath $TwinCatInteropAssemblyPath).Path
$TwinCatVsPublicAssemblies = (Resolve-Path -LiteralPath $TwinCatVsPublicAssemblies).Path
if ([Reflection.AssemblyName]::GetAssemblyName($TwinCatInteropAssemblyPath).Name -ne 'TCatSysManagerLib' -or
    -not (Test-Path -LiteralPath (Join-Path $TwinCatVsPublicAssemblies 'envdte80.dll'))) {
    throw 'A concrete machine-local native binding is required; a portable/fake Worker cannot be released.'
}
if ([int]((& node --version) -replace '^v(\d+).*', '$1') -lt 22) { throw 'Node.js >=22 is required.' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$packageName = "tia-guard-twincat-v$Version-windows-x64"
$stage = Join-Path $OutputDirectory $packageName
$zip = Join-Path $OutputDirectory ($packageName + '.zip')
if ((Test-Path -LiteralPath $stage) -or (Test-Path -LiteralPath $zip)) {
    throw 'Use a fresh output directory; existing packages are never replaced.'
}
New-Item -ItemType Directory -Path $stage -Force | Out-Null
$worker = Join-Path $stage 'worker'
dotnet publish (Join-Path $adapter 'src\Worker\TwinCat3.Worker.csproj') -c $Configuration -r win-x64 --self-contained false -o $worker -m:1 /p:UseSharedCompilation=false "/p:TwinCatInteropAssemblyPath=$TwinCatInteropAssemblyPath" "/p:TwinCatVsPublicAssemblies=$TwinCatVsPublicAssemblies" --disable-build-servers
if ($LASTEXITCODE -ne 0) { throw 'Native TwinCAT Worker publish failed.' }
foreach ($name in @('TwinCat3.Worker.exe', 'TwinCat3.Worker.dll', 'TwinCat3.Runtime.dll', 'TwinCat3.Engineering.dll', 'TwinCat.Validation.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $worker $name))) { throw "Missing packaged file: $name" }
}
# NuGet ADS libraries are redistributable dependencies. Installed XAE/DTE interop,
# native vendor binaries and licenses must instead be supplied by the target host.
$forbidden = @(Get-ChildItem -LiteralPath $stage -File -Recurse | Where-Object {
    $_.Name -match '^(TCatSysManagerLib|EnvDTE(?:80)?|Microsoft\.VisualStudio\.Interop|stdole|TcAdsDll)\.dll$' -or
    $_.Name -like 'Siemens*.dll' -or $_.Extension -in @('.lic', '.pfx', '.key', '.ap21', '.tsproj')
})
if ($forbidden.Count -gt 0) { throw 'Refusing to package installed vendor files, licenses or engineering project binaries.' }
$mcp = Join-Path $stage 'adapters\twincat3\mcp'
New-Item -ItemType Directory -Path $mcp -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $adapter 'mcp') -File | Where-Object { $_.Extension -eq '.js' -or $_.Name -in @('package.json', 'package-lock.json', 'LICENSE', 'NOTICE') } | Copy-Item -Destination $mcp
Copy-Item -LiteralPath (Join-Path $adapter 'mcp\licenses') -Destination (Join-Path $mcp 'licenses') -Recurse
Push-Location $mcp
try {
    & npm.cmd ci --omit=dev --ignore-scripts --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) { throw 'Locked production MCP dependency installation failed.' }
    & npm.cmd run check
    if ($LASTEXITCODE -ne 0) { throw 'Packaged MCP syntax checks failed.' }
} finally { Pop-Location }
$adapterStage = Join-Path $stage 'adapters\twincat3'
foreach ($directory in @('docs', 'contracts', 'evidence', 'scripts')) {
    $target = Join-Path $adapterStage $directory
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    # Only versioned sources: restore-generated obj JSON contains local paths.
    $tracked = @(& git -C $repoRoot ls-files -- "adapters/twincat3/$directory")
    if ($LASTEXITCODE -ne 0) { throw 'Versioned package file enumeration failed.' }
    $tracked | Where-Object { [IO.Path]::GetExtension($_) -in @('.md', '.json', '.ps1') } | ForEach-Object {
            $relative = $_.Substring("adapters/twincat3/$directory/".Length)
            $destination = Join-Path $target $relative
            New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
            Copy-Item -LiteralPath (Join-Path $repoRoot $_) -Destination $destination
        }
}
Copy-Item -LiteralPath (Join-Path $adapter 'README.md') -Destination $adapterStage
$exampleStage = Join-Path $stage 'examples'
New-Item -ItemType Directory -Path $exampleStage | Out-Null
foreach ($name in @('twincat3-generation', 'twincat3-verification')) {
    Copy-Item -LiteralPath (Join-Path $repoRoot "examples\$name") -Destination $exampleStage -Recurse
}
$docs = Join-Path $stage 'docs'
New-Item -ItemType Directory -Path (Join-Path $docs 'releases') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\ADAPTER-PACKAGES.md') -Destination $docs
Copy-Item -LiteralPath (Join-Path $repoRoot "docs\releases\v$Version.md") -Destination (Join-Path $docs 'releases')
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination $stage
@"
TIA-Guard TwinCAT adapter v$Version (alpha)

Start with docs/ADAPTER-PACKAGES.md, TwinCAT 3 section.
No npm install or source build is required for this package.
Requires Windows x64, Node.js >=22, .NET 8 Windows Desktop Runtime x64,
installed TwinCAT XAE and locally generated TCatSysManagerLib interop metadata.
Native Worker was compiled with the concrete XAE binding. Actual functionality
still depends on the target installation; doctor is not native qualification.

MCP: node adapters/twincat3/mcp/index.js
Worker: worker/TwinCat3.Worker.exe
Configure absolute TC3_WORKER_COMMAND, TC3_WORKSPACE, TC3_SCRATCH,
TC3_INTEROP and TC3_BUILD using the installation guide.
Offline writes and runtime writes are disabled unless explicitly enabled.
Engineering applications, installed vendor DLLs, license files and user projects
are not included. Public NuGet/Node dependencies retain their own licenses.
Core protocol versions remain 0.1.0; package version is v$Version.
"@ | Set-Content -LiteralPath (Join-Path $stage 'README.txt') -Encoding utf8
$sourceCommit = (& git -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Source identity unavailable.' }
@{ packageVersion = $Version; adapter = 'twincat3'; sourceCommit = $sourceCommit; nativeBindingCompiled = $true; runtimeQualification = 'See source-bound evidence; no new native runtime test implied by packaging.' } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'package-manifest.json') -Encoding utf8
# Preserve license information for public NuGet packages in the restore graph.
$assets = Get-Content -LiteralPath (Join-Path $adapter 'src\Worker\obj\project.assets.json') -Raw | ConvertFrom-Json
$notices = Join-Path $stage 'licenses\nuget'
foreach ($library in $assets.libraries.PSObject.Properties) {
    if ($library.Value.type -ne 'package') { continue }
    $packageRoot = $null
    foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
        $candidate = Join-Path $folder $library.Value.path
        if (Test-Path -LiteralPath $candidate) { $packageRoot = $candidate; break }
    }
    if ($null -eq $packageRoot) { throw "Restored package missing: $($library.Name)" }
    $destination = Join-Path $notices ($library.Name -replace '/', '-')
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Get-ChildItem -LiteralPath $packageRoot -File | Where-Object { $_.Extension -eq '.nuspec' -or $_.Name -match '(?i)license|notice|copying' } |
        Copy-Item -Destination $destination
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip)
Write-Output "package=$zip"
Write-Output "sha256=$((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant())"
