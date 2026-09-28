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
- AI never receives local paths, credentials, or raw customer project data by default.

## Development environment

The first target is Windows with TIA Portal V21 and TIA Portal Openness installed.

Run the environment probe:

    powershell -ExecutionPolicy Bypass -File .\scripts\check-env.ps1

Openness V21 is a **.NET Framework 4.8** integration boundary. Modern .NET components may be used elsewhere, but the collector boundary must be proven with the locally installed V21 assemblies.

## Repository layout

    src/TiaGuard.Openness   Siemens Openness collector / evidence adapter
    src/TiaGuard.Analysis   deterministic checks
    src/TiaGuard.Reporting  Markdown/JSON/SARIF
    src/TiaGuard.AI         optional advisory layer
    src/TiaGuard.Cli        CLI composition
    docs/contracts          draft shared evidence/finding contracts
    examples                sanitized fixtures only

## Release gates for v0.1

- one self-authored TIA V21 project collected and manually cross-checked against the GUI;
- repeated unchanged collection produces identical normalized engineering content;
- one known edit produces the expected diff;
- supported rules have positive and negative/exception fixtures;
- partial/protected collection does not produce a false PASS;
- SARIF upload is demonstrated on GitHub with correct location and stable rerun behavior.

## Disclaimer

This is an independent open-source project and is not affiliated with, authorized by, or endorsed by Siemens AG. Siemens, TIA Portal, SIMATIC, STEP 7, WinCC, and related names are trademarks of their respective owners.
