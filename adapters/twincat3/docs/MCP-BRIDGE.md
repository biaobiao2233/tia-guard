# D-owned local transport mapping (candidate)

This documents the Node framing/adapter seam, not a replacement shared DTO. The candidate maps contracts, engineering, compiler and independent capture services in src/Worker. A real local MCP repair/build/reopen sequence was observed on XAE 4026.27; mock tests remain separate. Qualification is source/environment scoped; Runtime operations remain absent. See WORKER.md and canonical #53.

## Framing and identity

The host spawns a single explicitly configured own executable, without shell, detached mode or inherited IDE attachment. It passes random `TC3_PIPE` (full Windows pipe endpoint) and `TC3_IDENTITY` via its environment. The worker binds only that pipe/identity and exits/disposes its owned resources on disconnect. Pipe ACL should restrict the current principal; identity is not a substitute for B's execution authorization.

Edge's NDJSON shape is retained:

```json
{"id":"1","action":"doctor","identity":"<own random identity>","payload":{"providerId":"<provider>","sessionId":"<session>"}}
```

Responses are `{"id":"1","ok":true,"result":{...}}`. `ok` means transport delivery only. UTF-8 decoding supports split multibyte characters; frames and requests are capped at 8 MiB; malformed/unknown IDs terminate the session. Dropped/timeout write calls are indeterminate and never retried. No reconnect or auto-spawn follows an established-session failure.

Before any operation, `hello` returns `protocolVersion:"1"`, exact identity, concrete providerId/sessionId and capability observations keyed by action. Capability requires `available:true` plus state `supported` or `untested` to dispatch; `untested` remains visibly unqualified. Never report mock or registered COM as real support.

## Fixed operation mapping

| Transport action | Concrete service binding required |
| --- | --- |
| `doctor` | A `IEngineeringEnvironment.InspectAsync`; detection only |
| `inspect` | read-only project capture with no input mutation/attach; missing service → unavailable |
| `preview` | A `GenerationInputLoader.Load` and B `EngineeringPipeline.Preview`; inputDirectory → SourceDirectory; outputDirectory → OutputDirectory |
| `authorize` | B `EngineeringPipeline.IssueGrant`; exact backend preview and current session; worker configured for offline writes |
| `build-generation` | B `BuildAsync` using same request/preview and actual B grant token; actual C compiler/capture/comparer required |
| `compile` | explicit owned-copy service/gate; absent → unavailable, never open/build user input in place |
| `verify` | independent C close/reopen/capture/compare on an owned execution with the original frozen input |
| `dispose` | own session `IAsyncDisposable` only; no discovery/spawn/reconnect |

COM context/NativeEngineeringContext must never be serialized or cross the STA. Do not expose upstream runtime actions, arbitrary COM, PowerShell, IDE commands, delete, routes or licensing.

Operation reports decorate the concrete typed service result with transport providerId/sessionId/operation and use schemaVersion `1.0`. The Node adapter expects `phase`, explicit booleans `success`/`published`, `failures`/`cleanup` arrays, and preserves A `CompilationReport` / `VerificationReport` fields unchanged. Empty cleanup arrays indicate no cleanup failure; nonempty string failures or object entries without `success:true` prevent PASS. Never fabricate completion, location, independent readback or publication metadata.

Preview provides backend previewId, actual A inputHash (64 hex), an expiring `expiresAtUtc`, and reviewable bound request/environment. Node replaces backend previewId with a local random handle and keeps the backend ID/request in memory. Actual B request hash must bind full input/output/provider/environment/session/operation/workspace/scratch, not just this front-end handle. A new snapshot after ST repair creates a new preview.

Authorize returns actual `grantToken`, matching inputHash and expiry no later than preview expiry. Node consumes/stores the token only in memory, returns local grantId, and sends that token back only to the authenticated same worker on execution. Worker always rechecks validity after client-side checks. For build, B `GenerationResult` has compilation/verification/cleanup evidence; the bridge must not replace it with `{success:true}`. A preflight exception remains a structured failure.

Successful build/compile provides a concrete owned executionId plus matching top-level/compilation inputHash. The Node adapter remembers that execution binding; independent verify must return the same frozen inputHash. A backend should return source/project identity and raw-evidence pointers needed by the coordinator to verify capture; a boolean pass alone is insufficient real acceptance.

The worker should provide bounded, sanitized errors, not raw exceptions containing token or secret-bearing paths. Node additionally sanitizes recursively and suppresses raw stderr. Per-operation compilation/cleanup failure is returned as `isError` with all diagnostic evidence; exit cleanup failure is sanitized structured stderr because the MCP input stream has ended.

## Integration gate

The candidate Worker binds real grants, generation, compile-on-owned-copy and independent verify. Cloud checks cover contracts/transport only; actual native compilation/reopen requires the qualified XAE host. Current observations are not source merge, release, hardware or Runtime acceptance.
