"use strict";
const test = require("node:test");
const assert = require("node:assert/strict");
const { releaseCandidate, checkUpdate } = require("../update.js");
const lock = require("../upstream.lock.json");
function release(tag = lock.candidate.tag) {
  const name = tag === lock.candidate.tag ? lock.candidate.assetName : "TIA_MCP_Delivery_" + tag + "_20261115.zip";
  return { draft: false, prerelease: false, tag_name: tag, assets: [{ name,
    digest: "sha256:" + lock.candidate.assetSha256,
    browser_download_url: "https://github.com/" + lock.repository + "/releases/download/" + tag + "/" + name }] };
}
test("same and newer releases produce metadata without download, execution or promotion", () => {
  assert.equal(releaseCandidate(release()).updateAvailable, false);
  const next = releaseCandidate(release("v2.8.0"));
  assert.equal(next.updateAvailable, true);
  assert.equal(next.downloaded, false); assert.equal(next.executed, false); assert.equal(next.promoted, false);
});
test("untrusted origin, missing checksum, ambiguous assets and replaced same tag fail", () => {
  const variants = [release(), release(), release(), release(), release()];
  variants[0].assets[0].browser_download_url = "https://example.invalid/engine.zip";
  variants[1].assets[0].digest = null;
  variants[2].assets.push(variants[2].assets[0]);
  variants[3].assets[0].digest = "sha256:" + "0".repeat(64);
  variants[4].prerelease = true;
  for (const variant of variants) assert.throws(() => releaseCandidate(variant));
});
test("release lookup sends no project data and HTTP errors remain explicit failures", async () => {
  let count = 0;
  const result = await checkUpdate(async (url, options) => {
    count++; assert.equal(url, "https://api.github.com/repos/" + lock.repository + "/releases/latest");
    assert.equal(options.redirect, "error"); assert.equal(options.body, undefined);
    return { ok: true, json: async () => release() };
  });
  assert.equal(count, 1); assert.equal(result.promoted, false);
  await assert.rejects(checkUpdate(async () => ({ ok: false })), /release-query-failed/);
});
