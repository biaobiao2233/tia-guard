"use strict";
const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs/promises");
const path = require("node:path");
const { fixture } = require("./support.js");
const { checkTools, payload, assertDisconnected } = require("../contract.js");
const { verifyRuntime, launchParameters } = require("../upstream.js");
const tools = () => [
  { name: "Bootstrap", inputSchema: { type: "object", properties: {} } },
  { name: "GetState", inputSchema: { type: "object", properties: {} } },
  { name: "Doctor", inputSchema: { type: "object", properties: { fix: { type: "boolean", default: true } } } }
];
test("required tool/schema projection allows additive optional fields but detects changed evidence", () => {
  const baseline = checkTools(tools()); const changed = tools();
  changed[1].inputSchema.properties.filter = { type: "string" };
  const next = checkTools(changed);
  assert.notEqual(next.schemaSha256, baseline.schemaSha256);
  assert.equal(next.upstreamToolCount, 3);
});
test("missing, duplicated and newly mandatory tool inputs fail closed", () => {
  assert.throws(() => checkTools(tools().slice(1)), /missing-required-tool/);
  assert.throws(() => checkTools([...tools(), tools()[0]]), /duplicate-or-invalid-tool/);
  const changed = tools(); changed[0].inputSchema.required = ["project"];
  assert.throws(() => checkTools(changed), /new-required-parameter/);
  changed[0].inputSchema.required = []; changed[2].inputSchema.properties.fix.type = "string";
  assert.throws(() => checkTools(changed), /doctor-fix-contract-mismatch/);
});
test("structured and JSON text values work; failures and incomplete results never become successes", () => {
  assert.equal(payload({ structuredContent: { count: 1 } }).count, 1);
  assert.equal(payload({ content: [{ type: "text", text: '{"count":2}' }] }).count, 2);
  for (const result of [{ isError: true }, { structuredContent: { meta: { success: false } } },
    { structuredContent: { complete: false } }, { structuredContent: { ok: false } },
    { content: [{ type: "text", text: "bad" }] }, { structuredContent: [] }]) {
    assert.throws(() => payload(result));
  }
});
test("connection status must be explicit false; unknown, connected and contradictory values fail", () => {
  assertDisconnected({ isConnected: false }); assertDisconnected({ IsConnected: false });
  for (const state of [{}, { isConnected: null }, { isConnected: true }, { isConnected: false, IsConnected: true },
    { isConnected: false, project: "foreign-project" }, { isConnected: false, session: "unknown" }]) {
    assert.throws(() => assertDisconnected(state));
  }
});
test("verified original runtime gets fixed read-only launch arguments and exact version", async t => {
  const f = await fixture(t);
  assert.equal(verifyRuntime(f.config, f.pin).fileCount, 1);
  const launch = launchParameters(f.config, f.pin);
  assert.deepEqual(launch.args.slice(0, 5), ["--read-only", "--profile", "full", "--tia-major-version", "21"]);
  assert.equal(launch.env.TIA_MCP_READ_ONLY, "1"); assert.equal(launch.env.MCP_TRANSPORT, "stdio");
});
test("version, executable contents, manifest and extra runtime files are rejected", async t => {
  const f = await fixture(t);
  assert.throws(() => verifyRuntime({ ...f.config, version: "other" }, f.pin), /invalid-local-configuration/);
  assert.throws(() => verifyRuntime({ ...f.config, files: {} }, f.pin), /runtime-manifest-mismatch/);
  await fs.writeFile(path.join(f.config.runtimeDirectory, "extra.dll"), "unexpected");
  assert.throws(() => verifyRuntime(f.config, f.pin), /runtime-file-set-drift/);
  await fs.unlink(path.join(f.config.runtimeDirectory, "extra.dll"));
  await fs.appendFile(path.join(f.config.runtimeDirectory, "TiaMcpServer.exe"), "changed");
  assert.throws(() => verifyRuntime(f.config, f.pin), /runtime-file-hash-drift/);
});
test("only the pinned ordinary startup log may appear between cold starts; code drift still fails", async t => {
  const f = await fixture(t);
  f.pin.mutableRuntimeFiles = ["TiaMcpServer.startup.log"];
  await fs.writeFile(path.join(f.config.runtimeDirectory, "TiaMcpServer.startup.log"), "first run");
  assert.equal(verifyRuntime(f.config, f.pin).fileCount, 1);
  await fs.appendFile(path.join(f.config.runtimeDirectory, "TiaMcpServer.startup.log"), "second run");
  assert.equal(verifyRuntime(f.config, f.pin).fileCount, 1);
  await fs.writeFile(path.join(f.config.runtimeDirectory, "unexpected.log"), "unknown");
  assert.throws(() => verifyRuntime(f.config, f.pin), /runtime-file-set-drift/);
  await fs.unlink(path.join(f.config.runtimeDirectory, "unexpected.log"));
  await fs.appendFile(path.join(f.config.runtimeDirectory, "TiaMcpServer.exe"), "tamper");
  assert.throws(() => verifyRuntime(f.config, f.pin), /runtime-file-hash-drift/);
  f.pin.mutableRuntimeFiles = ["extra.dll"];
  assert.throws(() => verifyRuntime(f.config, f.pin), /invalid-mutable-runtime-file-policy/);
});
