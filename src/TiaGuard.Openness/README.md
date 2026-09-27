# Openness adapter (TIA Portal V21)

`TiaProjectSession.OpenOfflineCopy(path)` requires an explicitly selected `.ap21` file. It copies the complete containing folder to an adapter-owned temporary directory and opens that copy in a headless V21 process. Disposal closes only this owned copy and removes its verified temporary directory. The supplied project is never opened or saved. `Attach(pid)` is available for a named running V21 process; if multiple open projects match, the caller must select a PID. Disposal disconnects this adapter without closing the user's project or Portal process.

`ReadProjectInfo()` reports project name, source path, source kind, project version when available, TIA version/build, and attached process ID when applicable. Paths and PIDs are operational information and are absent from Snapshot JSON.

`ReadSnapshot()` maps the current coordinator-owned Draft Snapshot v1 contract. It traverses device groups, nested device items, PLC software, user and system block groups, and PLC tag tables. Read failures, protected content, and unsupported evidence produce diagnostics. Warning/error diagnostics set a `partial` or `failed` capture; a successfully read empty collection remains `complete`. Diagnostics never include raw Siemens exception text or local paths. Devices, PLCs, blocks, tags, and diagnostics are ordered deterministically. Snapshot `contentId` is emitted only for a complete capture; it excludes capture time, process ID, local source path, artifact path, and observation time. Collector normalization version `2` uses a separate versioned SimaticML content digest for exported blocks, so the volatile root `DocumentInfo/Created` value cannot change `contentId`.

Object IDs are collector-owned IDs based on Unicode-normalized engineering group/name paths. They are repeatable while those paths remain unchanged; a rename or move changes the ID. They are **not** persistent Openness object GUIDs. `scopePath` and `engineeringPath` describe the traversed engineering hierarchy. If a device has more than one PLC software object, all are listed in `plcs` and a diagnostic explains that `device.plcId` names the lowest sorted ID.

Block `IsConsistent` is read as a property, without running a compile. If any block exposes it, compile mode is `consistency-only`; a `false` result yields `issues`, while all `true` results still yield `unknown`, since no compile was run. Otherwise mode is `not-observed`. `active-compile` is never emitted. Block export is `not-attempted` by default, or `protected` when know-how protection is observed. Explicit `SnapshotCollectionOptions.BlockExportDirectory` enables export outside both source and temporary project directories. Exported files are named by a hash of the block ID. `export.sha256` identifies the exact raw file; `export.contentSha256` and `export.contentNormalizationVersion` identify the normalized engineering content used in `contentId`. If normalization fails, the raw artifact remains evidence but capture becomes partial and no `contentId` is claimed. Protected and unknown-protection blocks are skipped. Raw export files stay in the caller's output directory and should be handled as project data.

Tag addresses preserve their raw form. Only common `%I/%Q/%M` bit, byte, word, and double-word forms are normalized; other forms are `unsupported`, without a guessed address. A single nonempty comment translation is preserved. Multiple nonempty translations set comment status `multiple` and a warning diagnostic, making capture partial rather than silently treating one translation as complete evidence. Comments also distinguish `present`, `missing`, `unavailable`, and `read-failed`.

The focused harness is in `tests/TiaGuard.Openness.Smoke`:

```powershell
dotnet build tests/TiaGuard.Openness.Smoke/TiaGuard.Openness.Smoke.csproj -c Release
dotnet run --project tests/TiaGuard.Openness.Smoke/TiaGuard.Openness.Smoke.csproj -c Release -- self-test
dotnet run --project tests/TiaGuard.Openness.Smoke/TiaGuard.Openness.Smoke.csproj -c Release -- probe
dotnet run --project tests/TiaGuard.Openness.Smoke/TiaGuard.Openness.Smoke.csproj -c Release -- info open-copy C:\path\to\project.ap21
dotnet run --project tests/TiaGuard.Openness.Smoke/TiaGuard.Openness.Smoke.csproj -c Release -- snapshot open-copy C:\path\to\project.ap21
dotnet run --project tests/TiaGuard.Openness.Smoke/TiaGuard.Openness.Smoke.csproj -c Release -- snapshot open-copy C:\path\to\project.ap21 --block-export-dir C:\outside\exports
dotnet run --project tests/TiaGuard.Openness.Smoke/TiaGuard.Openness.Smoke.csproj -c Release -- roundtrip open-copy C:\path\to\project.ap21 C:\outside\canonical-tree
dotnet run --project tests/TiaGuard.Openness.Smoke/TiaGuard.Openness.Smoke.csproj -c Release -- validate-build-input C:\outside\canonical-tree C:\outside\new-project
dotnet run --project tests/TiaGuard.Openness.Smoke/TiaGuard.Openness.Smoke.csproj -c Release -- build C:\outside\canonical-tree C:\outside\new-project
```

The `build` command reads only the canonical tree, requires an unused output directory, stages on the destination volume, and publishes the new `.ap21` only after saving and compiling with zero errors. It never opens the original source project.

Exit code `3` means the current Windows logon token lacks effective `Siemens TIA Openness` membership. Account membership alone is insufficient until a new token is issued. Exit code `2` means another failure. The harness resolves the installed V21 PublicAPI assemblies at runtime; Siemens DLLs are neither copied into build output nor committed.
