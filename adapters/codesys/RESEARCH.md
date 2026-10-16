# CODESYS capability and reuse investigation — 2026-10-15

Base verified live: TIA-Guard main `ed6a7bfe04d0fc90c718d4eda12335e4681d47c1`,
PR #62 merged. Dedicated task [#63](https://github.com/biaobiao2233/tia-guard/issues/63).
The older local TIA checkout and its untracked files were left untouched. Memo's
Siemens-Sales100 Handoff is explicitly superseded; it is not current runtime evidence.

## Official MCP

[Product data sheet](https://store.codesys.com/media/n98_media_assets/files/2101000023/4/CODESYS%20Development%20System%20MCP%20Server_en.pdf)
requires Development System 3.5.22.1 or higher; its detailed compatibility table
lists 1.0.0.0 for 3.5.22.10 onward. Local 3.5.22.40 meets both minimums, but this
does not establish installation, activation or compatibility of every future package.
The MCP needs a **PDE subscription**, distinct from the free base programming IDE.
Only one IDE can enable the official server at a time.

The [official tool reference](https://content.helpme-codesys.com/en/CODESYS%20Development%20System%20MCP%20Server/_idemcp_mcp_tools_and_ressources.html)
lists 20 tools: add_or_remove_library, add_or_remove_program_call_in_task,
browse_project_tree, check_for_errors, create_folder,
create_or_replace_structured_text_object, get_active_app_as_path,
get_active_path_and_selection, get_available_libraries_list,
get_detailed_library_documentation, get_device_and_io_configuration,
get_library_documentation, get_libraries_referenced_in_application,
get_structured_text_content, remove_object, rename_object,
replace_text_in_structured_text, search_in_files_by_regex,
search_libraries_for_type, search_path_by_glob.
(The names, not the count, are authoritative; future tool additions need discovery.)
`check_for_errors` defaults to precompile checking, with an option for full compile.
No project-create/open/close, download, Run/Stop or online read/write tool is listed.
The [overview](https://content.helpme-codesys.com/en/CODESYS%20Development%20System%20MCP%20Server/_idemcp_start_page.html)
describes stdio and access to the currently open project; ST is the writable language.
This is a documentation inventory, **not a live tools/list response**.

Local read-only file inspection found Scripting 4.2.0.0 and no MCP-named installed
plugin/license package under the selected installation. CodeMeter content listing
showed CODESYS protection/runtime/soft-license containers; no PDE entitlement was
confirmed. Do not infer an active PDE or Control Win license from an installed
container/service, and do not automatically buy/activate anything.

## Official ScriptEngine / command line / online / simulation

[Scripting](https://content.helpme-codesys.com/en/CODESYS%20Scripting/_script_scripting_with_codesys.html)
uses IronPython and exposes project, object, compiler and monitoring APIs.
[Official command line](https://content.helpme-codesys.com/en/CODESYS%20Scripting/_cds_starting_script_via_command_line.html)
supports `--profile`, `--runscript` and `--noUI`; this is sufficient transport for
an owned process, without an HTTP service. CODESYS uses its own Windows argument
parsing: quote the profile value explicitly, use no shell, and test the actual
installed profile. Ordinary generic argv quoting was rejected by this build.

[ScriptOnline](https://content.helpme-codesys.com/en/ScriptingEngine/ScriptOnline.html)
includes login/download, start/stop, read and prepared-value writes. `Never` means
no online change/full download, **not** no download. Pass `delete_foreign_apps=False`.
The adapter does not expose arbitrary ScriptEngine code or a real controller target.
Installed API stubs are useful version-specific references; their presence is not
execution evidence. The first native probe proved compilation and simulated counts
`DINT#20 -> DINT#40`; it did not establish adapter-level qualification.

[IDE simulation](https://content.helpme-codesys.com/en/CODESYS%20Development%20System/_cds_testing_in_simulation_mode.html)
supports testing without hardware. Login loads a simulated application. That
in-process simulator is the isolated first acceptance target. The installed
Control Win service was stopped and remains outside scope. Commercial
[Control Win SL](https://us.store.codesys.com/codesys-control-win-sl-1.html) has a
separate runtime license; its documentation describes a two-hour unlicensed demo.
This is not PDE, and IDE-simulation evidence does not qualify Control Win SL.

Native identity observation: `get_device_identification()` returns
`4096 / 0000 0001 / 3.5.22.40` with simulation disabled, and
`4097 / 0000 000a / 3.5.22.40` with simulation enabled, including after reopen.
The first strict identity-check candidate incorrectly expected the offline ID
in simulation and was rejected before compilation. Its native run was not PASS;
the owned process exited. The corrected guard checks the offline identity before
switching and requires the observed simulation identity thereafter.

## Open-source comparison (live GitHub API inspected)

| Candidate | Pin / license / maintenance | Reuse decision |
|---|---|---|
| [luke-harriman/Codesys-MCP](https://github.com/luke-harriman/Codesys-MCP) | `9a965c7e16cb49996de8d45596d6e15a27a39450`, MIT, not archived, latest push 2026-05-31 | Documents SP21+ headless-only; a useful official scripting/transport reference. Its headless executor falls back to exit code zero when no marker is returned and uses shell startup. Do not transplant that success/ownership policy. No upstream source copied. |
| [ArthurkaX/cds-text-sync](https://github.com/ArthurkaX/cds-text-sync) | `c6c450db3da8797abfddb29a733e8f274b340d9f`, MIT, not archived, latest push 2026-10-12 | Mature text/XML export/import and analysis work; useful for a subsequent full existing-project round trip. Existing-project sync and optional daemon are beyond this bounded new-project acceptance. No upstream source copied or installer executed. |

Reuse actual official compiler, ScriptEngine, ScriptOnline and simulation. Reuse
TIA-Guard's concepts: independent platform module, real SDK stdio registration,
default read-only host switch, frozen preview, one-use grants, revision checks,
owned child lifetime and explicit diagnostics/evidence. A shared platform ABI is
not introduced; existing TIA/TwinCAT product files are not modified. This adapter's
implementation is original project MIT code; vendor components stay installed
externally. MCP SDK versions match the merged TwinCAT adapter and are lockfile-pinned.

## Environment caveat

The native process emitted a hostfxr.dll load warning for a user-local .NET 10
runtime during probing. Native ST compile/IDE simulation still executed. No global
.NET setting was repaired or changed; document the warning and require behavioral
evidence rather than treating exit 0 as health. Future package features may still
need their own prerequisites. First observed free space: E about 8.2 GiB, C about
30.7 GiB; task caches and all generated projects/receipts are on E.
