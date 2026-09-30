param(
    [string]$Configuration = "Release",
    [string]$OutputDirectory = "",
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version = "0.1.0"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "artifacts\release"
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

$cliProject = Join-Path $repoRoot "src\TiaGuard.Cli\TiaGuard.Cli.csproj"
$guiProject = Join-Path $repoRoot "src\TiaGuard.Gui\TiaGuard.Gui.csproj"

dotnet build $cliProject -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "CLI build failed with exit code $LASTEXITCODE."
}

dotnet build $guiProject -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "GUI build failed with exit code $LASTEXITCODE."
}

$cliBin = Join-Path $repoRoot "src\TiaGuard.Cli\bin\$Configuration\net48"
$guiBin = Join-Path $repoRoot "src\TiaGuard.Gui\bin\$Configuration\net48"
$packageName = "tia-guard-v$Version-windows-x64"
$stage = Join-Path $OutputDirectory $packageName
$zip = Join-Path $OutputDirectory ($packageName + ".zip")

New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$removed = $false
$removeError = $null
for ($attempt = 1; $attempt -le 8; $attempt++) {
    try {
        if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
        $removed = -not (Test-Path $stage)
        if ($removed) { break }
    } catch {
        $removeError = $_
        Start-Sleep -Seconds (2 * $attempt)
    }
}
if (-not $removed) { throw "Could not replace the previous Windows package stage. $removeError" }
Remove-Item -Force $zip -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $stage | Out-Null

$guiFiles = @(
    "TiaGuard.exe",
    "TiaGuard.exe.config",
    "TiaGuard.Openness.dll"
)
foreach ($name in $guiFiles) {
    $source = Join-Path $guiBin $name
    if (-not (Test-Path $source)) {
        throw "Required GUI package file is missing: $source"
    }
    Copy-Item $source $stage
}

$cliFiles = @(
    "tia-guard.exe",
    "tia-guard.exe.config"
)
foreach ($name in $cliFiles) {
    $source = Join-Path $cliBin $name
    if (-not (Test-Path $source)) {
        throw "Required CLI package file is missing: $source"
    }
    Copy-Item $source $stage
}

$guiCoreHash = (Get-FileHash (Join-Path $guiBin "TiaGuard.Openness.dll") -Algorithm SHA256).Hash
$cliCoreHash = (Get-FileHash (Join-Path $cliBin "TiaGuard.Openness.dll") -Algorithm SHA256).Hash
if ($guiCoreHash -ne $cliCoreHash) {
    throw "CLI and GUI were built against different TiaGuard.Openness.dll content."
}

if (Get-ChildItem $stage -Filter "Siemens*.dll" -File -Recurse) {
    throw "Refusing to package Siemens DLLs."
}

$bridgeScript = Join-Path $PSScriptRoot "package-bridge.ps1"
& $bridgeScript -Configuration $Configuration -OutputDirectory $OutputDirectory -Version $Version
if ($LASTEXITCODE -ne 0) {
    throw "Bridge package failed with exit code $LASTEXITCODE."
}
$bridgeStage = Join-Path $OutputDirectory "tia-guard-bridge-v$Version-windows-x64"
$bundledBridge = Join-Path $stage "bridge"
if (-not (Test-Path (Join-Path $bridgeStage "tia-guard-bridge.exe"))) {
    throw "Bridge runtime was not staged."
}
New-Item -ItemType Directory -Force $bundledBridge | Out-Null
Copy-Item (Join-Path $bridgeStage "*") $bundledBridge -Recurse -Force
if (-not (Test-Path (Join-Path $bundledBridge "tia-guard-bridge.exe"))) {
    throw "Bundled bridge executable is missing."
}
if (-not (Test-Path (Join-Path $bundledBridge "worker\TiaGuard.Bridge.Worker.exe"))) {
    throw "Bundled bridge worker is missing."
}

if (Get-ChildItem $stage -Filter "Siemens*.dll" -File -Recurse) {
    throw "Refusing to package Siemens DLLs."
}

$readme = @"
TIA-Guard v$Version Windows x64 pre-alpha package

中文说明: docs/GETTING-STARTED.md
English guide: docs/GETTING-STARTED.en.md
Core CLI version remains 0.1.0; the package version identifies this preview.
See the release notes for exact verification scope.

Human UI:
  Double-click TiaGuard.exe

The window starts a local AI Gateway. A local Cursor, Codex, or other desktop agent can use it without a copied address. A cloud chat page cannot reach this machine just because it has the skill.

Runtime layout:
  bridge\tia-guard-bridge.exe
  bridge\worker\TiaGuard.Bridge.Worker.exe

Automation / AI CLI:
  tia-guard doctor
  tia-guard --version
  tia-guard ai-view <tia-source-dir>
  tia-guard export <project.ap21> <repo-dir>
  tia-guard build <repo-dir> --output <new-output-dir>
  tia-guard verify <original.ap21> <rebuilt.ap21>

Requirements:
- Windows x64
- Git for Windows available as git.exe (existing Git Credential Manager / SSH credentials are reused)
- TIA Portal V21 installed
- TIA Portal Openness installed/enabled
- Current Windows logon token has effective Siemens TIA Openness group membership

GUI product flows:
- one Git repository can contain many TIA projects under repo/tia-projects/<slot>/tia-source/
- Git repository URL -> clone/pull -> choose project -> fresh compiled .ap21 -> supported semantic verification; binary identity is reported separately
- owned .ap21 + Git repository URL -> choose existing project or add new slot -> safe Export staging -> commit -> push
- each publish changes only the selected project's tia-source/; sibling projects and normal repository files are untouched
- legacy repo/tia-source/ single-project repositories remain supported
- TIA-Guard never stores GitHub passwords/tokens; it reuses the system Git/GitHub credential chain
- Export / Build / Verify remain available under advanced diagnostics

Scope:
- bounded V21 S7-1200 demo profile only
- no PLC online/download/write operations
- Siemens DLLs, licenses and TIA project binaries are not redistributed
"@
[IO.File]::WriteAllText(
    (Join-Path $stage "README.txt"),
    $readme,
    [Text.UTF8Encoding]::new($false))

$docStage = Join-Path $stage "docs"
New-Item -ItemType Directory -Force $docStage | Out-Null
foreach ($name in @("GETTING-STARTED.md", "GETTING-STARTED.en.md", "TIA-AI-BRIDGE.md", "TIA-AI-BRIDGE.en.md")) {
    Copy-Item (Join-Path $repoRoot ("docs\" + $name)) $docStage
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipOk = $false
$zipError = $null
for ($attempt = 1; $attempt -le 8; $attempt++) {
    try {
        if (Test-Path $zip) { Remove-Item -Force $zip }
        [IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip)
        $zipOk = $true
        break
    } catch {
        $zipError = $_
        Start-Sleep -Seconds (2 * $attempt)
    }
}
if (-not $zipOk) { throw "Could not zip the Windows package. $zipError" }

Write-Output "package=$zip"
Write-Output "sha256=$((Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant())"
Write-Output "files=$((Get-ChildItem $stage -File | Sort-Object Name | ForEach-Object Name) -join ',')"
