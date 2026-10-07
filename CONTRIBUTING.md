# Contributing

TIA-Guard is currently pre-alpha.

Development principles:

1. Keep Siemens-specific API access inside TiaGuard.Openness.
2. Keep deterministic findings separate from AI-generated advice.
3. Do not commit Siemens DLLs, TIA project binaries, licenses, or proprietary sample projects.
4. Original engineering projects and PLCs remain untouched. Explicit Build creates a new owned offline project; Export/Verify may compile only owned disposable copies.
5. New checks need focused tests using sanitized Snapshot fixtures.
6. Run the pure contract suite and schema checks in README; changes to Openness calls additionally need disposable V21 integration evidence. Never substitute a passing pure test for that evidence.
7. Do not commit test results, raw integration exports, machine paths, private projects, or proprietary dependency binaries.

## PLC platform adapters

Community contributions for S7-1500, Beckhoff TwinCAT, Mitsubishi and other PLC engineering platforms are welcome. These are future directions; the current supported scope remains the V21 / S7-1200 subset documented in the paired READMEs.

An adapter may initially support only read-only parsing, export or Git management. Keep vendor-specific APIs and project formats within the corresponding adapter boundary, following the existing separation of Siemens APIs in `TiaGuard.Openness`. A shared adapter interface is a future design decision; agree on any shared contract changes before implementing them.

For an adapter PR:

1. Identify the controller family, engineering software version, environment requirements and supported project objects. Declare each supported capability separately, together with unsupported objects and known limitations.
2. Provide self-authored or sanitized fixtures, reproducible steps and focused positive and negative tests. Unsupported or incomplete content must remain explicit and cannot produce a false PASS. Preserve original engineering projects and use owned disposable copies for verification.
3. Link a sanitized verification summary bound to the exact candidate and platform version. Record actual outcomes, failures and untested cases; retain raw logs and engineering artifacts outside this source repository under the existing data policy. Read/export claims need actual platform evidence; rebuild claims need compilation and round-trip verification in that platform's engineering tool. Runtime claims additionally need real execution or simulation evidence. Pure tests alone do not establish platform support.
4. Update the Chinese and English READMEs with the exact verified scope and remaining gaps. Planned or untested capabilities must remain labeled accordingly. Follow the existing rules on proprietary dependencies, project data and credentials.

Passing applicable tests and maintainer review are required before merge. A partial adapter can be considered when its limited scope is useful, explicit and verified; submitting a PR does not automatically make a platform supported or released.
