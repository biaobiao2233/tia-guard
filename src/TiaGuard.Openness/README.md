# Openness adapter (V21)

`TiaProjectSession.Attach(pid)` selects one running TIA Portal V21 process with an open project. If more than one matches, supply a PID. `OpenOfflineCopy(path)` copies the **entire folder** containing a `.ap21` file to a unique temporary directory, opens only that copy in a headless V21 process, and removes the copy on disposal. Keep offline projects in dedicated folders; the adapter refuses a source folder that encloses its temporary location. It never calls `Save`, `Compile`, `Download`, or online access APIs.

`ReadProjectInfo()` returns project name/path, TIA version, and optional process ID. `ReadSnapshot()` returns the repository's Snapshot v1 DTO. It walks device groups, nested device items, block groups (including system block groups), and PLC tag table groups. Arrays are sorted with ordinal comparison. A PLC name is `device/software` when these differ. The single tag `comment` is the first nonempty translation in ordinal language order. Missing optional device metadata stays `null`. `compile` is always `null` because no compilation is run.

The smoke harness is in `tests/TiaGuard.Openness.Smoke`:

```powershell
dotnet build tests/TiaGuard.Openness.Smoke/TiaGuard.Openness.Smoke.csproj -c Release
dotnet run --project tests/TiaGuard.Openness.Smoke/TiaGuard.Openness.Smoke.csproj -c Release -- probe
dotnet run --project tests/TiaGuard.Openness.Smoke/TiaGuard.Openness.Smoke.csproj -c Release -- self-test
dotnet run --project tests/TiaGuard.Openness.Smoke/TiaGuard.Openness.Smoke.csproj -c Release -- snapshot attach 12345
dotnet run --project tests/TiaGuard.Openness.Smoke/TiaGuard.Openness.Smoke.csproj -c Release -- snapshot open-copy C:\path\to\project.ap21
```

Exit code `3` means the **current Windows logon token** lacks effective `Siemens TIA Openness` membership. Account membership alone is insufficient until a new token is issued. Exit code `2` means another failure. `OpennessRuntime.Initialize()` resolves V21 Siemens assemblies from the installed PublicAPI and `Bin/PublicAPI` directories; the DLLs are not copied into build output or committed. The public session factories call it automatically after the access check.
