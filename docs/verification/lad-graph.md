# Local LAD graph candidate verification — 2026-09-29

This is implementation evidence for `feat/ai-readable-engineering-view`, based on
`2becf72ecc8db72cc91ce8c32ac5572cbef880e4`. It is not main-branch acceptance or a release.
It supersedes the [historical inventory-only receipt](ai-engineering-view.md).
No Bridge worktree, production project, online PLC, permissions or credentials were modified.
No push, PR, merge or remote tracker write is part of this delivery.

## Final contract and proof

The [v2 schema](../contracts/ai-engineering-v2.schema.json) embeds `lad-evidence-v1` graphs,
all-or-nothing network analysis and guarded block relationships. Exact mappings and limits
are specified in [LAD-GRAPH](../LAD-GRAPH.md). Build/Verify and canonical validation code are unchanged.

The owned V21 re-export in `examples/lad-v21-fixture/tia-source` has six Bool tags and three
networks, on the already bounded CPU `OrderNumber:6ES7 212-1AE40-0XB0/V4.7`.
Its source XML SHA-256 is:
`591f981232f20ad268887625819506445cf2cc35bfcd1e3155dc052cd3b37e46`.

Derived assignments match topology and all 16 combinations of the four relevant input/read values:

- ForwardOut = StartForward AND NOT ReverseOut.
- ReverseOut = StartReverse AND NOT ForwardOut.
- Independent rail branches: BranchA = StartForward; BranchB = StartReverse.

Exactly one reciprocal `mutual-output-inhibit` has two network/read/write-node evidence records.
It is a sequential source dependency, not a simultaneous equation or safety proof.

## Automated and artifact gates

- 136/136 contract tests pass, 0 failed/skipped: existing 99 plus 37 graph-focused cases.
- Snapshot/canonical schemas, historical AI v1 and new AI v2 fixture validate; 17 total negative
  schema cases reject as expected (5 canonical, 5 AI v1, 7 AI v2).
- CLI, GUI and Smoke Release builds: 0 errors, 0 warnings. Smoke `self-test`: exit 0.
- Same source regenerated repeatedly and after deleting an owned derived directory: all four
  files byte-identical. All six original canonical files remain byte-identical.
- 23 JSON/XML source references and 42 graph node/wire selectors resolve to the actual source.
- No raw XML, base64 or machine-path leakage in the inspected generated view.
- Local Windows package contains only application files; packaged CLI repairs corrupt AI output
  to the same final bytes. No Siemens DLLs or project binaries are redistributed.
- Repository fixture has scoped Git attributes to preserve hashed source bytes and derived LF.
  A fresh index checkout validates canonical hashes and regenerates exactly the same AI bytes.
- Final staged `git diff --check`: PASS, including the preserved CRLF export fixture.

The generated view is 43,548 bytes total: JSON 38,790; PROJECT.md 1,951; symbols.md 657;
OB1.md 2,150. Graph evidence increases JSON size; the reading layer remains concise.
No claim is made about comparative LLM benchmark scores.

## Actual TIA V21 runtime gates

All operations used owned disposable copies and final production implementation assemblies.

1. With `ai/` absent, canonical Build created a fresh nonempty project, imported OB1/tags,
   saved and compiled with 0 errors / 0 warnings.
2. CLI Verify compared an owned copy of the original nonempty reference with the rebuilt
   project: `pass`, no differences, rebuilt compile 0 errors / 0 warnings.
3. With malformed JSON and fabricated Markdown in sibling `ai/`, `VerifySourceAgainstProject`
   returned `pass`, no differences, rebuilt compile 0 errors / 0 warnings.

The direct Build spent several minutes starting/creating through TIA before completing normally;
no permission change or forced termination was needed. Verify used the existing local Openness
host to load the final assembly. `.ap21` file identity alone is not treated as a program-content
proof; canonical semantic comparison and compile receipts establish the reported result.

## External corpus — local copies only

| Repository / exact HEAD | Actual work | Observed result |
| --- | --- | --- |
| s7-1200-lad-labs / `7bd90ae77747c824c4ed37dc58485117f5546406` | MIT Lab1.1 and Lab1.3 `.zap16` copied, RetrieveWithUpgrade in V21, compile/export | Both retrieve successfully; each compiles 0 errors / 0 warnings. Actual CPU 1211C V4.4 plus two HMI devices; whole-project export not round-trip ready. 4 + 5 instruction-bearing networks: no complete expressions. |
| practicalseries-pal / `e46b222ed9fb736456ccda40c4aa33af6c143b32` | MIT (`LICENCE.md`), 55 XML files copied for pure parser inspection | 239 instruction-bearing NetworkSource elements retain FlgNet/v4; all reject with `FLGNET_VERSION_UNSUPPORTED`. No TIA upgrade claimed. |
| sorting-cell-s7-1200 / `aa71457dcfa5d81d1699cf83461041a56a4283f9` | No confirmed license; local disposable `.zap21` Retrieve, compile, nine block exports | Actual CPU is **1211C V4.7**, not the initially expected 1214C. Compile 0 errors / 1 warning. Whole-project source blocked by CPU identity, extra block/types and capture incompleteness. 16 instruction-bearing networks: no complete expressions. |
| packt-s7-1200-advanced / `a48736dbfbdc95b15abd0e164599e88ed277cf08` | MIT license/archive inventory only | No upgrade, compile or parser coverage claimed. |
| plc-pid-level-control / `46a03f5a2d827c568aff890aa15e98479d7826d1` | License not confirmed; `.ap15` inventory only | No upgrade, compile or parser coverage claimed. |

Research calls the exact pure extractor against copied/exported XML. Synthetic unique Bool
declarations are supplied only to avoid missing-tag rejection masking structural blind spots;
they are not evidence of real operands or project support. All 264 evaluated nonempty networks
reject before producing writes. Product `ai-view` separately rejects all three retrieved
whole-project canonical trees with exit 2, creating no `ai/` output. Original corpus Git trees
remain clean; no third-party raw program source is committed or redistributed.

First Lab1 retrieve attempt failed on the harness destination-directory argument. Supplying an
absolute DirectoryInfo with a trailing separator under an owned short path fixed that harness
issue; it was not a V16 compatibility rejection. Final counts above use the successful runs.

## Adversarial findings and limits

Unknown instructions, namespaces, template/content shapes, dangling/duplicate connections,
joins, cycles, missing operands, aliases, duplicate writers, unknown wrappers and partial third
networks never emit complete expressions/interlocks. The implementation review tightened a
missing `CompositionName="CompileUnits"` case so an unrecognized wrapper cannot be promoted to
fully supported logic. A status-propagation regression in early graph failure handling was fixed;
existing inventory-only unsupported cases retain their expected status. A test-only XML whitespace
selection error was also repaired. Scoped Git attributes fix cross-checkout byte-digest risk.

No known false-complete semantics remains within this tested subset. This is bounded evidence,
not a proof for arbitrary Siemens XML. OR joins, stateful parts, other firmware/CPU profiles,
HMI and older FlgNet versions remain unsupported. The next evidence gate is an owned V21 OR/`O`
reconvergence fixture with topology and truth-table tests. GUI auto-generation and Bridge/MCP
integration are separate work and are not started here.
