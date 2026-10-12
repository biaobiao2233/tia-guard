# TwinCAT engineering bridge v1 — dispatch boundary

Status: design input for [#53](https://github.com/biaobiao2233/tia-guard/issues/53), not an implemented or qualified platform contract. Base: `main@542db2f33770d145ac351e891019501bf5335b1f`. This contract is separate from Siemens RoundTrip Source / Project Generation contracts.

## Objective and first slice

An external Agent must be able to start with a new directory, create a solution/TwinCAT/PLC project, author ST, build with complete diagnostics, repair errors, and deliver a project only after independent readback succeeds.

First supported candidate: one locally installed, explicitly detected TwinCAT build; one TwinCAT configuration, one PLC project; ST MAIN/Program, Function Block, GVL, simple Struct/Enum DUT; explicit task cycle/priority/call binding and resolved library references. Unsupported constructs remain explicit. A decides the precise supported input forms and freezes them in `CONTRACT_READY` before B/C consume them.

Existing PLC projects, user IDE sessions and active Runtime are protected. No hardware scan, activation, deployment, ADS routes, runtime writes, start/stop/reset, library installation, license changes or arbitrary IDE commands belong to v1.

## Independent files and owners

All adapter code goes under `adapters/twincat3/`; it does not edit Siemens providers, the existing Host, GUI or CLI during parallel implementation.

| Owner | Exclusive writable paths |
| --- | --- |
| A | `adapters/twincat3/contracts/**`, `adapters/twincat3/src/Environment/**`, `adapters/twincat3/tests/Environment/**`, `adapters/twincat3/THIRD_PARTY.Environment.md` |
| B | `adapters/twincat3/src/Engineering/**`, `adapters/twincat3/tests/Engineering/**`, `examples/twincat3-generation/**`, `adapters/twincat3/THIRD_PARTY.Engineering.md` |
| C | `adapters/twincat3/src/Validation/**`, `adapters/twincat3/tests/Validation/**`, `examples/twincat3-verification/**`, `adapters/twincat3/THIRD_PARTY.Validation.md` |
| D | `adapters/twincat3/mcp/**`, `adapters/twincat3/tests/Mcp/**`, `adapters/twincat3/docs/**`, `adapters/twincat3/THIRD_PARTY.Mcp.md` |
| Coordinator | Root/shared build entry points, integration executable composition, CI, root README, `docs/TWINCAT3-*`, this dispatch document, #53 coordination fields |

Each owner may create its own project/package file only within owned paths. A owns DTO/schema/interface changes. B/C/D can propose signatures but cannot change shared contracts. Missing services must produce a structured unavailable result, never a placeholder successful build/verification. The coordinator binds real providers in the final combination.

## Host and reuse decisions

- Prefer Windows x64/.NET 8 for environment/engineering if the installed COM/interop is compatible; a clearly isolated net472 worker is a permissible fallback. A freezes framework, bitness, interface signatures, local interop resolution and owned-session threading before consumers compile against it.
- COM runs on a dedicated STA, with bounded retry for rejected calls, timeouts, deterministic disposal and session identity. Only fresh objects/processes are owned; never attach to the user's active IDE. Do not terminate a shared COM server or Runtime service when disposing a session.
- Detect DTE and standalone capabilities separately. TcSysManagerRM registration is not proof of usable creation/build/diagnostic APIs. Probe `ITcSysManager16` and native build availability; fallback to a supported DTE only with an explicit reason. New PLC++ support is optional until exact executable/version/command evidence exists.
- Reuse MIT / 0BSD upstream code at the full source pins in the plan. Preserve license/copyright and file provenance. Vendor interop/install binaries stay machine-local. No source copy from unlicensed repositories.
- No whole upstream deployment, Git or licensing orchestrator. A thin optional CLI and MCP share the same operation service; no separate full TwinCAT CLI or HTTP layer is required for this slice.

## A's early interface delivery

Before consumers bind, A publishes `CONTRACT_READY` with exact commit, source files and tests for:

1. An immutable generation input: project/PLC names, requested installed build/platform, artifact IDs/kinds and frozen ST declarations/implementations, DUT fields, GVL values, tasks and explicitly resolved libraries; output is a new destination, with separately owned scratch.
2. An environment report: observed product builds, DTE/standalone interfaces, templates, tool/library identities, bitness, current capability availability and reasons. Read-only detection and an owned functional probe are distinct evidence.
3. Service boundaries for environment/session creation, engineering execution, compilation and readback/verification. COM objects are not serialized or shared outside their owning STA; calls are scheduled through an owned session abstraction.
4. A versioned request/result envelope, structured diagnostics, captured object inventory and verification result, as below. Both real and fake providers implement these signatures.
5. JSON schemas/DTO tests and minimal self-authored positive/negative inputs. No Siemens schema names, fixed CPU/order number or `roundTripReady` fields are carried into TwinCAT.

## Operation and authorization boundary

Expected public tools: `doctor_twincat`, `inspect_twincat_project`, `preview_twincat_generation`, `build_twincat_generation`, `compile_twincat_project`, `verify_twincat_project`. Final names/signatures are frozen by A; D documents them. Inspection/compile of an existing project requires an owned disposable copy, not the user project in place.

Preview is read-only and freezes all relevant manifest/source bytes, chosen environment/platform/libraries, destination, scratch and request into a deterministic input hash. Write authorization is one-time, expiring and bound to this operation, hash and owned worker session. Changing input, output, provider or session invalidates it. Tokens are never committed or returned in diagnostic logs.

The execution service validates the grant, request, canonical paths and ownership again. Frontend checks alone are insufficient. B owns this execution boundary; D presents grants through it. Paths must remain inside allocated workspaces after resolving traversal/reparse/links. Outputs must be new, and failed operations must not overwrite earlier valid projects.

Direct worker clients cannot bypass the allowlist. Never expose a generic arbitrary COM/PowerShell executor or upstream activation/download/delete/license handlers. Normal offline editing can be authorized once for the owned request; do not add a per-object approval ceremony.

## Result semantics

The precise schema belongs to A. It must encode these separate facts:

```json
{
  "schemaVersion": "1.0",
  "operation": "build-generation",
  "sessionId": "owned-session-id",
  "inputHash": "sha256-of-frozen-input-and-request",
  "phase": "compile",
  "success": false,
  "published": false,
  "environment": {},
  "compilation": {
    "state": "error",
    "buildStarted": true,
    "buildCompleted": true,
    "failedProjectCount": 1,
    "diagnosticsComplete": true,
    "diagnostics": []
  },
  "verification": { "verdict": "unknown", "complete": false },
  "failures": [],
  "cleanup": []
}
```

This is a field-shape sketch, not a positive fixture; a real error result must include consistent diagnostic/failure evidence. Transport `ok`, JSON parsing, process exit zero or MCP success do not prove compilation or publication. Required fields cannot silently default to success.

Diagnostics include severity, original code if available, message, project/object identity, source file/line/column if available, capture/source and completeness. Missing location is represented explicitly, not invented. Counts must agree with complete collected items; partial enumeration, stale/foreign items, unknown severity, failed capture or truncation prevent complete PASS.

For DTE, require actual initiation and completion of the intended build, correct active configuration/platform, `LastBuildInfo` and complete correlated diagnostics/Build Output. COM collections use the correct 1-based inclusive end; .NET lists use 0-based indexing. For standalone, use native result/diagnostic completeness evidence; no assumption that an absent DTE Error List means no errors.

All public build/publication paths require an actual verifier. Missing compiler, capture or verifier rejects before publication; no option disables required verification. Cleanup failures remain explicit even if build succeeds.

## Independent readback and publication

Expected facts come from frozen input before creation, never copied from generated output. C closes/saves and reopens the new owned project independently and captures:

- Project/PLC identity, object kind and object inventory, code declarations/implementations and structured DUT/GVL facts.
- Explicit system/PLC task cycles, priority, MAIN/call references and library requested/resolved versions.
- Tool/platform and dependency identities plus unsupported/opaque inventory needed to establish capture completeness.

Only proven volatile/layout fields may be normalized. Changes in types, literal values, task cycles, call order, library versions or code tokens remain significant. Known mismatch is FAIL; unsupported metadata, unproven normalization or incomplete capture is UNKNOWN. Neither can publish a fully verified project.

Publish only when all required environment, creation, compilation, independent comparison and ownership checks succeed. Retain raw evidence and failed staging only within owned scratch; sanitize versioned receipts. Vendor proprietary binaries, license files, secrets and user projects are not committed.

## Real integration acceptance

The actual combination must pass fresh Windows/XAE checks, not only mocks:

1. Empty directory creates new Solution/TwinCAT/PLC plus nonempty MAIN, FB, GVL, DUT, library and periodic Task.
2. Deliberate single ST error returns failed build with complete diagnostics and no successful publication; source repair then builds successfully.
3. Close/reopen readback matches independent input; type/task/library/source drift and incomplete capture do not pass.
4. MCP discovery/preview/build/diagnostic response works with the real worker; direct worker calls without a valid grant fail; hash drift, token reuse, output collision and session mismatch fail before mutation.
5. Exact candidate tests/builds, independent read-only technical review, raw-evidence pointers and owned-resource cleanup are recorded on #53. Original projects, pre-existing IDE/Runtime and Siemens worktrees remain untouched.

Runtime activation/TcUnit/ADS behavior is a separate future gate with a proven isolated target. Worker delivery, a docs commit, API availability and the creation of these tasks are not project acceptance, merge or release.
