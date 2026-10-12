"use strict";
const fs = require("node:fs");
const assert = require("node:assert/strict");
const { compilationPassed, verificationPassed } = require("../../mcp/result.js");
const fixtures = JSON.parse(fs.readFileSync(process.argv[2], "utf8").replace(/^\uFEFF/, ""));
for (const { name, report, passed } of fixtures.compilations) assert.equal(compilationPassed(report), passed, `CompilationReport.Passed: ${name}`);
for (const { name, report, passed } of fixtures.verifications) assert.equal(verificationPassed(report), passed, `VerificationReport.Passed: ${name}`);
process.stdout.write(`A contract parity PASS: ${fixtures.compilations.length + fixtures.verifications.length} cases\n`);
