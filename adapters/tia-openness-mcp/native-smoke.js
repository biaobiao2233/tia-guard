#!/usr/bin/env node
"use strict";
// Opt-in real-runtime acceptance. Keep its raw receipt local, not in source control.
const path = require("node:path");
const assert = require("node:assert/strict");
const { Client } = require("@modelcontextprotocol/client");
const { StdioClientTransport, getDefaultEnvironment } = require("@modelcontextprotocol/client/stdio");
const pin = require("./upstream.lock.json").candidate;
async function main() {
  if (!process.env.TG_TIA_UPSTREAM_CONFIG) throw new Error("Explicit prepared config is required");
  const transport = new StdioClientTransport({ command: process.execPath, args: [path.join(__dirname, "index.js")],
    cwd: __dirname, stderr: "pipe", env: { ...getDefaultEnvironment(), TG_TIA_UPSTREAM_CONFIG: process.env.TG_TIA_UPSTREAM_CONFIG } });
  let stderr = ""; transport.stderr.on("data", chunk => { stderr = (stderr + chunk.toString()).slice(-8192); });
  const client = new Client({ name: "tia-guard-native-acceptance", version: "0.1.0" }, { mode: "legacy" });
  const receipt = { protocolVerified: false, engineeringQualified: false, runtimeQualified: false };
  try {
    await client.connect(transport, { timeout: 20000 });
    const tools = (await client.listTools()).tools;
    assert.equal(tools.length, 6); receipt.facadeTools = tools.map(tool => tool.name).sort();
    const call = async (name, args = {}, allowError = false) => {
      const result = await client.callTool({ name, arguments: args }, { timeout: 20000 });
      if (result.isError === true && !allowError) throw new Error(name + ":" + (result.structuredContent?.code ?? "mcp-tool-error"));
      return result;
    };
    receipt.capabilities = (await call("upstream_capabilities")).structuredContent;
    assert.equal(receipt.capabilities?.upstreamVersion, pin.version);
    assert.equal(receipt.capabilities?.server?.version, pin.protocol.serverVersion);
    assert.equal(receipt.capabilities?.upstreamToolCount, pin.protocol.toolCount);
    receipt.state = (await call("upstream_state")).structuredContent;
    assert.equal(receipt.state?.upstream?.isConnected, false);
    receipt.environment = (await call("upstream_environment")).structuredContent;
    assert.equal(receipt.environment?.repairsEnabled, false);
    assert.equal(receipt.environment?.upstream?.meta?.success, true);
    receipt.reusedCapabilities = (await call("upstream_capabilities")).structuredContent;
    assert.equal(receipt.reusedCapabilities?.engineSessionId, receipt.capabilities.engineSessionId);
    for (const [name, args] of [["Connect", {}], ["upstream_environment", { fix: true }]]) {
      let rejected = false;
      try { rejected = (await call(name, args)).isError === true; } catch { rejected = true; }
      assert.equal(rejected, true);
    }
    receipt.forbiddenCallsRejected = true;
    const cleanup = await call("upstream_close");
    assert.equal(cleanup.structuredContent?.cleanupConfirmed, true);
    receipt.cleanupConfirmed = true;
    const ended = await call("upstream_state", {}, true);
    assert.equal(ended.isError, true);
    assert.equal(ended.structuredContent?.code, "upstream-session-ended");
    receipt.protocolVerified = true;
  } catch (error) { receipt.error = error.message; receipt.stderr = stderr; process.exitCode = 1; }
  finally {
    if (!receipt.cleanupConfirmed) {
      try {
        const cleanup = await client.callTool({ name: "upstream_close", arguments: {} }, { timeout: 10000 });
        receipt.cleanupConfirmed = cleanup.structuredContent?.cleanupConfirmed === true;
      } catch { receipt.cleanupConfirmed = false; }
      if (!receipt.cleanupConfirmed) { receipt.protocolVerified = false; process.exitCode = 1; }
    }
    await client.close(); process.stdout.write(JSON.stringify(receipt, null, 2) + "\n");
  }
}
main().catch(error => { process.stderr.write(error.message + "\n"); process.exitCode = 1; });
