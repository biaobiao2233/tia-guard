# TIA-Guard

[中文](README.md) | **English**

**Connect AI agents to engineering software: read projects, create or change code, compile it, and repair errors using real diagnostics.**

TIA-Guard is an open-source local integration tool with separate **TIA Portal** and **TwinCAT 3** adapters. MCP and official engineering APIs connect an agent to the software's own compiler and verification capabilities. This is an **alpha preview**; prerequisites and verified scope differ by adapter.

Existing names, commands and configuration remain compatible. The main focus is now **AI access to engineering software and verified operations**. TIA source conversion, Git workflows and round-trip rebuilding remain optional features.

## Download for your software

Choose the adapter you need from [one GitHub Release](https://github.com/biaobiao2233/tia-guard/releases). Install both packages if you use both applications; a shared Gateway or core installation is not required.

| Package | Contents | AI interface |
| --- | --- | --- |
| `tia-guard-tia-v<version>-windows-x64.zip` | TIA Portal V21 GUI, CLI, AI Gateway and isolated Worker; project generation and optional Git workflows | MCP stdio, Streamable HTTP, local HTTP API |
| `tia-guard-twincat-v<version>-windows-x64.zip` | TwinCAT 3 MCP, compiled Windows Worker, production Node dependencies and examples | MCP stdio |
| `tia-guard-bridge-v<version>-windows-x64.zip` | TIA integration without the GUI; already included in the full TIA package | MCP stdio, Streamable HTTP, local HTTP API |
| `skill.zip` | Optional TIA Gateway instructions for an agent; not a substitute for the adapter or engineering software | Companion Skill |

**Install the engineering application and required official components yourself.** Packages exclude TIA Portal, TwinCAT XAE, user projects, licenses and DLLs copied from vendor installation directories. The TwinCAT package includes public NuGet/Node dependencies under their respective licenses and requires Node.js ≥22 and the .NET 8 Windows Desktop Runtime x64.

Start with [package installation and MCP configuration](docs/ADAPTER-PACKAGES.md). The older `v0.1.0-prealpha.1` uses different asset names and does not include all capabilities below.

## Current capabilities

| Capability | TIA Portal V21 / S7-1200 | TwinCAT 3 |
| --- | --- | --- |
| Detect and read | CLI doctor, engineering context, tags, bounded LAD graphs and references | Doctor, static inspection and actual backend capability reporting |
| Create from source | SCL → new `.ap21`, with supported OB1 / FC / FB / DB / UDT / tags | ST → new project, with supported Program / FB / GVL / structures / tasks and libraries |
| Edit and repair | Change generation inputs and rebuild; bounded tag / LAD patches on a disposable copy | Change ST inputs, then obtain a new preview and grant to generate or compile an owned copy |
| Compile and diagnose | Actual Openness compilation and complete diagnostics | Actual XAE compilation, with error locations where available |
| Independently verify | Close/reopen the new project and compare supported source and engineering semantics | Save/close/reopen and independently compare objects, source and configuration |
| Software simulation | **Experimental, disabled by default**: instance lifecycle, power, SIM table addresses and compile preparation verified; full download / RUN / scans / actual I/O remain unqualified | Owned Usermode PLC simulation, RUN/STOP, scalar feedback and valid-license reuse verified; separate single-axis NC/PTP simulation evidence |
| Git engineering | Optional readable source, diffing, GitHub rebuild/export/commit and multi-project GUI | Existing text-project and Git tooling can be used directly |

Capabilities are bounded to the stated software versions, objects and environments. They do not cover arbitrary projects or physical equipment. **CODESYS remains in unmerged [PR #64](https://github.com/biaobiao2233/tia-guard/pull/64) and is not included in this release.**

## Agent workflow

1. Configure the adapter's MCP in your AI client. Read Doctor / capabilities to determine what is actually available.
2. Inspect a project or prepare supported SCL / ST inputs, then preview the requested operation and destination.
3. Explicitly enable writes to create a new project or operate on an owned copy; preserve the original project.
4. Compile using the engineering software, inspect actual diagnostics and repair the input. Changed source requires a fresh preview and execution.
5. Deliver the independently verified new project. Enter simulation separately, within that adapter's supported scope.

MCP is the primary agent interface. TIA also exposes HTTP and CLI; TwinCAT connects directly through its own stdio MCP and does not need the TIA Gateway. Use your existing AI model and client.

## TIA Portal

Requires Windows x64, TIA Portal V21, Openness and effective Openness membership in the current logon token. The user handles any official access confirmation; this tool does not change groups, access permissions or licenses. The current CPU profile is `OrderNumber:6ES7 212-1AE40-0XB0/V4.7`.

- [Installation and GUI guide](docs/GETTING-STARTED.en.md): Chinese WPF GUI with automatic local Gateway startup.
- [AI Gateway / MCP](docs/TIA-AI-BRIDGE.en.md): context, guarded patches, single-use previews, compilation, rollback and publication to a new copy.
- [Create a PLC project from source](docs/PROJECT-GENERATION.md): `init`, `build-generation` and HTTP / MCP equivalents.
- [PLCSIM experiment](docs/PLCSIM-AUTOMATION.md): exact scope, default-off switches and unfinished work.
- [Round-trip source contract](docs/contracts/roundtrip-source-v1.md): supported `.ap21 → readable source → new .ap21` semantics.

```powershell
# Run from the extracted full TIA package
.\tia-guard.exe doctor
.\tia-guard.exe init E:\PLC\input
New-Item -ItemType Directory -Path E:\PLC\scratch
.\tia-guard.exe build-generation E:\PLC\input --output E:\PLC\new-project --scratch E:\PLC\scratch
```

The optional Git GUI keeps GitHub → TIA rebuild, TIA → GitHub export/commit, and the `tia-projects/<slot>/tia-source/` layout. It reuses system Git and existing credentials, embeds no GitHub token and does not force-push. Git is not required for the AI project-generation workflow.

Existing LAD patches cover only verified contacts, negated reads, ordinary coils and bounded series conditions. Arbitrary parallel logic, stateful instructions, S7-1500, HMI, Safety, drives and multi-PLC projects are outside the verified scope. [Issue #41](https://github.com/biaobiao2233/tia-guard/issues/41) separately tracks the latest integrated GUI/Gateway real TIA modification loop; pure tests do not qualify it.

## TwinCAT 3

See the [adapter README](adapters/twincat3/README.md) and [package setup](docs/ADAPTER-PACKAGES.md#twincat-3). The qualified environment is Windows x64, XAE 3.1.4026.27 / `TwinCAT OS (x64)`; the NC Usermode module is 3.1.5049.0 and the NC example pins Tc2_MC2 3.3.75.0. Other versions require separate qualification.

- [MCP tools and workflow](adapters/twincat3/docs/MCP.md)
- [Worker, local interop and prerequisites](adapters/twincat3/docs/WORKER.md)
- [ST motor example](examples/twincat3-generation/motor/twincat-generation.json)
- [Single-axis NC/PTP example](examples/twincat3-generation/nc-axis.md)

Real MCP evidence covers invalid ST → actual compilation failure → repaired input → fresh preview/grant → successful build → independent reopen verification. Separate source-bound records cover [PLC runtime](adapters/twincat3/evidence/runtime-20261015.json), [persistent instance / valid-license reuse](adapters/twincat3/evidence/persistent-runtime-20261015.json) and [30→80→0 mm NC motion](adapters/twincat3/evidence/nc-motion-20261015.json).

Simulation requires official components and a valid license; the user handles official trial challenges. Only adapter-owned Usermode instances are operated. Physical motors, EtherCAT, existing user runtimes and global routes are excluded. Coordinate zeroing is not mechanical homing; simulation is not safety or real-time qualification. The adapter is independent of the TIA GUI / HTTP API.

## Verification, contributions and attribution

See [v0.2.0-alpha.1 release notes](docs/releases/v0.2.0-alpha.1.md) for source and packaging verification. `SHA256SUMS.txt` and `release-manifest.json` identify the uploaded artifacts. Native engineering evidence applies only to its recorded source/environment; package smoke tests do not qualify untested operations.

Contributions can add adapters or expand verified object coverage. Reuse official APIs/scripts and established projects where possible, with explicit detection, diagnostics, verification and operation boundaries. Adapters need not share one project-file format or Git workflow.

Design references include [EasyEDA Run API Gateway](https://github.com/easyeda/eext-run-api-gateway), [Czarnak/tia-portal-mcp](https://github.com/Czarnak/tia-portal-mcp) and [TIA_Portal_Openness_MCP](https://github.com/bulaofen0036-coder/TIA_Portal_Openness_MCP). See [related projects](docs/RELATED-PROJECTS.md) and the TwinCAT adapter's license directory for sources, reuse scope and licenses. Older Doctor/SARIF and advisory AI proposals [#7](https://github.com/biaobiao2233/tia-guard/pull/7) / [#5](https://github.com/biaobiao2233/tia-guard/pull/5) remain unmerged.

[MIT License](LICENSE). Independently developed; not affiliated with Siemens, Beckhoff or other vendors.
