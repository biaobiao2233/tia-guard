# TIA-Guard Antigravity event router

GitHub Actions routes selected repository events to the dedicated `antigravity-router` Windows runner. The runner writes a minimal JSON envelope as `*.tmp` and atomically renames it to `*.json` in `%LOCALAPPDATA%\TIA-Guard-Antigravity\inbox`. The Antigravity 2.0 native Sidecar watches that directory with `FileSystemWatcher`, then calls the bundled `agentapi` to create or continue a conversation in the TIA-Guard Project. The runner never calls Antigravity or handles its credentials.

## Versioned components

- `.github/workflows/antigravity-router.yml`: event filter and envelope writer. It does not check out or execute PR code.
- `router.ps1`: Sidecar worker. It handles validation, routing, deduplication, persisted mappings, startup reconciliation, and runtime event recovery.
- `prompts/*.txt`: role and safety instructions sent to Antigravity.
- `test-router.ps1`: isolated routing tests with a fake `agentapi`; no live GitHub or Antigravity writes.

The installed Sidecar lives under `~/.gemini/config/sidecars/tia-guard-router/`. Its `sidecar.json` uses Windows PowerShell and `restart_policy=always`. Enable it in `~/.gemini/config/config.json` with the verified TIA-Guard `projectId`. Its state is stored under `~/.gemini/antigravity/sidecar_data/tia-guard-router/data/` through `ANTIGRAVITY_EXECUTABLE_DATA_DIR`; Antigravity writes its logs and `agentapi` events in sibling directories. Do not commit the user configuration, runner registration files, or runtime data.

## Routes

| Event | Dispatch |
| --- | --- |
| PR `ready_for_review` | Reviewer for exact head SHA |
| PR `synchronize` | New Reviewer for new SHA when PR is no longer Draft, or an earlier review mapping exists |
| Issue labeled `agent-ready` | One Implementer conversation for the Issue |
| Exact `/agy review` comment on PR | Reviewer for the current PR head read with `gh` |
| Exact `/agy triage` or `/agy queue` comment | Read-only Triage/Scout, continued with `send-message` for the same target |
| Failed `Copilot` workflow run | Read-only Triage |
| Manual `workflow_dispatch` | Reviewer or Triage smoke route |

Ordinary comments are ignored. The prompts prohibit agents from posting `/agy` commands, merging, releasing, changing credentials or accounts, writing through TIA MCP, and operating a PLC online. Reviewers must check the current PR head again before a final verdict and cannot mark a stale SHA as current PASS. Implementers must read all Issue comments, post a Project Workbench CLAIM, immediately read back competing claims, use an isolated branch/worktree, test, open a Draft PR, and post RESULT.

The workflow synthesizes a stable `deliveryId` from event identity because the GitHub Actions event file does not expose the webhook delivery header. Retried jobs may enqueue more than one file; the Sidecar deduplicates both delivery IDs and logical review/Issue keys. It writes `dispatching` state before calling `agentapi`. If a crash occurs after conversation creation, startup recovery searches Antigravity's own Sidecar event files for the prompt marker and records the conversation ID. If no matching event exists, it leaves the dispatch uncertain instead of risking a duplicate conversation; inspect the runtime events before replay.

## Operations

Run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File automation/antigravity-router/test-router.ps1` for the isolated router checks. Verify the installed Sidecar through `~/.gemini/antigravity/sidecar_data/tia-guard-router/logs/sidecar.log`, `events/`, and conversation metadata. Verify runner label/status in repository Settings → Actions → Runners. `workflow_dispatch` and `workflow_run` only receive GitHub events after this workflow file is on the default branch; the PR alone does not activate them.

To disable routing, set `sidecars.tia-guard-router.enabled=false` in `~/.gemini/config/config.json` by atomic JSON update, then disable the GitHub workflow or take the dedicated runner offline. Preserve inbox, state, and event logs for diagnosis. Do not remove the runner registration or state as an incidental rollback step.
