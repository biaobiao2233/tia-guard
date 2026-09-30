param(
    [string]$UserName = [Security.Principal.WindowsIdentity]::GetCurrent().Name
)

$ErrorActionPreference = "Stop"

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-IsAdministrator)) {
    throw "Run this script from an elevated PowerShell window (Run as administrator)."
}

$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$logDir = Join-Path $repoRoot "logs"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$logPath = Join-Path $logDir "bootstrap-admin.log"

Start-Transcript -Path $logPath -Append | Out-Null
try {
    $group = "Siemens TIA Openness"
    $member = Get-LocalGroupMember -Group $group -ErrorAction Stop |
        Where-Object { $_.Name -ieq $UserName }

    if (-not $member) {
        Add-LocalGroupMember -Group $group -Member $UserName -ErrorAction Stop
        Write-Host "Added $UserName to $group."
    }
    else {
        Write-Host "$UserName is already a member of $group."
    }

    $sdkInstalled = (& dotnet --list-sdks 2>$null | Select-String '^8\.')
    if (-not $sdkInstalled) {
        Write-Host "Installing .NET SDK 8..."
        winget install --id Microsoft.DotNet.SDK.8 --exact --silent --accept-package-agreements --accept-source-agreements
        if ($LASTEXITCODE -ne 0) {
            throw "winget failed with exit code $LASTEXITCODE"
        }
    }
    else {
        Write-Host ".NET SDK 8 is already installed."
    }

    Write-Host "Bootstrap complete. Sign out and sign back in before using TIA Portal Openness."
}
finally {
    Stop-Transcript | Out-Null
}
