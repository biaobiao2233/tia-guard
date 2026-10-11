# Project-generation verification reuse (#47)

Pinned repositories and **Git blob SHA-256** values are in [provenance.json](provenance.json).
These hashes cover `git show <commit>:<path>` bytes, independent of Windows checkout CRLF.
Original MIT licenses and copyright notices are retained alongside this file.

| Source | Reuse | Changes / limits |
| --- | --- | --- |
| heilingbrunner/tiaportal-mcp `6824e4d671b403993c35d98dddf104686a950f4e`, `Portal.Documents.cs` | Actual reported files plus disk inventory; per-object document exports | New owned directory, exact report/disk agreement, no overwrite/delete, no extension guessing, strict UTF-8 and export success required |
| Same revision, `Portal.Sources.cs` | Per-object `GenerateSource(IGenerateSource[], FileInfo, GenerateOptions.None)` and filesystem readback | No truncation, no dependency expansion, raw outputs retained; no MCP host, logger, portal manager or cleanup copied |
| bulaofen0036-coder/TIA_Portal_Openness_MCP `3e8f8810924657a1adc4b41c0166a0829ad1caf7`, `FB_BasicLatch.scl` | Complete FB fixture, original source text and attribution | Unmodified apart from Git transport line-ending handling; upstream claims are not local execution evidence |
| Same revision, `MCPVerify_FB_LAD_v3.s7dcl` | `NETWORK/RUNG/Contact/Coil` syntax in minimal LAD document | Reduced to one Contact/Coil rung; removed resource-reference pragmas, timer/edge/comparison networks, fixed block number; renamed interface/block |

UDT, global DB, instance DB, calling OB, IO-table plan, comparator, coverage DTOs,
and pure tests are authored for TIA-Guard. No Siemens binary, license, proprietary
project, `.ap21`, MCP server or upstream build output is redistributed.

The adapted reader is compiled against the installed V21 SDK. Runtime API/proxy
behavior and sample syntax/importability are **unverified** until an owned V21
create/compile/readback run. Copies of upstream tools' test claims do not qualify
these changed fixtures.
