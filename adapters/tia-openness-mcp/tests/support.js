"use strict";
const fs = require("node:fs/promises");
const os = require("node:os");
const path = require("node:path");
const { StdioClientTransport, getDefaultEnvironment } = require("@modelcontextprotocol/client/stdio");
const { Upstream } = require("../upstream.js");
const { sha256, runtimeDigest } = require("../contract.js");
async function fixture(t, scenario = "normal", timeoutMs = 2000) {
  const base = await fs.realpath(process.env.TEST_TMP_DIR ?? os.tmpdir());
  const root = await fs.mkdtemp(path.join(base, "tia-upstream-"));
  const runtime = path.join(root, "runtime"); await fs.mkdir(runtime);
  const content = Buffer.from("sanitized non-executable runtime fixture\n");
  await fs.writeFile(path.join(runtime, "TiaMcpServer.exe"), content);
  const files = { "TiaMcpServer.exe": sha256(content) };
  const pin = { version: "fixture", sourceCommit: "0".repeat(40), executable: "TiaMcpServer.exe",
    executableSha256: files["TiaMcpServer.exe"], runtimeManifestSha256: runtimeDigest(files), tiaMajorVersion: 21 };
  const config = { schemaVersion: 1, version: pin.version, runtimeDirectory: runtime, tiaPortalLocation: root, files };
  const calls = path.join(root, "calls.jsonl"); const launches = [];
  const provider = new Upstream({ config, pin, timeoutMs, transportFactory: parameters => {
    launches.push(parameters);
    return new StdioClientTransport({ command: process.execPath, args: [path.join(__dirname, "fake-upstream.js")],
      cwd: __dirname, stderr: "pipe", env: { ...getDefaultEnvironment(), UPSTREAM_TEST_SCENARIO: scenario, UPSTREAM_TEST_CALLS: calls } });
  } });
  t.after(async () => {
    await provider.close();
    const actualRoot = await fs.realpath(root);
    if (!actualRoot.startsWith(base + path.sep) || !path.basename(actualRoot).startsWith("tia-upstream-")) {
      throw new Error("test cleanup path escaped its owned root");
    }
    await fs.rm(actualRoot, { recursive: true, force: true });
  });
  return { root, config, pin, provider, calls, launches,
    readCalls: async () => (await fs.readFile(calls, "utf8")).trim().split("\n").map(JSON.parse) };
}
module.exports = { fixture };
