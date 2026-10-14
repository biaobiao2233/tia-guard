"use strict";
// Derived from Edge Automation toolSchemas.js at 30d8e2938eeed1c41ee24765b469d13430ab0a32.
// Copyright (c) 2026 Edge Automation. MIT; see licenses/Edge-LICENSE.
// One source for discovery and validation. No arbitrary runtime/COM passthrough.
const z = require("zod/v4");
const name = z.string().min(1).max(4096).refine(v => !v.includes("\0"), "NUL is invalid");
const handle = z.string().min(1).max(128);
const previewRequest = z.strictObject({
  operation: z.enum(["build-generation", "compile", "start-simulation"]),
  inputDirectory: name,
  outputDirectory: name,
  projectPath: name.optional(),
  executionId: handle.optional(),
  probeSymbol: name.optional()
}).superRefine((v, ctx) => {
  if (v.operation === "compile" && !v.projectPath)
    ctx.addIssue({ code: "custom", path: ["projectPath"], message: "compile requires projectPath; the backend must compile an owned disposable copy" });
  if (v.operation === "start-simulation") {
    for (const key of ["projectPath", "executionId", "probeSymbol"])
      if (!v[key]) ctx.addIssue({ code: "custom", path: [key], message: "simulation requires an owned published execution and changing UDINT probe" });
  } else if (v.executionId || v.probeSymbol) {
    ctx.addIssue({ code: "custom", message: "executionId/probeSymbol are simulation-only" });
  }
});
const simulation = { simulationId: handle };
const scalar = { ...simulation, symbol: name, type: z.enum(["BOOL", "INT", "UINT", "DINT", "UDINT", "REAL", "LREAL"]) };
const definitions = {
  doctor_twincat: {
    action: "doctor", readOnly: true,
    description: "Inspect environment and report backend capabilities as supported, unavailable or untested. Registration/transport success is not a functional probe.",
    inputSchema: z.strictObject({})
  },
  inspect_twincat_project: {
    action: "inspect", readOnly: true,
    description: "Read the specified project only; never attach to a user's IDE or modify/build the input.",
    inputSchema: z.strictObject({ projectPath: name })
  },
  preview_twincat_generation: {
    action: "preview", readOnly: true,
    description: "Freeze and preview one offline operation without mutation. New output and worker-owned scratch are checked by the backend. Compile uses a disposable copy. Editing ST invalidates the preview.",
    inputSchema: previewRequest
  },
  authorize_twincat_preview: {
    action: "authorize", readOnly: false,
    description: "Request a single-use, expiring backend grant for the reviewed preview. Requires explicit host offline-write mode. Returns an opaque local grant handle, never the worker token.",
    inputSchema: z.strictObject({ previewId: handle })
  },
  build_twincat_generation: {
    action: "build-generation", readOnly: false,
    description: "Execute the exact authorized preview. Backend rechecks hash, input/output, provider/session, expiry and single-use grant. Requires compilation and independent verification before publication.",
    inputSchema: z.strictObject({ previewId: handle, grantId: handle })
  },
  compile_twincat_project: {
    action: "compile", readOnly: false,
    description: "Compile only the owned disposable copy bound to the preview/grant. Full diagnostics and build initiation/completion are mandatory. Never compile user input in place.",
    inputSchema: z.strictObject({ previewId: handle, grantId: handle })
  },
  start_twincat_simulation: {
    action: "start-simulation", readOnly: false,
    description: "Compile, verify and activate an owned disposable PLC-only project on a freshly created local Usermode Runtime. Requires explicit host simulation mode, reviewed preview and single-use grant. Actual changing UDINT feedback is mandatory.",
    inputSchema: z.strictObject({ previewId: handle, grantId: handle })
  },
  prepare_twincat_simulation: {
    action: "prepare-simulation", readOnly: false,
    description: "Consume a start-simulation preview/grant to compile, verify and deploy a new owned PLC-only instance, then show its official License node for the user. Preparation is not execution success. No license generation or copying; resume the SAME handle after the user handles official licensing.",
    inputSchema: z.strictObject({ previewId: handle, grantId: handle })
  },
  resume_twincat_simulation: {
    action: "resume-simulation", readOnly: false,
    description: "Continue this session's prepared instance after the user handles official licensing. Recompile and independently verify the owned copy, then require actual Run states, application/project/port identity and a changing UDINT probe. Cannot attach to an existing/foreign Runtime.",
    inputSchema: z.strictObject(simulation)
  },
  read_twincat_simulation: {
    action: "simulation-status", readOnly: true,
    description: "Read actual ADS system/PLC states of this session's owned simulation.",
    inputSchema: z.strictObject(simulation)
  },
  set_twincat_simulation_state: {
    action: "simulation-state", readOnly: false,
    description: "Request and observe PLC Run/Stop only on this session's owned software simulation.",
    inputSchema: z.strictObject({ ...simulation, state: z.enum(["Run", "Stop"]) })
  },
  read_twincat_symbol: {
    action: "simulation-read", readOnly: true,
    description: "Read an actual typed scalar rooted in the frozen PLC source. No arbitrary ADS address or remote target.",
    inputSchema: z.strictObject(scalar)
  },
  write_twincat_symbol: {
    action: "simulation-write", readOnly: false,
    description: "Write and read back an explicitly declared scalar GVL variable on the owned simulation. Program outputs, pointers, hardware and special symbols are protected.",
    inputSchema: z.strictObject({ ...scalar, value: z.union([z.boolean(), z.number().finite()]) })
  },
  stop_twincat_simulation: {
    action: "stop-simulation", readOnly: false,
    description: "Stop only the Runtime process created by this session and confirm its exit.",
    inputSchema: z.strictObject(simulation)
  },
  verify_twincat_project: {
    action: "verify", readOnly: false,
    description: "Independently reopen/read back an owned project from a prior execution; compare with its frozen input. Unknown/partial evidence is an error. Requires explicit offline mode because native reopen/save can write the owned copy.",
    inputSchema: z.strictObject({ executionId: handle })
  }
};
const toolSchemas = Object.freeze(definitions);
module.exports = { toolSchemas };
