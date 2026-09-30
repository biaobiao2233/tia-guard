# First Windows pre-alpha preparation — 2026-10-01

Tracking: [#38](https://github.com/biaobiao2233/tia-guard/issues/38).

## Source boundary

Base: `main@4d8251e0aaa2e908792e45f4bffa8f8b83fa7e5a`. This change adds paired Chinese/English README, getting-started and Gateway documents and versioned package names/document inclusion. Application `src/` and `tests/` are unchanged. The final candidate/merge and release state are recorded in #38.

The package version is `0.1.0-prealpha.1`; the unchanged CLI reports core version `0.1.0`.

## Checks performed in the isolated Windows checkout

- 152/152 engineering contracts and 45/45 Gateway tests; no failures, errors or skipped tests (TRX counters inspected).
- Source, AI v1 and LAD v2 schema/negative checks passed.
- CLI, GUI and net48 worker Release builds: zero warnings/errors; self-contained win-x64 Gateway publish passed.
- Paired documentation language links, local targets, code fences and essential command/scope parity passed.
- ZIP CRC and required GUI/CLI/isolated worker/document content passed. Packaged guides match current source bytes. No Siemens DLLs, TIA project binaries or private-key/certificate files found.
- Actual combined ZIP extracted for runtime checks: CLI `--version` and JSON `doctor` exited 0.
- Bundled Gateway started on an owned ephemeral loopback port: health, capabilities, OpenAPI and Gateway status returned 200. Unauthenticated allow/reject/shutdown controls returned 403; service remained healthy.
- Packaged GUI displayed its main window (`TIA-Guard`) and started its Gateway at the standard loopback port. The verifier closed only the newly launched GUI; no project was bound or edited.
- Skill frontmatter, exact single-entry ZIP and canonical byte-content match passed.

## Final artifacts

| Asset | Bytes | ZIP entries | SHA-256 |
| --- | ---: | ---: | --- |
| `tia-guard-v0.1.0-prealpha.1-windows-x64.zip` | 45,676,076 | 353 | `1bf1102949326f895982fbaf980bbfbd0999ee6125544254739d7b3970a8a446` |
| `tia-guard-bridge-v0.1.0-prealpha.1-windows-x64.zip` | 45,514,346 | 343 | `4a1cf9bc4af9064b244fd596a997b430cae15c2e74e911cec70c8b1ac18bbc39` |
| `skill.zip` | 2,995 | 1 | `61e63d6413d1798da664e59687d52b5e28fb66a6898296b3063375e38e4217e5` |

`SHA256SUMS.txt` records these three asset hashes. Package binaries came from the successful build; a final documentation-only update was copied into stages and ZIPs recreated and fully rechecked before extracting the runtime smoke copy.

## Acceptance limits

This qualifies bilingual user documentation, package preparation and packaged startup/control boundaries. It does not requalify the latest integrated real TIA GUI/Gateway engineering round trip or patch apply. Historical receipts remain bound to their exact candidates. No online PLC operations, supplied-project changes or email operations occurred.

A published preview is not a stable release or arbitrary-project/runtime acceptance. Draft/public release state must be checked on GitHub, not inferred from these local packages.
