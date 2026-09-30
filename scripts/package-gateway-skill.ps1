param([string]$OutputDirectory = "")
$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$skillRoot = Join-Path $repoRoot "skills\tia-guard-gateway"
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "artifacts\release"
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$text = [IO.File]::ReadAllText((Join-Path $skillRoot "SKILL.md"))
if ($text -notmatch '(?s)\A---\r?\nname: tia-guard-gateway\r?\ndescription: [^\r\n]+\r?\n---\r?\n') {
    throw "Skill frontmatter must contain its canonical name and a non-empty description."
}
$files = @(Get-ChildItem $skillRoot -File -Recurse)
# The companion currently needs only behavioral instructions; reject accidental additions.
if ($files.Count -ne 1 -or $files[0].Name -ne "SKILL.md" -or
    (Get-Item $skillRoot).Attributes -band [IO.FileAttributes]::ReparsePoint -or
    $files[0].Attributes -band [IO.FileAttributes]::ReparsePoint) {
    throw "Expected exactly the canonical SKILL.md; refusing extra or redirected files."
}
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$zipPath = Join-Path $OutputDirectory "skill.zip"
Add-Type -AssemblyName System.IO.Compression
$stream = [IO.File]::Open($zipPath, [IO.FileMode]::Create)
try {
    $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        $entry = $archive.CreateEntry("tia-guard-gateway/SKILL.md", [IO.Compression.CompressionLevel]::Optimal)
        # Stable entry metadata makes identical canonical content reproducibly packageable.
        $entry.LastWriteTime = [DateTimeOffset]::new(2026, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
        $entryStream = $entry.Open()
        try {
            $bytes = [Text.UTF8Encoding]::new($false).GetBytes($text)
            $entryStream.Write($bytes, 0, $bytes.Length)
        } finally { $entryStream.Dispose() }
    } finally { $archive.Dispose() }
} finally { $stream.Dispose() }
$check = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    if ($check.Entries.Count -ne 1 -or $check.Entries[0].FullName -ne "tia-guard-gateway/SKILL.md") {
        throw "Unexpected Skill ZIP structure."
    }
    $reader = [IO.StreamReader]::new($check.Entries[0].Open(), [Text.Encoding]::UTF8)
    try { if ($reader.ReadToEnd() -cne $text) { throw "Packaged Skill differs from canonical source." } }
    finally { $reader.Dispose() }
} finally { $check.Dispose() }
Write-Output "skill=$zipPath"
Write-Output "sha256=$((Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant())"
Write-Output "validation=frontmatter + exact single-entry ZIP + canonical byte-content match"
