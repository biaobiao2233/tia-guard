# TIA-Guard Round-trip Source v1

Status: **draft implementation contract for Core #14**

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

## tia-guard.json

The manifest records:

- `schemaVersion = "1.0"`
- `contractStatus = "draft"`
- `tiaVersion` (v0.1 requires V21)
- project name/version
- `roundTripReady`
- repository-relative hardware and PLC descriptor paths
- a stable, sorted capability ledger
- stable blocking diagnostics

No operational capture metadata is permitted.

## Hardware descriptor

`tia/hardware/<station-id>.json` records the supported root station and the build-grade CPU identity.

The critical field is `createTypeIdentifier`: it is read directly from the CPU-classified `DeviceItem.TypeIdentifier` and is later passed to the Openness `CreateWithItem` flow. TIA-Guard never manufactures an order number or firmware suffix from the generic station `System:Device.*` identifier. The v0.1 S7-1200 subset requires an `OrderNumber:...` CPU TypeIdentifier.

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

## Block descriptor and source.xml

v0.1 supports exactly one `Main` / OB1 block in LAD.

`block.json` records block identity, number, language, capability and a repository-relative source artifact reference.

`source.xml` is the full Siemens Openness SimaticML export. TIA-Guard does not invent a LAD DSL in v0.1. Unknown XML nodes are retained. The descriptor records a SHA-256 over the exact canonical artifact bytes.

If the block is protected, export fails, or the artifact hash cannot be verified, the block is not `supported-round-trip`.

Canonical SimaticML uses normalizer `simaticml-v1`. For TIA Portal V21, the only currently classified volatile XML field is `/Document/DocumentInfo/Created`: its value is normalized to `1970-01-01T00:00:00Z`. The exporter first verifies the SHA-256 of the untouched Siemens export against Snapshot evidence, then changes only this proven non-semantic value. Any unexpected `DocumentInfo/Created` shape fails closed; unknown XML nodes are never removed or rewritten. The block descriptor SHA-256 is over the resulting canonical `source.xml`.


Siemens requires a program block to be consistent before it can be exported. TIA-Guard therefore performs a preflight read first. If the supported OB1/LAD block is not consistent, `export` may compile **only the TIA-Guard-owned offline/disposable copy** before exporting SimaticML. It never compiles an attached user project. The canonical source project remains read-only and the disposable copy is destroyed after export.

## Determinism

Two exports of an unchanged project must produce:

- the same canonical relative file set;
- byte-identical canonical JSON;
- identical `source.xml` hashes.

If Siemens export contains proven volatile fields, a future versioned normalizer may remove only explicitly classified non-semantic fields. v0.1 must not silently discard unknown XML.

## Build boundary inherited by the next slice

The future builder must validate this tree and all hashes first, then fail closed unless `roundTripReady=true`. It will create only a fresh disposable V21 project; it must never download/write to a PLC.
