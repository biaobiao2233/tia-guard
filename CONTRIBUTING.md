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
