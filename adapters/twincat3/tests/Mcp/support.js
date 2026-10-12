"use strict";
const path = require("node:path");
const { createRequire } = require("node:module");
const mcpRoot = path.resolve(__dirname, "../../mcp");
const mcpRequire = createRequire(path.join(mcpRoot, "package.json"));
const { Client } = mcpRequire("@modelcontextprotocol/client");
const { StdioClientTransport } = mcpRequire("@modelcontextprotocol/client/stdio");
async function connectMcp(env = {}, mode = "legacy") {
  const transport = new StdioClientTransport({ command: process.execPath, args: [path.join(mcpRoot, "index.js")],
    stderr: "pipe", env: { ...process.env, TC3_WORKER_COMMAND: "", TC3_WORKER_ARGS: "[]", TC3_OFFLINE_WRITES: "0", ...env } });
  let stderr = ""; transport.stderr.on("data", chunk => { stderr += chunk.toString(); });
  const client = new Client({ name: "tc57-test", version: "1.0.0" }, { mode });
  try { await client.connect(transport); }
  catch (err) { await transport.close(); throw err; }
  return { client, transport, stderr: () => stderr, close: () => client.close() };
}
module.exports = { mcpRoot, mcpRequire, connectMcp };
