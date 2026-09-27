# Parallel agent plan

GitHub is the durable tracker. Physical worktrees may still execute in parallel, but the semantic dependency is now explicit: **real TIA evidence first, then rule/report closure, then optional AI**.

## Coordinator-owned files

Only the coordinator should edit these unless explicitly delegated:

- README.md
- docs/ARCHITECTURE.md
- docs/contracts/*
- .github/*

The Snapshot contract is currently **draft**, not frozen.

## Agent A — Openness evidence collector

Branch: feat/openness-core

Owns:

- src/TiaGuard.Openness/**
- focused tests / smoke harness for collector behavior

Goal:

- target a **specified offline TIA Portal V21 project copy**;
- traverse device/PLC/block/tag structures recursively enough to preserve ownership/scope;
- emit Draft Snapshot v1 evidence plus explicit collection diagnostics;
- record protected/unsupported/read-failed objects;
- separate read-only consistency observation from any active compile;
- produce one real sanitized Snapshot from the self-authored demo project;
- no save/import/edit/unlock/online PLC writes.

A's real sanitized output is the semantic input B must ultimately validate against.

## Agent B — deterministic doctor + reporting

Branch: feat/doctor-sarif

Owns:

- src/TiaGuard.Analysis/**
- src/TiaGuard.Reporting/**
- focused tests

Goal:

- consume the coordinator-owned Snapshot/Finding contracts;
- keep rule scope bounded and explicit;
- implement 3-5 checks with both positive and legitimate-exception fixtures;
- emit JSON/Markdown/SARIF.

Rule corrections:

- address overlap is an overlap finding for supported parsed forms, not automatically an error;
- M-area tag declaration is inventory/info, not actual-use proof;
- duplicate symbols are scope-aware;
- missing comment is distinct from comment-read failure;
- incomplete collection blocks a misleading clean result.

B may start against fixtures, but final acceptance waits for A's real sanitized Snapshot and a real GitHub SARIF upload.

## Agent C — optional AI experiment

Branch: feat/ai-review

Owns:

- src/TiaGuard.AI/**
- AI-specific tests/docs

Status: **optional experiment; not a v0.1 release dependency**.

Goal:

- structured advisory output only;
- cite Snapshot objects / Findings / artifacts;
- refuse unsupported engineering conclusions;
- keep deterministic findings authoritative;
- no TIA writes and no secrets/local-path leakage.

A provider-neutral abstraction is acceptable, but v0.1 should prove value with one grounded path before expanding provider breadth.

## Merge / acceptance order

1. Coordinator maintains draft Snapshot and Finding contracts.
2. Agent A proves real TIA V21 collection and supplies a sanitized evidence fixture.
3. Agent B is reconciled against that same fixture and proves deterministic reports/SARIF.
4. Coordinator proves before/after diff and GitHub display behavior.
5. Agent C is rebased/adapted and merged only if it demonstrates incremental value over deterministic reports.
6. CLI integration and release evidence come last.

Parallel code completion is not equivalent to end-to-end acceptance.
