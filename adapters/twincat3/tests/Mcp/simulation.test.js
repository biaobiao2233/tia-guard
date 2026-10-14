"use strict";
const test = require("node:test");
const assert = require("node:assert/strict");
const path = require("node:path");
const { mcpRoot } = require("./support.js");
const { ProviderClient } = require(path.join(mcpRoot, "providerClient.js"));
const { toolSchemas } = require(path.join(mcpRoot, "toolSchemas.js"));
const { isFailure } = require(path.join(mcpRoot, "result.js"));

test("offline authorization alone cannot dispatch runtime operations", async () => {
  let contacted = false;
  const provider = new ProviderClient({ offlineWrites: true, transport: { call() { contacted = true; }, close() {} } });
  for (const action of ["start-simulation", "simulation-status", "simulation-state", "simulation-read", "simulation-write", "stop-simulation"])
    await assert.rejects(provider.dispatch(action, { simulationId: "foreign" }), { code: "simulation-mode-required" });
  assert.equal(contacted, false);
});

test("runtime schemas reject arbitrary ADS targets, PIDs, raw addresses and incomplete previews", () => {
  for (const [key, value] of Object.entries({ netId: "1.2.3.4.5.6", processId: 123, indexGroup: 1, port: 851 }))
    assert.equal(toolSchemas.read_twincat_simulation.inputSchema.safeParse({ simulationId: "owned", [key]: value }).success, false);
  assert.equal(toolSchemas.preview_twincat_generation.inputSchema.safeParse({ operation: "start-simulation", inputDirectory: "input", outputDirectory: "output" }).success, false);
  assert.equal(toolSchemas.write_twincat_symbol.inputSchema.safeParse({ simulationId: "owned", symbol: "GVL.Command", type: "LREAL", value: Infinity }).success, false);
});

test("startup transport success and a static fake counter cannot qualify native execution", () => {
  const receipt = { success: true, failures: [], cleanup: [], simulationId: "fake", state: { systemState: "Run", plcState: "Run" }, probe: { before: 1, after: 1, nativeExecutionObserved: true } };
  assert.equal(isFailure(receipt, "start-simulation"), true);
  assert.equal(isFailure({ ...receipt, probe: { before: 1, after: 2, nativeExecutionObserved: true } }, "start-simulation"), true); // no build/readback proof
  assert.equal(isFailure({ success: true, failures: [], cleanup: [] }, "stop-simulation"), true);
});
