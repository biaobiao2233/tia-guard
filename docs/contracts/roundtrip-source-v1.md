# TIA-Guard Round-trip Source v1

Status: **versioned draft v1 contract; bounded Export #14, Build #16, and Verify #23 slices accepted**

This contract defines the canonical Git engineering source for the bounded TIA-Guard v0.1 round-trip subset. Snapshot v1 remains an observation/intermediate format; files defined here are build inputs.

## Root tree

```text
tia-guard.json
tia/
  hardware/
    <station-id>.json
  plc/
    <plc-id>/
      plc.json
      tags/
        <table-id>.json
      blocks/
        <block-id>/
          block.json
          source.xml
```

All JSON is UTF-8 without BOM and has deterministic member/list ordering. Canonical files must not contain capture timestamps, process IDs, absolute local paths, temporary paths, or machine identities.

## Capability states

Every encountered object represented by this contract has exactly one capability state:

- `supported-round-trip`: export, rebuild and semantic verification are supported by v0.1.
- `export-only`: textual evidence is preserved, but v0.1 does not rebuild the object.
- `opaque`: the object is detected but its reconstructable semantics are unavailable (for example protected content).
- `unsupported`: the object type/shape is known but outside the v0.1 subset.
- `failed`: an operation required to establish source semantics failed.

`tia-guard.json.roundTripReady` is true only when the source capture is complete and every required represented object is `supported-round-trip`.

Snapshot warning/error diagnostics retain their stable code and engineering object reference in the manifest. A failed or unconfirmed tag-table enumeration adds a failed scan capability and blocks readiness; zero tables are accepted only after a completed scan.

## tia-guard.json

The manifest records:

- `schemaVersion = "1.0"`
- `contractStatus = "draft"`
- `tiaVersion` (v0.1 requires V21)
- project name/version
- optional original `.ap21` identity: exact file name, byte length, and SHA-256 (the three fields appear together)
- `roundTripReady`
- repository-relative hardware and PLC descriptor paths
- a stable, sorted capability ledger
- stable blocking diagnostics

No operational capture metadata is permitted. The original project-file identity is deliberately limited to a file name, byte length, and content hash: no absolute local path is stored. When present, the Windows product surface preserves that file name on rebuild and checks the restored file's name, size, and SHA-256 after semantic Verify. A mismatch is surfaced explicitly instead of being reported as a complete restoration.

## Hardware descriptor

`tia/hardware/<station-id>.json` records the supported root station and the build-grade CPU identity.

The critical field is `createTypeIdentifier`: it is read directly from the CPU-classified `DeviceItem.TypeIdentifier` and is later passed to the Openness `CreateWithItem` flow. TIA-Guard never manufactures an order number or firmware suffix from the generic station `System:Device.*` identifier. The v0.1 S7-1200 subset requires an `OrderNumber:...` CPU TypeIdentifier.

For the first motor-control proof target, the exporter also verifies the observed rack, CPU, and CPU-integrated DeviceItem tree. Extra rack modules or an altered CPU child tree block `roundTripReady`; the CPU create identifier alone does not prove such items can be rebuilt. This exact demo shape is a bounded v0.1 capability, not a claim of support for arbitrary S7-1200 hardware topologies.

`createItemName` preserves the CPU DeviceItem name. `orderNumber` and `firmware` are supporting observed metadata; they are not used to guess a missing `createTypeIdentifier`.

This v0.1 slice supports only the verified root station type `System:Device.S71200` and TIA Portal `V21`. An `OrderNumber:` CPU identity by itself does not establish the station family.

## PLC descriptor

`tia/plc/<plc-id>/plc.json` records PLC identity plus repository-relative tag-table and block descriptor paths. v0.1 requires exactly one PLC software object.

## Tag-table descriptor

Each observed tag table is materialized even when it contains zero tags. This prevents an empty but real table from disappearing merely because Snapshot v1 has no tag row from which to infer it.

Each tag records:

- name
- data type
- raw logical address
- bounded single-comment status/text
- capability

v0.1 fails closed if a required tag cannot be reconstructed without guessing.
Multiple nonempty comment translations are outside the single-comment model and make that tag unsupported.

## Block descriptor and source.xml

v0.1 supports exactly one `Main` / OB1 block in LAD.

`block.json` records block identity, number, language, capability and a repository-relative source artifact reference.

`source.xml` is the full Siemens Openness SimaticML export. TIA-Guard does not invent a LAD DSL in v0.1. Unknown XML nodes are retained. The descriptor records a SHA-256 over the exact canonical artifact bytes.

If the block is protected, export fails, or the artifact hash cannot be verified, the block is not `supported-round-trip`.
An out-of-subset block is `export-only` only after its claimed raw artifact is found, hash-checked, and copied into canonical `source.xml`; otherwise it is `failed` or `unsupported` without a source claim.

Canonical SimaticML uses normalizer `simaticml-v1`. For TIA Portal V21, the only currently classified volatile XML field is `/Document/DocumentInfo/Created`: its value is normalized to `1970-01-01T00:00:00Z`. The exporter first verifies the SHA-256 of the untouched Siemens export against Snapshot evidence, then changes only this proven non-semantic value. Any unexpected `DocumentInfo/Created` shape fails closed; unknown XML nodes are never removed or rewritten. The block descriptor SHA-256 is over the resulting canonical `source.xml`.


Siemens requires a program block to be consistent before it can be exported. TIA-Guard therefore performs a preflight read first. If the supported OB1/LAD block is not consistent, `export` may compile **only the TIA-Guard-owned offline/disposable copy** before exporting SimaticML. It never compiles an attached user project. The canonical source project remains read-only and the disposable copy is destroyed after export.

## Determinism

Two exports of an unchanged project must produce:

- the same canonical relative file set;
- byte-identical canonical JSON;
- identical `source.xml` hashes.

If Siemens export contains proven volatile fields, a future versioned normalizer may remove only explicitly classified non-semantic fields. v0.1 must not silently discard unknown XML.

## Build boundary

The builder validates the complete tree and artifact SHA-256 before starting TIA. It requires v1/V21, `roundTripReady=true`, exactly one supported station and PLC, exactly one Main/OB1/LAD artifact, and a capability ledger that matches every descriptor. Missing, extra, duplicate, escaping or reparse-point paths fail closed. The bounded builder accepts root PLC tag tables and the single-comment tag model.

The evidenced CPU profile is exactly `OrderNumber:6ES7 212-1AE40-0XB0/V4.7`; an `OrderNumber:` prefix alone is not sufficient. Primitive tags are restricted to Bool, Byte, Char, SInt, USInt, Word, Int, UInt, DWord, DInt, UDInt and Real with a parsed I/Q/M address of the matching width. Null/empty addresses, user-defined types, duplicate PLC-wide tag names and nested tag/block scopes are rejected. The populated-tag profile has synthetic tests; the accepted real demo has an empty tag table.

Before publishing any `roundTripReady=true` export, the materializer runs this same complete-tree validator. Validation failure produces a diagnostic export with readiness false and a failed source-validation capability. Unsupported inventory scans check user constants, PLC types/type documents, external sources, technology objects, watch/force tables, user alarm text lists and user folders; unreadable/null collections are not treated as empty. Built-in system alarm texts and hardware parameter values are not covered engineering-source equality inputs. This is a named bounded inventory, not a claim to discover every future Openness object class.

JSON primitive types are checked before deserialization; serializers may not coerce numbers or strings into different field types. The manifest syntax schema requires emitted nullable members explicitly. `roundtrip-descriptors-v1.schema.json` covers descriptor syntax for both ready and diagnostic exports; cross-file relationships, exact profile support, XML cardinality and filesystem safety remain runtime gates. Schema validity alone is not build readiness.

Canonical XML must have unique root DocumentInfo/Created and unique Main Name/Number/ProgrammingLanguage fields. Additional top-level block objects are rejected. Unknown non-block content remains preserved and hash-significant.

The builder creates a fresh project in a destination-volume staging folder, uses the hardware descriptor's exact `createTypeIdentifier` with `CreateWithItem`, recreates tag tables/tags, imports canonical SimaticML, saves and compiles the PLC. The new project folder is published to the requested unused output path only after zero compile errors. It never reads the original source `.ap21` or downloads/writes to a PLC.

The imported XML is a private copy of the exact bytes validated at preflight. It is hash-checked and held with a read-only sharing handle during import, so later edits to the supplied source.xml do not change the build. Owned cleanup rejects reparse-point ancestors/children. These path checks are not a sandbox against a hostile same-account process racing directory replacement; use an exclusively owned workspace.

One observed system default is implicit: a single `Force table` with zero entries, created automatically by the fresh V21 CPU. Its name and entry count are checked; additional/renamed tables or any force entries block readiness. This does not permit online forcing.

## Verify boundary

`verify <original.ap21> <rebuilt.ap21>` opens two separate owned offline copies. It actively compiles the rebuilt copy and requires zero errors, then exports each project through this same v1 contract. Both complete trees must pass the builder's strict descriptor, capability, path, and artifact-hash validation before comparison. An invalid or incomplete capture is `blocked`, never an empty PASS.

The bounded comparison includes project name and V21, station identity and exact CPU create identifier, PLC identity/name, root tag tables and tags (name, data type, raw address and single-comment text), Main/OB1/LAD identity, and the SHA-256 of the full canonical SimaticML artifact. The hash includes all unknown XML nodes; only the versioned `simaticml-v1` normalization of root `/Document/DocumentInfo/Created` is excluded. Project version, binary bytes, capture time, local paths, PIDs, and TIA runtime object IDs are not equality inputs.

The result reports `pass`, `mismatch`, or `blocked`, with stable object/field differences and observed rebuilt-copy compile counts. It does not include project file paths or raw engineering source text. This is engineering-source equivalence for the stated v0.1 subset, not runtime or arbitrary-project equivalence.
