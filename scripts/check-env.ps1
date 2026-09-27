$ErrorActionPreference = 'Stop'

$portalRoot = 'C:\Program Files\Siemens\Automation\Portal V21'
$publicApiRoot = Join-Path $portalRoot 'PublicAPI\V21\net48'
$publicApiBase = Join-Path $publicApiRoot 'Siemens.Engineering.Base.dll'
$publicApiStep7 = Join-Path $publicApiRoot 'Siemens.Engineering.Step7.dll'
$opennessGroup = 'Siemens TIA Openness'
$currentIdentity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
$currentUser = $currentIdentity.Split('\\')[-1]

$groupExists = $null -ne (Get-LocalGroup -Name $opennessGroup -ErrorAction SilentlyContinue)
$member = $false
if ($groupExists) {
    $member = $null -ne (Get-LocalGroupMember -Group $opennessGroup -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ieq $currentIdentity -or $_.Name -match "\\$([regex]::Escape($currentUser))$" })
}

$dotnetInfo = (& dotnet --info 2>&1 | Out-String)
$sdkPresent = $dotnetInfo -match 'SDKs installed:\\s*\\r?\\n\\s*\\d'

$result = [ordered]@{
    tiaPortalV21 = Test-Path $portalRoot
    opennessAssembly = (Test-Path $publicApiBase) -and (Test-Path $publicApiStep7)
    opennessAssemblyPath = $publicApiRoot
    opennessGroupExists = $groupExists
    currentIdentity = $currentIdentity
    currentUserInOpennessGroup = $member
    dotnetSdkPresent = $sdkPresent
}

$result | ConvertTo-Json
if (-not $result.tiaPortalV21 -or -not $result.opennessAssembly) { exit 2 }
if (-not $result.currentUserInOpennessGroup) { exit 3 }
if (-not $result.dotnetSdkPresent) { exit 4 }
exit 0
