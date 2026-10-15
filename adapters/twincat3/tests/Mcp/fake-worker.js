"use strict";
// Test fixture only. NOT a TwinCAT compiler/verifier or B security acceptance.
const net = require("node:net");
const fs = require("node:fs");
const path = require("node:path");
const { randomUUID, createHash } = require("node:crypto");
const identity = process.env.TC3_IDENTITY;
const providerId = "fixture-only"; const sessionId = randomUUID();
const previews = new Map(); const grants = new Map(); const executions = new Map();
const actions = ["doctor", "inspect", "preview", "authorize", "build-generation", "compile", "verify", "dispose"];
const scenario = process.env.TC3_FAKE_SCENARIO;
const envelope = (operation, props = {}) => ({ schemaVersion: "1.0", operation, sessionId, providerId,
  phase: operation, success: true, published: false, failures: [], cleanup: [], ...props });
const capabilities = Object.fromEntries(actions.map(a => [a, { available: true, state: "untested", reason: "Fixture only; no XAE evidence" }]));
if (scenario === "missing-compiler") capabilities.compile = { available: false, state: "unavailable", reason: "missing compiler" };
function fail(action, code) { return envelope(action, { success: false, failures: [{ code, message: code }] }); }
function hash(p) {
  const sources = fs.readdirSync(p.inputDirectory).sort().filter(n => fs.statSync(path.join(p.inputDirectory, n)).isFile())
    .map(n => [n, fs.readFileSync(path.join(p.inputDirectory, n)).toString("base64")]);
  return createHash("sha256").update(JSON.stringify([p.operation, path.resolve(p.inputDirectory), path.resolve(p.outputDirectory), p.projectPath, providerId, sessionId, sources])).digest("hex");
}
function compileReport(inputHash, broken) { return {
  state: broken ? "error" : "success", buildStarted: true, buildCompleted: true,
  failedProjectCount: broken ? 1 : 0, errorCount: broken ? 1 : 0, warningCount: 0,
  diagnosticsComplete: true, outputCaptureComplete: true,
  diagnostics: broken ? [{ severity: "error", code: "fixture-E1", message: "BROKEN token; token=private-path-secret", file: "MAIN.st", line: 3, column: 1, correlated: true, origin: "fixture" }] : [],
  buildOutput: "fixture output", configuration: "Release", platform: "fixture", projectIdentity: "fixture-project", inputHash, failures: []
}; }
function dispatch(action, p) {
  if (action === "hello") return { protocolVersion: "1", identity: scenario === "foreign" ? randomUUID() : identity, providerId, sessionId, capabilities };
  if (p.sessionId !== sessionId || p.providerId !== providerId) return fail(action, "session-mismatch");
  if (action === "dispose") return envelope(action, scenario === "cleanup-failure" ? { success: false, failures: [{ code: "cleanup-failed" }] } : {});
  if (action === "doctor") return envelope(action, { environment: { capabilities }, adapter: "fixture", evidence: "untested" });
  if (action === "inspect") return envelope(action, { inputBytes: fs.readFileSync(p.projectPath, "utf8"), originalUnchanged: true });
  if (action === "preview") {
    if (fs.existsSync(p.outputDirectory)) return fail(action, "output-collision");
    const previewId = randomUUID(); const inputHash = hash(p);
    const expiresAtUtc = new Date(Date.now() + (scenario === "expiry" ? 250 : 300000)).toISOString();
    previews.set(previewId, { request: { ...p }, inputHash, expiresAtUtc });
    return envelope(action, { previewId, inputHash, expiresAtUtc, request: p });
  }
  if (["authorize", "build-generation", "compile"].includes(action)) {
    if (process.env.TC3_OFFLINE_WRITES !== "1") return fail(action, "read-only-backend");
    const preview = previews.get(p.previewId);
    if (!preview || Date.parse(preview.expiresAtUtc) <= Date.now() || preview.inputHash !== p.inputHash || hash(preview.request) !== preview.inputHash)
      return fail(action, "hash-drift-or-expired-preview");
    if (action === "authorize") {
      const grantToken = randomUUID() + randomUUID(); grants.set(grantToken, { previewId: p.previewId, inputHash: p.inputHash, expiresAtUtc: preview.expiresAtUtc });
      return envelope(action, { grantToken, inputHash: p.inputHash, expiresAtUtc: preview.expiresAtUtc });
    }
    const grant = grants.get(p.grantToken); grants.delete(p.grantToken);
    if (!grant || grant.previewId !== p.previewId || grant.inputHash !== p.inputHash || Date.parse(grant.expiresAtUtc) <= Date.now() ||
        action !== preview.request.operation || p.inputDirectory !== preview.request.inputDirectory || p.outputDirectory !== preview.request.outputDirectory)
      return fail(action, "grant-invalid");
    if (fs.existsSync(p.outputDirectory)) return fail(action, "output-collision");
    const broken = fs.readFileSync(path.join(p.inputDirectory, "MAIN.st"), "utf8").includes("BROKEN");
    const compilation = compileReport(p.inputHash, broken);
    const verification = { verdict: broken ? "unknown" : "pass", complete: !broken, differences: [], unknowns: broken ? ["compile failed"] : [] };
    if (scenario === "partial") compilation.diagnosticsComplete = false;
    if (scenario === "unknown") verification.verdict = "unknown";
    const executionId = randomUUID();
    const r = envelope(action, { executionId, inputHash: p.inputHash, compilation, ...(action === "build-generation" ? { verification } : {}),
      success: !broken, published: action === "build-generation" && !broken,
      failures: broken ? [{ code: "compilation-failed" }] : [],
      cleanup: scenario === "cleanup-failure" ? [{ success: false, code: "cleanup-failed" }] : [] });
    executions.set(executionId, { verification, inputHash: p.inputHash });
    if (r.published) fs.mkdirSync(p.outputDirectory);
    return r;
  }
  if (action === "verify") {
    const e = executions.get(p.executionId); return e ? envelope(action, { verification: e.verification, inputHash: e.inputHash }) : fail(action, "unknown-execution");
  }
  return fail(action, "operation-not-allowed");
}
const server = net.createServer(s => {
  s.setEncoding("utf8"); let buf = "";
  s.on("data", chunk => {
    buf += chunk; let nl;
    while ((nl = buf.indexOf("\n")) >= 0) {
      const line = buf.slice(0, nl); buf = buf.slice(nl + 1);
      let req; try { req = JSON.parse(line); } catch { s.destroy(); return; }
      if (req.identity !== identity) { s.destroy(); return; }
      let result; try { result = dispatch(req.action, req.payload); } catch { result = fail(req.action, "fixture-operation-failed"); }
      s.write(JSON.stringify({ id: req.id, ok: true, result }) + "\n");
      if (req.action === "dispose") {
        if (process.env.TC3_FAKE_LIFECYCLE) fs.writeFileSync(process.env.TC3_FAKE_LIFECYCLE, "disposed-own-worker");
        s.end(); server.close();
      }
    }
  }); s.on("error", () => {});
});
server.listen(process.env.TC3_PIPE);
