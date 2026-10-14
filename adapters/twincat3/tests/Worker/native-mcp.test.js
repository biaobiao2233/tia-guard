"use strict";
// Opt-in qualification against the real local XAE/compiler. Retain all evidence;
// never attach to an existing IDE or activate a Runtime.
const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs/promises");
const path = require("node:path");

test("native MCP: compiler error -> repair -> fresh authorization -> build -> independent reopen", {
  skip: process.platform !== "win32" || process.env.TC3_NATIVE_TEST !== "1", timeout: 600000
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
    TC3_INTEROP: process.env.TC3_INTEROP, TC3_OFFLINE_WRITES: "1", TC3_BUILD: manifest.environment.requestedBuild
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
    const bad = await generate("rejected-output");
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
    await fs.writeFile(main, correct);
    const good = await generate("published");
    assert.notEqual(good.preview.inputHash, bad.preview.inputHash);
    assert.equal(good.built.isError, false, JSON.stringify(good.built.structuredContent));
    assert.equal(good.built.structuredContent.published, true);
    const verification = await call("verify_twincat_project", { executionId: good.built.structuredContent.executionId });
    assert.equal(verification.isError, false, JSON.stringify(verification.structuredContent));
    assert.equal(verification.structuredContent.verification.verdict, "pass");
  } finally {
    await c.close();
    await fs.writeFile(path.join(root, "client-stderr.log"), c.stderr());
  }
});
