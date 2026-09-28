# Source-truth repair evidence

Observed 2026-09-28 on Windows, .NET SDK 8.0.425 / .NET Framework 4.8,
with locally installed Siemens TIA Portal V21 PublicAPI. Baseline:
`a4d2bbe51016c3067ad86a37c7774caacd31510c`.

This receipt belongs to the repair candidate containing it. It records implementer
verification, not independent acceptance or a merge. No original/canonical `.ap21`
was opened or modified. No PLC online, download, write or force operation occurred.
Only newly built projects and their owned offline copies were used for live tests.

## Repairs

- Named unsupported inventories now block readiness, including unreadable scans.
  The observed single empty default `Force table` is explicitly bounded.
- Export readiness uses the same complete-source validator as Build and Verify.
- CPU identity, tag addresses/types, JSON primitives, XML identities, additional
  block objects and Windows path components are checked before TIA starts.
- Build imports frozen validated XML from a private locked file; cleanup refuses
  reparse-point ancestors and children after caller ownership checks.
- Manifest/descriptor schemas, current architecture and product claims are aligned.
  A pure test workflow and synthetic fixtures require no Siemens distribution.

## Local checks

| Check | Observed result |
| --- | --- |
| Release CLI and Smoke builds | PASS, zero compiler warnings/errors |
| Pure production-source xUnit tests | 51 executed, 51 passed, zero failed/skipped |
| Snapshot and round-trip schemas | Positive fixtures and four negative cases PASS |
| Existing Smoke `self-test` | Exit 0 |
| Real Windows junction negative cases | Load/output/raw-input rejected; redirected cleanup refused; target preserved |
| Final CLI fresh build from historical canonical JSON/XML | Exit 0, PLC compile errors 0 / warnings 0 |
| Re-export of the first newly built project using repaired extractor | Ready, zero diagnostics; all six canonical files byte-identical to input |
| Full Verify of two separately built owned projects | Exit 0, verdict `pass`, differences empty; rebuilt-copy compile errors 0 / warnings 0 |
| Live owned-copy unused `Int` user constant | Readiness false, `USER_CONSTANTS_UNSUPPORTED`; invalid self-comparison `blocked` |
| Live owned-copy empty tag folder | Readiness false, `TAG_FOLDERS_UNSUPPORTED` |
| `git diff --check` | PASS |

The final CLI, Smoke harness and temporary live-negative helper loaded the same
`TiaGuard.Openness.dll`, SHA-256:
`bdd7e4c5ec4d15f9406264e06fea1032681c7da66db7d05592de521e2a54a614`.
This is an observation of the local build artifact, not a portable binary checksum
promise for builds in other paths/environments. No binary is included in the repair.

The historical canonical SimaticML input SHA-256 was:
`382e9a41898cdd73738690fda8b822d626b1852a51dfedea42858139de36d857`.
It was read as engineering-source input; its original project binary was not used.

The live negative procedure opened a disposable copy of the first fresh output,
created an unreferenced `Int` constant in its tag table, exported and checked both
readiness and comparison rejection, removed that constant in the copy, then created
an empty tag folder and checked diagnostic rejection. The copy was disposed without
saving to the supplied output. The temporary helper is test scaffolding, not a new
product command or a PLC operation.

## Failed attempts and limits

An earlier concurrent final-build attempt timed out with an Openness security error;
the concurrent negative helper terminated with a CLR exception. These are failed
attempts, not PASS evidence. Serial retries of the same final build/helper succeeded,
followed by successful full Verify. No machine permissions or Openness approval
settings were changed. The underlying transient cause was not established.

The initial broader force-table rejection also blocked the normal demo. Inspecting
only the newly built project established its automatic empty default table. The
bounded default exception was then added, unit-tested, and re-exported successfully.

The real project proof still covers the exact CPU/firmware, observed topology and
empty tag table. Populated tags and most unsupported-object categories have pure
tests but no new category-by-category Siemens integration acceptance. There is no
runtime/control-logic proof, hardware parameter/IP equivalence or arbitrary-project
support. The filesystem guards are not a hostile same-account race sandbox.

GitHub Actions configuration has been added but was not executed remotely in this
session. Issue/PR states, remote main, protection rules and the paused router were
not changed. Required independent review and integration remain separate steps.
Raw logs, real engineering exports, machine paths, Siemens DLLs, licenses and project
binaries are intentionally absent from this receipt and candidate.

The final local cleanup command for this run's newly built temporary output folders
was rejected by the tool policy (`blocked by policy`). It was not bypassed; those
local test outputs remain outside the repository. The owned Portal processes had
already exited, leaving the two pre-existing user processes unchanged.
