# TIA-Guard

**Traceable engineering evidence and deterministic review for Siemens TIA Portal projects.**

TIA-Guard is an independent pre-alpha project focused on a narrow problem: make evidence from an existing TIA Portal project reviewable outside TIA, then run bounded deterministic checks and produce Git/GitHub-friendly reports. AI is optional and explains evidence; it is not the source of engineering truth.

The project is designed around the locally installed **TIA Portal Openness API**. It does not redistribute Siemens software, DLLs, licenses, or TIA project binaries.

## v0.1 objective

Prove one reproducible end-to-end loop on **TIA Portal V21**:

1. open a **specified offline project copy** through Openness;
2. collect a traceable Snapshot with explicit completeness/failure state;
3. serialize the same engineering content deterministically;
4. run 3-5 bounded deterministic checks whose scope and false-positive boundaries are documented;
5. show a small before/after engineering diff;
6. emit JSON/Markdown and one **real GitHub-validated SARIF** workflow.

Planned CLI surface:

    tia-guard info
    tia-guard snapshot
    tia-guard doctor
    tia-guard diff
    tia-guard review --ai   # optional experiment, not a v0.1 release gate

Status: pre-alpha. The Snapshot contract is still **draft** until it has been validated against a real sanitized TIA V21 project.

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
- No automatic save, upgrade, import, property changes, protection unlock, controller state changes, or session shutdown.
- No active compile by default.
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
