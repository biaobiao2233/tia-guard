"use strict";
// An explicit protocol fixture, not evidence of TIA engineering capability.
const fs = require("node:fs");
const z = require("zod/v4");
const { McpServer } = require("@modelcontextprotocol/server");
const { serveStdio } = require("@modelcontextprotocol/server/stdio");
const scenario = process.env.UPSTREAM_TEST_SCENARIO;
const server = new McpServer({ name: "fixture-upstream", version: "0.0.0" });
function log(tool, args) {
  if (process.env.UPSTREAM_TEST_CALLS) fs.appendFileSync(process.env.UPSTREAM_TEST_CALLS,
    JSON.stringify({ tool, args, pid: process.pid }) + "\n");
}
function result(value) { return { content: [{ type: "text", text: JSON.stringify(value) }], structuredContent: value }; }
server.registerTool("Bootstrap", { inputSchema: z.strictObject({}) }, async args => { log("Bootstrap", args); return result({ fixture: true }); });
server.registerTool("GetState", { inputSchema: scenario === "required" ? z.strictObject({ project: z.string() }) : z.strictObject({}) }, async args => {
  log("GetState", args);
  if (scenario === "slow") await new Promise(() => {});
  if (scenario === "crash") process.exit(42);
  if (scenario === "text") return { content: [{ type: "text", text: "unstructured failure" }] };
  return result({ isConnected: scenario === "connected", meta: { success: scenario !== "failure" } });
});
server.registerTool("Doctor", { inputSchema: z.strictObject({ fix: z.boolean().optional() }) }, async args => {
  log("Doctor", args); return result({ ready: true, meta: { success: true }, fix: args.fix });
});
const handle = serveStdio(() => server);
process.stdin.once("end", () => handle.close());
