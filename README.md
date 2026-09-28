# TIA-Guard

**Reproducible Git engineering source for a bounded Siemens TIA Portal V21 subset.**

TIA-Guard is an independent pre-alpha project. The Core path exports an existing V21 project to a canonical Git tree, builds a fresh project from that tree, then verifies supported engineering semantics. Snapshot, deterministic checks, reporting and optional AI review remain supporting layers.

The project is designed around the locally installed **TIA Portal Openness API**. It does not redistribute Siemens software, DLLs, licenses, or TIA project binaries.

## v0.1 objective

Prove one reproducible loop on **TIA Portal V21**:

1. export the supported single S7-1200 / PLC / Main OB1 LAD project from an owned offline copy to a deterministic Git tree;
2. validate that tree and build a fresh disposable V21 project from it alone;
3. compare the supported engineering semantics of the source and rebuilt project.

The bounded export, fresh-project build, and semantic Verify slices are accepted for the self-authored V21 demo. The contracts remain versioned draft v1 formats; arbitrary TIA projects and runtime behavior are outside this proof.

That historical acceptance covers CPU `OrderNumber:6ES7 212-1AE40-0XB0/V4.7`, its observed Chinese-locale integrated item tree, and an empty tag table. The current validator explicitly limits the CPU profile; populated primitive tags have deterministic contract tests, not the same real-project acceptance evidence. See [architecture and coverage](docs/ARCHITECTURE.md).

For human use, the Windows GUI exposes the same bounded workflow without requiring command-line knowledge. It is a thin WPF front end over the same `TiaGuard.Openness` core, with a read-only environment check, one-click Export → Build → Verify, individual operations, human-readable PASS/BLOCKED/ERROR states, and a technical log.

```powershell
dotnet build src/TiaGuard.Gui/TiaGuard.Gui.csproj -c Release
& .\src\TiaGuard.Gui\bin\Release\net48\TiaGuard.exe
```

The product CLI remains the automation / AI surface and exposes the complete bounded loop:

```powershell
dotnet build src/TiaGuard.Cli/TiaGuard.Cli.csproj -c Release

# Read-only prerequisite check and product version
& .\src\TiaGuard.Cli\bin\Release\net48\tia-guard.exe doctor
& .\src\TiaGuard.Cli\bin\Release\net48\tia-guard.exe --version

# Existing V21 project -> canonical Git engineering tree
& .\src\TiaGuard.Cli\bin\Release\net48\tia-guard.exe export C:\path\to\project.ap21 C:\path\to\canonical-tree

# Canonical Git engineering tree -> fresh V21 project
& .\src\TiaGuard.Cli\bin\Release\net48\tia-guard.exe build C:\path\to\canonical-tree --output C:\path\to\new-project

# Original vs rebuilt supported engineering semantics
& .\src\TiaGuard.Cli\bin\Release\net48\tia-guard.exe verify C:\path\to\project.ap21 C:\path\to\new-project\Demo.ap21
```

`doctor` is a read-only prerequisite check. It does not start TIA Portal or modify a project; it reports x64 process state, the V21 installation/PublicAPI presence, effective Siemens TIA Openness group membership, and exits `0` when ready or `5` when blocked. `--version` prints the packaged CLI version.

`export` opens only an owned offline copy and prints the Round-trip Source v1 manifest as JSON. Exit code `0` means the exported tree is round-trip ready; `5` means an export was produced but the bounded profile is blocked. `build` reads only the canonical tree, requires an unused output directory, creates a separate headless V21 project, saves and compiles it, and publishes it only with zero compile errors. Keep the output path short enough for TIA Portal V21's 143-character staged project-folder limit. `verify` returns JSON with verdict `pass`, `mismatch`, or `blocked` and exit codes `0`, `4`, or `5`.

For a combined human + automation Windows package:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\package-windows.ps1
```

The combined package contains `TiaGuard.exe` for the GUI, `tia-guard.exe` for CLI/AI automation, the shared TIA-Guard core, and a short usage note. The CLI-only package remains available through `scripts/package-cli.ps1`. Neither package redistributes Siemens DLLs, licenses, or TIA project binaries; the target machine must already have TIA Portal V21 and Openness installed.

## Design principle

**Deterministic facts stay deterministic; AI handles interpretation.**

TIA-Guard must distinguish:

- verified engineering facts;
- coverage and collection failures;
- deterministic findings;
- advisory AI output.

An empty result must never mean both "nothing exists" and "collection failed".

## v0.1 rule scope

Initial candidates are intentionally narrow:

- missing tag comments under a documented language/policy;
- address-range overlap for supported primitive address forms, reported as overlap rather than automatically as an error;
- M-area tag declarations as an inventory/info finding, not a claim of actual program use;
- block consistency / compile evidence only when the evidence source and freshness are explicit;
- incomplete collection as a first-class diagnostic that prevents a misleading "all clear".

## Safety model

- v0.1 targets **specified offline project copies** first.
- No online PLC writes.
- The original project is never saved, upgraded or imported into. An explicit build creates, imports into, saves and compiles only a fresh disposable project.
- Export may compile only its owned offline copy when Siemens requires consistency before SimaticML export.
- Partial/unsupported/protected data is reported explicitly.
- Main has no AI/network review path. Preserved engineering XML/comments may themselves contain sensitive text; canonical export is not automatic anonymization.

## Development environment

The first target is Windows with TIA Portal V21 and TIA Portal Openness installed.

Run the environment probe:

    powershell -ExecutionPolicy Bypass -File .\scripts\check-env.ps1

Openness V21 is a **.NET Framework 4.8** integration boundary. Modern .NET components may be used elsewhere, but the collector boundary must be proven with the locally installed V21 assemblies.

## Repository layout

    src/TiaGuard.Openness   Siemens Openness collector / evidence adapter
    src/TiaGuard.Cli        V21 export / build / verify CLI
    src/TiaGuard.Gui        WPF human UI over the same bounded workflow
    tests/TiaGuard.Contracts.Tests  pure contract tests, no Siemens dependency
    tests/TiaGuard.Openness.Smoke   local V21 integration harness
    docs/contracts          draft shared evidence/finding contracts
    examples                sanitized fixtures only

Analysis/Reporting/AI remain unmerged supporting-layer proposals, not installed mainline modules. `examples/roundtrip-fixture` is synthetic validation input, not a Siemens-importable demo or proof of compilation.

## Safe automated checks

```powershell
dotnet test tests/TiaGuard.Contracts.Tests/TiaGuard.Contracts.Tests.csproj -c Release
python -m pip install jsonschema==4.26.0
python scripts/test-contract-schemas.py
```

These checks run on Windows without TIA or Siemens DLLs. The GitHub workflow runs this pure boundary only; it does not claim TIA runtime compatibility or PLC verification.

## Release gates for v0.1

- one self-authored TIA V21 project collected and manually cross-checked against the GUI;
- repeated unchanged collection produces identical normalized engineering content;
- one known edit produces the expected diff;
- supported rules have positive and negative/exception fixtures;
- partial/protected collection does not produce a false PASS;
- SARIF upload is demonstrated on GitHub with correct location and stable rerun behavior.

## Disclaimer

This is an independent open-source project and is not affiliated with, authorized by, or endorsed by Siemens AG. Siemens, TIA Portal, SIMATIC, STEP 7, WinCC, and related names are trademarks of their respective owners.
