# TwinCAT 3 contracts v1

These portable .NET 8 models are independent of Siemens and do not load COM.
The input root contains `twincat-generation.json` and exactly its referenced
UTF-8 ST declaration/implementation files. GenerationInputLoader.Load(root)
rejects ambiguous JSON, links, duplicate identities, unsupported profiles and
untracked files. The hash covers raw manifest and source bytes with sorted,
length-prefixed relative paths; FrozenSourceFile returns a copy of its bytes.
JSON uses camelCase names and string enum values, with unknown members rejected.
`generation.schema.json` covers structural constraints; the loader additionally
checks unique names/IDs, program/task references and source-file membership.

Services.cs is the single provider signature source. IOwnedEngineeringSession
schedules native callbacks on its STA; NativeEngineeringContext is never sent
over the worker protocol. Binding uses the actual tsproj path and
NativeProjectHandle.ComputeIdentity (path/configuration/PLC fingerprint), not a
fabricated vendor GUID. The PLC/system task names are the manifest task Name;
the expected LinkedSystemTask is `TIRT^Name`. v1 supports one Program per Task.

CompilationReport.Passed requires started/completed build, no failed projects,
complete correlated diagnostics/output and consistent counts/identity. The
native provider must also establish the actual target/configuration/source and
may not invent completeness. VerificationReport.Passed requires complete
independent comparison with no differences or unknowns. Neither transport ok,
an empty diagnostics array nor mock provider results establish XAE support.

The native host is Windows x64/.NET 8. Compile native providers against local
assemblies using TwinCatInteropAssemblyPath (TCatSysManagerLib.dll) and
TwinCatVsPublicAssemblies (XAE PublicAssemblies), Private=false; vendor binaries
are not committed or redistributed. Native probe availability is separate from
registration and from a qualified create/build/readback loop.
