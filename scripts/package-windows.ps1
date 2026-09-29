param(
    [string]$Configuration = "Release",
    [string]$OutputDirectory = ""
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
$packageName = "tia-guard-v0.1.0-windows-x64"
$stage = Join-Path $OutputDirectory $packageName
$zip = Join-Path $OutputDirectory ($packageName + ".zip")

New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue
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

$readme = @"
TIA-Guard v0.1.0 Windows x64 package

Human UI:
  Double-click TiaGuard.exe

Automation / AI CLI:
  tia-guard doctor
  tia-guard --version
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
- Git repository URL -> clone/pull -> repo/tia-source/ -> fresh compiled .ap21 -> automatic integrity check
- owned .ap21 + Git repository URL -> safe Export staging -> repo/tia-source/ -> commit -> push
- TIA-Guard never stores a GitHub token and manages only repo/tia-source/
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

Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -CompressionLevel Optimal

Write-Output "package=$zip"
Write-Output "sha256=$((Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant())"
Write-Output "files=$((Get-ChildItem $stage -File | Sort-Object Name | ForEach-Object Name) -join ',')"
