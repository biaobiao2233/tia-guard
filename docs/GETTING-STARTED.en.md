# Getting started

[中文](GETTING-STARTED.md) | **English**

[Home](https://github.com/biaobiao2233/tia-guard/blob/main/README.en.md) · [AI Gateway](TIA-AI-BRIDGE.en.md)

## Download and prerequisites

Download `tia-guard-v0.1.0-prealpha.1-windows-x64.zip` from [Releases](https://github.com/biaobiao2233/tia-guard/releases). This experimental preview is intended for your own offline project copies. Read the verification scope on the same release page first.

Requirements: Windows x64, .NET Framework 4.8, installed TIA Portal V21 and Openness, Git for Windows, and effective **Siemens TIA Openness** group membership in the current logon token. The Gateway includes its .NET 8 runtime. Siemens DLLs, licenses and TIA installers are not included.

1. Extract the entire ZIP to a short path such as `C:\TiaGuard`. Do not copy only the EXE.
2. Run `.\tia-guard.exe doctor` from that directory to check local prerequisites.
3. Double-click `TiaGuard.exe`. The GUI is currently Chinese; this guide provides language links.
4. If TIA presents an official access prompt on the first Openness operation, verify the application and follow Siemens' access process.

Check the downloaded ZIP's SHA-256 against the release's `SHA256SUMS.txt`:

```powershell
Get-FileHash .\tia-guard-v0.1.0-prealpha.1-windows-x64.zip -Algorithm SHA256
```

## GitHub → TIA

Enter a Git repository URL containing TIA-Guard canonical source → clone/pull → choose a project → choose a new output directory → rebuild, compile and verify supported semantics. A repository containing only an `.ap21` is not canonical source.

Both `tia-projects/<slot>/tia-source/` and legacy `tia-source/` layouts are supported. Use a new output directory; the original is not overwritten. A semantic PASS does not require identical binary files.

## TIA → GitHub

Choose your own `.ap21` → enter a Git repository URL you can write to → select or add a project slot → export and validate → commit/push. The application reuses system Git Credential Manager or SSH. First ensure ordinary Git can access the target repository.

Publication updates only the selected slot's `tia-source/`. Generate the derived `ai/` separately with `ai-view`; the GUI does not currently commit it automatically. Review names, comments and XML for sensitive information before making engineering source public.

## AI agents

The GUI starts a local Gateway at `127.0.0.1:18761`. The companion `skill.zip` contains `tia-guard-gateway/SKILL.md` for local agents; installation depends on the agent's Skill support. A Skill alone does not create a cloud-to-local network connection.

Agents first inspect capabilities and OpenAPI. HTTP engineering edits require an exact preview, one-time GUI approval and subsequent verification. See the [Gateway guide](TIA-AI-BRIDGE.en.md).

## Troubleshooting

| Symptom | Action |
| --- | --- |
| Missing Openness permissions | Check group membership; log out and back in after changes, then run doctor |
| TIA / PublicAPI not found | Check the V21/Openness installation; do not copy Siemens DLLs from elsewhere into the application folder |
| Git authentication failure | Authenticate with ordinary Git and check target repository permissions |
| Port occupied | Check for an existing TIA-Guard/Gateway instance; do not terminate unrelated programs |
| Verify returns blocked | Read diagnostics; unsupported content or incomplete capture cannot count as passing |
| TIA path error | Use short application/output directories and inspect the exact diagnostic |

## Current scope

Only the verified V21/S7-1200 single-PLC subset, bounded Main OB1 LAD and root tag tables are covered. No online PLC operations are exposed. Historical real TIA evidence belongs to its named revisions; the latest integrated GUI/Gateway engineering workflow still needs separate qualification. This preview is not a stable production tool.
