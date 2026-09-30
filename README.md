# TIA-Guard

**Readable, reproducible Git engineering source and a local AI engineering gateway for Siemens TIA Portal V21.**

TIA-Guard is an independent pre-alpha project. The Core path exports an existing V21 project to a canonical Git tree, builds a fresh project from that tree, then verifies supported engineering semantics. Snapshot, deterministic checks, reporting and optional AI review remain supporting layers.

The project is designed around the locally installed **TIA Portal Openness API**. It does not redistribute Siemens software, DLLs, licenses, or TIA project binaries.

## Why TIA-Guard

TIA Portal projects are normally stored as `.ap21` engineering binaries. That is convenient for TIA Portal itself, but it makes normal Git workflows, code review, open-source sharing, and AI-assisted engineering much harder than they are for text-based software projects.

TIA-Guard is intended to close that gap in three layers:

1. **Make PLC engineering readable, diffable, and shareable.** TIA-Guard translates the supported part of an S7-1200 project into deterministic, human-readable, Git-native engineering source under `tia-source/`. PLC programs, tags, and LAD logic can then be inspected, versioned, reviewed, searched, and consumed by AI tools. A readable repository also lowers the barrier to sharing PLC engineering work openly, so developers can learn from, reuse, review, and improve each other's implementations instead of exchanging opaque project binaries.
2. **Preserve the ability to reconstruct the real TIA project.** The readable form is not meant to be a lossy documentation export. The core loop is `.ap21 -> canonical source -> new .ap21`, followed by Build / Verify checks against the supported engineering semantics. The goal is that making a project readable to Git and AI does not silently change the PLC engineering when it is rebuilt.
3. **Let local AI agents operate the engineering project, not just read it.** Inspired by the interaction model of JLCEDA's API Gateway, the current development direction adds a local TIA AI Gateway plus a companion Skill. Local agents such as Codex, Cursor, and Claude Code can use the Gateway to inspect PLC tags and LAD logic, plan guarded changes, modify supported offline engineering content, and automatically compile, rebuild, and verify the result. The human should describe the engineering intent; the Gateway and Skill should handle the low-level workflow.

This creates a simple progression:

```text
opaque TIA project
    -> readable Git engineering source
    -> reproducible TIA project
    -> AI-readable engineering context
    -> AI-assisted, verified engineering changes
```

## v0.1 objective

Prove one reproducible loop on **TIA Portal V21**:

1. export the supported single S7-1200 / PLC / Main OB1 LAD project from an owned offline copy to a deterministic Git tree;
2. validate that tree and build a fresh disposable V21 project from it alone;
3. compare the supported engineering semantics of the source and rebuilt project.

The bounded export, fresh-project build, and semantic Verify slices are accepted for the self-authored V21 demo. The contracts remain versioned draft v1 formats; arbitrary TIA projects and runtime behavior are outside this proof.

That historical acceptance covers CPU `OrderNumber:6ES7 212-1AE40-0XB0/V4.7`, its observed Chinese-locale integrated item tree, and an empty tag table. The current validator explicitly limits the CPU profile; populated primitive tags have deterministic contract tests, not the same real-project acceptance evidence. See [architecture and coverage](docs/ARCHITECTURE.md).

The product CLI currently exposes **build**. Export and Verify are available through the library and [Openness Smoke harness](src/TiaGuard.Openness/README.md); they are not yet `tia-guard export` / `tia-guard verify` product commands.

Build a fresh V21 project from a validated canonical Round-trip Source v1 tree:

```powershell
dotnet build src/TiaGuard.Cli/TiaGuard.Cli.csproj -c Release
& .\src\TiaGuard.Cli\bin\Release\net48\tia-guard.exe build C:\path\to\canonical-tree --output C:\path\to\new-project
```

The output directory must not exist. Keep its path short enough for TIA Portal V21's 143-character staged project-folder limit; the command checks this before starting TIA Portal. It checks the complete source tree, then creates a separate headless V21 project, saves and compiles it, and publishes it only with zero compile errors. It does not accept an original `.ap21` as input or connect to a PLC.

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
    src/TiaGuard.Cli        V21 build command
    tests/TiaGuard.Contracts.Tests  pure contract tests, no Siemens dependency
    tests/TiaGuard.Openness.Smoke   local V21 integration harness
    docs/contracts          draft shared evidence/finding contracts
    examples                sanitized fixtures only

The default branch remains the conservative reproducible-core line. AI Engineering and the local Gateway are being developed on an integration candidate and are not yet claimed as released mainline functionality. `examples/roundtrip-fixture` is synthetic validation input, not a Siemens-importable demo or proof of compilation.

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
