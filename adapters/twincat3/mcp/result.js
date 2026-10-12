"use strict";

class BridgeError extends Error {
  constructor(code, phase = "transport") { super(code); this.code = code; this.phase = phase; }
}

function failure(operation, code, phase = "transport") {
  return { schemaVersion: "1.0", operation, phase, success: false, published: false,
    failures: [{ code, message: code }], cleanup: [] };
}

// Keep evidence, including false/null/empty fields. Never prune them into an "ok".
function compilationPassed(c) {
  return c?.state === "success" && c.buildStarted === true && c.buildCompleted === true &&
    c.failedProjectCount === 0 && c.errorCount === 0 && Number.isInteger(c.warningCount) && c.warningCount >= 0 &&
    c.diagnosticsComplete === true && c.outputCaptureComplete === true &&
    Array.isArray(c.failures) && c.failures.length === 0 &&
    Array.isArray(c.diagnostics) && c.diagnostics.every(d => d.correlated === true && ["warning", "message"].includes(d.severity)) &&
    c.warningCount === c.diagnostics.filter(d => d.severity === "warning").length &&
    [c.projectIdentity, c.inputHash, c.configuration, c.platform].every(v => typeof v === "string" && v.length > 0);
}

function verificationPassed(v) {
  return v?.verdict === "pass" && v.complete === true &&
    Array.isArray(v.differences) && v.differences.length === 0 && Array.isArray(v.unknowns) && v.unknowns.length === 0;
}

function isFailure(r, action) {
  if (!r || r.success !== true || !Array.isArray(r.failures) || r.failures.length ||
      !Array.isArray(r.cleanup) || r.cleanup.some(c => c?.success !== true)) return true;
  if (r.compilation && !compilationPassed(r.compilation)) return true;
  if (r.verification && !verificationPassed(r.verification)) return true;
  if (["build-generation", "compile"].includes(action) && !compilationPassed(r.compilation)) return true;
  if (["build-generation", "verify"].includes(action) && !verificationPassed(r.verification)) return true;
  if (action === "build-generation" && r.published !== true) return true;
  if (r.published === true && (action !== "build-generation" || !compilationPassed(r.compilation) || !verificationPassed(r.verification))) return true;
  return false;
}

function sanitize(value, secrets = []) {
  if (typeof value === "string") {
    let s = value;
    for (const secret of secrets) if (secret) s = s.split(secret).join("[REDACTED]");
    return s.replace(/((?:authorization|bearer|token|password|secret|api[-_]?key)\s*[:=]\s*)[^\s,;"']+/gi, "$1[REDACTED]")
      .replace(/([?&](?:token|secret|password|key)=)[^&#\s]+/gi, "$1[REDACTED]");
  }
  if (Array.isArray(value)) return value.map(v => sanitize(v, secrets));
  if (value && typeof value === "object") {
    const clean = {};
    for (const [key, v] of Object.entries(value)) {
      if (/token|secret|password|credential|authorization|^apiKey$/i.test(key)) continue;
      clean[key] = sanitize(v, secrets);
    }
    return clean;
  }
  return value;
}

function toMcpResult(result, action, secrets = []) {
  const invalid = !result || result.operation !== action || result.schemaVersion !== "1.0" ||
    typeof result.phase !== "string" || typeof result.success !== "boolean" || typeof result.published !== "boolean";
  const raw = invalid ? failure(action, "provider-result-invalid") : result;
  const structuredContent = sanitize(raw, secrets);
  return { content: [{ type: "text", text: JSON.stringify(structuredContent) }], structuredContent,
    isError: invalid || isFailure(raw, action) };
}
module.exports = { BridgeError, failure, compilationPassed, verificationPassed, sanitize, toMcpResult, isFailure };
