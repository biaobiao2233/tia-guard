# TIA-Guard

AI-assisted engineering review for Siemens TIA Portal projects.

TIA-Guard is an independent pre-alpha project that aims to make TIA Portal engineering projects easier to inspect, review, diff, and discuss in Git/GitHub workflows.

It is designed around the locally installed TIA Portal Openness API. The project does not redistribute Siemens software, DLLs, licenses, or TIA project binaries.

## v0.1 objective

Build a read-first CLI for TIA Portal V21 that can:

- inspect the engineering project;
- emit a deterministic JSON/Markdown snapshot of devices, PLCs, blocks, tags, and compile state;
- run deterministic engineering checks (doctor);
- generate GitHub-friendly SARIF findings;
- optionally feed the structured snapshot to an LLM for engineering explanation/review.

Planned CLI surface:

    tia-guard info
    tia-guard snapshot
    tia-guard doctor
    tia-guard review --ai

Status: repository scaffold only. These commands are the v0.1 contract, not yet implemented.

## Design principle

Deterministic facts stay deterministic; AI handles interpretation.

Address conflicts, missing comments, naming rules, compile errors, and structural checks belong to rules and compiler evidence. LLMs are used for explanation, summarization, review suggestions, and documentation.

## Safety model

- Read-only by default.
- No online PLC writes in v0.1.
- Future write operations must be dry-run first and require explicit --apply.
- Work against offline project copies for development/testing.

## Development environment

The intended first target is Windows with TIA Portal V21 and TIA Portal Openness installed.

Run the environment probe:

    powershell -ExecutionPolicy Bypass -File .\scripts\check-env.ps1

## Repository layout

    src/TiaGuard.Openness   Siemens Openness adapter
    src/TiaGuard.Analysis   deterministic checks
    src/TiaGuard.Reporting  Markdown/JSON/SARIF
    src/TiaGuard.AI         optional LLM review layer
    src/TiaGuard.Cli        CLI composition
    docs/contracts          stable contracts for parallel development
    examples                sanitized fixtures only

## Disclaimer

This is an independent open-source project and is not affiliated with, authorized by, or endorsed by Siemens AG. Siemens, TIA Portal, SIMATIC, STEP 7, WinCC, and related names are trademarks of their respective owners.
