# TwinCAT validation provenance

Scope: `src/Validation/**`, `tests/Validation/**`. This module does not contain vendor installation DLLs, license material or upstream activation/deployment handlers.

## Adapted API code

| Upstream / exact revision | Original file | Local use and changes |
| --- | --- | --- |
| [eponce00/twincat-mcp](https://github.com/eponce00/twincat-mcp/tree/588106ea2b8d51f0ce0b39355501ea2afcf79b99), MIT, `588106ea2b8d51f0ce0b39355501ea2afcf79b99` | `TcAutomation/Commands/BuildCommand.cs` | `NativeDteBuild.cs`: inclusive ErrorItems loop, typed error levels, `LastBuildInfo`, Build Output. Rewritten for owned-session binding, actual build begin/done events, bounded timeout, fresh baseline/output, intended solution/configuration/platform, complete enumeration and explicit unknowns. No session attach or success inferred from empty errors. |
| [Edge-JB/TwinCAT-XAE-MCP](https://github.com/Edge-JB/TwinCAT-XAE-MCP/tree/30d8e2938eeed1c41ee24765b469d13430ab0a32), MIT, `30d8e2938eeed1c41ee24765b469d13430ab0a32` | `daemon/Actions/XaeActions.cs`, `PlcPouActions.cs`, `PlcLibraryActions.cs`, `daemon/PlcProjectHelper.cs` | Typed DTE2 Error List, PLC declaration/implementation/library/task-reference reads in `NativeDteBuild.cs` / `NativeReadback.cs`. Do not copy best-effort exception swallowing, diagnostic caps pretending to be complete, arbitrary commands, install/remove libraries or runtime operations. No raw helper/action registry is imported. |

The pure compilation gate, token lexer, task projection and independent comparison are newly authored for this adapter. The self-authored verification inputs are not copied vendor projects. `rassrollers/TcAutomationInterface@94826e4f9e456849ca4adefc1c08e952ebe81b32` was inspected for its erroneous `i < Count` compilation collector, but its collector, build verdict and TcUnit runner are not copied.

Official API references: [DTE2 diagnostics / inclusive index](https://infosys.beckhoff.com/content/1033/tc3_automationinterface/21842703115.html), [PLC object subtypes](https://infosys.beckhoff.com/content/1033/tc3_automationinterface/21842468363.html), [task creation](https://infosys.beckhoff.com/content/1033/tc3_automationinterface/242740107.html). Availability of an API does not qualify this module against an installed XAE. `TWINCAT_DTE` and `TWINCAT_COM` require machine-local interop references; absence produces structured UNKNOWN without performing a build/capture.

## eponce00 MIT notice

MIT License

Copyright (c) 2026

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## Edge Automation MIT notice

MIT License

Copyright (c) 2026 Edge Automation

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
