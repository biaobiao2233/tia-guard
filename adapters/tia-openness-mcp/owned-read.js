"use strict";
const fs = require("node:fs");
const path = require("node:path");
const { AdapterError, sha256, canonical, assertDisconnected } = require("./contract.js");
const { completeResult } = require("./pages.js");

function verifyReadContract(tools, expected) {
  const names = ["ConnectIsolated", "GetState", "OpenProject", "GetProject", "GetProjectTree", "GetBlocks", "GetPlcTags", "ExportBlocks", "GetExport", "CloseProject", "Disconnect"];
  const selected = names.map(name => {
    const matches = tools.filter(t => t.name === name);
    if (matches.length !== 1) throw new AdapterError("owned-read-tool-contract-missing", "contract");
    const t = matches[0]; return { name, inputSchema: t.inputSchema, ...(t.outputSchema ? { outputSchema: t.outputSchema } : {}) };
  });
  if (!expected || sha256(JSON.stringify(canonical(selected))) !== expected) throw new AdapterError("owned-read-tool-contract-drift", "contract");
}

function noLinks(file) {
  for (let current = path.resolve(file); ; current = path.dirname(current)) {
    if (fs.lstatSync(current).isSymbolicLink()) throw new AdapterError("project-link-rejected", "ownership");
    if (path.dirname(current) === current) break;
  }
}
function inventory(root) {
  const files = {}; let bytes = 0;
  function walk(dir, prefix = "") {
    for (const item of fs.readdirSync(dir, { withFileTypes: true })) {
      const relative = prefix + item.name, full = path.join(dir, item.name);
      if (item.isSymbolicLink()) throw new AdapterError("project-link-rejected", "ownership");
      if (item.isDirectory()) walk(full, relative + "/");
      else if (item.isFile()) {
        bytes += fs.statSync(full).size;
        if (Object.keys(files).length >= 5000 || bytes > 500_000_000) throw new AdapterError("project-copy-limit", "ownership");
        files[relative] = sha256(fs.readFileSync(full));
      } else throw new AdapterError("project-entry-rejected", "ownership");
    }
  }
  walk(root); return files;
}
function sameFiles(a, b) {
  return JSON.stringify(Object.entries(a).sort()) === JSON.stringify(Object.entries(b).sort());
}
function prepareCopy(settings) {
  if (!settings || !path.isAbsolute(settings.sourceProjectFile ?? "") ||
      !path.isAbsolute(settings.artifactRoot ?? "") || !/\.ap21$/i.test(settings.sourceProjectFile)) {
    throw new AdapterError("invalid-owned-read-configuration", "configuration");
  }
  noLinks(settings.sourceProjectFile);
  const source = fs.realpathSync(path.dirname(settings.sourceProjectFile));
  const artifactRoot = path.resolve(settings.artifactRoot);
  if (artifactRoot.toLowerCase() === source.toLowerCase() ||
      artifactRoot.toLowerCase().startsWith(source.toLowerCase() + path.sep)) {
    throw new AdapterError("artifact-root-inside-source", "ownership");
  }
  // Validate ancestors that already exist before creating the dedicated output root.
  let parent = artifactRoot; while (!fs.existsSync(parent)) parent = path.dirname(parent);
  noLinks(parent); fs.mkdirSync(artifactRoot, { recursive: true }); noLinks(artifactRoot);
  const before = inventory(source);
  const capsule = fs.mkdtempSync(path.join(artifactRoot, "read-"));
  const projectDirectory = path.join(capsule, "project");
  fs.cpSync(source, projectDirectory, { recursive: true, errorOnExist: true, force: false, dereference: false });
  if (!sameFiles(before, inventory(source)) || !sameFiles(before, inventory(projectDirectory))) {
    throw new AdapterError("source-or-copy-changed", "ownership");
  }
  const prepared = { source, before, capsule, projectDirectory,
    projectFile: path.join(projectDirectory, path.basename(settings.sourceProjectFile)),
    exportDirectory: path.join(capsule, "exports") };
  fs.mkdirSync(prepared.exportDirectory);
  fs.writeFileSync(path.join(capsule, "source-manifest.json"), JSON.stringify(before, null, 2) + "\n", { flag: "wx" });
  return prepared;
}
function binding(projects, expected) {
  if (!Array.isArray(projects.items) || projects.items.length !== 1) throw new AdapterError("ambiguous-project-binding", "ownership");
  const attributes = projects.items[0].attributes;
  const paths = Array.isArray(attributes) ? attributes.filter(a => a.name === "Path") : [];
  if (paths.length !== 1 || typeof paths[0].value !== "string" ||
      path.resolve(paths[0].value).toLowerCase() !== path.resolve(expected).toLowerCase()) {
    throw new AdapterError("project-path-readback-mismatch", "ownership");
  }
  return projects.items[0].name;
}
function blockIdentity(blocks) {
  if (!Array.isArray(blocks.items) || blocks.items.length === 0) throw new AdapterError("missing-native-blocks", "contract");
  return blocks.items.map(b => {
    if (typeof b.name !== "string" || typeof b.typeName !== "string") throw new AdapterError("invalid-native-block", "contract");
    return { name: b.name, type: b.typeName, language: b.programmingLanguage };
  }).sort((a, b) => JSON.stringify(a).localeCompare(JSON.stringify(b)));
}
async function inspectOwnedCopy(client, settings, timeoutMs = 120000) {
  const copy = prepareCopy(settings);
  const receipt = { scope: "owned-copy-PLC-root-read-export-reopen", ownedReadVerified: false, engineeringQualified: false,
    runtimeQualified: false, sourceUnchanged: false, projectFile: copy.projectFile, calls: [] };
  let attempted = false, opened = false;
  const raw = async (name, args = {}) => {
    receipt.calls.push({ name });
    return client.callTool({ name, arguments: args }, { timeout: timeoutMs });
  };
  const call = async (name, args = {}) => completeResult(await raw(name, args),
    (exportId, offset) => raw("GetExport", { exportId, offset }));
  const confirm = async () => binding(await call("GetProject"), copy.projectFile);
  try {
    assertDisconnected(await call("GetState")); attempted = true;
    const connection = await call("ConnectIsolated");
    if (connection.meta?.verified !== true || connection.meta?.isolated !== true) throw new AdapterError("isolation-unverified", "ownership");
    const empty = await call("GetState");
    if (empty.isConnected !== true || empty.project !== "-" || empty.session !== "-") throw new AdapterError("isolated-instance-not-empty", "ownership");
    await call("OpenProject", { path: copy.projectFile, closeForeignProject: false }); opened = true;
    receipt.projectName = await confirm();
    const tree = await call("GetProjectTree");
    const names = [...new Set([...String(tree.tree ?? "").matchAll(/PlcSoftware: (.+?) \[PLC Program\]/g)].map(m => m[1]))];
    if (names.length !== 1) throw new AdapterError("expected-one-plc-software", "ownership");
    const softwarePath = names[0]; receipt.softwarePath = softwarePath;
    await confirm();
    receipt.blocks = blockIdentity(await call("GetBlocks", { softwarePath, regexName: "" }));
    const tags = await call("GetPlcTags", { softwarePath, maxTags: 5000 });
    if (tags.ok !== true || !tags.data || tags.data.complete !== true) throw new AdapterError("incomplete-native-tags", "contract");
    receipt.tags = tags.data;
    await confirm();
    const exported = await call("ExportBlocks", { softwarePath, exportPath: copy.exportDirectory, preservePath: true, regexName: "", softwareUnit: "" });
    if (exported.meta?.success !== true || exported.meta.failedBlocks !== 0 ||
        exported.meta.exportedBlocks !== receipt.blocks.length) throw new AdapterError("incomplete-native-export", "contract");
    receipt.exportFiles = inventory(copy.exportDirectory);
    if (Object.keys(receipt.exportFiles).length !== receipt.blocks.length) throw new AdapterError("export-file-count-mismatch", "contract");
    await confirm(); await call("CloseProject", { closeForeignProject: false }); opened = false;
    await call("OpenProject", { path: copy.projectFile, closeForeignProject: false }); opened = true;
    await confirm();
    const reopened = blockIdentity(await call("GetBlocks", { softwarePath, regexName: "" }));
    if (JSON.stringify(reopened) !== JSON.stringify(receipt.blocks)) throw new AdapterError("reopened-blocks-differ", "contract");
    receipt.reopenVerified = true;
  } catch (error) {
    receipt.error = { code: error.code ?? "owned-read-operation-failed", phase: error.phase ?? "engineering" };
    throw error;
  } finally {
    try {
      if (opened) { await confirm(); await call("CloseProject", { closeForeignProject: false }); }
      if (attempted) await call("Disconnect");
      assertDisconnected(await call("GetState")); receipt.disconnected = true;
    } catch (error) {
      receipt.cleanupError = { code: error.code ?? "owned-read-cleanup-failed", phase: error.phase ?? "cleanup" };
      throw error;
    } finally {
      receipt.sourceUnchanged = sameFiles(copy.before, inventory(copy.source));
      receipt.ownedReadVerified = receipt.reopenVerified === true && receipt.disconnected === true &&
        receipt.sourceUnchanged && !receipt.error && !receipt.cleanupError;
      fs.writeFileSync(path.join(copy.capsule, "receipt.json"), JSON.stringify(receipt, null, 2) + "\n", { flag: "wx" });
    }
  }
  if (!receipt.sourceUnchanged) throw new AdapterError("source-project-changed", "ownership");
  return receipt;
}
module.exports = { inventory, sameFiles, prepareCopy, binding, blockIdentity, inspectOwnedCopy, verifyReadContract };
