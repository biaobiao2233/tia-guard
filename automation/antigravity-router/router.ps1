param(
    [string]$InboxRoot = 'E:\AGENT\Automation\TIA-Guard-Antigravity',
    [string]$DataDir = $env:ANTIGRAVITY_EXECUTABLE_DATA_DIR,
    [string]$AgentApiCommand = 'agentapi',
    [string]$MutexName = 'Local\TIA-Guard-Antigravity-Router',
    [switch]$Once
)

$ErrorActionPreference = 'Stop'
if (-not $DataDir) { throw 'ANTIGRAVITY_EXECUTABLE_DATA_DIR is required' }
$utf8 = New-Object System.Text.UTF8Encoding($false)
$inbox = Join-Path $InboxRoot 'inbox'
$processed = Join-Path $InboxRoot 'processed'
$failed = Join-Path $InboxRoot 'failed'
foreach ($path in @($inbox, $processed, $failed, $DataDir)) {
    [System.IO.Directory]::CreateDirectory($path) | Out-Null
}
$statePath = Join-Path $DataDir 'router-state.json'

function To-Hashtable($value) {
    if ($null -eq $value) { return $null }
    if ($value -is [System.Collections.IDictionary]) {
        $result = @{}
        foreach ($key in $value.Keys) { $result[$key] = To-Hashtable $value[$key] }
        return $result
    }
    if ($value -is [pscustomobject]) {
        $result = @{}
        foreach ($property in $value.PSObject.Properties) { $result[$property.Name] = To-Hashtable $property.Value }
        return $result
    }
    if ($value -is [array]) { return ,@($value | ForEach-Object { To-Hashtable $_ }) }
    return $value
}

function Read-State {
    if (-not [System.IO.File]::Exists($statePath)) {
        return @{ deliveries = @{}; mappings = @{} }
    }
    $loaded = To-Hashtable (Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json)
    if (-not $loaded.ContainsKey('deliveries') -or -not $loaded.ContainsKey('mappings')) {
        throw 'Router state has invalid schema; refusing dispatch'
    }
    return $loaded
}

function Save-State($state) {
    $tmp = "$statePath.$([guid]::NewGuid().ToString('N')).tmp"
    $json = ConvertTo-Json -InputObject $state -Depth 20 -Compress
    [System.IO.File]::WriteAllText($tmp, $json, $utf8)
    if ([System.IO.File]::Exists($statePath)) {
        $backup = "$statePath.$([guid]::NewGuid().ToString('N')).bak"
        [System.IO.File]::Replace($tmp, $statePath, $backup)
        [System.IO.File]::Delete($backup)
    } else {
        [System.IO.File]::Move($tmp, $statePath)
    }
}

function Hash-Key([string]$text) {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($text)
    $hash = [System.Security.Cryptography.SHA256]::Create().ComputeHash($bytes)
    return [BitConverter]::ToString($hash).Replace('-', '').ToLowerInvariant()
}

function Move-Envelope([string]$source, [string]$directory) {
    $destination = Join-Path $directory ([System.IO.Path]::GetFileName($source))
    if ([System.IO.File]::Exists($destination)) {
        $destination = Join-Path $directory "$([guid]::NewGuid().ToString('N')).json"
    }
    [System.IO.File]::Move($source, $destination)
}

function Invoke-AgentApi([string[]]$arguments) {
    $output = @(& $AgentApiCommand @arguments 2>&1 | ForEach-Object { [string]$_ })
    $code = $LASTEXITCODE
    $parsed = $null
    try { $parsed = ($output -join "`n") | ConvertFrom-Json } catch { }
    if ($null -eq $parsed) {
        for ($index = $output.Count - 1; $index -ge 0; $index--) {
            try { $parsed = $output[$index] | ConvertFrom-Json; break } catch { }
        }
    }
    if ($code -ne 0 -or $null -eq $parsed -or $parsed.error) {
        throw "agentapi failed (exit=$code); see Sidecar runtime events/logs"
    }
    return $parsed
}

function Find-RuntimeConversation([string]$deliveryId) {
    $events = Join-Path (Split-Path -Parent $DataDir) 'events'
    if (-not [System.IO.Directory]::Exists($events)) { return $null }
    $marker = "[TIA-Guard router: $deliveryId]"
    foreach ($file in @(Get-ChildItem -LiteralPath $events -Filter '*.json' -File | Sort-Object LastWriteTime -Descending)) {
        try {
            $record = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
            if (([string]$record.payload.newConversation.prompt).StartsWith($marker, [System.StringComparison]::Ordinal)) {
                $id = [string]$record.payload.newConversation.conversationId
                if ($id -cmatch '^[0-9a-fA-F-]{36}$') { return $id }
            }
        } catch { }
    }
    return $null
}

function Get-PrHead([int]$number) {
    $json = & gh api "repos/biaobiao2233/tia-guard/pulls/$number" 2>$null
    if ($LASTEXITCODE -ne 0) { throw "Cannot read PR #$number" }
    $pr = $json | ConvertFrom-Json
    if ($pr.state -ne 'open') { throw "PR #$number is not open" }
    return [string]$pr.head.sha
}

function Get-Plan($event, $state) {
    if ($event.schemaVersion -ne 1 -or $event.repository -ne 'biaobiao2233/tia-guard' -or
        [string]$event.deliveryId -cnotmatch '^[A-Za-z0-9:._-]{1,200}$' -or
        [string]$event.url -cnotmatch '^https://github\.com/biaobiao2233/tia-guard/(issues|pull|actions/runs)/[0-9]+(?:#[-A-Za-z0-9]+)?$' -or
        -not $event.event) {
        throw 'Invalid event envelope'
    }
    $number = [int]$event.number
    $repo = 'biaobiao2233/tia-guard'
    $kind = $null
    $key = $null
    $sha = [string]$event.headSha
    switch ([string]$event.event) {
        'pull_request' {
            if ($number -le 0 -or $event.action -cnotin @('ready_for_review', 'synchronize')) { throw 'Invalid PR event' }
            if ($event.action -eq 'ready_for_review' -and $event.draft) { return $null }
            if ($sha -cnotmatch '^[0-9a-f]{40}$') { throw 'PR event missing exact head SHA' }
            if ($event.action -eq 'synchronize' -and $event.draft) {
                $prefix = "review:$repo`:$number`:"
                if (-not @($state.mappings.Keys | Where-Object { $_.StartsWith($prefix) }).Count) { return $null }
            }
            $kind = 'review'
            $key = "review:$repo`:$number`:$sha"
        }
        'issues' {
            if ($number -le 0 -or $event.action -ne 'labeled' -or
                'agent-ready' -cnotin @($event.labels)) { return $null }
            $kind = 'implement'
            $key = "implement:$repo`:$number"
        }
        'issue_comment' {
            if ($number -le 0 -or $event.action -ne 'created') { throw 'Invalid comment event' }
            $command = [string]$event.commentCommand
            if ($command -cnotin @('/agy review', '/agy triage', '/agy queue')) { return $null }
            if ($command -eq '/agy review') {
                if ($event.targetType -ne 'pull_request') { return $null }
                $sha = Get-PrHead $number
                $kind = 'review'
                $key = "review:$repo`:$number`:$sha"
            } else {
                $kind = if ($command -eq '/agy triage') { 'triage' } else { 'queue' }
                $key = "$kind`:$repo`:$($event.targetType)`:$number"
            }
        }
        'workflow_run' {
            if ($event.action -ne 'completed' -or $event.conclusion -ne 'failure') { return $null }
            $kind = 'triage'
            $key = "triage:$repo`:workflow:$($event.url)"
        }
        'workflow_dispatch' {
            if ($number -le 0 -or $event.action -cnotin @('review', 'triage')) { throw 'Invalid dispatch test' }
            if ($event.action -eq 'review') {
                if ($sha -cnotmatch '^[0-9a-f]{40}$') { throw 'Dispatch review needs exact SHA' }
                $kind = 'review'
                $key = "review:$repo`:$number`:$sha"
            } else {
                $kind = 'triage'
                $key = "triage:$repo`:issue:$number"
            }
        }
        default { return $null }
    }
    $templateName = if ($kind -eq 'review') { 'review.txt' } elseif ($kind -eq 'implement') { 'implement.txt' } else { 'triage.txt' }
    $template = Get-Content -LiteralPath (Join-Path (Join-Path $PSScriptRoot 'prompts') $templateName) -Raw
    $prompt = $template.Replace('{deliveryId}', [string]$event.deliveryId).Replace('{number}', [string]$number).
        Replace('{sha}', [string]$sha).Replace('{url}', [string]$event.url).Replace('{kind}', [string]$kind)
    $prompt = ($prompt -replace '\s+', ' ').Trim()
    if ($prompt.Length -gt 7000 -or $prompt.Contains('"')) { throw 'Prompt is unsafe for Windows agentapi transport' }
    return @{ kind = $kind; key = $key; prompt = $prompt; sha = $sha }
}

function Process-Envelope([string]$path) {
    if (-not [System.IO.File]::Exists($path)) { return }
    $state = Read-State
    try {
        $event = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        if (-not $event.deliveryId) { throw 'Missing deliveryId' }
        $deliveryKey = Hash-Key "biaobiao2233/tia-guard:$($event.deliveryId)"
        if ($state.deliveries.ContainsKey($deliveryKey)) {
            $prior = $state.deliveries[$deliveryKey]
            if ($prior.status -eq 'dispatching') {
                $recoveredId = Find-RuntimeConversation ([string]$event.deliveryId)
                if (-not $recoveredId) {
                    throw 'Prior dispatch is uncertain; inspect Sidecar runtime events before replay'
                }
                $recoveredPlan = Get-Plan $event $state
                $state.mappings[$recoveredPlan.key] = @{ conversationId = $recoveredId; kind = $recoveredPlan.kind; sha = $recoveredPlan.sha; at = [DateTime]::UtcNow.ToString('o') }
                $state.deliveries[$deliveryKey] = @{ status = 'done'; mapping = $recoveredPlan.key; conversationId = $recoveredId; recovered = $true }
                Save-State $state
            }
            if ((Split-Path -Parent $path) -ne $processed) { Move-Envelope $path $processed }
            return
        }
        $plan = Get-Plan $event $state
        if ($null -eq $plan) {
            $state.deliveries[$deliveryKey] = @{ status = 'ignored'; at = [DateTime]::UtcNow.ToString('o') }
            Save-State $state
            Move-Envelope $path $processed
            return
        }
        $existing = $state.mappings[$plan.key]
        if ($existing -and $plan.kind -in @('review', 'implement')) {
            $state.deliveries[$deliveryKey] = @{ status = 'deduplicated'; mapping = $plan.key }
            Save-State $state
            Move-Envelope $path $processed
            return
        }
        $state.deliveries[$deliveryKey] = @{ status = 'dispatching'; mapping = $plan.key; at = [DateTime]::UtcNow.ToString('o') }
        Save-State $state
        if ($existing -and $existing.conversationId) {
            $null = Invoke-AgentApi @('send-message', [string]$existing.conversationId, [string]$plan.prompt)
            $conversationId = [string]$existing.conversationId
        } else {
            $response = Invoke-AgentApi @('new-conversation', [string]$plan.prompt)
            $conversationId = [string]$response.response.newConversation.conversationId
            if ($conversationId -cnotmatch '^[0-9a-fA-F-]{36}$') {
                throw 'agentapi returned no verified conversation ID; inspect Sidecar events before replay'
            }
        }
        $state.mappings[$plan.key] = @{ conversationId = $conversationId; kind = $plan.kind; sha = $plan.sha; at = [DateTime]::UtcNow.ToString('o') }
        $state.deliveries[$deliveryKey] = @{ status = 'done'; mapping = $plan.key; conversationId = $conversationId }
        Save-State $state
        Move-Envelope $path $processed
        Write-Output "Dispatched $($plan.kind) $($plan.key) conversation=$conversationId"
    } catch {
        Write-Error "Envelope $path failed: $($_.Exception.Message)" -ErrorAction Continue
        if ([System.IO.File]::Exists($path)) { Move-Envelope $path $failed }
    }
}

$mutex = [System.Threading.Mutex]::new($false, $MutexName)
$acquired = $false
try {
    $acquired = $mutex.WaitOne(0)
    if (-not $acquired) { throw 'Another TIA-Guard router is already running' }
    Get-ChildItem -LiteralPath $inbox -Filter '*.json' -File | Sort-Object Name | ForEach-Object { Process-Envelope $_.FullName }
    Get-ChildItem -LiteralPath $failed -Filter '*.json' -File | ForEach-Object {
        try {
            $event = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
            if (Find-RuntimeConversation ([string]$event.deliveryId)) { Process-Envelope $_.FullName }
        } catch { }
    }
    if ($Once) { return }
    $watcher = [System.IO.FileSystemWatcher]::new($inbox, '*.json')
    $watcher.EnableRaisingEvents = $true
    Register-ObjectEvent $watcher Created -SourceIdentifier 'tia-router-created' | Out-Null
    Register-ObjectEvent $watcher Renamed -SourceIdentifier 'tia-router-renamed' | Out-Null
    Register-ObjectEvent $watcher Error -SourceIdentifier 'tia-router-error' | Out-Null
    try {
        while ($true) {
            $notice = Wait-Event
            if ($notice.SourceIdentifier -eq 'tia-router-error') {
                Get-ChildItem -LiteralPath $inbox -Filter '*.json' -File | Sort-Object Name | ForEach-Object { Process-Envelope $_.FullName }
            } else {
                $candidate = Join-Path $inbox $notice.SourceEventArgs.Name
                Process-Envelope $candidate
            }
            Remove-Event -EventIdentifier $notice.EventIdentifier
        }
    } finally {
        Unregister-Event -SourceIdentifier 'tia-router-created' -ErrorAction SilentlyContinue
        Unregister-Event -SourceIdentifier 'tia-router-renamed' -ErrorAction SilentlyContinue
        Unregister-Event -SourceIdentifier 'tia-router-error' -ErrorAction SilentlyContinue
        $watcher.Dispose()
    }
} finally {
    if ($acquired) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
