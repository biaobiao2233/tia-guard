# Contributing

TIA-Guard is currently pre-alpha.

Development principles:

1. Keep Siemens-specific API access inside TiaGuard.Openness.
2. Keep deterministic findings separate from AI-generated advice.
3. Do not commit Siemens DLLs, TIA project binaries, licenses, or proprietary sample projects.
4. v0.1 stays read-only toward TIA engineering projects and controllers.
5. New checks need focused tests using sanitized Snapshot fixtures.
