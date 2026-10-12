"use strict";
const test = require("node:test");
const assert = require("node:assert/strict");
const net = require("node:net");
const path = require("node:path");
const os = require("node:os");
const { randomUUID } = require("node:crypto");
const { DaemonClient } = require("../../mcp/daemonClient.js");
const { ProviderClient } = require("../../mcp/providerClient.js");
const { sanitize, toMcpResult } = require("../../mcp/result.js");
async function transportFixture(t, onRequest, options = {}) {
  const endpoint = process.platform === "win32" ? `\\\\.\\pipe\\tc57-test-${randomUUID()}` : path.join(os.tmpdir(), `tc57-${randomUUID()}.sock`);
  const sockets = new Set();
  const server = net.createServer(s => {
    sockets.add(s); s.on("close", () => sockets.delete(s)); s.on("error", () => {});
    let buf = ""; s.setEncoding("utf8"); s.on("data", chunk => {
      buf += chunk; let nl;
      while ((nl = buf.indexOf("\n")) >= 0) { const line = buf.slice(0, nl); buf = buf.slice(nl + 1); onRequest(JSON.parse(line), s); }
    });
  });
  await new Promise(resolve => server.listen(endpoint, resolve));
  const c = new DaemonClient({ endpoint, identity: "test-identity", timeoutMs: 100, ...options });
  t.after(async () => { c.close(); for (const s of sockets) s.destroy(); await new Promise(resolve => server.close(resolve)); });
  return c;
}
test("split UTF-8 NDJSON replies are decoded and correlated without losing Chinese diagnostics", async t => {
  const c = await transportFixture(t, (req, s) => {
    const bytes = Buffer.from(JSON.stringify({ id: req.id, ok: true, result: { message: "编译错误" } }) + "\n");
    const i = bytes.indexOf(Buffer.from("编")); s.write(bytes.subarray(0, i + 1)); s.write(bytes.subarray(i + 1));
  });
  assert.equal((await c.call("compile")).result.message, "编译错误");
});
test("malformed/oversized/unmatched frames close the session immediately", async t => {
  for (const scenario of ["invalid", "huge", "id"]) {
    const c = await transportFixture(t, (req, s) => {
      if (scenario === "invalid") s.write("{oops}\n");
      if (scenario === "huge") s.write("x".repeat(512));
      if (scenario === "id") s.write(JSON.stringify({ id: "alien", ok: true }) + "\n");
    }, { maxBytes: 256 });
    await assert.rejects(c.call("build-generation"), err => /^provider-(frame|response)/.test(err.code));
    await assert.rejects(c.call("build-generation"), err => err.code === "session-closed");
  }
});
test("timeout/pipe drop never retries a write; concurrent pending calls are rejected", async t => {
  let count = 0; const c = await transportFixture(t, () => { count++; });
  const r = await Promise.allSettled([c.call("build-generation"), c.call("compile")]);
  assert.equal(count, 2); assert.equal(r.filter(x => x.status === "rejected").length, 2);
  await assert.rejects(c.call("build-generation"), err => err.code === "session-closed"); assert.equal(count, 2);
  let drops = 0; const dropped = await transportFixture(t, (req, s) => { drops++; s.destroy(); });
  await assert.rejects(dropped.call("build-generation")); assert.equal(drops, 1);
});
test("invalid provider/session response invalidates all authority, not just that tool call", async () => {
  const transport = { close() {}, async call(action) {
    if (action === "hello") return { ok: true, result: { identity: "id", protocolVersion: "1", providerId: "p", sessionId: "s", capabilities: { doctor: { available: true, state: "supported" } } } };
    return { ok: true, result: { schemaVersion: "1.0", operation: action, sessionId: "other", providerId: "p", success: true } };
  } };
  const provider = new ProviderClient({ transport, identity: "id" });
  await assert.rejects(provider.dispatch("doctor", {}), e => e.code === "provider-result-binding-invalid");
  await assert.rejects(provider.dispatch("doctor", {}), e => e.code === "session-closed");
});
test("raw secrets are removed from structured evidence, paths, messages and transport exceptions", () => {
  const clean = sanitize({ grantToken: "secret-value", nested: { password: "secret-value", file: "C:/scratch/secret-value/main.st", message: "token=abc; https://local/?key=xyz" }, nullFact: null, complete: false }, ["secret-value"]);
  assert.doesNotMatch(JSON.stringify(clean), /secret-value|abc|xyz|grantToken|password/);
  assert.equal(clean.nullFact, null); assert.equal(clean.complete, false);
  assert.equal(toMcpResult({ ok: true }, "compile").isError, true);
});
