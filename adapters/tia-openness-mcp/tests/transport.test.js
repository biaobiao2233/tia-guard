"use strict";
const test = require("node:test");
const assert = require("node:assert/strict");
const { fixture } = require("./support.js");
test("real SDK subprocess keeps one upstream session and calls only fixed read-only operations", async t => {
  const f = await fixture(t);
  await assert.rejects(f.provider.invoke("Connect"), /operation-not-allowed/);
  assert.equal(f.launches.length, 0);
  const first = await f.provider.invoke("capabilities");
  const results = await Promise.all([f.provider.invoke("state"), f.provider.invoke("environment"), f.provider.invoke("capabilities")]);
  assert.ok(first.engineSessionId);
  assert.equal(results[2].engineSessionId, first.engineSessionId);
  assert.equal(f.launches.length, 1);
  assert.equal(results[1].repairsEnabled, false);
  const calls = await f.readCalls();
  assert.deepEqual([...new Set(calls.map(c => c.tool))].sort(), ["Doctor", "GetState"]);
  assert.equal(calls.filter(c => c.tool === "Doctor")[0].args.fix, false);
  assert.equal(new Set(calls.map(c => c.pid)).size, 1);
  const pid = calls[0].pid;
  assert.equal((await f.provider.close()).cleanupConfirmed, true);
  assert.throws(() => process.kill(pid, 0));
  await assert.rejects(f.provider.invoke("state"), /upstream-session-ended/);
});
for (const scenario of ["required", "connected", "failure", "text", "crash", "slow"]) {
  test("fixture " + scenario + " is rejected without restart and its owned process exits", async t => {
    const f = await fixture(t, scenario, 750);
    await assert.rejects(f.provider.invoke("capabilities"));
    assert.equal(f.provider.status().failed, true);
    assert.equal(f.provider.client, null);
    await assert.rejects(f.provider.invoke("state"), /upstream-session-ended/);
    assert.equal(f.launches.length, 1);
    assert.equal((await f.provider.close()).cleanupConfirmed, true);
  });
}
test("runtime protocol identity drift is rejected even when files match", async t => {
  const f = await fixture(t);
  f.pin.protocol = { serverName: "unexpected", serverVersion: "0.0.0", toolCount: 3, schemaSha256: "0".repeat(64) };
  await assert.rejects(f.provider.invoke("capabilities"), /pinned-protocol-identity-drift/);
  assert.equal(f.provider.status().failed, true);
  assert.equal(f.provider.client, null);
});
