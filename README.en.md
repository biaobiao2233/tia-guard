# TIA-Guard

[中文](README.md) | **English**

**Make supported TIA Portal projects readable, version them with Git, and rebuild them with verification.**

TIA-Guard is an independent **pre-alpha** engineering tool for local **Siemens TIA Portal V21 / S7-1200** projects. It uses the local Openness API to turn projects into deterministic engineering source, with a Windows GUI, CLI, and a local API Gateway / MCP interface for AI agents.

## Project overview: TIA Portal engineering versioning and AI engineering Gateway

- **Readable engineering source and Git collaboration**: uses TIA Portal V21 / Openness API to convert supported S7-1200 projects into structured engineering source while retaining PLC tags and LAD logic for Git comparison, review and reuse. The Chinese WPF GUI implements GitHub import/rebuild, export/commit and multi-project management.
- **Round-trip rebuild and verification within supported scope**: completes the `.ap21 → readable engineering source → new .ap21` loop. A self-authored V21 / S7-1200 fixture has real Build / Compile / Verify evidence for supported object logic, structure and engineering semantics. Unsupported objects or insufficient evidence block acceptance.
- **Local AI engineering Gateway**: implements localhost HTTP / MCP, AI engineering views and a dedicated Skill for Codex, Cursor and Claude Code to query tags, bounded LAD logic and references. It supports edit previews, one-time GUI approval, compilation, rebuild, verification and rollback on failure. GUI / CLI / Gateway source is public; 152 contract tests and 45 Host tests passed. The latest integrated real TIA modification loop still needs requalification.

## Download and documentation

- [Releases](https://github.com/biaobiao2233/tia-guard/releases): downloadable preview packages and checksums.
- [Getting started](docs/GETTING-STARTED.en.md): requirements, extraction, GUI workflows and troubleshooting.
- [AI Gateway guide](docs/TIA-AI-BRIDGE.en.md): desktop agents, MCP and guarded offline edits.

The first release is a pre-alpha preview. The Windows GUI is currently Chinese; these links switch repository documentation languages.

## Current source status

The source includes the implementations below. Capability boundaries and evidence are defined by the code, tests and [integration receipt](docs/verification/publication-20261001.md).

| Module | Current implementation |
| --- | --- |
| Windows GUI | Chinese WPF interface with a custom title bar; GitHub → TIA rebuild and TIA → GitHub export/commit; multiple projects per repository |
| CLI | `doctor`, `--version`, `export`, `build`, `verify`, `ai-view` |
| AI engineering view | Derived JSON / Markdown, symbol tables, bounded LAD graphs and source references from validated canonical source |
| API Gateway | GUI starts `127.0.0.1:18761`; capability discovery, engineering context, program graphs, reference queries and guarded patches |
| MCP | stdio and Streamable HTTP; read-only tools by default, opt-in write tools retain preview / apply validation |
| Gateway Skill | [Dedicated Skill](skills/tia-guard-gateway/SKILL.md) for local Codex, Cursor, Claude Code and other agents |
| Windows package | GUI + CLI + Gateway + isolated net48 worker; no Siemens DLLs |

This is source integration, not a stable-release claim. Checks cover automated tests, builds, packaging and local HTTP controls; real TIA round-trip evidence is described below. Historical receipts do not qualify the latest integrated GUI/Gateway engineering runtime.

## Two GUI workflows

### GitHub → TIA

Paste a Git repository URL → clone / pull → choose a project → rebuild a new `.ap21` from engineering source → compile and verify supported semantics.

The new project goes into a selected output directory without overwriting the original. The GUI shows stages, timing, environment errors and Git authentication errors. Where source identity metadata exists, it preserves the original filename and checks that metadata.

### TIA → GitHub

Select your own `.ap21` and a Git repository URL → choose an existing project or add a slot → export and validate → update the selected engineering source → commit / push.

A repository can contain multiple projects:

```text
repo/
  README.md
  tia-projects/
    motor-reversing/
      tia-source/
      ai/
    conveyor-control/
      tia-source/
```

The legacy `repo/tia-source/` layout remains supported. Publication replaces only the selected project's `tia-source/`, preserving sibling projects and ordinary repository files. It uses system Git and existing Git Credential Manager / SSH credentials, embeds no GitHub token and does not force-push.

`ai/` is a regenerable derived view. The GUI Git workflow does not automatically generate or commit it; run `ai-view` separately when needed.

## CLI

```powershell
dotnet build src/TiaGuard.Cli/TiaGuard.Cli.csproj -c Release

tia-guard doctor
tia-guard --version
tia-guard export C:\demo\original.ap21 C:\repo\tia-source
tia-guard build C:\repo\tia-source --output C:\demo\rebuilt
tia-guard verify C:\demo\original.ap21 C:\demo\rebuilt\original.ap21
tia-guard ai-view C:\repo\tia-source
```

`doctor` checks local prerequisites without writes. Export opens a controlled offline copy. Build strictly validates canonical source before creating, saving and compiling a new project. Verify returns `pass`, `mismatch` or `blocked`. Short internal staging paths separate TIA project-creation path limits from ordinary Git / output directory lengths.

## Local AI API Gateway

Opening the GUI starts the Gateway. Agents first read `/capabilities` and `/openapi.json` and use the interfaces actually advertised:

| Endpoint | Purpose |
| --- | --- |
| `GET /health`, `GET /capabilities` | Health and capability discovery |
| `GET /api/v1/gateway/status` | Gateway, AI connection, TIA binding and pending approvals |
| `POST /api/v1/open-offline` | Open a controlled offline project copy |
| `GET /api/v1/ai/context`, `program-graph`, `network`, `where-used` | Source-backed engineering, network and variable-reference queries |
| `POST /api/v1/ai/patches/preview`, `apply` | Preview and request one controlled edit |

HTTP patches wait for one-time GUI approval before writing. Private operator controls use a random process key generated by the GUI; an independently started read-only Gateway cannot approve requests or shut down the service. Preview tokens expire, bind the project/request state and are single-use.

Structured patches currently support `upsert_tag` in root tag tables and bounded LAD `replace_output_condition`. Successful apply requires Compile, export determinism, round-trip Verify and AI semantic checks. Failure rolls back the controlled copy. The input `.ap21` is never saved.

MCP tools are read-only by default. Explicit headless `--allow-write` skips GUI approval while retaining preview tokens and offline-copy validation. See [TIA AI Gateway](docs/TIA-AI-BRIDGE.en.md).

The Gateway binds only to localhost. Local desktop agents can connect; installing a Skill alone does not let a cloud chat page access a user's computer.

## Scope and verification boundaries

- Supported subset: V21 / S7-1200, one PLC, Main OB1 LAD and root tag tables. Current CPU profile: `OrderNumber:6ES7 212-1AE40-0XB0/V4.7`.
- A self-authored demo has real TIA evidence for Export → fresh Build → Compile → re-export → Verify. A later nonempty LAD fixture covers six Bool tags and three networks. See [architecture and coverage](docs/ARCHITECTURE.md), [LAD receipt](docs/verification/lad-graph.md) and [source contract](docs/contracts/roundtrip-source-v1.md).
- LAD graphs and patches accept only verified ordinary contacts, negated reads, ordinary coils and serial AND structures. Unknown instructions, ambiguous topology and unsupported content cannot yield a complete expression or a false PASS.
- OR reconvergence, arbitrary parallel rewrites, stateful timers/counters, S7-1500, HMI, Safety, drives and multi-PLC projects are not covered.
- No online PLC download, start/stop, force or variable writes. Engineering-semantic verification does not prove arbitrary runtime behavior or identical `.ap21` binary bytes.

Older acceptance records apply to their named candidates, not automatically to the latest integrated revision. Doctor/SARIF and provider-neutral advisory AI remain separate PR #7 / #5.

## Development and packaging

Target: Windows x64, TIA Portal V21 / Openness, effective Siemens TIA Openness group permissions and Git for Windows. GUI / worker use .NET Framework 4.8; the .NET 8 Gateway host is packaged self-contained.

```powershell
dotnet build src/TiaGuard.Gui/TiaGuard.Gui.csproj -c Release
pwsh -File scripts/package-windows.ps1 -Version 0.1.0-prealpha.1
pwsh -File scripts/package-gateway-skill.ps1
```

The Windows ZIP includes GUI, CLI, Gateway, the isolated `bridge/worker/` net48 worker, TIA-Guard core and bilingual getting-started/Gateway guides. `scripts/package-bridge.ps1` builds a standalone Gateway; `scripts/package-cli.ps1` builds a standalone CLI.

```powershell
dotnet test tests/TiaGuard.Contracts.Tests/TiaGuard.Contracts.Tests.csproj -c Release
dotnet test tests/TiaGuard.Bridge.Host.Tests/TiaGuard.Bridge.Host.Tests.csproj -c Release
python -m pip install jsonschema==4.26.0
python scripts/test-contract-schemas.py
```

Pure tests need no Siemens DLLs and do not replace real local Openness evidence.

## Public source and engineering data

The repository contains application source, tests, documentation, icons and sanitized/self-authored canonical fixtures. It excludes TIA project binaries, Siemens DLLs, licenses, credentials, environment configuration and build outputs. See the [ignore policy](.gitignore). Comments and canonical XML can still contain engineering information; export is not automatic anonymization.

## Disclaimer

This is an independent open-source project and is not affiliated with, authorized by, or endorsed by Siemens AG. Siemens, TIA Portal, SIMATIC, STEP 7, WinCC, and related names are trademarks of their respective owners.
