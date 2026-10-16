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

Additional doubts from review: null/empty monitoring now fails; explicit False
from a compiler API fails conservatively (installed stubs do not document a
boolean success contract). Sudden OS power loss or killing the MCP host without
letting it clean up is not fully qualified: the native idle deadline is five
minutes and normal signal/stdio closure is handled, but this is not an OS-level
parent-death guarantee. No automatic reattachment/recovery is introduced.
