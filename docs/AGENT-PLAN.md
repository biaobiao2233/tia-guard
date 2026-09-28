# Project work and acceptance

Use GitHub Issue #9 as the current handoff index, then verify the actual main/source revision and linked evidence. Current repository state outranks a historical comment. The original three-agent Snapshot/Doctor/AI branch plan has been superseded by the bounded Export → Build → Verify path; it is not the current integration order.

## Current boundaries

- Export #14 / PR #15, Build #16 / PR #22, Verify #23 / PR #24 and CLI Build PR #25 record historical bounded demo acceptance.
- A source-truth repair is a new candidate. Test evidence for the old demo does not automatically accept changed traversal, validation or resource handling.
- Product CLI currently contains build. Export and Verify remain library/Smoke-harness operations.
- Doctor/SARIF and AI are unmerged supporting work and require explicit re-scoping and contract reconciliation before integration.
- Upstream contributions #10/#11/#12 and the paused #27/#28 router remain separate scopes. Closing a router PR is not evidence of machine cleanup.

## Safe engineering sequence

1. Recover the current task, exact source identity and protected resources using Project Workbench.
2. Use a short-lived branch; concurrent writers must use separate worktrees. Never modify the canonical `.ap21` or a real PLC.
3. Keep object coverage and syntax/semantic validation explicit. Add positive and negative contract cases to the pure suite.
4. Run Release build, pure contract/schema tests and the Smoke self-test. Openness call changes also require fresh disposable V21 evidence or an explicit evidence gap.
5. Independently review the exact candidate before integration when required by the task. Do not confuse implementation completion, test PASS, technical review, merge or release.
6. Update the canonical tracker/handoff only within current authorization. Do not create duplicate status ledgers or revive paused automation incidentally.

There is no standing authorization here to merge, release, alter credentials, broaden permissions, deploy a router or operate a PLC.
