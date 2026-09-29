# TIA AI Bridge

TIA-Guard is adding a vendor-neutral bridge between external AI agents and a local Siemens TIA Portal V21 engineering session.

The bridge is deliberately separate from the AI-readable renderer:

- **AI-readable view**: deterministic, derived engineering context generated from validated `tia-source/`.
- **TIA AI Bridge**: live, explicit access to the local TIA Portal engineering session.

The two surfaces can be integrated later, but neither is allowed to become an implicit replacement for the authoritative round-trip source.

## Design sources

Two public MIT-licensed TIA MCP projects were studied for architecture and capability coverage:

- `Czarnak/tia-portal-mcp`: persistent .NET Framework Openness worker, exact project binding, fail-closed targeting, and strong write-safety patterns.
- `bulaofen0036-coder/TIA_Portal_Openness_MCP`: broad TIA capability coverage, stdio + HTTP connectivity, declarative workflows, LAD/SCL generation, hardware/HMI operations, and VCI/Git workflows.

TIA-Guard does not copy either repository wholesale. The Bridge keeps its own contracts and remains bounded by TIA-Guard's product invariants.

## Architecture

```text
External AI / MCP client / HTTP client
                 |
                 v
        tia-guard-bridge (.NET 8)
          |               |
        MCP            local REST
          \               /
           \             /
            v           v
        persistent worker protocol
                 |
                 v
   TiaGuard.Bridge.Worker (.NET Framework 4.8, x64)
                 |
                 v
       TiaGuard.Openness
                 |
                 v
        TIA Portal V21
```

The host never loads Siemens assemblies. Siemens Openness remains isolated in the net48 worker.
Release packages place that net48 worker under a dedicated `worker/` directory so it cannot probe the self-contained .NET 8 host runtime assemblies. Development builds may keep the worker next to the host binary as a fallback.

The default mode is intentionally read-only. It exposes project discovery, exact binding, project metadata and a bounded engineering snapshot.

An explicit `--allow-write` switch enables a separate guarded write tool type. The current write scope is still bounded to disposable offline project copies: one root PLC-tag upsert in memory plus an explicit, separately previewed publish-to-new-directory operation. Tag mutation itself is not saved; publication compiles the disposable copy, uses SaveAs only into a brand-new destination, then reopens the new `.ap21` through a fresh disposable copy and verifies its engineering `contentId`.

Neither mode exposes:

- PLC download;
- PLC start/stop;
- force;
- online variable writes;
- Safety operations;
- writes to an attached user project;
- overwrite/save of an attached user project;
- archive or in-place SaveAs.

## Project binding

The Bridge never selects an arbitrary TIA instance.

- If exactly one V21 process has an open project, `connect_project` may omit a PID.
- If more than one candidate exists, the caller must pass an exact process ID.
- Once bound, the Bridge refuses to switch to a different PID until an explicit disconnect.
- If the bound project identity drifts, the worker invalidates the session instead of guessing.
- Offline inspection opens a disposable copy through the existing TIA-Guard safety path; the supplied `.ap21` is never opened directly.

## External interfaces

### MCP stdio

```powershell
tia-guard-bridge.exe --transport stdio
```

Initial MCP tools:

- `bridge_doctor`
- `list_open_projects`
- `connect_project`
- `open_offline_project`
- `disconnect_project`
- `get_bridge_state`
- `get_project`
- `get_project_snapshot`

Read-write mode additionally registers:

- `preview_tag_upsert`
- `apply_tag_upsert`
- `preview_publish_modified_copy`
- `apply_publish_modified_copy`

Those tools are not present at all in default read-only mode.

### Local HTTP

```powershell
tia-guard-bridge.exe --transport http --port 18761
```

The HTTP listener is intentionally bound to loopback only:

```text
http://127.0.0.1:18761
```

MCP Streamable HTTP endpoint:

```text
/mcp
```

Plain JSON gateway endpoints:

```text
GET  /health
GET  /api/v1/projects
POST /api/v1/connect
POST /api/v1/open-offline
POST /api/v1/disconnect
GET  /api/v1/state
GET  /api/v1/project
GET  /api/v1/project/snapshot

# only when --allow-write is enabled
POST /api/v1/tags/preview-upsert
POST /api/v1/tags/apply-upsert
POST /api/v1/project/preview-publish
POST /api/v1/project/apply-publish
```

The plain JSON API exists for external agents that do not speak MCP.

## Guarded write contract

The first engineering mutation slice is now implemented as an opt-in contract:

```text
requested tag upsert
      |
      v
preview current root tag state
      |
      v
hash exact project binding + request + current state
      |
      v
issue random single-use token (10-minute TTL)
      |
      v
apply with the same request + token
      |
      v
consume token before Siemens mutation
      |
      v
mutate disposable offline copy in memory
      |
      v
post-read and exact state verification
```

The safety token is rejected when it is replayed, when the requested change differs from the preview, when the project binding differs, or when the tag state changed after preview. Tokens are consumed on the first apply attempt, including a failed validation attempt after token lookup.

The current tag operation supports only root tag-table names and exact tag names; paths are rejected. It can create a missing tag or set the data type and logical address of an existing tag. It deliberately does not save the scratch project, so closing/disconnecting discards the change.

The guarded **publish modified copy** operation is implemented as a separate tokenized write: it requires a complete engineering snapshot, binds the token to the exact project/request/content state, preserves the current TIA project name exactly, compiles before SaveAs, refuses rename-through-publish and any existing destination, invalidates the original worker binding after SaveAs, reopens the new `.ap21` through a fresh disposable copy, and verifies that the deterministic engineering `contentId` is unchanged. Callers choose a different parent directory when they want another copy. Live attached-project writes remain a later permission domain, and online PLC control remains separate from engineering edits.
