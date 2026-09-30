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

## Design review grounded in the current source

An agent needs project/target identity, declared symbols and addresses, program entry points,
execution order, data flow, state/timing, and explicit coverage before it can reason about a PLC.
The current canonical JSON reliably provides only the first three: V21/project identity,
one station/CPU/PLC, root primitive tag declarations with raw I/Q/M addresses and one comment,
and Main/OB1/LAD identity. It is not a program access inventory. Hardware IP parameters,
comment language identity and runtime behavior cannot be recovered from those descriptors.

Block interfaces, compile units, network titles/comments and any LAD instruction/wire content
are present only in full SimaticML. The current canonical validator verifies XML integrity and
OB1 identity, not every LAD element's execution semantics. A valid round-trip source may therefore
contain LAD content the AI renderer does not understand.

The historical first-stage `S7-1200-Motor-Reversing-Control` export had one compile unit with an empty
`NetworkSource`, and an empty default tag table. Its source XML digest is
`382e9a41898cdd73738690fda8b822d626b1852a51dfedea42858139de36d857`.
The name does not establish a reversing circuit. That historical source offered no contact/coil/wire example. The next-stage owned V21
re-export now has six Bool tags and three real LAD networks; it grounds the bounded
[graph contract](LAD-GRAPH.md) and its separate source-evidence and derived-analysis layers.

The alternatives lead to materially different correctness boundaries:

- **Markdown only:** cheap to read, but difficult to query reliably and to distinguish a missing
  fact from a prose omission. It also hides per-object source locations and coverage states.
- **Typed JSON IR plus generated Markdown (chosen):** one deterministic semantic projection feeds
  both surfaces. JSON carries exact declarations, ordered network observations, nullable facts,
  explicit coverage and source references; Markdown answers the common orientation questions.
  This was the inventory-only first stage and remains the foundation of v2.
- **Graph/network IR plus Markdown (now implemented for a bounded subset):** an extension for actual contacts, coils and
  connections, but requires fixtures and runtime cross-checks for instruction types, pin meaning,
  branches, negation, coils with state, edge operations, timers and execution order. Copying XML
  IDs and edges into JSON without those semantics would give an unjustified impression of support.
- **Normalized pseudo-code:** deferred. Expressions can conceal scan order and stateful behavior;
  first establish a bounded graph contract and cross-check it against Openness/TIA exports.

This is not a measured ranking of ChatGPT, Codex, Claude Code or Cursor. All can receive the
small Markdown overview; coding agents can query ordinary JSON by field without a Siemens-specific
parser or plugin. No model-specific prompt language or model-generated engineering facts are used.

## Implemented v2 representation

`tia-guard ai-view <tia-source-dir>` produces four UTF-8/LF files beside that directory:

- `PROJECT.md`: overview, station/PLC/CPU identity, navigation, authority and coverage;
- `project.json`: typed `ai-engineering-v2` IR with a [JSON Schema](contracts/ai-engineering-v2.schema.json);
- `symbols.md`: root tag declarations, raw addresses, data types and comment status/text;
- `programs/OB1.md`: block language, ordered networks, reads/writes, proven conditions and relationships.

Compared with the original candidate, hardware is folded into the overview because this profile
contains one CPU and few hardware facts. The JSON combines engineering inventory with bounded LAD evidence graphs and derived analysis.
See [LAD-GRAPH](LAD-GRAPH.md) for exact mappings, identity, topology, rejection and scan-order rules.
The historical v1 schema and fixture remain available for older generated views.

The schema fixes property names and rejects unexpected fields. `sourceRef` is relative to
`tia-source/` with URI-escaped path segments; after `#`, JSON files use JSON Pointer and XML uses XPath. Canonical object IDs appear
only inside source paths, not as prominent reading content. Network ordinals preserve XML compile-unit
order; unordered tables/tags/coverage use ordinal sorting. Per-tag pointers retain canonical array
indices, even when display order differs. Canonical array reordering may consequently change pointers;
silently pointing at the wrong tag would be worse than that honest diff.

No timestamp, machine path, GUID, binary file digest or whole-project digest is generated in the view.
This keeps a meaningful change local instead of changing every Markdown file. Existing source
references support inspection; they do not certify freshness. Rerun generation after source changes.
JSON preserves canonical engineering text; Markdown escapes markup and replaces long/blob-like text
with an explicit omission marker and reference to JSON/source. Raw XML/framework/resources are never
copied into the view. A very long user-authored comment can still be large in JSON; that is preserved
engineering data, not embedded Siemens storage. Names and comments remain data, never agent instructions.

Coverage vocabulary:

| State | Meaning |
| --- | --- |
| `extracted` | This stated inventory was obtained from the validated source. |
| `empty` | A specifically observed `NetworkSource` is an empty XML element of the recognized shape. |
| `not-extracted` | This renderer/source contract does not project the named information. |
| `unsupported` | The representation/profile cannot interpret the named content. |
| `incomplete` | Required structural evidence is missing or unrecognized; counts may be incomplete. |
| `unknown` | No reliable conclusion is available for the named information. |
| `supported` | The complete network satisfies the bounded semantic proof rules. |
| `partial` | Overall LAD coverage is limited, or graph evidence is incomplete; not permission to use a partial expression. |
| `ambiguous` | Topology or binding has more than one interpretation; no expression is emitted. |

JSON `null` is unknown/not captured, not absence. A missing `ObjectList` is incomplete, not zero networks.
A missing/duplicate `NetworkSource` is incomplete. Recognized V21 FlgNet/v5 contact/coil
networks receive evidence graphs and all-or-nothing semantic analysis. Unknown structures
remain unsupported; incomplete networks never receive apparently complete output conditions.
An empty table means zero declarations in that validated table, not that the program never accesses I/O.

## Publication and isolation

The CLI accepts one directory named `tia-source` and derives its sibling `ai`; it has no arbitrary
output override. Both legacy `repo/tia-source` and `repo/tia-projects/<slot>/tia-source` work. Source
validation and rendering complete before output mutation. An exclusive per-slot creation lock,
private stage and rename/backup transaction protect ordinary regeneration. Unknown files/directories
inside existing `ai` and any reparse point cause refusal rather than deletion. Malformed contents of
the known generated files may be replaced. A publication rollback restores the previous view; cleanup
failure after successful publication may leave a private backup and is reported without rolling back
from a partly deleted backup. This is not crash-atomic storage or a hostile-process sandbox.

The renderer never changes canonical contracts or source. Builder, verifier, repository discovery,
source replacement and GUI staging have no dependency on it. Failure/corruption in one project's view
does not require reading or changing another slot's view.

Automatic GUI generation/staging is deliberately deferred to a separate integration: publishing two
directories together needs an explicit transaction/recovery design and staging tests for exactly the
selected slot. For now regeneration is an explicit local CLI command, and Git publish remains source-only.
No AI output enters build input or Verify comparison. Future edit support must use typed patches and
deterministic validation before changing canonical data, followed by Build / Compile / Verify.

## Verification questions and limits

For the real demo, opening only `ai/` should answer:

1. Project? `S7-1200-Motor-Reversing-Control`; TIA `V21`; projectVersion unknown/not captured.
2. Target? `PLC_1`, station `S7-1200 station_1`, S7-1200,
   CPU identity `OrderNumber:6ES7 212-1AE40-0XB0/V4.7`.
3. Program? One `Main` / `OB1` in `LAD`; three supported nonempty networks.
4. Symbols? Six Bool tags: two I inputs, two Q outputs and two M branch markers.
5. Conditions? Reciprocal `Start AND NOT opposite-output` assignments, and two independent
   parallel assignments. Mutual output inhibit is derived with explicit network/node evidence.
6. Coverage gaps? Hardware configuration, interfaces, joins/stateful LAD, comment languages,
   other PLC/product profiles and runtime behavior are explicitly listed.

Pure tests cover regeneration bytes, source immutability, legacy/multi-slot isolation, corrupt output,
invalid inputs, stable ordering, escaping/blob omission, source pointers and coverage states. Schema
tests reject invalid authority, coverage, source references, profile and unexpected fields. Real TIA
Build / Compile / Verify evidence remains separate from pure tests and is recorded for this candidate;
tests do not establish arbitrary ladder interpretation or runtime equivalence.
