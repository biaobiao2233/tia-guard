"use strict";
const { AdapterError, object } = require("./contract.js");
const lock = require("./upstream.lock.json");
function releaseCandidate(release, pinned = lock) {
  if (!object(release) || release.draft !== false || release.prerelease !== false ||
      !/^v\d+\.\d+\.\d+$/.test(release.tag_name ?? "") || !Array.isArray(release.assets)) {
    throw new AdapterError("invalid-stable-release", "upgrade");
  }
  const matches = release.assets.filter(asset =>
    typeof asset.name === "string" && asset.name.startsWith("TIA_MCP_Delivery_" + release.tag_name + "_") && asset.name.endsWith(".zip"));
  if (matches.length !== 1 || !/^sha256:[a-f0-9]{64}$/.test(matches[0].digest ?? "")) {
    throw new AdapterError("ambiguous-or-unverifiable-release-asset", "upgrade");
  }
  const asset = matches[0];
  const expectedUrl = "https://github.com/" + pinned.repository + "/releases/download/" + release.tag_name + "/" + asset.name;
  if (asset.browser_download_url !== expectedUrl) throw new AdapterError("unexpected-release-origin", "upgrade");
  const candidate = { tag: release.tag_name, assetName: asset.name,
    assetUrl: expectedUrl, assetSha256: asset.digest.slice(7) };
  if (candidate.tag === pinned.candidate.tag && (candidate.assetName !== pinned.candidate.assetName ||
      candidate.assetSha256 !== pinned.candidate.assetSha256)) throw new AdapterError("pinned-release-asset-drift", "upgrade");
  const version = release.tag_name.slice(1).split(".").map(Number);
  const current = pinned.candidate.version.split(".").map(Number);
  const difference = version.map((n, i) => n - current[i]).find(n => n !== 0) ?? 0;
  return { success: true, currentCandidate: pinned.candidate.tag, latest: candidate,
    updateAvailable: difference > 0, olderThanPinned: difference < 0,
    downloaded: false, executed: false, promoted: false,
    nextGate: difference > 0 ? "pin-source-and-runtime-identity-then-requalify" : "no-version-change" };
}
async function checkUpdate(fetchImpl = fetch) {
  try {
    const response = await fetchImpl("https://api.github.com/repos/" + lock.repository + "/releases/latest", {
      headers: { Accept: "application/vnd.github+json", "User-Agent": "TIA-Guard-upstream-check" },
      signal: AbortSignal.timeout(10000), redirect: "error"
    });
    if (!response.ok) throw new AdapterError("release-query-failed", "upgrade");
    return releaseCandidate(await response.json());
  } catch (error) { throw error instanceof AdapterError ? error : new AdapterError("release-query-failed", "upgrade"); }
}
module.exports = { releaseCandidate, checkUpdate };
