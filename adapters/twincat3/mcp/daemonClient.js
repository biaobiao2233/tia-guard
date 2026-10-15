"use strict";
// Adapted from Edge Automation daemonClient.js at 30d8e2938eeed1c41ee24765b469d13430ab0a32.
// Copyright (c) 2026 Edge Automation. MIT; see licenses/Edge-LICENSE.
// NDJSON id/action/payload framing retained. No default shared pipe, spawn or retry.
const net = require("node:net");
const { StringDecoder } = require("node:string_decoder");
const { BridgeError } = require("./result.js");

class DaemonClient {
  constructor({ endpoint, identity, timeoutMs = 180000, connectMs = 5000, maxBytes = 8 * 1024 * 1024 }) {
    if (!endpoint || !identity) throw new BridgeError("missing-provider-identity");
    this.endpoint = endpoint; this.identity = identity; this.timeoutMs = timeoutMs;
    this.connectMs = connectMs; this.maxBytes = maxBytes; this.nextId = 1;
    this.pending = new Map(); this.socket = null; this.connecting = null; this.closed = false;
  }
  async connect() {
    if (this.closed) throw new BridgeError("session-closed");
    if (this.socket) return this.socket;
    if (this.connecting) return this.connecting;
    this.connecting = new Promise((resolve, reject) => {
      const s = net.connect(this.endpoint);
      const timer = setTimeout(() => fail(), this.connectMs);
      const fail = () => { clearTimeout(timer); s.destroy(); reject(new BridgeError("provider-unavailable")); };
      s.once("error", fail);
      s.once("connect", () => {
        clearTimeout(timer); s.removeListener("error", fail);
        if (this.closed) { s.destroy(); reject(new BridgeError("session-closed")); return; }
        this.socket = s; this.attach(s); resolve(s);
      });
    });
    try { return await this.connecting; } finally { this.connecting = null; }
  }
  attach(s) {
    const decoder = new StringDecoder("utf8"); let buf = "";
    s.setNoDelay(true);
    s.on("data", chunk => {
      buf += decoder.write(chunk);
      let nl;
      while ((nl = buf.indexOf("\n")) >= 0) {
        const line = buf.slice(0, nl); buf = buf.slice(nl + 1);
        if (Buffer.byteLength(line) > this.maxBytes) return this.drop("provider-frame-too-large");
        if (!line.trim()) continue;
        let msg; try { msg = JSON.parse(line); } catch { return this.drop("provider-frame-invalid"); }
        const waiter = this.pending.get(String(msg?.id));
        // A stale/unknown reply makes the session indeterminate; do not ignore it.
        if (!waiter) return this.drop("provider-response-id-mismatch");
        this.pending.delete(String(msg.id)); clearTimeout(waiter.timer); waiter.resolve(msg);
      }
      if (Buffer.byteLength(buf) > this.maxBytes) this.drop("provider-frame-too-large");
    });
    s.on("error", () => this.drop("provider-pipe-closed"));
    s.on("close", () => this.drop("provider-pipe-closed"));
  }
  drop(code) {
    const s = this.socket; this.socket = null; this.closed = true;
    for (const waiter of this.pending.values()) { clearTimeout(waiter.timer); waiter.reject(new BridgeError(code)); }
    this.pending.clear(); if (s) s.destroy();
  }
  async call(action, payload = {}, timeoutMs = this.timeoutMs) {
    const s = await this.connect(); const id = String(this.nextId++);
    const line = JSON.stringify({ id, action, payload, identity: this.identity }) + "\n";
    if (Buffer.byteLength(line) > this.maxBytes) throw new BridgeError("provider-request-too-large");
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => this.drop("provider-timeout-indeterminate"), timeoutMs);
      this.pending.set(id, { resolve, reject, timer });
      s.write(line, err => { if (err) this.drop("provider-pipe-closed"); });
    });
  }
  close() { this.drop("session-closed"); }
}
module.exports = { DaemonClient };
