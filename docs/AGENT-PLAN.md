# Parallel agent plan

GitHub is the durable tracker. Each implementation agent works on an isolated branch/worktree and owns non-overlapping paths.

## Coordinator-owned files

Only the coordinator should edit these unless explicitly delegated:

- README.md
- docs/ARCHITECTURE.md
- docs/contracts/*
- .github/*

## Agent A — Openness adapter

Branch: feat/openness-core

Owns:

- src/TiaGuard.Openness/**
- focused tests for the Openness adapter

Goal:

- attach/open a TIA Portal V21 project through Openness;
- implement info and Snapshot v1 extraction;
- no project writes.

## Agent B — deterministic doctor + SARIF

Branch: feat/doctor-sarif

Owns:

- src/TiaGuard.Analysis/**
- src/TiaGuard.Reporting/**
- focused tests for those components

Goal:

- consume Snapshot v1 fixtures;
- implement first deterministic checks;
- emit Markdown/JSON/SARIF.

## Agent C — AI review layer

Branch: feat/ai-review

Owns:

- src/TiaGuard.AI/**
- AI-specific tests/docs inside that directory

Goal:

- provider-neutral review interface;
- consume Snapshot v1;
- keep prompts/results separate from deterministic findings;
- no TIA project writes.

## Merge order

1. Agent A and Agent B can proceed in parallel against Snapshot v1.
2. Agent C can proceed against the same contract.
3. Coordinator integrates CLI composition after focused branches have tests/evidence.
