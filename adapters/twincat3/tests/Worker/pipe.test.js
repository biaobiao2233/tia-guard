"use strict";
// Actual executable/pipe protocol checks. Offline writes stay disabled; no COM is created.
const test = require("node:test");
const assert = require("node:assert/strict");
const net = require("node:net");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const crypto = require("node:crypto");
const { spawn } = require("node:child_process");
const { once } = require("node:events");
const command = path.resolve(__dirname, "../../src/Worker/bin/Release/net8.0-windows/TwinCat3.Worker.exe");
const wait = ms => new Promise(resolve => setTimeout(resolve, ms));

async function start() {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "tc53-pipe-"));
  const scratch = path.join(root, "scratch"); fs.mkdirSync(scratch);
  const endpoint = `\\\\.\\pipe\\tc53-test-${crypto.randomUUID()}`;
  const identity = crypto.randomUUID();
  const child = spawn(command, [], { windowsHide: true, stdio: ["ignore", "ignore", "pipe"],
    env: { ...process.env, TC3_PIPE: endpoint, TC3_IDENTITY: identity, TC3_WORKSPACE: root,
      TC3_SCRATCH: scratch, TC3_OFFLINE_WRITES: "0" } });
  let stderr = ""; child.stderr.on("data", b => { stderr += b; });
  const exited = once(child, "exit").then(([code]) => code);
  let socket;
  for (let attempt = 0; attempt < 100; attempt++) {
    try {
      socket = await new Promise((resolve, reject) => {
        const s = net.connect(endpoint); s.once("error", e => { s.destroy(); reject(e); });
        s.once("connect", () => { s.removeAllListeners("error"); s.on("error", () => {}); resolve(s); });
      }); break;
    } catch { if (child.exitCode !== null) throw new Error(`Worker startup failed: ${stderr}`); await wait(50); }
  }
  assert.ok(socket, "worker pipe startup");
  let nextId = 1, buffer = ""; const pending = new Map();
  socket.setEncoding("utf8");
  socket.on("data", chunk => {
    buffer += chunk;
    let end;
    while ((end = buffer.indexOf("\n")) >= 0) {
      const response = JSON.parse(buffer.slice(0, end)); buffer = buffer.slice(end + 1);
      pending.get(response.id)?.(response); pending.delete(response.id);
    }
  });
  async function call(action, payload = {}, ownIdentity = identity, id = String(nextId++)) {
    let timer;
    try { return await Promise.race([
      new Promise(resolve => { pending.set(id, resolve); socket.write(JSON.stringify({ id, action, identity: ownIdentity, payload }) + "\n"); }),
      new Promise((_, reject) => { timer = setTimeout(() => reject(new Error("protocol timeout")), 10000); })
    ]); } finally { clearTimeout(timer); }
  }
  return { root, scratch, socket, child, identity, call, exited,
    async cleanup() { socket.destroy(); if (child.exitCode === null) { await Promise.race([exited, wait(10000)]); if (child.exitCode === null) child.kill(); } await exited; fs.rmSync(root, { recursive: true, force: true }); } };
}

test("real executable hello/doctor/static inspect/preview/dispose do not mutate or create COM", { skip: process.platform !== "win32" }, async () => {
  const worker = await start();
  try {
    const hello = (await worker.call("hello")).result;
    assert.equal(hello.protocolVersion, "1"); assert.equal(hello.identity, worker.identity);
    assert.equal(hello.capabilities.authorize.available, false);
    const bind = { providerId: hello.providerId, sessionId: hello.sessionId };
    const doctor = (await worker.call("doctor", bind)).result;
    assert.equal(doctor.nativeCreated, false); assert.equal(doctor.success, true);
    const project = path.join(worker.root, "readonly.tsproj");
    const original = "<TcSmProject TcVersion=\"3.1.4026.27\"><Project /></TcSmProject>";
    fs.writeFileSync(project, original);
    const inspection = (await worker.call("inspect", { ...bind, projectPath: project })).result;
    assert.equal(inspection.success, true); assert.equal(inspection.complete, false); assert.equal(inspection.nativeCapture, false);
    assert.equal(fs.readFileSync(project, "utf8"), original);
    const input = path.join(worker.root, "input");
    fs.cpSync(path.resolve(__dirname, "../../../../examples/twincat3-generation/motor"), input, { recursive: true });
    const preview = (await worker.call("preview", { ...bind, operation: "build-generation", inputDirectory: input,
      outputDirectory: path.join(worker.root, "output") })).result;
    const requested = JSON.parse(fs.readFileSync(path.join(input, "twincat-generation.json"), "utf8")).environment;
    const detected = doctor.environment;
    const ready = detected?.installedBuilds?.includes(requested.requestedBuild) &&
      detected?.comRegistrations?.some(r => r.progId === requested.dteProgId && r.registered) && detected.projectTemplatePath;
    if (ready) {
      assert.equal(preview.success, true, JSON.stringify(preview)); assert.match(preview.inputHash, /^[a-f0-9]{64}$/);
    } else {
      // A clean cloud runner has no XAE. Refusal is the correct result; doctor,
      // inspection and disposal must still work without creating a COM object.
      assert.equal(preview.success, false); assert.equal(preview.phase, "preflight");
    }
    assert.equal(fs.existsSync(path.join(worker.root, "output")), false); assert.deepEqual(fs.readdirSync(worker.scratch), []);
    const rejected = (await worker.call("authorize", { ...bind, ...preview.request, previewId: preview.previewId, inputHash: preview.inputHash })).result;
    assert.equal(rejected.success, false); assert.equal(rejected.grantToken, undefined);
    assert.equal((await worker.call("doctor", bind)).result.nativeCreated, false);
    assert.equal((await worker.call("dispose", bind)).result.success, true);
    assert.equal(await worker.exited, 0);
  } finally { await worker.cleanup(); }
});

test("identity mismatch terminates the actual pipe session", { skip: process.platform !== "win32" }, async () => {
  const worker = await start();
  try {
    worker.socket.write(JSON.stringify({ id: "1", action: "hello", identity: "wrong-identity", payload: {} }) + "\n");
    assert.equal(await worker.exited, 1); assert.deepEqual(fs.readdirSync(worker.scratch), []);
  } finally { await worker.cleanup(); }
});

test("disconnect ends the actual worker without reconnect or native creation", { skip: process.platform !== "win32" }, async () => {
  const worker = await start();
  try { await worker.call("hello"); worker.socket.destroy(); assert.equal(await worker.exited, 1); }
  finally { await worker.cleanup(); }
});
