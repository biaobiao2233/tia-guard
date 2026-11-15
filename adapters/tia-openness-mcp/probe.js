#!/usr/bin/env node
"use strict";
const { fromEnvironment } = require("./upstream.js");
const { checkUpdate } = require("./update.js");
async function main() {
  if (process.argv[2] === "--check-update" && process.argv.length === 3) {
    process.stdout.write(JSON.stringify(await checkUpdate(), null, 2) + "\n"); return;
  }
  if (process.argv.length !== 2) throw new Error("unsupported probe arguments");
  const provider = fromEnvironment();
  const receipt = { protocolVerified: false, engineeringQualified: false, runtimeQualified: false };
  try {
    receipt.capabilities = await provider.invoke("capabilities");
    receipt.state = await provider.invoke("state");
    receipt.environment = await provider.invoke("environment");
    receipt.protocolVerified = true;
  } catch (error) {
    receipt.error = { code: error.code ?? "probe-failed", phase: error.phase ?? "probe" };
    process.exitCode = 1;
  } finally {
    try { Object.assign(receipt, await provider.close()); }
    catch (error) { receipt.protocolVerified = false; receipt.cleanupConfirmed = false; receipt.cleanupError = error.code; process.exitCode = 1; }
    process.stdout.write(JSON.stringify(receipt, null, 2) + "\n");
  }
}
if (require.main === module) main().catch(error => {
  process.stdout.write(JSON.stringify({ success: false, code: error.code ?? "invalid-probe-configuration" }) + "\n");
  process.exitCode = 1;
});
