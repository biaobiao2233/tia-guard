# P0 contract and native test plan

The adapter integrates an unchanged upstream process through public MCP. Fixture tests prove adapter behavior only. Native tests must use the checksum-verified official runtime; they cannot inherit engineering or PLC runtime qualification.

Required tests:

- Runtime identity: full pinned file inventory/hash, unexpected or changed files, manifest/version drift and path rejection.
- Contract evolution: missing tool, duplicate tool, new required argument, changed Doctor.fix type, optional additive parameter, malformed/partial/failed results and unknown connection state.
- MCP subprocess composition: persistent upstream process, forced read-only launch arguments, Doctor(fix=false), no generic command forwarding, strict caller schemas, actual stdout JSON-RPC, timeout/crash denial, owned-child close and no implicit restart.
- Upgrade metadata: same version, newer stable version, wrong origin, missing checksum, ambiguous asset and same-tag replacement; checks never download or promote.
- Native Windows: real upstream initialize/list/GetState/Doctor through the facade, fixed version/source/runtime identity, stable engine session, explicit close, unchanged pre-existing TIA/PLCSIM processes. Keep raw local paths/logs under .local.

General tests run on an isolated cloud workspace; original Windows engine checks run locally. No engineering creation, compilation, HMI or PLC run is covered by P0.

## Executed on 2026-11-15

- Cloud: syntax checks, 20 Node tests and 4 Python tests passed, with no failures or skips. Canonical source hashes for the tested 17 files match the local files.
- Windows 11 / TIA V21: the real facade and unchanged v2.7.11 engine passed discovery, disconnected GetState, Doctor(fix=false), persistent session, forbidden-call rejection and owned-child cleanup. Two consecutive cold starts used the same already-used runtime directory.
- Earlier failed runs and fixes are retained in the [sanitized verification](../../../docs/verification/upstream-mcp-p0-20261115.md), with exact source/receipt hashes. Raw machine-specific receipts stay in .local.
- Engineering and PLC runtime qualification remain false; first onboarding has no accepted or verified rollback version.

## Owned-copy read subset on 2026-11-15

- Final cloud checks: 27 Node tests, 4 Python tests; no failed/skipped tests. Pagination, exact project binding, new output directories, partial-result denial and caller path rejection are covered.
- The native MCP workflow read 14 PLC-root blocks and 67 tags, exported 14 XML files, reopened the copy and compared block identities, confirmed unchanged source files, disconnected and exited. XML names were independently checked against the native list.
- See the [owned-read evidence](../../../docs/verification/upstream-mcp-owned-read-20261115.md). This does not qualify project edits/save, compilation, HMI or PLC execution. Earlier failures remain in their original local receipts.
