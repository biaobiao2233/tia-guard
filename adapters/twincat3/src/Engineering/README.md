# Owned offline engineering

The public composition uses `TwinCat3.Contracts` at A's CONTRACT_READY
`b39178a9c69c4e94060abd5124342968644c81d9`. There are no Siemens references.
`TwinCat3.Engineering` targets .NET 8 Windows x64.

The worker configures an `EngineeringAllocation(workspaceRoot, scratchRoot)`, a
`StageAuthorizationRegistry`, and an `ExecutionGrantStore(writesEnabled: false)`.
It creates `NativeEngineeringCreator(allocation, registry)`, then an
`EngineeringPipeline(allocation, registry, grants, creator, compiler, capture,
comparer)`. Use the same allocation/registry throughout this composition.

Public operations are:

- `Preview(GenerationRequest, IOwnedEngineeringSession)` returns an immutable
  source/request/environment/session binding; it creates no engineering files.
- `IssueGrant(previewId, sessionId)` issues an opaque, expiring one-use token only
  when trusted worker configuration enables writes. Never expose a generic
  authorization or arbitrary COM endpoint. Tokens must not enter receipts/logs.
- `BuildAsync(request, previewId, grant, ownedSession, cancellationToken)` reloads
  source bytes, checks the complete request binding and consumes the grant before
  allocating stage/native actions. Missing compiler/capture/comparer rejects
  before mutation. Native creator calls require a private active stage lease.

The only native operations are fresh solution/configuration/empty PLC creation,
Program/Function/FB/GVL/Struct/Enum authoring, one explicit program call per
periodic task, fixed project-local installed library references, save and close.
Identifiers are constrained to non-reserved IEC names up to 64 characters.
Functions require a simple explicit return type on the first declaration line.
Unknown templates, implicit library versions, source/output/scratch overlap,
existing outputs, link/reparse escape, foreign sessions and input drift reject.

All failed stages are retained in the allocated scratch with a phase-bound report.
Successful publication requires full correlated compilation, independent saved /
closed / reopened complete capture and source-bound comparison PASS. The final
input/path checks run again before moving the complete stage to a new output.
`ArtifactTreeHash` covers relative paths and SHA-256 of every saved file. Compilation
and capture identities refer to the staging paths actually checked; the returned
`SolutionPath` names the published output. Worker lifecycle disposes only its own
fresh IDE/session. The pipeline never attaches a user IDE, generates a boot
project, activates a configuration, installs a library or operates a Runtime.

Build the native provider with a machine-local interop reference:

```powershell
dotnet build adapters/twincat3/src/Engineering/TwinCat3.Engineering.csproj -c Release `
  -p:TwinCatInteropAssemblyPath=<owned-local-generated-TCatSysManagerLib.dll>
```

This reference is `Private=false`: the coordinator's owned host resolves the
installed/generated assembly. No vendor DLL belongs in source control or a public
package. Without this property the project still supports pure pipeline tests,
but native authoring explicitly throws unavailable; it never substitutes a fake
successful project. Test fake providers verify gates only and are not native
creation, compilation or readback qualification.
