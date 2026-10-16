# Project Generation v1 offline input

`tia-guard init <new-directory>` writes `project-generation.json` and an editable
`sources/Main.scl`. The parent must exist and be plain; an existing destination
(including an empty directory) is refused. This is a pure file operation and
does not open TIA or create an `.ap21`. The old `build` command still consumes
RoundTrip Source v1; it does not consume this new description.

`minimal/` matches the init template. `multi-object/` adds a nonempty tag table
and a separate FC source, with an explicit dependency. These are offline input
fixtures, not TIA creation/compile/verification receipts.

The current capability table accepts SCL envelopes for OB/FB/FC/DB/UDT on the
exact V21/1212C V4.7 identity in the coordination contract. SD and SimaticML
remain reserved format names and are explicitly rejected until format-specific
validators and B/D runtime evidence are integrated. Names in this first input
profile use ASCII PLC identifiers; project names additionally obey portable
Windows filename rules. Unknown fields, duplicate JSON keys, non-string scalars,
unlisted files, escaping paths and reparse points are rejected. Relative source
paths use `/` and `.scl`. The spec folder is dedicated to inputs; keep project
notes and licenses outside individual fixture/input folders.

Static validation checks source object **envelopes** and names, not arbitrary
SCL grammar, block numbers, interfaces, calls or implicit instance generation.
Declarations sharing one source must list every explicit object. Cycles are
rejected in both the artifact graph and the grouped source-import graph.
The importer must import each `OrderedSourcePaths` entry once. It must bind the
generated runtime object set (including implicit DBs), language, number,
interfaces and instances to the declared inventory, refuse omissions/extras,
and obtain complete V21 compile diagnostics before success. A loader success
must never be reported as a build/verify PASS.

## Consumer API

- `ProjectGenerationSpecLoader.Load(string specPath)` returns an immutable
  `ProjectGenerationSpecInput`; the argument is the JSON file, not its directory.
- `Spec`, `OrderedArtifacts`, `OrderedSourcePaths`, `SourceSha256` are immutable
  declarations and import ordering/digests.
- `GetSourceBytes(string sourcePath)` returns a private copy of validated bytes.
- `ValidateUnchanged()` rejects changed descriptions, sources or file inventories.
  Consumers must import snapshot bytes from private owned files; checking and
  then re-reading the original path is not a race-free protocol.
- `ProjectGenerationInitializer.Initialize(string newDirectory)` returns the
  validated generated input; template identity is
  `tia-guard/project-generation-v1/minimal-scl-main@1`.

## Upstream reuse and license

`multi-object/sources/Scale.scl` reuses
`bulaofen0036-coder/TIA_Portal_Openness_MCP` at
`3e8f8810924657a1adc4b41c0166a0829ad1caf7`, original path
`templates/plc/scl-examples/FC_BasicScaleLimit.scl`. Copyright (c) 2026
bulaofen0036-coder. Full MIT terms are in `LICENSE.bulaofen.txt` beside this file.
The original SCL source is retained, with a provenance header added.

The same revision's `tools/tiaportal-mcp/src/TiaMcpServer/Cli/SpecLoader.cs`,
`CliCommands.cs` and `templates/project-blueprints` were inspected. Their JSON
pass-through, YAML coercion, `__BUNDLE__` absolute-path expansion and
S7-1500/HMI blueprint defaults do not satisfy this strict contract; those modules
are not copied. Initialization, strict JSON loading and the minimal Main envelope
are local implementations. No whole MCP host or installer is included.
