"use strict";
const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs/promises");
const path = require("node:path");
const { Client } = require("@modelcontextprotocol/client");
const { StdioClientTransport, getDefaultEnvironment } = require("@modelcontextprotocol/client/stdio");
const { fixture } = require("./support.js");
test("facade really speaks MCP, blocks generic writes and caller repairs, and closes its child", async t => {
  const f = await fixture(t);
  const options = path.join(f.root, "options.json"); await fs.writeFile(options, JSON.stringify({ config: f.config, pin: f.pin }));
  const transport = new StdioClientTransport({ command: process.execPath, args: [path.join(__dirname, "adapter-fixture.js")],
    cwd: __dirname, stderr: "pipe", env: { ...getDefaultEnvironment(), UPSTREAM_TEST_OPTIONS: options, UPSTREAM_TEST_CALLS: f.calls } });
  transport.stderr.on("data", () => {});
  const client = new Client({ name: "adapter-test", version: "1.0.0" }, { mode: "legacy" });
  try {
    await client.connect(transport);
    const inventory = (await client.listTools()).tools;
    assert.equal(inventory.length, 6);
    assert.equal(inventory.some(tool => /Connect|Download|CallTool|press/i.test(tool.name)), false);
    const status = await client.callTool({ name: "upstream_status", arguments: {} });
    assert.equal(status.structuredContent.started, false);
    const first = await client.callTool({ name: "upstream_capabilities", arguments: {} });
    assert.equal(first.isError, undefined);
    const before = (await f.readCalls()).length;
    async function denied(name, args) {
      try { return (await client.callTool({ name, arguments: args })).isError === true; }
      catch { return true; }
    }
    assert.equal(await denied("Connect", {}), true);
    assert.equal(await denied("upstream_environment", { fix: true }), true);
    assert.equal((await f.readCalls()).length, before);
    const env = await client.callTool({ name: "upstream_environment", arguments: {} });
    assert.equal(env.structuredContent.repairsEnabled, false);
    const child = (await f.readCalls())[0].pid;
    const close = await client.callTool({ name: "upstream_close", arguments: {} });
    assert.equal(close.structuredContent.cleanupConfirmed, true);
    assert.throws(() => process.kill(child, 0));
    const ended = await client.callTool({ name: "upstream_state", arguments: {} });
    assert.equal(ended.isError, true); assert.equal(ended.structuredContent.code, "upstream-session-ended");
  } finally { await client.close(); }
});
test("production facade reports missing configuration without pretending it has a backend", async () => {
  const transport = new StdioClientTransport({ command: process.execPath, args: [path.join(__dirname, "../index.js")],
    stderr: "pipe", env: { ...getDefaultEnvironment(), TG_TIA_UPSTREAM_CONFIG: "" } });
  transport.stderr.on("data", () => {});
  const client = new Client({ name: "missing-config-test", version: "1.0.0" }, { mode: "legacy" });
  try {
    await client.connect(transport);
    const r = await client.callTool({ name: "upstream_state", arguments: {} });
    assert.equal(r.isError, true); assert.equal(r.structuredContent.code, "missing-local-configuration");
  } finally { await client.close(); }
});
test("owned-copy MCP is opt-in and caller paths are rejected before any upstream launch", async t => {
  const f=await fixture(t); f.config.ownedRead={sourceProjectFile:path.join(f.root,"Owned.ap21"),artifactRoot:path.join(f.root,"artifacts")};f.config.ownedProgram={};
  const options=path.join(f.root,"owned-options.json");await fs.writeFile(options,JSON.stringify({config:f.config,pin:f.pin}));
  const transport=new StdioClientTransport({command:process.execPath,args:[path.join(__dirname,"adapter-fixture.js")],cwd:__dirname,stderr:"pipe",env:{...getDefaultEnvironment(),UPSTREAM_TEST_OPTIONS:options,UPSTREAM_TEST_CALLS:f.calls}});
  transport.stderr.on("data",()=>{});const client=new Client({name:"owned-scope-test",version:"1.0.0"},{mode:"legacy"});
  try{
    await client.connect(transport);const tools=(await client.listTools()).tools;
    assert.equal(tools.filter(t=>t.name==="upstream_inspect_copy").length,1);
    let denied=false;try{denied=(await client.callTool({name:"upstream_inspect_copy",arguments:{sourceProjectFile:path.join(f.root,"Other.ap21")}})).isError===true;}catch{denied=true;}
    assert.equal(denied,true);
    let writeDenied=false;try{writeDenied=(await client.callTool({name:"upstream_program_copy",arguments:{sourceFile:"Other.scl"}})).isError===true;}catch{writeDenied=true;}
    assert.equal(writeDenied,true);await assert.rejects(fs.access(f.calls));
    const close=await client.callTool({name:"upstream_close",arguments:{}});assert.equal(close.structuredContent.cleanupConfirmed,true);
  }finally{await client.close();}
});
