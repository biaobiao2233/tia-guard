# TwinCAT compilation and readback fixtures

These are self-authored inputs for a fresh owned offline XAE project. They are not platform acceptance evidence.

`single-error` deliberately assigns `MissingSignal`, which is not declared. `repaired` adds its declaration and preserves the assignment. Select and freeze the actual installed TwinCAT build in `twincat-generation.json` before preview; the example build is illustrative and must not be silently accepted on another version. No project is downloaded or activated.

Required real sequence: preview invalid input, authorize its owned destination, create a fresh project, observe actual failed build and complete single-error diagnostics, confirm no verified publication, repair source into a new frozen input, use a fresh grant and destination, rebuild, save/close/reopen, capture native objects/tasks/libraries and compare them against the original frozen source. The coordinator records exact combined source and real results on #53.

To run pure regression checks:

```powershell
dotnet test adapters/twincat3/tests/Validation/TwinCat.Validation.Tests.csproj -c Release
```

Machine binding additionally needs `-p:TwinCatVsPublicAssemblies=<XAE PublicAssemblies>` and `-p:TwinCatInteropAssemblyPath=<machine-generated TCatSysManagerLib interop DLL>`. These are references to installed/generated files, not redistributable vendor binaries. Without them compiler/capture return UNKNOWN.

The initial Task XML projection accepts only evidenced cycle/priority and structural identity/layout fields. Unproven additional task metadata, opaque nodes, unsupported methods/graphical languages or unresolved library placeholders prevent full verification. Extend this boundary only after real raw XML has been independently reviewed at a pinned XAE build.
