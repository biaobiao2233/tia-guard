# GUI and local AI Gateway source publication — 2026-10-01

## Candidate identity and scope

This publication integrates the existing `feat/ai-bridge-v2-integration` implementation at `cfb43b0a5126fbd2b0ba93d645c80201f4a75b11` with public `main@1e8c49171770c248b7aae907a116a21b3e0a3f51`.

The publication integration does not change `src/` or `tests/` relative to that implementation:
- `src/` tree: `ca5ed1b547543be9584efbf60a849f0410faa31f`.
- `tests/` tree: `e699c3d9037d88e1ebcc5ce1d49aa28cff3953cc`.

Integration changes are the public README/status documentation, inherited bootstrap identity cleanup, this receipt, and the CI step for existing Bridge Host tests. The final commit, PR, CI and main pointers are recorded in [publication issue #36](https://github.com/biaobiao2233/tia-guard/issues/36).

The source includes the GUI and multi-project Git workflows, CLI, derived AI engineering views, local HTTP/MCP Gateway, structured patch coordination, GUI approval controls and isolated net48 worker. Source publication is distinct from a binary Release and real TIA runtime qualification.

## Automated checks

| Check | Result |
| --- | --- |
| Engineering, Git and filesystem contract tests | 152 / 152 passed; no failures or skips |
| Bridge Host product and authorization tests | 45 / 45 passed; no failures or skips |
| Snapshot/canonical source schemas and negative cases | Passed |
| AI Engineering v1 schema and five negative cases | Passed |
| LAD v2 schema and seven negative cases | Passed |
| CLI, GUI and net48 worker Release builds | Passed, zero warnings/errors |
| Self-contained .NET 8 win-x64 host publish | Passed |
| Openness Smoke Release build / pure self-test | Passed, zero build warnings/errors |
| Staged whitespace/conflict check | Passed |

Tests ran on Windows in an isolated publication checkout. The final contract run used a short dedicated TEMP/TMP directory: the command connector's deep default temporary path exceeded .NET Framework fixture path limits. No application code was changed for this environment issue. Packaging completed with PowerShell 7 after a mixed PowerShell module search path blocked `Get-FileHash` under Windows PowerShell 5.1.

## Package inspection

Local validation outputs only; no GitHub Release assets are published by this source integration.

| Artifact | Entries | SHA-256 |
| --- | ---: | --- |
| `tia-guard-v0.1.0-windows-x64.zip` | 345 | `f0116ff7c7328c3906497322907e8f807efe95263448f7f240c051b5c3706356` |
| `tia-guard-bridge-v0.1.0-windows-x64.zip` | 339 | `b73b3010ad75d85942bc2592badd4b7fecded9f24cee6001df97ce12e85fd64c` |
| `skill.zip` | 1 | `61e63d6413d1798da664e59687d52b5e28fb66a6898296b3063375e38e4217e5` |

All ZIP entries passed CRC inspection. The combined package includes GUI, CLI, the self-contained host and `bridge/worker/TiaGuard.Bridge.Worker.exe`; the standalone Bridge also isolates the worker. No Siemens DLLs, TIA project binaries or license/private-key files were found. Skill ZIP contains the single canonical `tia-guard-gateway/SKILL.md`.

## Packaged runtime boundary

The bundled Gateway was started by the verifier on an unused ephemeral `127.0.0.1` port, without `--allow-write` or an operator approval key. Only the owned verifier process was terminated after checking.

- `/health`, `/capabilities`, `/openapi.json` and `/api/v1/gateway/status` returned HTTP 200.
- Health reported `TIA-Guard Bridge`, `read-only`, and bind `127.0.0.1`.
- Gateway status reported ready, no TIA project connection and no pending approvals.
- OpenAPI contained 15 public paths and excluded the private approval/shutdown controls.
- Unauthenticated allow, reject and shutdown calls each returned HTTP 403 / `APPROVAL_FORBIDDEN`. Health remained available afterward.
- Packaged CLI `--version` returned `tia-guard 0.1.0`; read-only `doctor` returned valid JSON and exit 0.

These checks did not bind an engineering project, launch an engineering operation, edit a supplied project or interact with a PLC.

## Public-source hygiene

Current tracked source was checked for credentials, private keys, personal machine identifiers/paths and prohibited generated/project/vendor artifacts. No confirmed current credentials or prohibited artifacts were found. The all-history Gitleaks scan reported two occurrences of the same already known false positive: a baseline Git commit hash in a historical verification document.

Historical author metadata is retained as explicitly requested by the repository owner. Public canonical source can still contain engineering names/comments; export is not an automatic anonymization process.

## Acceptance limits

This receipt qualifies source integration, pure automated checks, Windows packaging and the packaged HTTP control boundary. It does **not** record a new real TIA GUI/Gateway patch-apply round trip.

Earlier bounded TIA V21/S7-1200 runtime receipts remain evidence for their stated candidates. They must not be relabeled as verification of this latest integrated version. Arbitrary hardware, languages, runtime equivalence, production use and online PLC operations remain outside this gate. The next runtime gate should use a self-authored disposable fixture and record the exact integrated revision.
