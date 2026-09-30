# Historical inventory-only AI view verification

This first-stage receipt is superseded by [LAD graph verification](lad-graph.md) for the current
v2 implementation. The statements below describe the earlier empty-network test run only.

This is local implementation evidence, not integration acceptance or a release.
Base: `2becf72ecc8db72cc91ce8c32ac5572cbef880e4` (`feat/33-gui-v01`).
Candidate: uncommitted changes on isolated `feat/ai-readable-engineering-view`.
No push, PR, merge or GitHub tracker writes were performed.

## Design and scope

See [AI-READABILITY](../AI-READABILITY.md) for the independent alternatives review.
The implemented contract is typed JSON plus Markdown, including explicit ordered network
observations. There is no non-empty LAD graph or pseudo-code interpreter. The real demo's
empty network does not support a claim about contacts, coils, reversing conditions or interlocks.
GUI publish/staging remains unchanged; `ai-view` is an explicit local CLI operation.

## Automated gates

- Before implementation: 80/80 pure contract tests passed.
- Final candidate: 99/99 pure contract tests passed, zero skipped/failed.
- Canonical/Snapshot schemas plus AI schema and five AI negative cases: PASS.
- CLI, GUI and Smoke Release builds: zero errors, zero warnings.
- Smoke self-test: exit 0 with normal snapshot output.
- `git diff --check`: PASS.
- Packaged CLI: real view generation, regeneration and inspection: PASS.

New tests cover deterministic bytes across cultures, delete/regenerate, source immutability,
missing/corrupt views ignored by Build preflight and Verify comparison, legacy/multiple slots,
sibling isolation even with corrupt/unmanaged sibling AI files, output ownership/locks/reparse
protection, failed publication preservation, JSON parsing, ordinal sorting, source references,
markup/blob presentation, and incomplete/unsupported/unknown versus observed-empty logic.

## Real demo evidence

Input is the existing Openness-derived six-file export for
`S7-1200-Motor-Reversing-Control`, not a synthetic fixture or raw `.ap21` parser.
Canonical XML SHA-256:
`382e9a41898cdd73738690fda8b822d626b1852a51dfedea42858139de36d857`.

The reference `.ap21` is the existing previously rebuilt real demo in `builder-demo-output-2`.
Original user project files were not changed. New TIA operations used disposable outputs and
owned offline copies only.

- No `ai/`: actual Build compiled with 0 errors/0 warnings. CLI Verify against the reference
  demo returned `pass`, no differences, rebuilt compile 0 errors/0 warnings.
- Deliberately malformed JSON and fabricated Markdown in `ai/`: actual Build compiled with
  0 errors/0 warnings. `VerifySourceAgainstProject` against the selected canonical source
  returned `pass`, no differences, rebuilt compile 0 errors/0 warnings.
- Both rebuilt `.ap21` files equal the reference in size (151394 bytes) and SHA-256:
  `b0ca8738b321e15074ff48019a4cf53b0918914d5f9f1138a967e25c4f30caae`.
- Packaged renderer repaired the corrupt view, generated it repeatedly, then regenerated after
  deleting all of `ai/`: all four files byte-identical; six canonical files unchanged.
- Real JSON validates against the new schema; all nine JSON/XML source references resolve.
- Inspection questions confirm V21, station/CPU/PLC identity, Main/OB1/LAD, the empty default
  tag table, one observed empty network, and explicit unknown interlocks/output conditions.

The Build/CLI Verify runs preceded the final source-reference URI-escaping-only change in the
renderer. Final source-based Verify, all 99 tests, builds, Smoke and packaged rendering used the
final implementation. No canonical, Build, Verify or repository workflow source was changed.
Binary equality is evidence for this demo only, not a broader project/runtime guarantee.

## Next evidence gate

Obtain an owned V21/S7-1200 canonical export with real contacts, coils, branches and an interlock,
cross-check it against the TIA network display, then define a bounded graph IR and adversarial
fixtures before emitting output conditions. GUI automatic generation/staging is a separate
transactional integration gate. No PLC/profile expansion or free-form AI editing is authorized.
