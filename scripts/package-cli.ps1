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

$project = Join-Path $repoRoot "src\TiaGuard.Cli\TiaGuard.Cli.csproj"
dotnet build $project -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "CLI build failed with exit code $LASTEXITCODE."
}

$bin = Join-Path $repoRoot "src\TiaGuard.Cli\bin\$Configuration\net48"
$packageName = "tia-guard-v0.1-win-x64"
$stage = Join-Path $OutputDirectory $packageName
$zip = Join-Path $OutputDirectory ($packageName + ".zip")

New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue
Remove-Item -Force $zip -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $stage | Out-Null

$required = @(
    "tia-guard.exe",
    "tia-guard.exe.config",
    "TiaGuard.Openness.dll"
)
foreach ($name in $required) {
    $source = Join-Path $bin $name
    if (-not (Test-Path $source)) {
        throw "Required package file is missing: $source"
    }
    Copy-Item $source $stage
}

if (Get-ChildItem $stage -Filter "Siemens*.dll" -File) {
    throw "Refusing to package Siemens DLLs."
}

$readme = @"
TIA-Guard bounded v0.1 demo package

Requirements:
- Windows x64
- TIA Portal V21 installed
- TIA Portal Openness installed/enabled
- Current Windows logon token has effective Siemens TIA Openness group membership

Commands:
  tia-guard export <project.ap21> <repo-dir>
  tia-guard build <repo-dir> --output <new-output-dir>
  tia-guard verify <original.ap21> <rebuilt.ap21>

Scope:
- bounded V21 S7-1200 demo profile only
- no PLC online/download/write operations
- Siemens DLLs, licenses and project binaries are not redistributed
"@
[IO.File]::WriteAllText((Join-Path $stage "README.txt"), $readme, [Text.UTF8Encoding]::new($false))

Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -CompressionLevel Optimal

Write-Output "package=$zip"
Write-Output "sha256=$((Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant())"
