# Architecture

TIA-Guard is a bounded engineering-source round-trip prototype for TIA Portal V21.
It does not replace Siemens engineering tools or establish control-logic correctness.

## Mainline path

1. Export an existing project through an owned offline copy to canonical JSON and full SimaticML.
2. Validate the complete source tree, object capabilities, paths, JSON types and XML identity.
3. Build a fresh project using the evidenced CPU identity, root tags and Main/OB1/LAD.
4. Save and compile the fresh PLC software; publish only after zero compile errors.
5. Verify two owned project copies by actively compiling the rebuilt copy, exporting both, validating both complete trees and comparing covered engineering-source fields.

`tia-guard build` is the product CLI entry. Export, Snapshot and Verify are library/Smoke-harness entry points. Snapshot is intermediate evidence; it is not the canonical build input. There is no original `.ap21` argument in the builder.

## Responsibilities

- `SnapshotExtractor`: Siemens traversal and explicit incomplete/unsupported diagnostics. User constants, types, external sources, technology objects, watch/force tables, user alarm text lists and user folders cannot silently disappear as empty collections.
- `RoundTripSourceExporter`: offline-copy export preparation, exact CPU/topology observations and completed tag-table scans.
- `RoundTripSourceMaterializer`: deterministic JSON and preserved XML; a ready tree must pass `RoundTripBuildInput.LoadSource` before publication.
- `RoundTripBuildInput` / `RoundTripProfile`: shared ready profile, descriptor relationships, primitive JSON types, capability inventory, exact file set, path safety, hash and XML checks. The CPU allowlist currently contains only `6ES7 212-1AE40-0XB0/V4.7`.
- `RoundTripBuilder`: fresh disposable project, locked private copy of preflight-validated XML, reconstruction, save/compile and publication.
- `RoundTripVerifier.Compare`: pure engineering-source comparison; `RoundTripVerifier` supplies the separate live project/copy/compile lifecycle. Invalid sources are blocked, unequal covered fields mismatch, and only valid equal sources pass.
- `FileSystemSafety`: refuse reparse-point traversal and redirected cleanup; individual callers retain ownership checks.

## Meaning and limits of equality

Covered fields are project name/V21, station/PLC identity, CPU create identifier, root tables and primitive tags with matching-width raw I/Q/M addresses and single comment text, Main/OB1/LAD identity and the complete canonical SimaticML digest.

The SimaticML normalizer changes only the validated root `DocumentInfo/Created` field. Unknown XML remains comparison-significant. Hash validity is necessary but does not replace object shape validation. Descriptor schemas describe syntax; cross-file consistency and the supported profile are runtime checks.

This does not cover hardware IP/parameter configuration, built-in system alarm text, language identity of a single comment, runtime behavior, binary project equality, HMI, Safety, drives, multiple PLCs or arbitrary Openness content. User folders and the named unsupported collections above block readiness. Future object classes require explicit discovery and evidence before any support claim.

The historical real acceptance is one self-authored CPU/profile with Chinese-locale integrated item names and an empty tag table. Populated primitive tags have synthetic contract coverage. The topology check is intentionally specific; different firmware, locale or devices require a new evidence gate.

A fresh V21 CPU automatically contains one empty `Force table`. Only this observed name with zero entries is an implicit default; additional/renamed force tables or any force entries are rejected. No force operation is ever executed.

## Safety and privacy

Original projects and user Portal sessions remain untouched. Build writes only its new output; Export/Verify work on complete owned offline copies. Compile is local PLC-software compilation, never PLC download or online control. Permission failures remain blocked.

Source XML is frozen at validation and copied into a private stage for import with a held read-only sharing handle. Source/output overlap, unsafe path components and reparse points are rejected. Cleanup checks parent/GUID ownership and refuses redirected ancestors/children. Exclusive workspace ownership is still required; this is not a hostile-process sandbox.

Operational paths may appear in local CLI output. Canonical output does not add local runtime paths/PIDs, but preserves engineering comments/XML; content containing customer data is not automatically anonymized. Main has no AI/network review pipeline. Siemens assemblies resolve from the user's local installation and are not redistributed.

## Evidence and tests

The pure net48 xUnit project compiles the exact production source boundary without Siemens dependencies. Windows CI runs those tests and JSON Schema fixtures. The local Smoke harness checks the installed V21 boundary and integration behavior. Real creation/import/compile/export evidence must remain separately identified, with disposable-resource ownership and source revision recorded.

Pure test success does not establish runtime compatibility. A historic accepted commit is not automatic acceptance of a changed implementation, nor does a technical review under one GitHub account constitute approval by another account.

## Supporting work

Doctor/rules, Finding-based Markdown/JSON/SARIF and optional advisory AI exist as separate unmerged proposals. They need current-contract reconciliation and their own tests. GitHub SARIF upload/display behavior and AI privacy/value gates are not fulfilled by core source comparison. They do not block repairing core truth boundaries and are not present mainline features.
