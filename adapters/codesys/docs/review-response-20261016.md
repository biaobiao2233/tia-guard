# Response to independent review of PR #64

Reviewed head: `26d46c20917ab97ff437e346f408398a507a2b3d`; base:
`ed6a7bfe04d0fc90c718d4eda12335e4681d47c1`. The user supplied the external
read-only review on 2026-10-16. Its seven findings are accepted. The earlier
single normal-path native observation remains historical evidence for `f856071`;
it does not qualify these changes or the exceptional paths identified by review.

| Finding | Repair | Regression evidence |
| --- | --- | --- |
| P1 missing tasks can pass equal snapshots | Compare native PLCopenXML object kinds/types, exact object set/text, simulation device and the single MainTask/Cyclic/t#20ms/entry call against the frozen request, before compile and both sides of reopen. | Production worker functions with missing/wrong tasks, wrong object kind and input type; independent outer snapshot checks. |
| P1 stale qualification after reopen failure | Revoke verified before any precondition or vendor operation; revoke compilation before every edit/compile attempt; bind the compiled snapshot fingerprint to reopen/login. | Inject save/open failures after an earlier verified state; subsequent login rejected. |
| P1 uncertain native command can be followed by another | Timeout, child loss and protocol/result validation errors close the guard and transport permanently; revoke all previews/grants; allow only bounded safety cleanup, then kill/await the exact owned child when needed. | Actual portable child process with ignored commands, wrong operation/context, premature exit; provider verifies no subsequent call/grant is possible. |
| P2 weak compiler origin/completeness | Clear message stores per rebuild/code-generation phase. Require target application header, known compiler/analysis category and no foreign project/object. Preserve phase diagnostics and exceptions; bind request ID/hash, build ID, native target IDs and source fingerprint. Outer validation independently requires both phases, correlated rows and a real C0 code-generation summary. | Foreign origin/target/request, missing header/summary, wrong category/count, unrelated Text, exception and unexpected False return are rejected. |
| P2 incomplete success accepted by MCP layer | Validate operation-specific success evidence, not just success=true. Create/edit/inspect require complete intended snapshots; reopen requires both matching expected snapshots/fingerprint; login/run/stop require state; reads/writes require complete values/types. Invalid success is terminal. | Each success-only response is rejected, together with mismatched target/context. |
| P2 type erased before input write comparison | Reserved GVL_Test has an intentionally bounded BOOL Enable / DINT Step declaration grammar. Check actual XML metadata and a complete native typed read before preparing a write, then compare complete typed value on readback. | Changed declarations, INT#7 before/after a DINT write, missing monitoring values rejected. |
| P2 repeated cleanup hides earlier failures | Cache cumulative terminal cleanup outcome; record Stop, Logout, Dispose and Close independently, attempting later cleanup steps after earlier failures. Finally returns the same evidence. Outer layer also compares explicit/final cleanup and awaits child exit. | Inject Stop/Dispose/Close failures, repeat cleanup, verify failures unchanged; actual child cleanup-failure result remains failed. |

Portable checks execute the production Python functions with simulated vendor APIs;
they do not prove that the corresponding exceptions occurred in CODESYS. Native
qualification of a new exact source is recorded separately, after cloud checks.
No independent re-review PASS is claimed by the author.

Repair/test source `9152c96754ab155b4046abaaeec51cb54f714680` passed 18 Node
contracts and 15 embedded Python fault-injection checks on Linux and Windows,
plus the existing source regression workflow. Actual stdio MCP / CODESYS
normal-path requalification also passed: native type/phase/proof validation,
wrong reserved input type and invalid monitor variable rejection, compile repair
to 0 errors/0 warnings, request-based reopen verification, counter 37→66,
Stop 132→132 with FB result 139, Run 167→197, and exact owned process exit.
See [source-bound repair receipt](../evidence/review-repair-20261016.json).
The first repair run failed on a Unicode BOM in native PLCopenXML export; its
failure/hash is retained and UTF-8 byte parsing is now tested. No licenses were
activated. This result does not replace an independent review of the new head.

Additional doubts from review: null/empty monitoring now fails; explicit False
from a compiler API fails conservatively (installed stubs do not document a
boolean success contract). Sudden OS power loss or killing the MCP host without
letting it clean up is not fully qualified: the native idle deadline is five
minutes and normal signal/stdio closure is handled, but this is not an OS-level
parent-death guarantee. No automatic reattachment/recovery is introduced.

## Second independent review: remaining summary-count mismatch

Review of `4ff3b6d` confirmed the three P1 and three other P2 fixes, but reproduced
one remaining P2: `构建完整--0错误,9警告:准备下载` with zero Warning rows still passed.
That finding is accepted; earlier statements of complete repair did not establish
count consistency and are superseded on this point.

The worker and outer validator now independently parse the numeric error/warning
counts from exactly one C0 summary per phase and compare them with that phase's
actual diagnostic severities. Missing, malformed, duplicate or contradictory
summaries fail closed. An inconsistent Rebuild prevents Generate. Phase proof
records observed counts, parsed summary counts and whether they match.

The report's aggregate `errorCount`/`warningCount` explicitly uses
`countSemantics=phase-diagnostic-occurrences`: it sums captured occurrences, not
unique source findings. The same warning reported once in Rebuild and once in
Generate must match 1 in each phase and contributes 2 captured occurrences in
the report; neither phase is compared against the cross-phase sum. Diagnostics
are not silently deduplicated.

Regression tests cover the exact nine-warning/zero-row repro in both production
Python and Node, an inconsistent Rebuild, missing reported warnings, unobserved
errors, duplicate summaries, and the valid repeated-warning case across phases.
New-source portable and native verification is recorded separately; a third
independent review is still required before claiming review acceptance.

Count implementation `9bc4464617ea3fe9249ed2b037140b87ec5167d5` and qualification
client correction `ae4bb69beaabfcd9ebf78747b91fbf79810e08f7` passed 19 Node
contracts with 20 embedded Python fault-injection checks on Linux and Windows.
The first native attempt timed out at the SDK client's default 60 seconds during
cold startup/create; the project then closed and the owned process exited 0.
The test client had passed options in the old SDK argument position. SDK 2 takes
them in argument two; the corrected 330-second client deadline covers the bounded
native startup/operation/cleanup deadlines. Product transport limits are unchanged.

Actual CODESYS requalification of `ae4bb69` passed: two real compiler errors
repaired to zero, both phase summaries match their captured 0-error/0-warning
diagnostics, intended object/task/source and reopen proof, counter 31→51,
Step=DINT#7, Stop 99→99 with FB result 106, Run 121→141, and owned cleanup/exit 0.
All eight loaded file hashes match exact Git source. The
[new source-bound receipt](../evidence/summary-counts-20261016.json) retains the
failed attempt's hash and separates portable count fault injection from native
normal-path observations. No native nonzero-warning scenario was induced, no
license was activated, and no independent acceptance is claimed.
