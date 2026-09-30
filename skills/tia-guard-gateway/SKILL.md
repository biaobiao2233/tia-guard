---
name: tia-guard-gateway
description: Operate the local TIA-Guard AI Gateway at 127.0.0.1:18761. Use when the user asks to inspect or modify a TIA Portal PLC project, LAD logic, tags, compile, or verify from Cursor or another local agent.
---

# TIA-Guard AI Gateway

The user should not have to provide an endpoint, route, token, or schema. This skill already knows the local gateway.

## Where it runs

- Default gateway: `http://127.0.0.1:18761`
- This works for a local agent that can call the machine's loopback interface.
- A cloud chat page cannot reach that address. Do not claim that a browser-only cloud model can use the gateway without a local agent or connector.
- Never ask the user to copy an MCP config, an HTTP URL, or a port.

## Start of every task

1. `GET /health`
2. `GET /capabilities`
3. Follow `/capabilities`. Do not invent writes it lists as unsupported.

Authority, in this order:

- canonical `tia-source` is authoritative
- AI Engineering is a derived view
- a patch is only the requested change

## Read

Use the HTTP API, not a second LAD parser:

- `GET /api/v1/ai/context`
- `GET /api/v1/ai/program-graph?block=OB1`
- `GET /api/v1/ai/network?block=OB1&network=1`
- `GET /api/v1/ai/where-used?symbol=ForwardOut`
- `POST /api/v1/ai/refresh` after a change

`GET /openapi.json` describes these routes when a detail is unclear.

## Write

Supported patch operations are only those named in `/capabilities`, currently `upsert_tag` and `replace_output_condition` for serial AND, ordinary contacts, negated reads, and ordinary coils.

1. `POST /api/v1/ai/patches/preview` with the patch.
2. `POST /api/v1/ai/patches/apply` with the same patch and the returned `safetyToken`.
3. Wait. The TIA-Guard window asks the user to allow that one change. Do not approve it yourself and do not call the operator approval route.
4. Treat `status=applied` as success only when compile, export determinism, round-trip verify, and AI semantics all passed. `verifyVerdict=pass` means the round-trip verify passed.
5. Read the network again and tell the user what changed, in engineering language.

The original `.ap21` is never saved. A failed apply restores the disposable copy.

If `/capabilities` says `authorization` is `single-use-preview-token`, apply still uses the preview token and does not wait for a window.

## Do not

- Download, start, stop, or force a PLC
- Write online values
- Rewrite OR or parallel networks
- Target S7-1500, HMI, Safety, or drives
- Store or repeat safety tokens, fingerprints, or raw prompts in the user-facing answer
