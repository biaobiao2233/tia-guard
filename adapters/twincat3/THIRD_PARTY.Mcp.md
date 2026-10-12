# MCP source provenance

This adapter contains selectively adapted MIT Node code, not an imported complete TwinCAT runtime controller.

| Upstream pin / files | Local files | Adaptation |
| --- | --- | --- |
| [Edge-JB/TwinCAT-XAE-MCP@30d8e2938eeed1c41ee24765b469d13430ab0a32](https://github.com/Edge-JB/TwinCAT-XAE-MCP/tree/30d8e2938eeed1c41ee24765b469d13430ab0a32), `index.js` | `mcp/index.js` | Retained CommonJS SDK server-factory/stdio and schema-driven registration structure; removed runtime/IDE/UIA actions, confirmation strings, pruning and transport-success result formatting. |
| Same pin, `toolSchemas.js` | `mcp/toolSchemas.js` | Retained single schema source used for discovery and calls, Zod v4 objects; replaced broad XAE/action maps with seven concrete offline tools and strict fields. Shared generation schema remains A-owned. |
| Same pin, `daemonClient.js` | `mcp/daemonClient.js` | Retained namedpipe NDJSON id/action/payload and pending correlation design; removed default shared pipe, detached auto-spawn, watcher, silent malformed-frame handling and automatic write retry. Added random worker identity, bounds, UTF-8 decoder, fail-closed sessions and explicit dispose. |
| [eponce00/twincat-mcp@588106ea2b8d51f0ce0b39355501ea2afcf79b99](https://github.com/eponce00/twincat-mcp/tree/588106ea2b8d51f0ce0b39355501ea2afcf79b99), `mcp-server/twincat_mcp/{scope.py,safety.py,errors.py,jobs.py}` | Design references for `providerClient.js`, `result.js` | Owned bounded subprocess, scope/grant/error and no implicit restart ideas only; no Python implementation copied or runtime/job engine imported. Backend authority is B's service, not Node's memory handles. |

Edge copyright (c) 2026 Edge Automation, MIT text and original NOTICE retained verbatim in `mcp/licenses/Edge-LICENSE` and `Edge-NOTICE`. Eponce MIT copyright (c) 2026 and license retained verbatim in `mcp/licenses/eponce-LICENSE` for the referenced design. Header attribution appears on adapted Edge files. No Beckhoff-USA-Community source, vendor installation assemblies, licenses, tokens or user engineering artifacts are distributed.

Dependencies actually resolved for this candidate: `@modelcontextprotocol/server` and test client `@modelcontextprotocol/client` **2.3.1** (Apache-2.0), `zod` **4.6.6** (MIT). `mcp/package-lock.json` locks the full dependency graph and integrity. Dependency license files remain in their installed packages; node_modules is not committed. The Edge pin used SDK server ^2.0.0; this adapter selected the currently installable 2.3.1 and tested both legacy and modern stdio with its real client.

All other D files and tests are self-authored. A contract-only commit `b39178a9c69c4e94060abd5124342968644c81d9` is consumed unchanged as a dependency; its DTOs/schema/services have not been edited by D. This provenance and mock tests do not qualify real B/Windows/XAE operation or permission boundaries.
