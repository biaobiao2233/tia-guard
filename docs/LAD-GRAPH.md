# Bounded LAD evidence and derived analysis

The current output contract is [ai-engineering-v2](contracts/ai-engineering-v2.schema.json).
Each ordered network has `id`, `ordinal`, metadata and `sourceRef`, plus nullable `graph` and
`analysis`. `graph.schemaVersion` is `lad-evidence-v1`. The historical v1 schema remains for old
inventory-only artifacts; new generation emits v2. Consumers must check versions and coverage.

## Evidence mapping

| V21 FlgNet/v5 source | Evidence representation | Derived use only after full validation |
| --- | --- | --- |
| `Access Scope="GlobalVariable" / Symbol / Component` | `access` node with scope and symbol | Resolves to exactly one declared Bool tag/address |
| `Part Name="Contact"` | `instruction` node, instruction `Contact` | Read and a path condition |
| `Part Name="Coil"` | `instruction` node, instruction `Coil` | Ordinary assignment target |
| `Negated Name="operand"` | `negated: true` on the part | Contact condition becomes `not(read)`; negated coil rejected |
| `Powerrail` | One network-local `powerrail` node | Single flow driver; selectors can locate multiple rail occurrences |
| `Wire` | Hyperedge preserving every endpoint, source UID and selector | Operand binding or directed flow, never inferred from XML order |
| `IdentCon UId` | Endpoint referencing an access node | One side of an operand wire |
| `NameCon UId Name` | Endpoint referencing instruction and exact port | `operand` binding, `in` sink, or contact `out` driver |

`sourceRef` locates a canonical JSON object or XML compile unit. Node/wire `sourceSelector` is
an XPath evaluated **relative to that network's compile unit**, not relative to the document.
The numeric Siemens UID is retained for traceability. Local IDs hash instruction, operand and
two rounds of neighboring topology signatures, with sorted output and occurrence suffixes.
Renumbering UIDs or reordering XML does not change IDs/analysis in the evidenced fixture.
Structurally indistinguishable duplicates use source UID as a final occurrence tie-break;
these are deterministic local identities, not persistent edit IDs or graph-isomorphism proofs.
Source selectors honestly change when Siemens renumbers objects. Network identity uses ordinal
because execution order matters; inserting a network changes later ordinal IDs.

## Two separate layers

`graph` contains source observations: nodes, wire endpoints and traceability. `complete` means
the entire recognized graph shape was read. It does not mean the instructions are understood.
Unknown shapes produce a `partial` evidence graph plus diagnostic codes. Raw XML, icon payloads,
framework blobs and arbitrary unsupported element content are never copied into the IR.

`analysis` contains inferred reads, writes, directed flows, node paths and Boolean ASTs.
`supported` requires the whole network to pass. Any unknown instruction, operand, port,
unbound/dangling connection, duplicate connection, join, cycle, unreachable part, unused access,
feedback or overlapping intra-network write clears **all** reads/writes/flows. Diagnostics remain.
No plausible partial expression is emitted. If the compile-unit wrapper is unrecognized,
graph/analysis are null and network logic coverage explains that it is unsupported/incomplete.

AST operators currently are `read`, `not` and `and`; `or` is intentionally absent. Each write
includes its operand-wire ID and full rail-to-coil node path. Unit tests assert topology,
bindings, ports and truth tables; generated prose is not the semantic oracle.

## Serial and parallel proof

Each signal wire must have one driver and one or more `in` sinks; each instruction has exactly
one incoming flow. A coil is traced backwards to the rail, with cycle detection. Contacts along
that path form a conjunction in flow order. All instructions must belong to proven coil paths.

The owned V21 fixture proves serial NO/NC contacts and two **independent** parallel assignments:

```text
Network 1: ForwardOut = StartForward AND NOT ReverseOut
Network 2: ReverseOut = StartReverse AND NOT ForwardOut
Network 3: BranchA = StartForward; BranchB = StartReverse
```

Network 3's rail wire has three endpoints: rail plus two contact inputs. The two branches remain
separate paths and writes. It is not an OR reconvergence. Multiple output drivers on one wire,
`O` parts and all reconverging joins remain unsupported until an owned V21 re-export proves them.

These assignments describe values **at each network's execution**. Networks execute in ordinal
order; the two reversing expressions are not simultaneous equations. The later network reads
the value produced by the earlier network when they share an output symbol.

## Cross-network relationship gate

`block.relationships` is derived analysis. `mutual-output-inhibit` requires two distinct fully
supported networks, unique writes to two Q outputs, reciprocal mandatory negated reads on each
writer's exact path, complete block network inventory and no unanalyzed network. Duplicate
writers and declared address aliases (including overlapping wider addresses) suppress the result.
Evidence names the two network IDs, read nodes, write nodes and canonical compile units.

This proves a source-level reciprocal inhibit pattern, not hardware interlocking, simultaneous
output safety, online behavior, scan initial state, or control-system correctness. A complete
relationship analysis with an empty list means this bounded pattern was not found, not that no
other kind of relationship exists.

## Unsupported and authority boundaries

SET/RESET, negated coils, edge contacts, TON/TOF/TP, CTU/CTUD, arithmetic, Move, calls, FC/FB/DB,
local/compound operands, direct addresses, constants, unconditional coils and unknown namespaces
are not semantically supported. FlgNet/v4 corpus is rejected without rewriting its namespace.
Limits are 128 part/access nodes, 256 wires and 128 endpoints per wire. No product hardware,
firmware, locale, HMI or TIA version acceptance is expanded by this parser.

The product entry remains validated `tia-source` -> renderer -> replaceable `ai/`. Build and
Verify consume canonical source only. Research may call the pure extractor on disposable exported
blocks to study rejection, but that cannot turn an unsupported project into accepted build input.
Future Bridge tools can consume the versioned JSON and source references; this implementation
has no source dependency on a Bridge, MCP service or Gateway.

The next useful extension is a real V21 `O`/OR reconvergence fixture and proof rules, followed
by truth-table and ambiguity tests. Stateful instructions need a separate state/scan contract.
