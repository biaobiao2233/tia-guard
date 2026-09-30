---
name: tia-guard-gateway
description: Inspect, modify, compile and verify bounded TIA Portal V21 S7-1200 offline engineering projects through the local TIA-Guard Gateway. Use for PLC LAD logic, tags, project context and TIA-Guard Git publish/restore requests from Codex, Cursor, Claude Code or another agent with local shell/HTTP access.
---

# TIA-Guard Gateway

Translate the user's PLC engineering intent into Gateway operations. Handle discovery, schemas and safety tokens internally; explain results in PLC language.

## Reach the local product

Use `http://127.0.0.1:18761` from the computer running TIA-Guard. A Skill does not create a network channel: browser-only cloud chat cannot reach the user's localhost without an existing local connector.

At the beginning of every TIA task:

1. Call `GET /health`. Check the product identity and healthy status. If unavailable, tell the user **“请打开 TIA-Guard”** and stop dependent operations. Do not ask for a URL, port or MCP configuration, or start a bridge executable.
2. Call `GET /capabilities`. Treat the live response as the capability and authorization truth; a list in this Skill is never permission to exceed it. Interpret `health.accessMode = read-only` as no standing write grant, not a blanket prohibition: supported patches can still proceed through `one-shot-gui-approval`. A manually started no-key Gateway cannot be approved; never try to turn it into a writable session yourself.
3. Call `GET /openapi.json` when request or response details are needed. Use its current schemas instead of inventing fields.
4. Read `GET /api/v1/state` and `GET /api/v1/project` as appropriate. If no project is bound, use the user's selected owned project with the documented offline-copy workflow; obtain a missing project choice rather than guessing.

## Read the engineering state

Read `GET /api/v1/ai/context`, then the relevant program graph, network and symbol usage:

- `GET /api/v1/ai/program-graph?block=OB1`
- `GET /api/v1/ai/network?block=OB1&network=1`
- `GET /api/v1/ai/where-used?symbol=ForwardOut`

Read the actual current tags and LAD before proposing a change. Preserve these authorities:

- canonical `tia-source` = authoritative reproducible engineering source
- AI Engineering / `ai/` = derived AI-readable view
- structured patch = mutation intent

Do not edit derived AI files or bypass the Gateway by editing `tia-source` and claiming the TIA project changed. Do not build a second LAD XML parser.

## Apply one bounded change

Current operations are `upsert_tag` and `replace_output_condition` within the live capabilities' bounded LAD subset: serial AND, ordinary contacts, negated reads and ordinary coils.

1. Build the structured patch from the user's intent and the current real engineering state.
2. Call `POST /api/v1/ai/patches/preview`. Check its binding, target, existing/expected semantics and support boundary. Stop on an error or unsupported/ambiguous result.
3. Call `POST /api/v1/ai/patches/apply` with the exact same patch and its returned single-use safety token.
4. In default GUI mode, explain that the TIA-Guard window needs **“允许本次修改”** and wait for the user's decision. Never access private operator controls, obtain an operator key or approve your own request. Do not restart the Gateway with broader authority.
5. If the live capabilities report `single-use-preview-token`, use that existing explicitly authorized headless mode without a GUI decision. Still require preview, exact binding/request/state, expiry and a single-use token.
6. Report success only when the actual apply response has all of:
   - `status = applied`
   - `compileVerdict = pass` and `compileErrors = 0`
   - `exportDeterminismVerdict = pass`
   - `roundTripVerifyVerdict = pass` and `verifyVerdict = pass`
   - `aiSemanticVerificationVerdict = pass`
   - `savedOriginalProject = false` and `savedDisposableCopy = true`
7. Call `POST /api/v1/ai/refresh`. Reread the changed network or tag from the refreshed context and compare it to the intended result.
8. Explain what changed, the resulting PLC logic and the verification results. Make clear that the result belongs to an offline disposable copy; keep the original project intact.

On rejection, expiry, cancellation, stale binding or verification failure, do not claim success or retry apply with the old token. Reread state and preview again only within the user's existing intent; a fresh preview needs its own approval. If rollback failed, stop and report that state without further mutation.

Do not expose tokens, content IDs, hashes, fingerprints or internal protocol details in ordinary user explanations.

## Fail closed on unsupported intent

If live capabilities do not support the requested operation, say that the Gateway cannot safely perform it and stop. Never guess a patch or use an alternate write path.

Unsupported scope includes OR mutation, arbitrary parallel graph rewrites, stateful instructions, unknown graphs, arbitrary project/hardware creation, S7-1500, HMI, Safety engineering, drives, online PLC writes, download, CPU start/stop and force.

## Publish or restore with Git

When the user asks to upload the completed PLC project, prefer TIA-Guard's existing product workflow:

`owned TIA project -> canonical tia-source -> selected Git project slot -> commit/push`

Use a safely published owned result of the offline edit, not the untouched original, as the source for an edited-project upload. If the current product/capabilities cannot safely publish that result, explain the missing step instead of uploading the original and claiming it contains the edit.

Preserve the chosen project slot, sibling projects and normal repository files. Reuse the system Git credentials; do not read or store credentials. Never delete or overwrite the user's original project.

For requested restore verification, use a fresh directory:

`clone -> selected tia-source -> Build -> fresh .ap21 -> Verify`

Check actual Git command/tool results and the resulting remote commit before claiming push succeeded. Keep local commit, remote push, engineering Verify and binary identity distinct. Do not infer byte-identical `.ap21` files from an engineering-semantic Verify pass.
