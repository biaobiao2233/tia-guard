# CODESYS adapter (experimental, independent)

This adapter uses the installed official CODESYS ScriptEngine, invoked by
`CODESYS.exe --profile="..." --runscript="..." --noUI`. It is an independent
stdio MCP entry, outside the existing TIA CLI/GUI/HTTP packages. No HTTP gateway,
controller network scan, Control Win service start or arbitrary Python execution
tool is required or exposed.

## Scope

- New adapter-owned `.project`, CODESYS Control Win device description
  `4096 / 0000 0001 / 3.5.22.40`, switched immediately to **IDE Simulation**
  (`4097 / 0000 000a / 3.5.22.40` observed and checked through the native API).
- ST programs, function blocks, GVLs and one 20 ms cyclic program call.
- Create and edit supplied declarations/implementations; native rebuild and code
  generation; complete categorized diagnostic messages, codes, objects and positions.
- Save, close and reopen through the native project API, compare text against
  the frozen request and compare object/task snapshots before/after reopening.
- Download only into that IDE's simulated application; real monitoring and
  Run/Stop. Writes only `GVL_Test.Enable` (BOOL) and `GVL_Test.Step` (DINT 0..10).
- Stop/logout/dispose native online connection, close owned project and await the
  exact child process exit. Keep the generated project and private receipts.

This is **IDE simulation**, not qualification of Control Win SL, hardware,
real-time behavior, I/O, fieldbuses, motion, safety, graphical editors, HMI,
breakpoints/single stepping or arbitrary existing projects. No engineering
binaries, licenses, raw projects or private integration outputs belong in Git.

## Tools and authorization

`doctor_codesys`, `preview_codesys`, `authorize_codesys`, `execute_codesys`,
`inspect_codesys`, `read_codesys`, `dispose_codesys`.

Default read-only: doctor and preview work without launching CODESYS. A trusted
local host must explicitly configure `CODESYS_GUARD_WRITES=1` before mutation
grants can be issued. Previews freeze the exact request hash, revision and
two-minute expiry; grants are single-use and session-bound. A failed mutation
also invalidates prior previews. Calls are serialized. The native mailbox is
HMAC authenticated with an ephemeral host secret; no arbitrary path or target
is accepted from an MCP request. Every process creates a fresh unique directory
under the host-configured workspace. Cleanup is allowed without another grant.

The host and local filesystem are trusted. This is not a sandbox for hostile ST
or other local processes; only the self-authored test code should be used.
No general scripting, force, routes, gateway management, licensing or service
management tool exists. Native idle timeout is five minutes; cleanup then ends
the session rather than reconnecting to an old instance.

Example trusted-host MCP configuration (adjust your own absolute paths):

```json
{
  "mcpServers": {
    "tia-guard-codesys": {
      "command": "node",
      "args": ["E:/your-checkout/adapters/codesys/index.js"],
      "env": {
        "CODESYS_GUARD_EXE": "D:/codesys/CODESYS/Common/CODESYS.exe",
        "CODESYS_GUARD_PROFILE": "CODESYS V3.5 SP22 Patch 4",
        "CODESYS_GUARD_WORKSPACE": "E:/your-owned-test-root",
        "CODESYS_GUARD_WRITES": "0"
      }
    }
  }
}
```

Install locked dependencies with `npm ci --ignore-scripts`; run portable contracts
with `npm test`. They do not invoke a PLC compiler. `tests/native-run.js` is an
explicit opt-in native qualification client: set the above trusted host settings
with writes enabled and `CODESYS_GUARD_EVIDENCE` to an absolute private JSON output
path, then `node tests/native-run.js`. This test creates an owned project, tests
an actual compiler error/repair, edits all three object kinds, compiles/reopens,
downloads to IDE simulation, observes changing counters, writes the two test
inputs, checks their FB effect, verifies Stop/Run, rejects an output write and
checks native process exit. It never purchases or activates a license.

Portable CI and native qualification are separate. The [source-bound receipt](evidence/native-20261015.json)
qualifies product source `f856071`: actual compiler errors and repair (0 errors,
0 warnings), all three object kinds changed/read back, save/close/reopen,
counter 36→66, Stop 123→123 with FB result 130 after writing Step=7, Run 158→188,
rejected output write, and exact owned process exit. Eight portable contracts
passed on Linux and Windows; those tests use no vendor compiler.
A discovered tool or successful process exit alone
does not establish a supported capability. Read errors and cleanup reports even
when preceding steps succeeded. Local paths, native exceptions and full text
receipts remain private under the configured workspace.

## Official capabilities, licenses and reuse

Research and pinned open-source comparison: [RESEARCH.md](RESEARCH.md).
The free Development System and commercial PDE MCP add-on are different products.
This adapter uses installed Scripting; it does not claim or activate a PDE license.
The official MCP can be used separately when properly installed/licensed, but its
current project access must not be pointed at protected existing projects.
