"use strict";
const { spawn } = require("node:child_process");
const { randomUUID } = require("node:crypto");
const path = require("node:path");
const { DaemonClient } = require("./daemonClient.js");
const { BridgeError, failure, isFailure, sanitize } = require("./result.js");
const { toolSchemas } = require("./toolSchemas.js");
const allowed = new Set(Object.values(toolSchemas).map(t => t.action));
const simulationActions = new Set(["start-simulation", "prepare-simulation", "resume-simulation", "simulation-status", "simulation-state", "simulation-read", "simulation-write", "stop-simulation"]);
const simulationAllocations = new Set(["start-simulation", "prepare-simulation"]);

class ProviderClient {
  constructor({ transport = null, identity = null, offlineWrites = false, runtimeWrites = false, child = null, startupMs = 10000 } = {}) {
    this.transport = transport; this.identity = identity; this.offlineWrites = offlineWrites;
    this.child = child; this.startupMs = startupMs; this.session = null; this.ready = null;
    this.runtimeWrites = runtimeWrites; this.simulations = new Map();
    this.previews = new Map(); this.grants = new Map(); this.executions = new Map();
    this.secrets = new Set(identity ? [identity] : []); this.closed = false; this.queue = Promise.resolve();
  }
  async ensureReady() {
    if (this.closed) throw new BridgeError("session-closed");
    if (!this.transport) throw new BridgeError("missing-provider");
    if (this.session) return this.session;
    if (!this.ready) this.ready = (async () => {
      // Retry only connection establishment before any operation was submitted.
      if (this.child) {
        const deadline = Date.now() + this.startupMs;
        while (true) {
          try { await this.transport.connect(); break; }
          catch { if (this.child.exitCode !== null || Date.now() >= deadline) throw new BridgeError("provider-startup-failed"); }
          await new Promise(resolve => setTimeout(resolve, 100));
        }
      }
      const msg = await this.transport.call("hello", {});
      const r = msg?.result;
      if (msg?.ok !== true || r?.identity !== this.identity || r?.protocolVersion !== "1" ||
          typeof r?.providerId !== "string" || !r.providerId || typeof r?.sessionId !== "string" || !r.sessionId ||
          !r.capabilities || typeof r.capabilities !== "object") throw new BridgeError("provider-session-mismatch");
      this.session = { providerId: r.providerId, sessionId: r.sessionId, capabilities: r.capabilities };
      return this.session;
    })().catch(err => { this.invalidate(); throw err; });
    return this.ready;
  }
  invalidate() {
    this.closed = true; this.session = null; this.previews.clear(); this.grants.clear(); this.executions.clear();
    this.simulations.clear();
    this.transport?.close();
  }
  dispatch(action, payload) {
    const next = this.queue.then(() => this.dispatchOnce(action, payload));
    this.queue = next.catch(() => {}); return next;
  }
  async dispatchOnce(action, payload) {
    if (!allowed.has(action)) throw new BridgeError("operation-not-allowed", "authorization");
    if (action === "dispose" && (!this.session || this.closed || !this.transport))
      throw new BridgeError("no-active-session", "cleanup");
    if ((simulationActions.has(action) || (action === "preview" && payload.operation === "start-simulation")) &&
        (!this.runtimeWrites || !this.offlineWrites)) throw new BridgeError("simulation-mode-required", "authorization");
    if (["authorize", "build-generation", "compile", "verify"].includes(action) && !this.offlineWrites)
      throw new BridgeError("read-only-mode", "authorization");
    const session = await this.ensureReady();
    const capability = session.capabilities[action];
    if (capability?.available !== true || !["supported", "untested"].includes(capability.state))
      throw new BridgeError("provider-capability-unavailable", "availability");
    let request = payload;
    let preview;
    if (["authorize", "build-generation", "compile", "start-simulation", "prepare-simulation"].includes(action)) {
      preview = this.previews.get(payload.previewId);
      if (!preview) throw new BridgeError("missing-preview", "authorization");
      if (preview.request.operation === "start-simulation" && (!this.runtimeWrites || !this.offlineWrites))
        throw new BridgeError("simulation-mode-required", "authorization");
      if (Date.parse(preview.expiresAtUtc) <= Date.now()) { this.previews.delete(payload.previewId); throw new BridgeError("preview-expired", "authorization"); }
      request = { ...preview.request, previewId: preview.backendId, inputHash: preview.inputHash };
      if (action !== "authorize") {
        const grant = this.grants.get(payload.grantId); this.grants.delete(payload.grantId);
        const operation = action === "prepare-simulation" ? "start-simulation" : action;
        if (!grant || grant.previewId !== payload.previewId || preview.request.operation !== operation ||
            Date.parse(grant.expiresAtUtc) <= Date.now()) throw new BridgeError("invalid-or-consumed-grant", "authorization");
        request.grantToken = grant.token;
      }
    }
    if (action === "verify" && !this.executions.has(payload.executionId))
      throw new BridgeError("unknown-owned-execution", "authorization");
    if (action === "preview" && payload.operation === "start-simulation" && !this.executions.has(payload.executionId))
      throw new BridgeError("unknown-owned-execution", "authorization");
    if (simulationActions.has(action) && !simulationAllocations.has(action) && !this.simulations.has(payload.simulationId))
      throw new BridgeError("unknown-owned-simulation", "authorization");
    if (["simulation-state", "simulation-read", "simulation-write"].includes(action) && this.simulations.get(payload.simulationId)?.prepared)
      throw new BridgeError("simulation-not-qualified", "authorization");
    if (action === "resume-simulation" && !this.simulations.get(payload.simulationId)?.prepared)
      throw new BridgeError("unknown-prepared-simulation", "authorization");
    let msg;
    try { msg = await this.transport.call(action, { ...request, sessionId: session.sessionId, providerId: session.providerId },
      action === "dispose" ? 120000 : undefined); }
    catch (err) { this.invalidate(); throw err; }
    // ok is a transport flag only. Preserve business failure reports even for ok:false.
    const r = msg?.result;
    if (!r || r.sessionId !== session.sessionId || r.providerId !== session.providerId ||
        r.operation !== action || r.schemaVersion !== "1.0" || typeof r.phase !== "string" || typeof r.success !== "boolean" || typeof msg.ok !== "boolean") {
      this.invalidate(); throw new BridgeError("provider-result-binding-invalid");
    }
    // Do not retain a failed reply's authority. Record secrets before any public formatting.
    for (const key of ["grantToken", "token", "secret"]) if (typeof r[key] === "string") this.secrets.add(r[key]);
    if (this.secrets.size > 4096) { this.invalidate(); throw new BridgeError("session-authority-limit"); }
    if (isFailure(r, action) || !msg.ok) {
      const report = sanitize(r, [...this.secrets]);
      if (!msg.ok && report.success === true) { report.success = false; report.failures.push({ code: "provider-transport-failure" }); }
      if (action === "dispose") this.invalidate();
      return report;
    }
    if (action === "preview") {
      if (typeof r.previewId !== "string" || !/^[a-f0-9]{64}$/.test(r.inputHash || "") ||
          !Number.isFinite(Date.parse(r.expiresAtUtc)) || Date.parse(r.expiresAtUtc) <= Date.now())
        throw new BridgeError("provider-preview-invalid", "preview");
      this.expireHandles(); if (this.previews.size >= 256) throw new BridgeError("preview-limit", "preview");
      const previewId = randomUUID();
      this.previews.set(previewId, { backendId: r.previewId, request: { ...payload }, inputHash: r.inputHash, expiresAtUtc: r.expiresAtUtc });
      return { ...sanitize(r, [...this.secrets]), previewId };
    }
    if (action === "authorize") {
      if (typeof r.grantToken !== "string" || r.grantToken.length < 32 || r.inputHash !== preview.inputHash ||
          !Number.isFinite(Date.parse(r.expiresAtUtc)) || Date.parse(r.expiresAtUtc) <= Date.now() ||
          Date.parse(r.expiresAtUtc) > Date.parse(preview.expiresAtUtc))
        throw new BridgeError("provider-grant-invalid", "authorization");
      this.expireHandles(); if (this.grants.size >= 256) throw new BridgeError("grant-limit", "authorization");
      const grantId = randomUUID(); this.grants.set(grantId, { token: r.grantToken, previewId: payload.previewId, expiresAtUtc: r.expiresAtUtc });
      return { ...sanitize(r, [...this.secrets]), previewId: payload.previewId, grantId };
    }
    if (["build-generation", "compile"].includes(action)) {
      if (r.inputHash !== preview.inputHash || r.compilation?.inputHash !== preview.inputHash || typeof r.executionId !== "string") {
        this.invalidate(); throw new BridgeError("provider-execution-binding-invalid");
      }
      if (this.executions.size >= 256) throw new BridgeError("execution-limit", "execution");
      this.executions.set(r.executionId, { inputHash: preview.inputHash, projectIdentity: r.compilation.projectIdentity });
    }
    if (action === "verify" && r.inputHash !== this.executions.get(payload.executionId).inputHash) {
      this.invalidate(); throw new BridgeError("provider-verification-binding-invalid");
    }
    if (simulationAllocations.has(action)) {
      if (r.inputHash !== preview.inputHash || r.compilation?.inputHash !== preview.inputHash ||
          r.projectIdentity !== r.compilation.projectIdentity ||
          typeof r.simulationId !== "string" || !r.simulationId || this.simulations.size !== 0) {
        this.invalidate(); throw new BridgeError("provider-simulation-binding-invalid");
      }
      this.simulations.set(r.simulationId, { inputHash: preview.inputHash, projectIdentity: r.projectIdentity,
        prepared: action === "prepare-simulation" });
    } else if (simulationActions.has(action)) {
      if (r.simulationId !== payload.simulationId) {
        this.invalidate(); throw new BridgeError("provider-simulation-binding-invalid");
      }
      if (action === "resume-simulation") {
        const prior = this.simulations.get(payload.simulationId);
        if (r.inputHash !== prior.inputHash || r.compilation.inputHash !== prior.inputHash ||
            r.projectIdentity !== prior.projectIdentity || r.compilation.projectIdentity !== prior.projectIdentity) {
          this.invalidate(); throw new BridgeError("provider-simulation-binding-invalid");
        }
        prior.prepared = false;
      }
      if (action === "stop-simulation") this.simulations.delete(payload.simulationId);
    }
    const sanitized = sanitize(r, [...this.secrets]);
    if (action === "dispose") this.invalidate();
    return sanitized;
  }
  expireHandles() {
    for (const [id, p] of this.previews) if (Date.parse(p.expiresAtUtc) <= Date.now()) this.previews.delete(id);
    for (const [id, g] of this.grants) if (Date.parse(g.expiresAtUtc) <= Date.now()) this.grants.delete(id);
  }
  async dispose() {
    let report = null;
    // Use an already authenticated session only: shutdown must not spawn/reconnect.
    if (!this.closed && this.session && this.transport) {
      try {
        const msg = await this.transport.call("dispose", { sessionId: this.session.sessionId, providerId: this.session.providerId }, 120000);
        if (msg?.ok !== true || msg.result?.success !== true) report = failure("dispose", "cleanup-failed", "cleanup");
      } catch { report = failure("dispose", "cleanup-indeterminate", "cleanup"); }
    }
    this.invalidate(); this.secrets.clear();
    if (this.child && this.child.exitCode === null) {
      await new Promise(resolve => { const timer = setTimeout(resolve, 5000); this.child.once("exit", () => { clearTimeout(timer); resolve(); }); });
      if (this.child.exitCode === null) { this.child.kill(); report = failure("dispose", "owned-worker-forced-exit", "cleanup"); }
    }
    return report;
  }
}

function fromEnvironment(env = process.env) {
  const command = env.TC3_WORKER_COMMAND;
  if (!command) return new ProviderClient();
  if (!path.isAbsolute(command)) throw new BridgeError("worker-command-must-be-absolute", "configuration");
  let args = []; try { args = JSON.parse(env.TC3_WORKER_ARGS || "[]"); } catch { throw new BridgeError("worker-arguments-invalid", "configuration"); }
  if (!Array.isArray(args) || !args.every(v => typeof v === "string")) throw new BridgeError("worker-arguments-invalid", "configuration");
  const identity = randomUUID(); const pipeName = `tia-guard-tc3-${randomUUID()}`;
  const endpoint = process.platform === "win32" ? `\\\\.\\pipe\\${pipeName}` : path.join(env.TMPDIR || "/tmp", `${pipeName}.sock`);
  const offlineWrites = env.TC3_OFFLINE_WRITES === "1";
  const runtimeWrites = env.TC3_RUNTIME_WRITES === "1";
  const child = spawn(command, args, { shell: false, detached: false, windowsHide: true,
    stdio: ["ignore", "ignore", "pipe"], env: { ...env, TC3_PIPE: endpoint, TC3_IDENTITY: identity,
      TC3_OFFLINE_WRITES: offlineWrites ? "1" : "0", TC3_RUNTIME_WRITES: runtimeWrites ? "1" : "0" } });
  child.on("error", () => {}); child.stderr.on("data", () => {}); // Never publish raw backend stderr/secrets.
  const transport = new DaemonClient({ endpoint, identity });
  return new ProviderClient({ transport, identity, offlineWrites, runtimeWrites, child });
}
module.exports = { ProviderClient, fromEnvironment };
