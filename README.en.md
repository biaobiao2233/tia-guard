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

## Future direction and community adapters

The long-term goal is to extend readable engineering source, Git versioning, review and verified rebuilds to more PLC projects, bringing different platforms into shared project indexing, version history and collaboration workflows. Adapter work will follow the platforms we actually use, available engineering environments and opportunities for verification. Community adapters built on this project are welcome.

### Candidate platforms and engineering environments

The following are potential directions, all awaiting implementation and verification. Specific models, software versions and capability boundaries will be determined when adapter work begins. The list stays open to additional platforms encountered in practice.

| Platform / vendor | Candidate engineering scope and reference |
| --- | --- |
| Siemens | S7-1500; progressively evaluate more TIA Portal CPU profiles and engineering objects. |
| Beckhoff | TwinCAT 3 PLC projects, ST source and project configuration. |
| Mitsubishi | MELSEC projects; evaluate versions of [GX Works3](https://www.mitsubishielectric.com/fa/products/cnt/plceng/smerit/gx_works3/index.html) and other tools actually used. |
| Omron | NJ/NX and engineering environments such as [Sysmac Studio](https://www.ia.omron.com/products/category/automation-systems/machine-automation-controllers/software/). |
| Rockwell / Allen-Bradley | ControlLogix, CompactLogix and [Studio 5000](https://www.rockwellautomation.com/en-us/products/hardware/programmable-controllers.html) projects. |
| Schneider Electric | Modicon; evaluate [Machine Expert](https://www.se.com/uk/en/product-range/2226-ecostruxure-machine-expert/) and [Control Expert](https://www.se.com/us/en/product-range/548-ecostruxure-control-expert-software/) projects separately. |
| B&R | [Automation Studio](https://www.br-automation.com/en/products/software/automation-studio/) projects and PLC program objects. |
| ABB | AC500 and [Automation Builder](https://www.abb.com/global/en/areas/motion/digital-tools/automation-builder/engineering) projects. |
| CODESYS ecosystem | [CODESYS](https://www.codesys.com/ecosystem/discover-codesys/codesys-inside/) projects and SoftPLCs; qualify vendor extensions, device descriptions, libraries and versions separately. |
| WAGO | [CODESYS engineering environments](https://www.wago.com/global/products/automation-technology/discover-software/codesys-v3) and corresponding controller projects. |
| Bosch Rexroth | [ctrlX PLC](https://apps.boschrexroth.com/microsites/ctrlx-automation/en/portfolio/ctrlx-plc/) and engineering environments encountered in practice. |
| Festo | [CODESYS controllers](https://www.festo.com/media/catalog/204060_documentation.pdf) and their vendor-specific engineering configuration. |
| Phoenix Contact | PLCnext and [PLCnext Engineer](https://www.phoenixcontact.com/en-de/products/programming-software-plcnext-engineer-1046008) projects. |
| Panasonic | FP-series and [Control FPWIN Pro](https://industry.panasonic.eu/products/automation-devices-solutions/programmable-logic-controllers-plc/plc-software/programming-software-control-fpwin-pro) projects. |
| KEYENCE | KV-series and [KV STUDIO](https://www.keyence.com/support/user/controls/plc/) projects. |
| LS ELECTRIC | XGT/XGB and other PLC projects using [XG5000](https://sol.ls-electric.com/ww/en/product/category/476). |
| Delta | DVP, AS, AH and other series using [ISPSoft](https://www.deltaelectronicsindia.com/en-IN/products/PLC-Programmable-Logic-Controllers/15402) or other engineering environments. |
| FATEK | FBs/B1 and engineering environments such as [WinProladder](https://www.fatek.com/en/product.php?act=view&id=162). |
| Inovance | [PLC products](https://www.inovance.com/product) and the programming tools actually used; evaluate by family and version. |
| XINJE | [Programmable controllers](https://www.xinje.com/web/downloadCenter/index) and their engineering tools; evaluate by family and version. |
| Kinco | [PLC products](https://www.kinco.cn/company-introduction) and their programming environments; evaluate by family and version. |
| HollySys | [PLC projects](https://www.hollysys.com/products/industrial-intelligence/control-safety-systems/plc) and their toolchains; evaluate by family and version. |
| SUPCON | [PLC platforms](https://www.global.supcon.com/control-safety-systems/plc) and their engineering environments; evaluate by family and version. |

### Bringing projects into management incrementally

1. **Readable source and versioning**: first investigate read-only parsing, source or project export, indexing and deterministic comparison, making programs, tags, configuration and changes within the adapter's scope reviewable in Git.
2. **Rebuild and verification**: where the platform toolchain permits and sufficient evidence is available, add import, fresh project creation, compilation and round-trip semantic verification, declaring each capability individually.
3. **Shared collaboration and AI engineering views**: explore cross-platform project queries, source references and AI-assisted review while keeping platform-specific engineering objects and verification rules within their adapters.

The guiding principle is: **evaluate adapters for the platforms we actually encounter, and welcome verified community contributions.** This list is not a fixed schedule. These capabilities are not implemented or verified and carry no fixed delivery dates. Each platform retains its own project format, compiler and verification rules. Sharing IEC 61131-3 or a CODESYS foundation still requires separate qualification. Shared management does not imply direct program conversion between vendors, and passing tests on one platform does not qualify another.

### Adapter PRs are welcome

Contributions may start with read-only parsing, export or Git management and add rebuild and verification later, with explicit supported and unsupported scope for each capability. Include reproducible test steps, self-authored or sanitized fixtures and actual verification evidence from the relevant platform. Rebuild support requires compilation and round-trip verification in the engineering tool; runtime claims additionally require real execution or simulation evidence. Passing tests, clear boundaries and maintainer review make a contribution eligible for merge. See the [contribution guide](CONTRIBUTING.md#plc-platform-adapters).

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
