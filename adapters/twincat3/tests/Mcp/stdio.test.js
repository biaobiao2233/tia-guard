"use strict";
const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs/promises");
const os = require("node:os");
const path = require("node:path");
const { connectMcp, mcpRequire, mcpRoot } = require("./support.js");
const { toolSchemas } = require(path.join(mcpRoot, "toolSchemas.js"));
const z = mcpRequire("zod/v4");
async function fixture(t, { write = false, scenario = "", mode = "legacy", broken = false } = {}) {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), "tc57-mcp-"));
  let connection;
  t.after(async () => { if (connection) await connection.close(); await fs.rm(root, { recursive: true, force: true }); });
  const inputDirectory = path.join(root, "input"); await fs.mkdir(inputDirectory);
  const source = path.join(inputDirectory, "MAIN.st"); await fs.writeFile(source, broken ? "PROGRAM MAIN\nVAR END_VAR\nBROKEN" : "PROGRAM MAIN\nVAR END_VAR\n");
  await fs.writeFile(path.join(inputDirectory, "twincat-generation.json"), "{}");
  const lifecycle = path.join(root, "lifecycle.txt");
  connection = await connectMcp({ TC3_WORKER_COMMAND: process.execPath,
    TC3_WORKER_ARGS: JSON.stringify([path.join(__dirname, "fake-worker.js")]), TC3_OFFLINE_WRITES: write ? "1" : "0",
    TC3_FAKE_SCENARIO: scenario, TC3_FAKE_LIFECYCLE: lifecycle }, mode);
  const call = async (name, args = {}) => connection.client.callTool({ name, arguments: args });
  const request = { operation: "build-generation", inputDirectory, outputDirectory: path.join(root, "out") };
  return { ...connection, call, request, source, lifecycle, root };
}
test("real SDK legacy and modern discovery have exact tool/schema parity; missing provider fails", async t => {
  for (const mode of ["legacy", "auto"]) {
    const c = await connectMcp({}, mode);
    try {
      const { tools } = await c.client.listTools();
      assert.deepEqual(tools.map(v => v.name).sort(), Object.keys(toolSchemas).sort());
      for (const tool of tools) {
        const expected = z.toJSONSchema(toolSchemas[tool.name].inputSchema, { io: "input" });
        assert.deepEqual(tool.inputSchema.properties, expected.properties);
        assert.deepEqual(tool.inputSchema.required, expected.required);
        assert.equal(tool.inputSchema.additionalProperties, false);
        assert.equal(tool._meta["tia-guard/backendState"], "unavailable");
      }
      const r = await c.client.callTool({ name: "doctor_twincat", arguments: {} });
      assert.equal(r.isError, true); assert.equal(r.structuredContent.failures[0].code, "missing-provider");
      assert.match(r.content[0].text, /missing-provider/);
    } finally { await c.close(); }
  }
});
test("default read-only doctor/inspect/preview work; authorization blocked and inputs unchanged", async t => {
  const f = await fixture(t); const original = await fs.readFile(f.source);
  assert.equal((await f.call("doctor_twincat")).isError, false);
  assert.equal((await f.call("inspect_twincat_project", { projectPath: f.source })).isError, false);
  const preview = await f.call("preview_twincat_generation", f.request);
  assert.equal(preview.isError, false);
  const denied = await f.call("authorize_twincat_preview", { previewId: preview.structuredContent.previewId });
  assert.equal(denied.isError, true); assert.equal(denied.structuredContent.failures[0].code, "read-only-mode");
  await assert.rejects(fs.stat(f.request.outputDirectory)); assert.deepEqual(await fs.readFile(f.source), original);
  await f.close(); assert.equal(await fs.readFile(f.lifecycle, "utf8"), "disposed-own-worker");
});
async function authorize(f, request = f.request) {
  const preview = await f.call("preview_twincat_generation", request); assert.equal(preview.isError, false);
  const previewId = preview.structuredContent.previewId;
  const grant = await f.call("authorize_twincat_preview", { previewId }); assert.equal(grant.isError, false);
  assert.equal(grant.structuredContent.grantToken, undefined); assert.doesNotMatch(JSON.stringify(grant), /grantToken|private-path-secret/);
  return { previewId, grantId: grant.structuredContent.grantId };
}
test("fixture pipeline: ST error → repair → new preview/grant → build → independent verify; grant cannot be reused", async t => {
  const f = await fixture(t, { write: true, broken: true });
  const first = await authorize(f);
  const failed = await f.call("build_twincat_generation", first);
  assert.equal(failed.isError, true); assert.equal(failed.structuredContent.compilation.diagnostics.length, 1);
  assert.equal(failed.structuredContent.compilation.diagnostics[0].line, 3);
  assert.doesNotMatch(JSON.stringify(failed), /private-path-secret/);
  await assert.rejects(fs.stat(f.request.outputDirectory));
  assert.equal((await f.call("build_twincat_generation", first)).isError, true);
  await fs.writeFile(f.source, "PROGRAM MAIN\nVAR END_VAR\n");
  const next = await authorize(f);
  const built = await f.call("build_twincat_generation", next);
  assert.equal(built.isError, false); assert.equal(built.structuredContent.published, true);
  const verified = await f.call("verify_twincat_project", { executionId: built.structuredContent.executionId });
  assert.equal(verified.isError, false);
  assert.equal((await f.call("build_twincat_generation", next)).isError, true);
});
test("hash drift and output collision fail at fixture backend after authorization", async t => {
  const f = await fixture(t, { write: true });
  const grant = await authorize(f); await fs.appendFile(f.source, "// changed");
  const drift = await f.call("build_twincat_generation", grant); assert.equal(drift.isError, true);
  assert.equal(drift.structuredContent.failures[0].code, "hash-drift-or-expired-preview");
  const second = await authorize(f); await fs.mkdir(f.request.outputDirectory);
  const collision = await f.call("build_twincat_generation", second); assert.equal(collision.isError, true);
  assert.equal(collision.structuredContent.failures[0].code, "output-collision");
});
test("expired handles, foreign worker identity and unsupported compiler are explicit errors", async t => {
  const expired = await fixture(t, { write: true, scenario: "expiry" });
  const grant = await authorize(expired); await new Promise(r => setTimeout(r, 300));
  assert.equal((await expired.call("build_twincat_generation", grant)).isError, true);
  await expired.close();
  const foreign = await fixture(t, { scenario: "foreign" });
  assert.equal((await foreign.call("doctor_twincat")).structuredContent.failures[0].code, "provider-session-mismatch");
  await foreign.close();
  const missing = await fixture(t, { write: true, scenario: "missing-compiler" });
  const compileGrant = await authorize(missing, { ...missing.request, operation: "compile", projectPath: missing.source });
  assert.equal((await missing.call("compile_twincat_project", compileGrant)).structuredContent.failures[0].code, "provider-capability-unavailable");
});
test("partial, unknown and cleanup failure remain MCP errors despite success:true", async t => {
  for (const scenario of ["partial", "unknown", "cleanup-failure"]) {
    const f = await fixture(t, { write: true, scenario }); const grant = await authorize(f);
    const r = await f.call("build_twincat_generation", grant); assert.equal(r.isError, true, scenario);
    assert.equal(r.structuredContent.success, true); // preserve contradictory backend evidence, flag it
    await f.close();
  }
});
test("tool allowlist and strict schemas reject injected runtime actions or worker tokens", async t => {
  const f = await fixture(t, { write: true });
  await assert.rejects(f.call("activate_twincat", {}));
  assert.equal((await f.call("doctor_twincat", { action: "ActivateConfiguration" })).isError, true);
  assert.equal((await f.call("preview_twincat_generation", { ...f.request, grantToken: "injected" })).isError, true);
  assert.equal((await f.call("preview_twincat_generation", { ...f.request, operation: "compile" })).isError, true);
  const grant = await authorize(f);
  const mismatch = await f.call("compile_twincat_project", grant);
  assert.equal(mismatch.isError, true); assert.equal(mismatch.structuredContent.failures[0].code, "invalid-or-consumed-grant");
  assert.equal((await f.call("build_twincat_generation", grant)).isError, true);
});
test("MCP process restart cannot reuse local preview/grant or foreign execution handles", async t => {
  const first = await fixture(t, { write: true }); const grant = await authorize(first); await first.close();
  const second = await fixture(t, { write: true });
  assert.equal((await second.call("build_twincat_generation", grant)).structuredContent.failures[0].code, "missing-preview");
  assert.equal((await second.call("verify_twincat_project", { executionId: "foreign" })).structuredContent.failures[0].code, "unknown-owned-execution");
});
