# Architecture

## Problem

TIA Portal projects are rich engineering artifacts, but their native project format is not directly reviewable in GitHub. A reviewer cannot easily answer basic questions from a pull request: what devices exist, what blocks changed, whether I/O overlaps, whether tags are undocumented, or what the compile state is.

## v0.1 architecture

    TIA Portal V21
          |
          | TIA Portal Openness (.NET Framework 4.8)
          v
    TiaGuard.Openness
          |
          | Snapshot v1 contract
          v
    +-------------------+---------------------+
    | TiaGuard.Analysis | TiaGuard.AI         |
    | deterministic     | optional reasoning  |
    +---------+---------+----------+----------+
              |                    |
              +---------+----------+
                        v
                 TiaGuard.Reporting
                 JSON / Markdown / SARIF
                        |
                        v
                   TiaGuard.Cli

## Boundaries

### Openness adapter

Owns all Siemens-specific API calls. It must not leak Siemens API object graphs into the rest of the codebase. Instead it maps them into the stable Snapshot v1 model.

### Analysis

Runs deterministic checks against Snapshot v1. No LLM calls.

Initial checks:

- duplicate/overlapping PLC I/O addresses;
- missing tag comments;
- duplicate symbolic names;
- direct M-memory usage inventory;
- compile-error/warning propagation when compile evidence is available.

### AI review

Consumes the same sanitized Snapshot v1 representation and optional exported block text. AI output is advisory and must be clearly separated from deterministic findings.

### Reporting

Produces report.json, report.md, and tia-guard.sarif.

## Safety

v0.1 is read-only. It may open/attach to TIA Portal and export/inspect engineering data, but it must not download to a PLC or modify a running controller.
