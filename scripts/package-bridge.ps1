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

$hostProject = Join-Path $repoRoot "src\TiaGuard.Bridge.Host\TiaGuard.Bridge.Host.csproj"
$workerProject = Join-Path $repoRoot "src\TiaGuard.Bridge.Worker\TiaGuard.Bridge.Worker.csproj"

dotnet build $workerProject -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Bridge worker build failed with exit code $LASTEXITCODE."
}

$packageName = "tia-guard-bridge-v0.1.0-windows-x64"
$stage = Join-Path $OutputDirectory $packageName
$zip = Join-Path $OutputDirectory ($packageName + ".zip")
$publish = Join-Path $OutputDirectory ".bridge-publish"

New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force $publish -ErrorAction SilentlyContinue
Remove-Item -Force $zip -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $stage | Out-Null

dotnet publish $hostProject -c $Configuration -r win-x64 --self-contained true -o $publish
if ($LASTEXITCODE -ne 0) {
    throw "Bridge host publish failed with exit code $LASTEXITCODE."
}

Copy-Item (Join-Path $publish "*") $stage -Recurse -Force

$workerBin = Join-Path $repoRoot "src\TiaGuard.Bridge.Worker\bin\$Configuration\net48"
$workerFiles = @(
    "TiaGuard.Bridge.Worker.exe",
    "TiaGuard.Bridge.Worker.exe.config",
    "TiaGuard.Openness.dll"
)
foreach ($name in $workerFiles) {
    $source = Join-Path $workerBin $name
    if (-not (Test-Path $source)) {
        throw "Required Bridge worker package file is missing: $source"
    }
    Copy-Item $source $stage -Force
}

$bridgeExe = Join-Path $stage "tia-guard-bridge.exe"
if (-not (Test-Path $bridgeExe)) {
    throw "Published Bridge executable is missing: $bridgeExe"
}

if (Get-ChildItem $stage -Filter "Siemens*.dll" -File -Recurse) {
    throw "Refusing to package Siemens DLLs."
}

$help = & $bridgeExe --help 2>&1
if ($LASTEXITCODE -ne 0 -or ($help -join [Environment]::NewLine) -notmatch "TIA-Guard Bridge") {
    throw "Packaged Bridge --help smoke failed."
}

$readme = @"
TIA-Guard Bridge v0.1.0 experimental package

Purpose:
- external AI -> local TIA Portal V21 engineering bridge
- MCP stdio
- MCP Streamable HTTP on loopback
- plain loopback JSON API

Default read-only:
  tia-guard-bridge.exe --transport stdio
  tia-guard-bridge.exe --transport http --port 18761

Explicit guarded write mode:
  tia-guard-bridge.exe --transport stdio --allow-write
  tia-guard-bridge.exe --transport http --port 18761 --allow-write

Current write scope:
- disposable offline .ap21 copy only
- preview_tag_upsert -> single-use safety token -> apply_tag_upsert
- preview_publish_modified_copy -> separate single-use safety token -> apply_publish_modified_copy
- exact binding/request/current-state checks
- tag mutation remains in memory until explicit publish
- publish preserves the TIA project name, writes only to a NEW parent/destination, reopens, and verifies engineering contentId

Not exposed:
- writes to an attached user project
- attached-project save/overwrite or arbitrary archive (only guarded SaveAs-to-new-directory is exposed)
- PLC download
- CPU start/stop
- force
- online variable writes
- Safety operations

Requirements:
- Windows x64
- TIA Portal V21 installed
- TIA Portal Openness installed/enabled
- current Windows logon token has effective Siemens TIA Openness group membership

The package does not redistribute Siemens DLLs, licenses or TIA project binaries.
"@
[IO.File]::WriteAllText(
    (Join-Path $stage "README-BRIDGE.txt"),
    $readme,
    [Text.UTF8Encoding]::new($false))

Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -CompressionLevel Optimal
Remove-Item -Recurse -Force $publish -ErrorAction SilentlyContinue

Write-Output "package=$zip"
Write-Output "sha256=$((Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant())"
Write-Output "siemensDllCount=$((Get-ChildItem $stage -Filter 'Siemens*.dll' -File -Recurse | Measure-Object).Count)"
Write-Output "files=$((Get-ChildItem $stage -File | Sort-Object Name | ForEach-Object Name) -join ',')"
