"use strict";
// Opt-in qualification against the real local XAE/compiler. Retain all evidence;
// never attach to an existing IDE. Runtime qualification is separately opt-in
// and can activate only a freshly allocated, owned Usermode simulation.
const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs/promises");
const path = require("node:path");

test("native MCP: compiler error -> repair -> fresh authorization -> build -> independent reopen", {
  skip: process.platform !== "win32" || process.env.TC3_NATIVE_TEST !== "1", timeout: 2700000
}, async () => {
  const { connectMcp } = require("../Mcp/support.js");
  const root = path.resolve(process.env.TC3_NATIVE_TEST_ROOT || "");
  assert.ok(process.env.TC3_NATIVE_TEST_ROOT && path.isAbsolute(process.env.TC3_NATIVE_TEST_ROOT));
  await fs.mkdir(root); // Existing output/evidence must never be overwritten.
  const scratch = path.join(root, "scratch"); await fs.mkdir(scratch);
  const inputDirectory = path.join(root, "input");
  await fs.cp(path.resolve(__dirname, "../../../../examples/twincat3-generation/motor"), inputDirectory, { recursive: true });
  const manifest = JSON.parse(await fs.readFile(path.join(inputDirectory, "twincat-generation.json"), "utf8"));
  const main = path.join(inputDirectory, manifest.artifacts.find(a => a.name === "MAIN").implementationFile);
  const correct = await fs.readFile(main, "utf8");
  await fs.writeFile(main, correct + "\nUndefinedRegressionIdentifier := TRUE;\n");
  const c = await connectMcp({
    TC3_WORKER_COMMAND: path.resolve(__dirname, "../../src/Worker/bin/Release/net8.0-windows/TwinCat3.Worker.exe"),
    TC3_WORKER_ARGS: "[]", TC3_WORKSPACE: root, TC3_SCRATCH: scratch,
    TC3_INTEROP: process.env.TC3_INTEROP, TC3_OFFLINE_WRITES: "1", TC3_BUILD: manifest.environment.requestedBuild,
    TC3_RUNTIME_WRITES: process.env.TC3_RUNTIME_TEST === "1" ? "1" : "0"
  });
  const receipts = [];
  async function call(name, args = {}) {
    const result = await c.client.callTool({ name, arguments: args }, { timeout: 240000 });
    receipts.push({ tool: name, isError: result.isError === true, report: result.structuredContent });
    await fs.writeFile(path.join(root, "mcp-evidence.json"), JSON.stringify(receipts, null, 2));
    console.log(JSON.stringify({ tool: name, isError: result.isError === true, phase: result.structuredContent?.phase,
      compilation: result.structuredContent?.compilation?.state }));
    return result;
  }
  async function generate(output) {
    const preview = await call("preview_twincat_generation", { operation: "build-generation", inputDirectory,
      outputDirectory: path.join(root, output) });
    assert.equal(preview.isError, false, JSON.stringify(preview.structuredContent));
    const authorization = await call("authorize_twincat_preview", { previewId: preview.structuredContent.previewId });
    assert.equal(authorization.isError, false, JSON.stringify(authorization.structuredContent));
    const handles = { previewId: preview.structuredContent.previewId, grantId: authorization.structuredContent.grantId };
    return { preview: preview.structuredContent, handles, built: await call("build_twincat_generation", handles) };
  }
  try {
    assert.equal((await call("doctor_twincat")).isError, false);
    const runtimeOnly = process.env.TC3_RUNTIME_TEST === "1";
    let bad;
    if (!runtimeOnly) {
    bad = await generate("rejected-output");
    assert.equal(bad.built.isError, true);
    const failed = bad.built.structuredContent;
    assert.equal(failed.published, false);
    // A native Error List can refresh asynchronously. UNKNOWN must remain a failure;
    // full compiler output must still return the exact error so it can be repaired.
    assert.ok(["error", "unknown"].includes(failed.compilation.state));
    assert.equal(failed.compilation.outputCaptureComplete, true);
    assert.equal(failed.compilation.buildStarted, true); assert.equal(failed.compilation.buildCompleted, true);
    assert.ok(failed.compilation.diagnostics.some(d => d.severity === "error" && d.code === "C0046" &&
      d.origin === "DTE.BuildOutput" && d.line > 0 && d.file && d.message.includes("UndefinedRegressionIdentifier")));
    assert.equal((await call("build_twincat_generation", bad.handles)).isError, true);
    }
    await fs.writeFile(main, correct);
    const good = await generate("published");
    if (bad) assert.notEqual(good.preview.inputHash, bad.preview.inputHash);
    assert.equal(good.built.isError, false, JSON.stringify(good.built.structuredContent));
    assert.equal(good.built.structuredContent.published, true);
    const verification = await call("verify_twincat_project", { executionId: good.built.structuredContent.executionId });
    assert.equal(verification.isError, false, JSON.stringify(verification.structuredContent));
    assert.equal(verification.structuredContent.verification.verdict, "pass");
    if (runtimeOnly) {
      const published = good.built.structuredContent;
      const preview = await call("preview_twincat_generation", { operation: "start-simulation", inputDirectory,
        projectPath: published.solutionPath, executionId: published.executionId,
        outputDirectory: path.join(scratch, "simulation"), probeSymbol: "MAIN.Motor.Status.CycleCount" });
      assert.equal(preview.isError, false, JSON.stringify(preview.structuredContent));
      const authorization = await call("authorize_twincat_preview", { previewId: preview.structuredContent.previewId });
      assert.equal(authorization.isError, false, JSON.stringify(authorization.structuredContent));
      const handles = { previewId: preview.structuredContent.previewId, grantId: authorization.structuredContent.grantId };
      let started;
      if (process.env.TC3_RUNTIME_HUMAN_LICENSE === "1") {
        const prepared = await call("prepare_twincat_simulation", handles);
        assert.equal(prepared.isError, false, JSON.stringify(prepared.structuredContent));
        assert.equal(prepared.structuredContent.executionReady, false);
        const pendingId = prepared.structuredContent.simulationId;
        assert.equal((await call("write_twincat_symbol", { simulationId: pendingId, symbol: "GVL_Signals.Start", type: "BOOL", value: true })).isError, true);
        await call("read_twincat_simulation", { simulationId: pendingId });
        await fs.writeFile(path.join(root, "license-preparation.json"), JSON.stringify(prepared.structuredContent, null, 2));
        const ideReceiptPath = path.join(scratch, "simulation/runtime/ide-ownership.json");
        const licenseIde = JSON.parse(await fs.readFile(ideReceiptPath, "utf8"));
        console.log("Awaiting user's official license step; no trial is generated by the test.");
        const ready = path.join(root, "runtime-license-ready.txt");
        const deadline = Date.now() + 1800000;
        while (true) {
          try { await fs.access(ready); break; } catch { }
          if (Date.now() >= deadline) assert.fail("User licensing readiness was not provided; execution remains unqualified.");
          await new Promise(resolve => setTimeout(resolve, 500));
        }
        started = await call("resume_twincat_simulation", { simulationId: pendingId });
        const compilationIde = JSON.parse(await fs.readFile(ideReceiptPath, "utf8"));
        await fs.writeFile(path.join(root, "ide-reopen-evidence.json"), JSON.stringify({ licenseIde, compilationIde }, null, 2));
        assert.notEqual(compilationIde.OwnedProcessId, licenseIde.OwnedProcessId);
        assert.equal(compilationIde.SimulationId, licenseIde.SimulationId);
      } else {
        started = await call("start_twincat_simulation", handles);
      }
      assert.equal(started.isError, false, JSON.stringify(started.structuredContent));
      const simulationId = started.structuredContent.simulationId;
      const scalar = (symbol, type, value) => ({ simulationId, symbol, type, ...(value === undefined ? {} : { value }) });
      const successful = async (tool, args) => {
        const response = await call(tool, args); assert.equal(response.isError, false, JSON.stringify(response.structuredContent));
        return response.structuredContent;
      };
      const waitValue = async (symbol, type, value) => {
        const deadline = Date.now() + 10000;
        while (Date.now() < deadline) {
          const actual = await successful("read_twincat_symbol", scalar(symbol, type));
          if (actual.value === value) return;
          await new Promise(resolve => setTimeout(resolve, 100));
        }
        assert.fail(`Actual PLC value was not observed: ${symbol}=${value}`);
      };
      await successful("read_twincat_simulation", { simulationId });
      for (const [command, feedback, wanted] of [["Start", "Running", true], ["Fault", "Faulted", true]]) {
        await successful("write_twincat_symbol", scalar(`GVL_Signals.${command}`, "BOOL", true));
        await waitValue(`MAIN.Motor.Status.${feedback}`, "BOOL", wanted);
        await successful("write_twincat_symbol", scalar(`GVL_Signals.${command}`, "BOOL", false));
      }
      await waitValue("MAIN.Motor.Status.Running", "BOOL", false);
      await successful("write_twincat_symbol", scalar("GVL_Signals.Reset", "BOOL", true));
      await waitValue("MAIN.Motor.Status.Faulted", "BOOL", false);
      await successful("write_twincat_symbol", scalar("GVL_Signals.Reset", "BOOL", false));
      await successful("write_twincat_symbol", scalar("GVL_Signals.Start", "BOOL", true));
      await waitValue("MAIN.Motor.Status.Running", "BOOL", true);
      await successful("write_twincat_symbol", scalar("GVL_Signals.Start", "BOOL", false));
      await successful("write_twincat_symbol", scalar("GVL_Signals.Stop", "BOOL", true));
      await waitValue("MAIN.Motor.Status.Running", "BOOL", false);
      await successful("write_twincat_symbol", scalar("GVL_Signals.Stop", "BOOL", false));
      assert.equal((await call("write_twincat_symbol", scalar("MAIN.Motor.Status.CycleCount", "UDINT", 0))).isError, true);
      const stopped = await successful("set_twincat_simulation_state", { simulationId, state: "Stop" });
      assert.equal(stopped.state.plcState, "Stop");
      const first = await successful("read_twincat_symbol", scalar("MAIN.Motor.Status.CycleCount", "UDINT"));
      await new Promise(resolve => setTimeout(resolve, 500));
      const second = await successful("read_twincat_symbol", scalar("MAIN.Motor.Status.CycleCount", "UDINT"));
      assert.equal(first.value, second.value);
      const resumed = await successful("set_twincat_simulation_state", { simulationId, state: "Run" });
      assert.equal(resumed.state.plcState, "Run");
      const stoppedProcess = await successful("stop_twincat_simulation", { simulationId });
      assert.equal(stoppedProcess.ownedProcessExited, true);
      assert.equal((await call("read_twincat_simulation", { simulationId })).isError, true);
    }
  } finally {
    try {
      const cleanup = await call("dispose_twincat_session");
      await fs.writeFile(path.join(root, "session-cleanup.json"), JSON.stringify(cleanup.structuredContent, null, 2));
      assert.equal(cleanup.isError, false, JSON.stringify(cleanup.structuredContent));
    } finally {
      await c.close();
      await fs.writeFile(path.join(root, "client-stderr.log"), c.stderr());
    }
  }
});
