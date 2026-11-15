#!/usr/bin/env node
"use strict";
const { McpServer } = require("@modelcontextprotocol/server");
const { serveStdio } = require("@modelcontextprotocol/server/stdio");
const z = require("zod/v4");
const { AdapterError } = require("./contract.js");
const { fromEnvironment } = require("./upstream.js");
const { checkUpdate } = require("./update.js");

function envelope(value) { return { content: [{ type: "text", text: JSON.stringify(value) }], structuredContent: value }; }
function buildServer(provider) {
  const server = new McpServer({ name: "tia-guard-upstream-tia", version: "0.1.0" });
  for (const [name, operation, description] of [
    ["upstream_status", null, "Inspect local adapter configuration without launching the upstream engine."],
    ["upstream_capabilities", "capabilities", "Verify the pinned runtime and inspect its public MCP contract. Does not connect to TIA."],
    ["upstream_state", "state", "Read disconnected upstream state. Connecting or attaching to TIA is not available in this P0 adapter."],
    ["upstream_environment", "environment", "Run upstream Doctor with fix=false and verify it remains disconnected. No environment repairs."],
    ["upstream_close", "close", "Stop only this adapter's upstream child. The session cannot restart until the adapter is restarted."],
    ["upstream_update_check", "update", "Read official release metadata and compare it with the pinned candidate. Never download, execute or promote a release."]
  ]) {
    server.registerTool(name, { description, inputSchema: z.strictObject({}),
      annotations: { readOnlyHint: operation !== "close", destructiveHint: false, idempotentHint: true, openWorldHint: operation === "update" } }, async () => {
      try {
        if (operation === "close") return envelope(await provider.close());
        if (operation === "update") return envelope(await checkUpdate());
        return envelope(operation ? await provider.invoke(operation) : provider.status());
      }
      catch (error) {
        const safe = error instanceof AdapterError ? error : new AdapterError("adapter-operation-failed");
        return { ...envelope({ success: false, code: safe.code, phase: safe.phase,
          engineeringQualified: false, runtimeQualified: false }), isError: true };
      }
    });
  }
  return server;
}
function main(provider = fromEnvironment()) {
  const handle = serveStdio(() => buildServer(provider), { onerror: () => {} });
  let closing;
  const close = () => closing ??= (async () => {
    try { await provider.close(); } finally { await handle.close(); }
  })().catch(() => { process.stderr.write("upstream adapter cleanup failed\n"); process.exitCode = 1; });
  process.stdin.once("end", close);
  process.once("SIGINT", close); process.once("SIGTERM", close);
  return { close };
}
if (require.main === module) {
  try { main(); }
  catch { process.stderr.write("upstream adapter configuration invalid\n"); process.exitCode = 1; }
}
module.exports = { buildServer, main };
