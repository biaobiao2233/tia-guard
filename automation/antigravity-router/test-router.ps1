$ErrorActionPreference = 'Stop'
$router = Join-Path $PSScriptRoot 'router.ps1'
$root = Join-Path $env:TEMP "tia-router-test-$([guid]::NewGuid().ToString('N'))"
$data = Join-Path $root 'state'
$inbox = Join-Path $root 'inbox'
[System.IO.Directory]::CreateDirectory($inbox) | Out-Null
$fake = Join-Path $root 'agentapi.ps1'
$log = Join-Path $root 'calls.txt'
$fakeSource = @'
$prompt = if ($args[0] -eq 'send-message') { $args[2] } else { $args[1] }
if ($prompt -notmatch 'Safety:' -or $prompt -match '[\r\n]') { throw 'Incomplete or multiline agentapi prompt' }
$entry = if ($args[0] -eq 'send-message') { "send-message $($args[1])" } else { 'new-conversation' }
Add-Content -LiteralPath $env:TIA_ROUTER_TEST_LOG -Value $entry
$global:LASTEXITCODE = 0
if ($args[0] -eq 'send-message') {
    '{"response":{}}'
} else {
    '{"response":{"newConversation":{"conversationId":"' + [guid]::NewGuid().ToString() + '"}}}'
}
'@
[System.IO.File]::WriteAllText($fake, $fakeSource, [System.Text.UTF8Encoding]::new($false))
$env:TIA_ROUTER_TEST_LOG = $log

function Add-Envelope($id, $event, $action, $number, $sha, $command = $null, $labels = @()) {
    $envelope = [ordered]@{
        schemaVersion = 1; deliveryId = $id; repository = 'biaobiao2233/tia-guard'
        event = $event; action = $action; actor = 'test'; number = $number
        targetType = if ($event -eq 'pull_request' -or $action -eq 'review') { 'pull_request' } else { 'issue' }
        headSha = $sha; baseSha = $null; draft = $false; labels = $labels
        commentCommand = $command; conclusion = $null
        url = "https://github.com/biaobiao2233/tia-guard/issues/$number"
        timestamp = [DateTime]::UtcNow.ToString('o')
    }
    $path = Join-Path $inbox "$([guid]::NewGuid().ToString('N')).json"
    [System.IO.File]::WriteAllText($path, ($envelope | ConvertTo-Json -Depth 8), [System.Text.UTF8Encoding]::new($false))
}

function Run-Once {
    & $router -InboxRoot $root -DataDir $data -AgentApiCommand $fake -MutexName "Local\TIA-Guard-Router-Test-$PID" -Once
    if (-not $?) { throw 'Router run failed' }
}

function Assert-Count($expected, $message) {
    $actual = @(Get-Content -LiteralPath $log -ErrorAction SilentlyContinue).Count
    if ($actual -ne $expected) { throw "$message expected=$expected actual=$actual" }
}

try {
    $sha1 = 'a' * 40
    $sha2 = 'b' * 40
    Add-Envelope 'review-one' 'pull_request' 'ready_for_review' 123 $sha1
    Run-Once
    Assert-Count 1 'First exact SHA must dispatch'
    Add-Envelope 'review-one' 'pull_request' 'ready_for_review' 123 $sha1
    Add-Envelope 'review-retry' 'pull_request' 'ready_for_review' 123 $sha1
    Run-Once
    Assert-Count 1 'Delivery and SHA dedup must survive restart'
    Add-Envelope 'review-two' 'pull_request' 'synchronize' 123 $sha2
    Run-Once
    Assert-Count 2 'New exact SHA must dispatch separately'
    Add-Envelope 'implement-one' 'issues' 'labeled' 456 $null $null @('agent-ready')
    Add-Envelope 'implement-retry' 'issues' 'labeled' 456 $null $null @('agent-ready')
    Run-Once
    Assert-Count 3 'Issue must dispatch one Implementer'
    Add-Envelope 'ordinary' 'issue_comment' 'created' 456 $null 'hello'
    Run-Once
    Assert-Count 3 'Ordinary comment must be ignored'
    Add-Envelope 'triage-one' 'issue_comment' 'created' 456 $null '/agy triage'
    Add-Envelope 'triage-two' 'issue_comment' 'created' 456 $null '/agy triage'
    Run-Once
    Assert-Count 5 'Triage must create then send-message'
    $calls = @(Get-Content -LiteralPath $log)
    if ($calls[4] -notmatch '^send-message ') { throw 'Triage continuation did not use send-message' }
    $recoverId = 'recovery-one'
    $sha3 = 'c' * 40
    $recoverKey = "review:biaobiao2233/tia-guard:123:$sha3"
    $bytes = [System.Text.Encoding]::UTF8.GetBytes("biaobiao2233/tia-guard:$recoverId")
    $hash = [BitConverter]::ToString([System.Security.Cryptography.SHA256]::Create().ComputeHash($bytes)).Replace('-', '').ToLowerInvariant()
    $statePath = Join-Path $data 'router-state.json'
    $before = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    $before.deliveries | Add-Member -NotePropertyName $hash -NotePropertyValue @{status='dispatching';mapping=$recoverKey}
    [System.IO.File]::WriteAllText($statePath, ($before | ConvertTo-Json -Depth 20), [System.Text.UTF8Encoding]::new($false))
    $events = Join-Path $root 'events'
    [System.IO.Directory]::CreateDirectory($events) | Out-Null
    $recoveredConversation = [guid]::NewGuid().ToString()
    $runtimeEvent = @{payload=@{newConversation=@{prompt="[TIA-Guard router: $recoverId] Full Safety: instructions";conversationId=$recoveredConversation}}}
    [System.IO.File]::WriteAllText((Join-Path $events 'recovered.json'), ($runtimeEvent | ConvertTo-Json -Depth 8), [System.Text.UTF8Encoding]::new($false))
    Add-Envelope $recoverId 'pull_request' 'synchronize' 123 $sha3
    Run-Once
    Assert-Count 5 'Crash recovery must not call agentapi again'
    if (@(Get-ChildItem -LiteralPath $inbox -Filter '*.json').Count -ne 0) { throw 'Inbox is not empty' }
    $state = Get-Content -LiteralPath (Join-Path $data 'router-state.json') -Raw | ConvertFrom-Json
    if (@($state.mappings.PSObject.Properties).Count -ne 5) { throw 'Unexpected mapping count' }
    Write-Output 'PASS: review SHA/delivery dedup, new SHA, Issue dedup, ordinary comment filter, triage send-message, persisted restart state, runtime-event crash recovery'
} finally {
    Remove-Item Env:TIA_ROUTER_TEST_LOG -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
