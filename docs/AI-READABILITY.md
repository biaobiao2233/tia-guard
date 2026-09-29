# AI-readable engineering view

## Why this exists

TIA-Guard already has a harder, lower-level responsibility: preserve a bounded TIA Portal V21 engineering source well enough to rebuild and verify it deterministically. That representation lives in `tia-source/` and is authoritative for round-trip behavior.

The next product direction is different: make the same engineering project practical for AI agents and humans to understand in Git without requiring TIA Portal for every read-only question.

The target is therefore:

> **Git version control + deterministic round-trip + an AI-readable engineering representation.**

This is an extension of the current round-trip core, not a replacement for it.

## Why raw `.ap21` is not the AI contract

On the currently accepted self-authored V21 demo, the `.ap21` file is at least partly readable XML. It exposes useful facts such as the project name and compatibility version. It also contains large embedded assets/base64 payloads, Siemens framework metadata, internal type names, identifiers, references and other implementation detail.

So an AI system can technically read that observed file, but that does not make raw `.ap21` a good model interface:

- useful engineering intent is mixed with framework/storage detail;
- large payloads waste context tokens;
- internal representation is not the public TIA-Guard contract;
- model interpretation would depend on Siemens-internal structure rather than the validated TIA-Guard source model;
- a generic reader has to rediscover project topology, tags, program structure and relationships on every task.

TIA-Guard must therefore avoid treating direct raw-`.ap21` parsing as its primary AI interface. Openness extraction plus the validated canonical source remains the trusted boundary.

## Two-layer repository model

A project slot should be able to contain two different representations with different authority:

```text
tia-projects/
└─ motor-reversing/
   ├─ tia-source/          # authoritative round-trip source
   │  ├─ tia-guard.json
   │  └─ tia/...
   └─ ai/                  # derived, replaceable AI/human view
      ├─ PROJECT.md
      ├─ project.json
      ├─ hardware.md
      ├─ symbols.md
      └─ programs/
         └─ OB1.md
```

### `tia-source/`

This is the deterministic engineering-source layer.

It owns:

- build input;
- canonical validation;
- compile/Verify evidence;
- original project-file identity checks;
- the data required for bounded reconstruction.

It must not be simplified merely to make Markdown prettier.

### `ai/`

This is a derived presentation layer.

It should make common engineering questions cheap to answer:

- What CPU and firmware does this project target?
- What PLCs, blocks and tag tables exist?
- What are the important I/O symbols?
- What does OB1 contain?
- Which networks write a given output?
- Where are interlocks or dependencies visible?
- What is unsupported or missing from TIA-Guard's current coverage?

It may contain Markdown for fast reading and a stable structured form for agents.

## Authority rule

The direction of trust is one-way:

```text
.ap21
  ↓ Openness / validated extraction
tia-source/        authoritative
  ↓ deterministic renderer
ai/                derived
```

For the first AI-readable implementation:

- `ai/` MUST NOT be a build input;
- editing Markdown MUST NOT modify the reconstructed TIA project;
- AI-generated prose MUST NOT silently become an engineering fact;
- deleting `ai/` must be harmless because it can be regenerated;
- a renderer failure must not corrupt or weaken `tia-source/`;
- generated facts should be traceable back to canonical source objects where practical.

A future AI-editing workflow, if built, should use a structured patch/command contract validated by TIA-Guard before changing canonical source, followed by Build → Compile → Verify. It should not turn arbitrary model-written Markdown directly into a PLC project.

## Candidate AI view

The exact format is intentionally not frozen yet. A useful first candidate is:

- `PROJECT.md` — concise project overview and navigation;
- `project.json` — stable machine-readable project graph / AI IR;
- `hardware.md` — station, PLC, CPU, firmware and supported topology;
- `symbols.md` — tag tables, addresses, data types and comments;
- `programs/<block>.md` — block identity, language and program/network representation;
- explicit coverage/unsupported notes so an agent can distinguish “not present” from “not extracted/supported”.

The representation should optimize for:

1. engineering fidelity;
2. token efficiency;
3. deterministic regeneration and useful Git diffs;
4. traceability to canonical source;
5. clear unsupported/incomplete states;
6. readability by common coding agents without TIA Portal installed.

## Next implementation question

The next phase should not start by inventing prose summaries. It should inspect the current canonical contracts and real S7-1200 demo, then define the smallest deterministic AI IR that can faithfully describe the supported project.

A good first proof is:

```text
validated tia-source/
      ↓
deterministic AI renderer
      ↓
ai/project.json + Markdown views
      ↓
independent questions/tests over the generated view
```

The round-trip core remains frozen unless the AI-view work discovers a genuine missing fact that cannot be obtained from the existing canonical model.
