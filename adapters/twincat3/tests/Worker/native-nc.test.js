"use strict";
// Actual XAE/NC qualification only. Cloud runs skip it; no software position integrator.
const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs/promises");
const path = require("node:path");
const { connectMcp } = require("../Mcp/support.js");

test("native MCP: create NC axis, bind, compile, reopen and optional actual NC motion", {
  skip: process.platform !== "win32" || process.env.TC3_NC_NATIVE_TEST !== "1", timeout: 2400000
}, async () => {
  const root = process.env.TC3_NATIVE_TEST_ROOT;
  assert.ok(root && path.isAbsolute(root));
  await fs.mkdir(root);
  const scratch = path.join(root, "scratch"); await fs.mkdir(scratch);
  const inputDirectory = path.join(root, "input");
  await fs.cp(path.resolve(__dirname, "../../../../examples/twincat3-generation/nc-axis"), inputDirectory, { recursive: true });
  const runtime = process.env.TC3_NC_RUNTIME_TEST === "1";
  const c = await connectMcp({
    TC3_WORKER_COMMAND: path.resolve(__dirname, "../../src/Worker/bin/Release/net8.0-windows/TwinCat3.Worker.exe"),
    TC3_WORKSPACE: process.env.TC3_NATIVE_WORKSPACE || root, TC3_SCRATCH: scratch,
    TC3_INTEROP: process.env.TC3_INTEROP, TC3_BUILD: "4026.27", TC3_OFFLINE_WRITES: "1", TC3_RUNTIME_WRITES: runtime ? "1" : "0"
  });
  const receipts = []; const samples = []; const results = { runtimeRequested: runtime, motionPassed: false };
  async function call(name, args = {}, allowError = false) {
    const response = await c.client.callTool({ name, arguments: args }, { timeout: 240000 });
    receipts.push({ tool: name, isError: !!response.isError, report: response.structuredContent });
    await fs.writeFile(path.join(root, "mcp-evidence.json"), JSON.stringify(receipts, null, 2));
    if (!name.includes("symbol")) console.log(JSON.stringify({ tool: name, error: !!response.isError, phase: response.structuredContent?.phase }));
    if (!allowError) assert.equal(!!response.isError, false, JSON.stringify(response.structuredContent));
    return response;
  }
  async function authorize(args) {
    const p = (await call("preview_twincat_generation", { inputDirectory, ...args })).structuredContent;
    const a = (await call("authorize_twincat_preview", { previewId: p.previewId })).structuredContent;
    return { previewId: p.previewId, grantId: a.grantId };
  }
  try {
    await call("doctor_twincat");
    const built = (await call("build_twincat_generation", await authorize({ operation: "build-generation", outputDirectory: path.join(root, "published") }))).structuredContent;
    assert.equal(built.published, true);
    results.inputHash = built.inputHash;
    await call("verify_twincat_project", { executionId: built.executionId });
    results.engineeringPassed = true;
    if (runtime) {
      const handles = await authorize({ operation: "start-simulation", executionId: built.executionId, projectPath: built.solutionPath,
        outputDirectory: path.join(scratch, "simulation"), probeSymbol: "MAIN.CycleCount" });
      let started;
      if (process.env.TC3_NC_HUMAN_LICENSE === "1") {
        const prepared = (await call("prepare_twincat_simulation", handles)).structuredContent;
        await fs.writeFile(path.join(root, "license-preparation.json"), JSON.stringify(prepared, null, 2));
        console.log("Waiting for the user's official license step; no code generates or enters a security code.");
        const until = Date.now() + 1800000;
        while (true) {
          try { await fs.access(path.join(root, "runtime-license-ready.txt")); break; } catch { }
          assert.ok(Date.now() < until, "Official licensing not completed; NC execution NOT_RUN.");
          await new Promise(r => setTimeout(r, 500));
        }
        started = (await call("resume_twincat_simulation", { simulationId: prepared.simulationId })).structuredContent;
      } else started = (await call("start_twincat_simulation", handles)).structuredContent;
      const simulationId = started.simulationId;
      const read = async (symbol, type) => (await call("read_twincat_symbol", { simulationId, symbol, type })).structuredContent.value;
      const write = async (name, type, value) => call("write_twincat_symbol", { simulationId, symbol: "GVL_Command." + name, type, value });
      const pause = () => new Promise(r => setTimeout(r, 120));
      async function waitFor(symbol, type, predicate) {
        const until = Date.now() + 30000;
        while (Date.now() < until) { const value = await read(symbol, type); if (predicate(value)) return value; await pause(); }
        assert.fail("NC feedback timeout: " + symbol);
      }
      async function pulse(name) { await write(name, "BOOL", true); await pause(); await write(name, "BOOL", false); }
      async function sample(label) {
        const data = { label, at: new Date().toISOString(), position: await read("MAIN.ActualPosition", "LREAL"),
          velocity: await read("MAIN.ActualVelocity", "LREAL"), step: await read("MAIN.Step", "INT"), error: await read("MAIN.Error", "BOOL") };
        samples.push(data); return data;
      }
      await write("Enable", "BOOL", true); await waitFor("MAIN.Enabled", "BOOL", v => v === true);
      await waitFor("MAIN.Step", "INT", v => v === 20);
      await pulse("HomeRequest"); await waitFor("MAIN.Homed", "BOOL", v => v === true);
      const initialMoves = await read("MAIN.CompletedMoves", "UDINT");
      for (const [index, position] of [30, 80, 0].entries()) {
        await waitFor("MAIN.Step", "INT", v => v === 20);
        await write("TargetPosition", "LREAL", position); await pulse("MoveRequest");
        const moving = await sample("move-" + position); assert.equal(moving.error, false);
        await waitFor("MAIN.CompletedMoves", "UDINT", v => v === initialMoves + index + 1);
        const done = await sample("done-" + position); assert.ok(Math.abs(done.position - position) < 0.02);
        assert.equal(await read("MAIN.Done", "BOOL"), true); assert.equal(done.error, false);
      }
      const rejected = await read("MAIN.RejectedCommands", "UDINT");
      await write("TargetPosition", "LREAL", 101); await pulse("MoveRequest");
      await waitFor("MAIN.RejectedCommands", "UDINT", v => v === rejected + 1);
      assert.equal(await read("MAIN.Error", "BOOL"), false);
      assert.equal(await read("MAIN.CompletedMoves", "UDINT"), initialMoves + 3);
      await write("TargetPosition", "LREAL", 100); await pulse("MoveRequest");
      await waitFor("MAIN.ActualVelocity", "LREAL", v => v > 0.1);
      await write("StopRequest", "BOOL", true);
      await waitFor("MAIN.StopHeld", "BOOL", v => v === true);
      await waitFor("MAIN.ActualVelocity", "LREAL", v => Math.abs(v) < 0.001);
      const stopped = await sample("stopped"); assert.ok(stopped.position < 100);
      await write("StopRequest", "BOOL", false); await write("ReleaseStop", "BOOL", true);
      await waitFor("MAIN.StopHeld", "BOOL", v => v === false);
      await write("ReleaseStop", "BOOL", false);
      await pulse("ResetRequest"); await waitFor("MAIN.Step", "INT", v => v === 20);
      assert.equal(await read("MAIN.Error", "BOOL"), false);
      for (const symbol of ["MAIN.ActualPosition", "GVL_Axis.Axis.PlcToNc.ControlDWord"]) {
        assert.equal((await call("write_twincat_symbol", { simulationId, symbol, type: "LREAL", value: 0 }, true)).isError, true);
      }
      await write("Enable", "BOOL", false);
      const ended = (await call("stop_twincat_simulation", { simulationId })).structuredContent;
      assert.equal(ended.ownedProcessExited, true);
      results.motionPassed = true;
    }
  } finally {
    await fs.writeFile(path.join(root, "nc-results.json"), JSON.stringify({ ...results, samples }, null, 2));
    try {
      const cleanup = (await call("dispose_twincat_session")).structuredContent;
      await fs.writeFile(path.join(root, "session-cleanup.json"), JSON.stringify(cleanup, null, 2));
    } finally { await c.close(); await fs.writeFile(path.join(root, "client-stderr.log"), c.stderr()); }
  }
});
