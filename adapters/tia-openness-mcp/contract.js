"use strict";
const crypto = require("node:crypto");

class AdapterError extends Error {
  constructor(code, phase = "adapter") { super(code); this.code = code; this.phase = phase; }
}
const object = value => value !== null && typeof value === "object" && !Array.isArray(value);
const sha256 = value => crypto.createHash("sha256").update(value).digest("hex");
function canonical(value) {
  if (Array.isArray(value)) return value.map(canonical);
  if (object(value)) return Object.fromEntries(Object.keys(value).sort().map(key => [key, canonical(value[key])]));
  return value;
}
function runtimeDigest(files) {
  return sha256(Object.keys(files).sort().map(name => name + "\0" + files[name] + "\n").join(""));
}
function checkTools(tools) {
  if (!Array.isArray(tools) || tools.length > 1000) throw new AdapterError("invalid-tool-inventory", "contract");
  const byName = new Map();
  for (const tool of tools) {
    if (!object(tool) || typeof tool.name !== "string" || byName.has(tool.name)) {
      throw new AdapterError("duplicate-or-invalid-tool", "contract");
    }
    byName.set(tool.name, tool);
  }
  const supported = ["Bootstrap", "GetState", "Doctor"];
  const projection = [];
  for (const name of supported) {
    const tool = byName.get(name);
    if (!tool) throw new AdapterError("missing-required-tool:" + name, "contract");
    const schema = tool.inputSchema;
    if (!object(schema) || schema.type !== "object" || !Array.isArray(schema.required ?? [])) {
      throw new AdapterError("invalid-input-schema:" + name, "contract");
    }
    const permitted = name === "Doctor" ? ["fix"] : [];
    if ((schema.required ?? []).some(key => !permitted.includes(key))) {
      throw new AdapterError("new-required-parameter:" + name, "contract");
    }
    if (name === "Doctor" && schema.properties?.fix?.type !== "boolean") {
      throw new AdapterError("doctor-fix-contract-mismatch", "contract");
    }
    projection.push({ name, inputSchema: schema, ...(tool.outputSchema ? { outputSchema: tool.outputSchema } : {}) });
  }
  return { upstreamToolCount: tools.length, allowedTools: supported,
    schemaSha256: sha256(JSON.stringify(canonical(projection))) };
}
function payload(result) {
  if (!object(result) || result.isError === true) throw new AdapterError("upstream-tool-error", "upstream");
  let value = result.structuredContent;
  if (value === undefined) {
    const blocks = (result.content ?? []).filter(item => item.type === "text");
    if (blocks.length !== 1) throw new AdapterError("unstructured-upstream-result", "contract");
    try { value = JSON.parse(blocks[0].text); }
    catch { throw new AdapterError("invalid-upstream-json", "contract"); }
  }
  if (!object(value)) throw new AdapterError("invalid-upstream-payload", "contract");
  if (value.success === false || value.ok === false || value.meta?.success === false || value.complete === false) {
    throw new AdapterError("upstream-reported-failure-or-incomplete", "upstream");
  }
  return value;
}
function alias(value, lower, upper) {
  if (!object(value)) throw new AdapterError("missing-state-object", "contract");
  if (Object.hasOwn(value, lower) && Object.hasOwn(value, upper) && value[lower] !== value[upper]) {
    throw new AdapterError("conflicting-state-fields", "contract");
  }
  return value[lower] ?? value[upper];
}
function assertDisconnected(value) {
  const connected = alias(value, "isConnected", "IsConnected");
  if (connected !== false) throw new AdapterError("unexpected-or-unknown-tia-connection", "ownership");
  for (const [lower, upper] of [["project", "Project"], ["session", "Session"]]) {
    const bound = alias(value, lower, upper);
    if (bound !== undefined && bound !== null && bound !== "" && bound !== "-") {
      throw new AdapterError("unexpected-project-or-session-binding", "ownership");
    }
  }
  return value;
}
module.exports = { AdapterError, object, canonical, sha256, runtimeDigest, checkTools, payload, alias, assertDisconnected };
