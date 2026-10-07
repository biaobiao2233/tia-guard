# Related projects, design sources and next improvements

[中文](RELATED-PROJECTS.md) | **English**

[Home](../README.en.md) · [AI Gateway](TIA-AI-BRIDGE.en.md)

Checked on 2026-10-07. This page acknowledges existing tools and explains our scope and possible improvements. It uses pinned upstream documentation and repository records, not a comprehensive upstream audit or a claim that every project is production-ready.

## Evidence for reference relationships

- **Recorded MCP design sources**: [Bridge documentation](TIA-AI-BRIDGE.en.md#design-sources) names both TIA MCP projects and records no wholesale repository copy; our own contracts and boundaries remain.
- **Gateway design source**: on 2026-10-07 the project author explicitly confirmed the EasyEDA Pro Run API Gateway influence. Its companion Skill belongs to its published architecture. This does not establish reuse of code, WebSocket protocol or EDA APIs.
- **Research subjects**: [the 2026-09-27 report in #8](https://github.com/biaobiao2233/tia-guard/issues/8#issuecomment-5855568296) lists CODESCRIBE, iec-checker, RuSTy, IronPLC, radevgit/plc and CODESYS File-Based Storage. Research is not automatically a dependency or direct design source, and does not recover the complete original discussion list.
- Scope differences describe platforms and tasks. A missing README feature is not proof of its absence. Upstream capability is not TIA-Guard acceptance evidence.

## Existing coverage and complementary roles

| Project / pinned source | Published coverage and strengths | Scope difference and lessons |
| --- | --- | --- |
| [CODESCRIBE](https://github.com/greenforge-labs/codescribe/blob/ee404ce9cf4a1d13bf3599518114fa39d77f58ab/README.md) | CODESYS ScriptEngine ST/native-XML export/import, Git review and derived graphical reading views. | Some configuration relies on templates; derived diagrams are not import source. Learn object-level text, source/view separation, export-only status and staged replacement. This does not qualify a CODESYS adapter here. |
| [iec-checker](https://github.com/iec-checker/iec-checker/blob/d3e5dae2c9b5096a197e4134d7d0549201f3a953/README.md) | IEC 61131-3 static analysis, PLCopen rules and structured diagnostics. | Optional analysis candidate, not a substitute for Siemens compilation/rebuild verification. Not integrated. |
| [RuSTy](https://github.com/PLC-lang/rusty/blob/8032704d1c454be34a13daa54dfa51c959c09333/README.md) | Rust/LLVM ST compiler and language testing ecosystem. | Learn conformance and test cases; compiler results do not prove a TIA project or PLC runtime. Not our compiler. |
| [IronPLC](https://github.com/ironplc/ironplc/blob/0f4c100ad8440488e118c12732d915b3dfe7b230/README.md) | IEC 61131-3 toolchain with compiler, runtime, editor and MCP entry points. | Learn format/dialect boundaries, diagnostics and tests; its acceptance does not cover our Openness loop. Not integrated. |
| [radevgit/plc](https://github.com/radevgit/plc/blob/8d940638e85d4e4d721e4a94c6aa85fbf04e9455/README.md) | L5X/ST parsing, code-smell detection and visualization. | Learn source locations and parsing/analysis/view separation. Parsing is not a vendor-tool rebuild. Not integrated. |
| [CODESYS File-Based Storage](https://www.codesys.com/ecosystem/release-lifecycle/releases-updates/file-based-storage/) | Official file-based storage direction recorded in #8. | No installation/runtime check in this review. Assess official entry points first; a commercial product is not an open-source library to copy. |
| [Czarnak/tia-portal-mcp](https://github.com/Czarnak/tia-portal-mcp/blob/b11cbf230f8fa3a96f0c84c49a202a9e25b57d78/README.md) | TIA V21 MCP, persistent net48 worker, exact binding, guarded access and structured results. | Recorded design source. Learn pagination, completeness/warnings, identity drift and recovery. Keep our default read-only/offline preview/apply contract; evolved upstream policy does not mean current protocols match. |
| [TIA_Portal_Openness_MCP](https://github.com/bulaofen0036-coder/TIA_Portal_Openness_MCP/blob/59b2ffaa88dcf494b2242b5b4c7d4f43f46b0cf8/README.md) | TIA V20/V21, MCP/CLI, declarative PLC/HMI operations, compilation diagnostics and VCI/Git. | Recorded capability study. Learn matrices, templates and tool documentation, adding tools individually against fixtures. VCI export and our fresh Build/Verify have different acceptance scopes. |
| [EasyEDA Pro Run API Gateway](https://github.com/easyeda/eext-run-api-gateway/blob/e55348027d1008a3d448b8b228fbca7b82dc8381/README.md) and [companion Skill](https://github.com/easyeda/easyeda-api-skill/blob/b9bf568239b235592bb0c12c6ac233502b9801c1/README.md) | Local agent/Bridge/EDA access, WebSocket handshake, connection/recovery feedback and API documentation/workflows. | Gateway design influence. Learn local bridging, service identity, connection feedback and companion docs. Our HTTP/MCP, net48 Openness worker and bounded offline patches are not EDA APIs or arbitrary-code execution. |

## The concrete problem addressed here

Existing tools contribute text export, analysis, compilation and engineering automation. TIA-Guard focuses on an inspectable supported Siemens engineering loop:

`TIA V21 project → deterministic canonical source → Git review → fresh rebuild → compilation and supported engineering-semantic Verify`

Canonical source carries build facts; `ai/`, graphs and summaries are derived and cannot replace it. GUI, CLI and Gateway share this source. Unsupported content, incomplete reads and failed verification must not produce false PASS. Exact binding, single-use preview tokens, per-preview GUI approval for HTTP and disposable-copy rollback exist, but not every real path is qualified.

- [Historical LAD verification](verification/lad-graph.md) includes nonempty fixture Build/Compile/Verify, limited truth tables and source references.
- [Publication evidence](verification/publication-20261001.md) separates pure tests and package/startup checks from real TIA operations. The latest integrated GUI/Gateway patch loop still needs requalification.
- [Source contract](contracts/roundtrip-source-v1.md) and [LAD scope](LAD-GRAPH.md) define boundaries. Historical evidence belongs to its stated revision; semantic equality is neither PLC runtime proof nor arbitrary TIA support.

## Adoption order and acceptance

These are plans, not new features or completion claims. Close evidence gaps before expanding; no fixed dates are promised.

| Order | Lessons and existing foundation | Next step and acceptance |
| --- | --- | --- |
| **P0: qualify current integration** | MCP identity/failure contracts and EasyEDA connection feedback; binding, preview/apply, rollback and Host tests exist. | [#41](https://github.com/biaobiao2233/tia-guard/issues/41): pin candidate/nonempty fixture, execute current GUI/Gateway in real V21; patch then Compile, re-export, fresh Build and Verify; separate refusal/rollback evidence; original hashes unchanged. |
| **P1: improve Git review** | Object-level text and derived views; canonical source, references, limited LAD graphs and slots exist. | Audit current output, then add deterministic PR summaries of symbol/network changes and support states using the existing parser. Repeatable bytes, no layout-induced logic false positives, source-resolvable changes, incomplete unknowns and preserved valid output on failure. |
| **P2: extend logic and diagnostics individually** | Compiler conformance/dialect tests and static diagnostics. | First candidate: self-authored V21 OR reconvergence, with native export, topology, exhaustive small truth tables, positive/negative tests, unknown-instruction rejection and real Compile/Build/Verify. Assess ST checks only after a validated ST input contract; timers/counters/scan order require separate semantics. |
| **P3: one read-only platform entry point** | Script/official file entry points and explicit capability/version matrices. | With environment and nonempty fixture, select one TwinCAT Automation Interface or CODESYS ScriptEngine platform/version for reads/export. Record tool/device/library versions, states, repeatable export and preserved output on failure. No round-trip without rebuild evidence or assumption that the TIA contract is universal. |

P1–P3 have not started implementation. Before each slice, inspect code and ownership, select one bounded change and acceptance fixture, and use a short branch. Upstream #10/#11/#12 do not automatically become core work.

## Attribution, reuse and contributions

This change adds documentation and plans only, with no upstream code or runtime dependencies. Future code/document/fixture reuse must record repository, exact commit, files and modifications; check licenses and preserve required copyright/license/NOTICE. Separate design influence from code reuse. Third-party engineering data requires redistribution permission and sanitization.

Follow the [contribution guide](../CONTRIBUTING.md#plc-platform-adapters) with reproducible positive/negative cases and actual environment evidence. Tests and maintainer review precede merge. API connectivity, pure tests, compilation, semantic round trips and actual execution are separate evidence.
