# Architecture

## Problem

TIA Portal already has vendor-supported engineering, version-control and testing capabilities. TIA-Guard does not claim to make TIA "Git-capable" for the first time.

The narrower problem is **reviewable evidence**:

- extract a bounded, traceable representation from an existing TIA project;
- make collection completeness and unsupported/protected data explicit;
- run deterministic checks with documented coverage;
- produce a low-noise before/after review artifact that can be consumed without every reviewer having TIA installed;
- optionally let an LLM explain already-grounded evidence.

## v0.1 architecture

    specified offline TIA Portal V21 project copy
                    |
                    | Openness / .NET Framework 4.8
                    v
             TiaGuard.Openness
             collector + diagnostics
                    |
                    | Draft Snapshot v1
                    v
          +---------+----------+
          |                    |
          v                    v
    TiaGuard.Analysis     TiaGuard.Diff
    deterministic          bounded evidence diff
          |                    |
          +---------+----------+
                    v
             Finding contract
                    |
                    v
             TiaGuard.Reporting
          JSON / Markdown / SARIF
                    |
                    v
               TiaGuard.Cli

          Optional experiment:
             TiaGuard.AI
        consumes Snapshot + Findings
        and returns advisory output

## Contracts

### Snapshot

Snapshot is an evidence package, not just an inventory. It must encode:

- stable object identity/association where available;
- PLC/device/scope relationships;
- collection status (complete / partial / failed);
- unsupported/protected/read-failed diagnostics;
- block export state and content hash when export evidence exists;
- parsed address semantics only for supported forms;
- compile evidence mode/source/freshness;
- collector/TIA/schema versions;
- deterministic normalization rules.

Runtime noise such as process IDs and absolute local paths must not participate in stable engineering-content hashes.

### Finding

Analysis emits a separate Finding model. Reporting and AI must not invent findings independently.

A finding contains at minimum:

- stable ruleId;
- severity;
- message;
- object/evidence references;
- applicability/coverage information;
- optional artifact location for SARIF.

## Boundaries

### Openness collector

Owns all Siemens-specific API access.

v0.1 prioritizes a **specified offline project copy**. It must not silently select the first process/project.

It may collect/export evidence, but by default it does **not**:

- save or upgrade the project;
- import or modify engineering objects;
- unlock protected content;
- compile the project;
- change PLC RUN/STOP or online state;
- close the user's TIA session.

Read failures, protected blocks, unsupported objects and partial traversal are evidence, not empty success.

### Analysis

Runs deterministic checks only against evidence the Snapshot says was actually collected.

Initial rule semantics:

- address overlaps are reported only for supported parsed address forms and are not automatically declared errors;
- M-area tag declarations are inventory/info, not proof of direct program use;
- duplicate names are evaluated within an explicit scope;
- comment checks distinguish empty/missing from unavailable/failed reads;
- compile findings identify whether they came from consistency observation or an explicit compile action.

### Diff

v0.1 diff is intentionally limited to evidence with stable normalization:

- object add/remove;
- supported tag address/type/comment changes;
- block export hash change only when valid export evidence exists.

Behavioral/semantic equivalence is out of scope.

### AI review

AI is optional and not a v0.1 release gate.

It may:

- explain deterministic findings;
- summarize evidence-backed changes;
- propose questions for an engineer to confirm.

It must not claim control-logic correctness from missing evidence. Every engineering statement should cite a Snapshot object, Finding, or exported artifact.

### Reporting / SARIF

SARIF is an output adapter, not the internal domain model.

GitHub-facing SARIF must use real repository-relative artifacts/locations when possible. A schema-valid SARIF file is not accepted until GitHub upload, location rendering, rerun stability, and resolved-finding behavior have been demonstrated.

## Runtime boundary

TIA Portal V21 Openness is treated as a **.NET Framework 4.8 collector boundary**. If the CLI uses modern .NET, the collector may be an isolated net48 process exchanging versioned JSON with the rest of the tool. Do not assume .NET SDK 8 alone proves Openness compatibility.

## CI boundary

GitHub-hosted CI can validate:

- Snapshot/Finding schema;
- deterministic normalization;
- rules;
- serializers/reporters;
- fixture-based diff/AI behavior.

It cannot prove:

- local V21 assembly loading;
- Openness group permissions;
- traversal completeness;
- compatibility with installed TIA updates/device catalogs.

Those require local integration evidence tied to a commit.

## v0.1 acceptance

1. One self-authored real TIA V21 project is collected and manually cross-checked.
2. Repeated unchanged collection yields identical normalized engineering content.
3. One known modification yields the expected bounded diff.
4. Supported rules have positive and legitimate-exception fixtures.
5. Partial/protected collection cannot produce a false clean result.
6. One real GitHub SARIF workflow is demonstrated end-to-end.
