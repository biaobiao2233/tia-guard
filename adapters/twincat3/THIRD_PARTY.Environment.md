# Environment reuse provenance

`src/Environment/OleMessageFilter.cs` adapts the COM interface, bounded rejected-call
retry and registration pattern from `Edge-JB/TwinCAT-XAE-MCP`, exact source
`30d8e2938eeed1c41ee24765b469d13430ab0a32`, `daemon/OleMessageFilter.cs`.
Changes: namespace, nullable .NET 8 declarations, previous-filter restoration,
HRESULT validation and deterministic disposal. No active-IDE/ROT acquisition,
runtime/deployment/route/license handlers were copied.

`StaDispatcher.cs` is a new explicit queue/pump. The rassrollers project
`94826e4f9e456849ca4adefc1c08e952ebe81b32` was reviewed as a design source, but its
base SynchronizationContext scheduler is not reused: that context can dispatch
callbacks to the thread pool instead of retaining STA ownership.

Beckhoff/Visual Studio interop assemblies are machine-local build/runtime inputs;
they are not committed or bundled. Generated temporary COM interop is likewise
kept outside the source repository.

## Edge upstream license

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
