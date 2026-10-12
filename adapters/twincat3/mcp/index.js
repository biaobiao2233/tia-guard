#!/usr/bin/env node
"use strict";
// Derived from Edge Automation index.js at 30d8e2938eeed1c41ee24765b469d13430ab0a32.
// Copyright (c) 2026 Edge Automation. MIT; see licenses/Edge-LICENSE.
const { McpServer } = require("@modelcontextprotocol/server");
const { serveStdio } = require("@modelcontextprotocol/server/stdio");
const { toolSchemas } = require("./toolSchemas.js");
const { fromEnvironment } = require("./providerClient.js");
const { BridgeError, failure, toMcpResult } = require("./result.js");

function buildServer(provider) {
  const server = new McpServer({ name: "tia-guard-twincat3", version: "0.1.0" }, { capabilities: { tools: {} } });
  for (const [name, tool] of Object.entries(toolSchemas)) {
    server.registerTool(name, {
      description: tool.description + " Backend availability/evidence must be read from doctor_twincat; a tools/list entry alone proves only adapter support.",
      inputSchema: tool.inputSchema,
      annotations: { readOnlyHint: tool.readOnly, destructiveHint: !tool.readOnly, idempotentHint: tool.readOnly, openWorldHint: false },
      _meta: { "tia-guard/backendState": provider.transport ? "untested" : "unavailable", "tia-guard/adapterSupported": true }
    }, async params => {
      try {
        const parsed = tool.inputSchema.safeParse(params);
        if (!parsed.success) return toMcpResult(failure(tool.action, "invalid-arguments", "validation"), tool.action);
        const result = await provider.dispatch(tool.action, parsed.data);
        return toMcpResult(result, tool.action, [...provider.secrets]);
      } catch (err) {
        const safe = err instanceof BridgeError ? err : new BridgeError("provider-operation-failed");
        return toMcpResult(failure(tool.action, safe.code, safe.phase), tool.action, [...provider.secrets]);
      }
    });
  }
  return server;
}

function main() {
  let provider;
  try { provider = fromEnvironment(); }
  catch { process.stderr.write("TwinCAT MCP configuration invalid\n"); process.exitCode = 1; return; }
  const handle = serveStdio(() => buildServer(provider), { onerror: () => {} });
  let closing = null;
  const close = () => {
    if (!closing) closing = (async () => {
      await handle.close(); const report = await provider.dispose();
      if (report) { process.stderr.write(JSON.stringify(report) + "\n"); process.exitCode = 1; }
    })().catch(() => { process.stderr.write("TwinCAT MCP cleanup failed\n"); process.exitCode = 1; });
    return closing;
  };
  process.stdin.once("end", close);
  process.once("SIGINT", close); process.once("SIGTERM", close);
}
if (require.main === module) main();
module.exports = { buildServer, main };
