# Engineering provenance and licenses

The small offline authoring sequence in `src/Engineering/NativeEngineeringCreator.cs`
adapts the following pinned upstream operations. No upstream orchestrator,
deployment, online, boot generation, Git, library installation or license handlers
are included. All source fixtures, grant, path and pipeline code are authored for
this repository.

| Upstream and exact revision | Source files | Reuse / changes |
| --- | --- | --- |
| Beckhoff/TC_AI_DOTNET_Samples `48fd388784b1b58d89e5f786ae5f3cd9ff4227ca` | `src/ScriptingContainer/CodeGenerationDemo/CodeGenerationScript.cs`; `src/ScriptingContainer/Scripting.CSharp.Scripts/Scripts/TaskCPUSettings.cs`; `src/ScriptingContainer/Scripting.CSharp.Scripts/Scripts/ManagePlcLibraries.cs`; `src/ScriptingContainer/Scripting.CSharp.ScriptsLateBound/Scripts/GeneratePlcProject.cs`; `src/ScriptingContainer/ScriptingTestContainerBase/ItemTypes.cs` | Empty PLC CreateChild, PLC object types, typed declaration/implementation, task association, fixed project-local library reference. Removed boot/autostart/activation and library-repository changes. Added request-bound empty-stage ownership, explicit path/name and actual saved-file checks. 0BSD. |
| rassrollers/TcAutomationInterface `94826e4f9e456849ca4adefc1c08e952ebe81b32` | `AutomationInterface.core/Environment/VisualStudioEnvironment.cs`, CreateEmptySolution / AddProjectFromTemplate | DTE Solution.Create/SaveAs/AddFromTemplate sequence, rewritten synchronously inside the shared owned STA callback. No build/error collector, permissive retry or deployment code copied. MIT. |
| Edge-JB/TwinCAT-XAE-MCP `30d8e2938eeed1c41ee24765b469d13430ab0a32` | `daemon/Actions/PlcProjectActions.cs`; `PlcPouActions.cs`; `TaskActions.cs`; `PlcLibraryActions.cs` | PLC CreateChild / typed code interfaces, ST subtype parameter arrays, child-name/path postconditions, task cycle conversion and fixed AddLibrary. No arbitrary action router, delete-on-error, online, placeholder, install or repository handlers. Added executing-side one-use grant/session/hash/workspace checks. MIT. |

Runtime vendor interop assemblies are generated/resolved from the local licensed
installation by the Environment component. They are not committed to this repo.
The following complete upstream notices apply to the adapted API sequences.

## Beckhoff — BSD Zero Clause License

Copyright (c) 2022 Beckhoff Automation GmbH & Co. KG

Permission to use, copy, modify, and/or distribute this software for any
purpose with or without fee is hereby granted.

THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH
REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY
AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT,
INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM
LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR
OTHER TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR
PERFORMANCE OF THIS SOFTWARE.

## Rasmus Bertelsen — MIT License

Copyright (c) 2026 Rasmus Bertelsen

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

## Edge Automation — MIT License

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
