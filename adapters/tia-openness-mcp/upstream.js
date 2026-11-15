"use strict";
const fs = require("node:fs");
const path = require("node:path");
const { randomUUID } = require("node:crypto");
const { setTimeout: delay } = require("node:timers/promises");
const { Client } = require("@modelcontextprotocol/client");
const { StdioClientTransport, getDefaultEnvironment } = require("@modelcontextprotocol/client/stdio");
const { AdapterError, object, sha256, runtimeDigest, checkTools, payload, assertDisconnected } = require("./contract.js");
const lock = require("./upstream.lock.json");

function readJson(file) { return JSON.parse(fs.readFileSync(file, "utf8").replace(/^\uFEFF/, "")); }
function verifyRuntime(config, pin) {
  if (!object(config) || config.schemaVersion !== 1 || config.version !== pin.version ||
      !path.isAbsolute(config.runtimeDirectory ?? "") || !path.isAbsolute(config.tiaPortalLocation ?? "") ||
      !object(config.files)) throw new AdapterError("invalid-local-configuration", "configuration");
  if (runtimeDigest(config.files) !== pin.runtimeManifestSha256 ||
      config.files[pin.executable] !== pin.executableSha256) {
    throw new AdapterError("runtime-manifest-mismatch", "integrity");
  }
  const root = fs.realpathSync(config.runtimeDirectory);
  const expected = Object.keys(config.files).sort();
  const mutable = pin.mutableRuntimeFiles ?? [];
  if (!Array.isArray(mutable) || mutable.some(name => !/^[A-Za-z0-9_.-]+\.log$/.test(name) || expected.includes(name))) {
    throw new AdapterError("invalid-mutable-runtime-file-policy", "integrity");
  }
  const actual = [];
  function walk(directory, prefix = "") {
    for (const item of fs.readdirSync(directory, { withFileTypes: true })) {
      if (item.isSymbolicLink()) throw new AdapterError("runtime-link-rejected", "integrity");
      const relative = prefix + item.name;
      if (item.isDirectory()) walk(path.join(directory, item.name), relative + "/");
      else if (item.isFile()) actual.push(relative);
      else throw new AdapterError("runtime-entry-rejected", "integrity");
    }
  }
  walk(root);
  if (JSON.stringify(actual.filter(name => !mutable.includes(name)).sort()) !== JSON.stringify(expected)) {
    throw new AdapterError("runtime-file-set-drift", "integrity");
  }
  for (const name of expected) {
    if (!name || name.includes("\\") || name.includes(":") || name.includes("\0") ||
        name.split("/").some(part => !part || part === ".." || part === ".") || path.isAbsolute(name)) {
      throw new AdapterError("invalid-runtime-path", "integrity");
    }
    if (sha256(fs.readFileSync(path.join(root, ...name.split("/")))) !== config.files[name]) {
      throw new AdapterError("runtime-file-hash-drift", "integrity");
    }
  }
  return { root, executable: path.join(root, pin.executable), fileCount: expected.length };
}
function launchParameters(config, pin) {
  const verified = verifyRuntime(config, pin);
  return { command: verified.executable,
    args: ["--read-only", "--profile", "full", "--tia-major-version", String(pin.tiaMajorVersion),
      "--tia-portal-location", config.tiaPortalLocation],
    cwd: verified.root, stderr: "pipe",
    env: { ...getDefaultEnvironment(), MCP_TRANSPORT: "stdio", TIA_MCP_READ_ONLY: "1", TIA_MCP_PROFILE: "full" } };
}
function pidPresent(pid) {
  if (!pid) return false;
  try { process.kill(pid, 0); return true; }
  catch (error) { return error.code !== "ESRCH"; }
}
class Upstream {
  constructor({ config, pin = lock.candidate, transportFactory = p => new StdioClientTransport(p), timeoutMs = 20000 } = {}) {
    this.config = config; this.pin = pin; this.transportFactory = transportFactory; this.timeoutMs = timeoutMs;
    this.client = null; this.transport = null; this.identity = null; this.contract = null;
    this.closed = false; this.failed = false; this.queue = Promise.resolve(); this.stderr = "";
    this.lastPid = null; this.engineSessionId = null;
  }
  serial(operation) {
    const pending = this.queue.then(operation); this.queue = pending.catch(() => {}); return pending;
  }
  status() {
    return { adapterVersion: "0.1.0", stage: this.config?.ownedRead ? "owned-read-candidate" : "P0-discovery", upstreamVersion: this.pin.version,
      sourceCommit: this.pin.sourceCommit, configured: !!this.config, started: !!this.client,
      closed: this.closed, failed: this.failed, engineSessionId: this.engineSessionId,
      accepted: lock.accepted, rollback: lock.rollback,
      engineeringQualified: false, runtimeQualified: false };
  }
  inspectCopy() {
    return this.serial(async () => {
      if (!this.config?.ownedRead) throw new AdapterError("owned-read-not-configured", "configuration");
      try {
        await this.start();
        const { inspectOwnedCopy, verifyReadContract } = require("./owned-read.js");
        const tools = await this.client.listTools({}, { timeout: this.timeoutMs });
        verifyReadContract(tools.tools, this.pin.ownedReadProtocolSha256);
        return await inspectOwnedCopy(this.client, this.config.ownedRead);
      } catch (error) {
        this.failed = true; throw error;
      } finally {
        this.closed = true; await this.stopInternal();
      }
    });
  }
  async start() {
    if (this.closed || this.failed) throw new AdapterError("upstream-session-ended", "lifecycle");
    if (this.client) return;
    if (!this.config) throw new AdapterError("missing-local-configuration", "configuration");
    const parameters = launchParameters(this.config, this.pin);
    this.transport = this.transportFactory(parameters);
    this.transport.stderr?.on("data", chunk => { this.stderr = (this.stderr + chunk.toString()).slice(-8192); });
    const client = new Client({ name: "tia-guard-upstream", version: "0.1.0" }, { mode: "legacy" });
    this.client = client;
    try {
      await client.connect(this.transport, { timeout: this.timeoutMs });
      this.lastPid = this.transport.pid;
      this.engineSessionId = randomUUID();
      this.identity = client.getServerVersion();
      const inventory = await client.listTools({}, { timeout: this.timeoutMs });
      this.contract = checkTools(inventory.tools);
      if (this.pin.protocol && (this.identity?.name !== this.pin.protocol.serverName ||
          this.identity?.version !== this.pin.protocol.serverVersion ||
          this.contract.upstreamToolCount !== this.pin.protocol.toolCount ||
          this.contract.schemaSha256 !== this.pin.protocol.schemaSha256)) {
        throw new AdapterError("pinned-protocol-identity-drift", "contract");
      }
      const state = payload(await client.callTool({ name: "GetState", arguments: {} }, { timeout: this.timeoutMs }));
      assertDisconnected(state);
    } catch (error) {
      this.lastPid ??= this.transport.pid;
      this.failed = true;
      await this.stopInternal();
      throw error instanceof AdapterError ? error : new AdapterError("upstream-start-failed", "transport");
    }
  }
  async stateInternal() {
    const state = payload(await this.client.callTool({ name: "GetState", arguments: {} }, { timeout: this.timeoutMs }));
    return assertDisconnected(state);
  }
  async invoke(operation) {
    // Fixed semantic operations only: there is deliberately no arbitrary tool/argument proxy.
    if (!["capabilities", "state", "environment"].includes(operation)) {
      throw new AdapterError("operation-not-allowed", "policy");
    }
    return this.serial(async () => {
      try {
        await this.start();
        const before = await this.stateInternal();
        if (operation === "state") return { ...this.status(), upstream: before };
        if (operation === "capabilities") return { ...this.status(), server: this.identity, ...this.contract };
        const diagnostic = payload(await this.client.callTool({ name: "Doctor", arguments: { fix: false } },
          { timeout: this.timeoutMs }));
        await this.stateInternal();
        return { ...this.status(), upstream: diagnostic, repairsEnabled: false };
      } catch (error) {
        this.failed = true;
        await this.stopInternal();
        throw error instanceof AdapterError ? error : new AdapterError("upstream-request-failed", "transport");
      }
    });
  }
  async stopInternal() {
    const client = this.client; this.client = null;
    const transport = this.transport; this.transport = null;
    this.lastPid ??= transport?.pid;
    try { if (client) await client.close(); else if (transport) await transport.close(); }
    catch { throw new AdapterError("upstream-cleanup-failed", "cleanup"); }
    // The SDK can clear transport.pid before its final process kill has completed.
    // Observe only that child PID; never kill a process rediscovered by name or PID.
    for (let i = 0; i < 30 && pidPresent(this.lastPid); i++) await delay(100);
    if (pidPresent(this.lastPid)) throw new AdapterError("upstream-exit-unconfirmed", "cleanup");
  }
  close() { return this.serial(async () => { this.closed = true; await this.stopInternal(); return { cleanupConfirmed: true }; }); }
}
function fromEnvironment() {
  const file = process.env.TG_TIA_UPSTREAM_CONFIG;
  if (file && !path.isAbsolute(file)) throw new AdapterError("configuration-path-must-be-absolute", "configuration");
  return new Upstream({ config: file ? readJson(file) : null });
}
module.exports = { Upstream, fromEnvironment, readJson, verifyRuntime, launchParameters };
