# TIA-Guard

**Readable Git engineering source, bounded round-trip reconstruction, and AI-assisted engineering workflows for Siemens TIA Portal V21.**

TIA-Guard is an independent pre-alpha project built around the locally installed **TIA Portal Openness API**. Its core goal is to turn a supported TIA Portal project into deterministic, human-readable engineering source that can be reviewed in Git, rebuilt into a fresh TIA project, and verified against the supported engineering semantics.

The repository does **not** redistribute Siemens software, DLLs, licenses, or TIA project binaries.

## Why TIA-Guard

TIA Portal projects are normally stored as `.ap21` engineering binaries. That works well inside TIA Portal, but makes normal source-control workflows, code review, open collaboration, and AI-assisted engineering much harder than they are for text-based software projects.

TIA-Guard addresses that gap in three layers:

1. **Readable and Git-native engineering source**  
   Export the supported part of an S7-1200 project into deterministic, structured source that can be diffed, reviewed, searched, versioned, and consumed by local AI tools.

2. **Reproducible reconstruction and verification**  
   Rebuild a fresh TIA Portal project from the canonical source, then use Build / Verify checks to confirm the covered PLC engineering semantics were preserved.

3. **AI-assisted engineering workflow**  
   A local AI engineering Gateway + companion Skill is the next interaction layer: local agents such as Codex, Cursor, and Claude Code can inspect supported PLC context, plan guarded changes, and participate in compile / rebuild / verify loops without treating an AI answer as proof.

The intended progression is:

```text
opaque .ap21 project
    -> readable canonical engineering source
    -> Git review / version history
    -> fresh rebuilt TIA project
    -> bounded semantic verification
    -> AI-assisted, verified engineering workflow
```

## Current status

The default branch is intentionally conservative. It contains the bounded V21 round-trip core and keeps unsupported areas fail-closed.

| Capability | Current repository status |
| --- | --- |
| Export supported V21 engineering source | Implemented through the Openness library / Smoke harness |
| Build a fresh V21 project from canonical source | Implemented; product CLI exposes `tia-guard build` |
| Verify original vs rebuilt supported semantics | Implemented through the Openness library / Smoke harness |
| Unified `export / build / verify` product CLI | Candidate in [PR #32](../../pull/32) |
| Local AI engineering Gateway + Skill | Active development layer; not claimed as released mainline functionality |

The accepted real-project proof is deliberately narrow: one self-authored **TIA Portal V21 / S7-1200 / Main OB1 LAD** project profile. Arbitrary TIA projects, runtime/control-logic equivalence, HMI, Safety, drives, multiple PLCs, and unsupported Openness objects are **not** implied by this proof.

See [architecture and coverage](docs/ARCHITECTURE.md) and the [Round-trip Source v1 contract](docs/contracts/roundtrip-source-v1.md) for the exact boundary.

## Core round-trip

The bounded core is:

```text
existing .ap21
  -> owned offline copy
  -> canonical Git source
  -> strict source validation
  -> fresh disposable V21 project
  -> compile
  -> re-export
  -> supported semantic comparison
```

The original project is never used as a writable build target. Build creates a new project in a separate output directory, saves and compiles it, and only publishes the result after successful validation.

### Build example

```powershell
dotnet build src/TiaGuard.Cli/TiaGuard.Cli.csproj -c Release
& .\src\TiaGuard.Cli\bin\Release\net48\tia-guard.exe build C:\path\to\canonical-tree --output C:\path\to\new-project
```

Export and Verify are currently available through the library / [Openness Smoke harness](src/TiaGuard.Openness/README.md). PR #32 exposes the already bounded implementations as one simple CLI surface.

## Verification model

TIA-Guard separates evidence from claims:

- verified engineering facts;
- incomplete or unsupported collection;
- deterministic validation findings;
- advisory AI output.

Unsupported, protected, unreadable, or incomplete engineering content cannot silently produce a PASS.

The real V21 acceptance demonstrated a bounded source -> rebuild -> re-export -> Verify loop with zero compile errors/warnings for the accepted demo profile. Pure contract tests cover additional deterministic negative cases without claiming those cases are real Siemens integration evidence.

## Safety model

- Offline project copies first; no PLC online writes.
- No download, force, or runtime control operations.
- The supplied project is not saved, upgraded, or imported into.
- Build writes only to a fresh disposable output.
- Unsupported or incomplete content blocks readiness instead of being treated as empty.
- Engineering comments/XML may contain project data; canonical export is **not** automatic anonymization.
- Siemens assemblies are resolved from the user's local installation and are not copied into packages or committed.

## Public-source hygiene

This repository is intended to remain source-only:

- no `.ap21` / TIA project archives;
- no Siemens DLLs or executables;
- no licenses, private keys, credentials, or local `.env` files;
- no build output, logs, local scratch directories, or customer project data;
- examples are sanitized/synthetic fixtures only.

The ignore policy is defined in [`.gitignore`](.gitignore).

## Development environment

Target environment:

- Windows
- Siemens TIA Portal V21
- TIA Portal Openness V21
- .NET Framework 4.8 integration boundary
- .NET SDK 8 for supporting tooling/tests

Environment probe:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\check-env.ps1
```

If administrative bootstrap is needed, `scripts/bootstrap-admin.ps1` uses the current Windows identity by default; no machine-specific user name is required.

## Repository layout

```text
src/TiaGuard.Openness          Siemens Openness adapter / round-trip implementation
src/TiaGuard.Cli               product CLI
tests/TiaGuard.Contracts.Tests pure contract and filesystem tests
tests/TiaGuard.Openness.Smoke  local V21 integration harness
docs/contracts                 versioned source/evidence contracts
docs/verification              verification receipts and bounded evidence
examples                       sanitized fixtures only
```

## Safe automated checks

These checks require no Siemens DLL redistribution:

```powershell
dotnet test tests/TiaGuard.Contracts.Tests/TiaGuard.Contracts.Tests.csproj -c Release
python -m pip install jsonschema==4.26.0
python scripts/test-contract-schemas.py
```

Pure tests prove contract behavior, not TIA runtime compatibility. Real Openness behavior requires a local V21 installation and separately identified integration evidence.

## Project direction

TIA-Guard is not trying to replace TIA Portal. The project explores a safer bridge between traditional PLC engineering and modern software-engineering workflows:

- readable engineering source;
- Git review and version history;
- deterministic reconstruction;
- bounded verification;
- local AI agents operating through explicit engineering contracts rather than opaque UI automation alone.

## Disclaimer

This is an independent open-source project and is not affiliated with, authorized by, or endorsed by Siemens AG. Siemens, TIA Portal, SIMATIC, STEP 7, WinCC, and related names are trademarks of their respective owners.
