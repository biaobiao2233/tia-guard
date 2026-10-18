# Opt-in offline V21 qualification. Never performs download, online or simulation operations.
param(
    [Parameter(Mandatory = $true)][string]$RunRoot,
    [Parameter(Mandatory = $true)][string]$CatalogEvidencePath,
    [Parameter(Mandatory = $true)][string]$PublicApiDirectory,
    [ValidateSet('minimal', 'multiblock', 'fc', 'bad-repair', 'baseline1200', 'all')]
    [string]$Case = 'minimal'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$cli = Join-Path $repo 'src\TiaGuard.Cli\bin\Release\net48\tia-guard.exe'
$core = Join-Path $repo 'src\TiaGuard.Cli\bin\Release\net48\TiaGuard.Openness.dll'
$cpu = 'OrderNumber:6ES7 511-1AK02-0AB0/V2.9'
$legacyCpu = 'OrderNumber:6ES7 212-1AE40-0XB0/V4.7'
if (-not (Test-Path -LiteralPath $cli) -or -not (Test-Path -LiteralPath $core)) { throw 'Build the candidate CLI before explicit native qualification.' }
$sourceChanges = @(& git -C $repo status --porcelain)
if ($LASTEXITCODE -ne 0 -or $sourceChanges.Count -ne 0) { throw 'Commit the exact candidate and build it before native qualification; dirty source cannot identify an accepted candidate.' }
$catalog = Get-Content -LiteralPath $CatalogEvidencePath -Raw | ConvertFrom-Json
if ($catalog.query -ne '1511' -or -not @($catalog.items | Where-Object { $_.TypeIdentifier -eq $cpu -and $_.TypeName -eq 'CPU 1511-1 PN' }).Count) {
    throw 'Exact native catalog observation is required before testing the S7-1500 candidate.'
}
$RunRoot = [IO.Path]::GetFullPath($RunRoot)
if (Test-Path -LiteralPath $RunRoot) { throw 'RunRoot must be new; never overwrite previous projects/evidence.' }
$parent = Split-Path $RunRoot
if (-not (Test-Path -LiteralPath $parent -PathType Container)) { throw 'RunRoot parent must already exist.' }
for ($item = Get-Item -LiteralPath $parent; $null -ne $item; $item = $item.Parent) {
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'RunRoot parent contains a reparse point.' }
}
if ($RunRoot.Length -gt 75) { throw 'Use a short RunRoot so the V21 staging path stays within its 143-character limit.' }
New-Item -ItemType Directory -Path $RunRoot | Out-Null
$identity = @{ sourceCommit = (& git -C $repo rev-parse HEAD).Trim(); cliSha256 = (Get-FileHash -LiteralPath $cli).Hash; coreSha256 = (Get-FileHash -LiteralPath $core).Hash;
    catalogSha256 = (Get-FileHash -LiteralPath $CatalogEvidencePath).Hash; native = $true; physicalPlc = $false; simulation = $false }
$identity | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $RunRoot 'candidate.json') -Encoding utf8
function Invoke-OwnedBuild([string]$name, [string]$inputPath, [bool]$mustPass, [bool]$baseline = $false) {
    if ((Get-FileHash -LiteralPath $cli).Hash -ne $identity.cliSha256 -or (Get-FileHash -LiteralPath $core).Hash -ne $identity.coreSha256) { throw 'Candidate binaries changed during native qualification.' }
    $output = Join-Path $RunRoot ($name + '-output')
    $scratch = Join-Path $RunRoot ($name + '-scratch')
    New-Item -ItemType Directory -Path $scratch | Out-Null
    $reportPath = Join-Path $RunRoot ($name + '-result.json')
    & $cli build-generation $inputPath --output $output --scratch $scratch --api $PublicApiDirectory 2> (Join-Path $RunRoot ($name + '-stderr.txt')) |
        Set-Content -LiteralPath $reportPath -Encoding utf8
    $exit = $LASTEXITCODE
    $raw = Get-Content -LiteralPath $reportPath -Raw
    $result = $raw | ConvertFrom-Json
    if ($mustPass) {
        if ($exit -ne 0 -or -not $result.Published -or $result.Verification.Verdict -ne 'PASS' -or
            $result.Compile.ErrorCount -ne 0 -or -not $result.Compile.DiagnosticsComplete -or @($result.CleanupFailures).Count -ne 0) {
            throw "Native case $name did not pass; preserve its report and diagnose without automatic retry."
        }
        $expectedCpu = if ($baseline) { $legacyCpu } else { $cpu }
        if ($result.Observation.CpuTypeIdentifier -ne $expectedCpu) { throw 'Observed CPU differs from the exact requested profile.' }
        if (-not $baseline -and (-not $result.Observation.ReopenedFromDisk -or $result.Observation.SoftwareUnitsEmpty -ne $true -or $result.Observation.NoExtraRackModules -ne $true)) {
            throw 'S7-1500 disk reopen / software-unit / rack evidence is missing.'
        }
    } elseif ($exit -eq 0 -or $result.Published -or (Test-Path -LiteralPath $output) -or
        ($result.Phase -ne 'compile' -and -not $result.Phase.StartsWith('import:'))) {
        throw 'Bad-source case must fail at actual native import/compile, not preflight or publication.'
    }
    if (-not $mustPass -and -not $raw.Contains('UndefinedNativeIdentifier')) { throw 'Native bad-source diagnostics did not identify the deliberate undeclared symbol.' }
    Write-Output "native case=$name passed=$mustPass phase=$($result.Phase)"
}
$cases = if ($Case -eq 'all') { @('minimal', 'multiblock', 'fc', 'bad-repair', 'baseline1200') } else { @($Case) }
foreach ($selected in $cases) {
    $source = Join-Path $RunRoot ($selected + '-input')
    if ($selected -in @('minimal', 'bad-repair', 'baseline1200')) {
        $arguments = @('init', $source)
        if ($selected -ne 'baseline1200') { $arguments += @('--profile', 's7-1500-1511-1pn-v2.9') }
        & $cli @arguments
        if ($LASTEXITCODE -ne 0) { throw 'Native input init failed.' }
    } else {
        $fixture = if ($selected -eq 'multiblock') { 'programs\s71500-multiblock' } else { 'specs\fc-return' }
        Copy-Item -LiteralPath (Join-Path $repo "examples\project-generation-v1\$fixture") -Destination $source -Recurse
        $manifest = Join-Path $source 'project-generation.json'
        [IO.File]::WriteAllText($manifest, [IO.File]::ReadAllText($manifest).Replace($legacyCpu, $cpu), [Text.UTF8Encoding]::new($false))
    }
    if ($selected -eq 'bad-repair') {
        $main = Join-Path $source 'sources\Main.scl'
        $correct = [IO.File]::ReadAllText($main)
        [IO.File]::WriteAllText($main, $correct.Replace('END_ORGANIZATION_BLOCK', 'UndefinedNativeIdentifier := TRUE;' + [Environment]::NewLine + 'END_ORGANIZATION_BLOCK'), [Text.UTF8Encoding]::new($false))
        Invoke-OwnedBuild 'bad-source' $source $false
        [IO.File]::WriteAllText($main, $correct, [Text.UTF8Encoding]::new($false))
        Invoke-OwnedBuild 'repaired-source' $source $true
    } else { Invoke-OwnedBuild $selected $source $true ($selected -eq 'baseline1200') }
}
Write-Output 'Native qualification cases completed; reports identify exact source/binaries. No runtime qualification is implied.'
