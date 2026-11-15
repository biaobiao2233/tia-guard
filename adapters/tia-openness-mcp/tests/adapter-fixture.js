"use strict";
const fs = require("node:fs");
const path = require("node:path");
const { StdioClientTransport, getDefaultEnvironment } = require("@modelcontextprotocol/client/stdio");
const { Upstream } = require("../upstream.js");
const { main } = require("../index.js");
const options = JSON.parse(fs.readFileSync(process.env.UPSTREAM_TEST_OPTIONS, "utf8"));
main(new Upstream({ ...options, timeoutMs: 2000, transportFactory: () => new StdioClientTransport({
  command: process.execPath, args: [path.join(__dirname, "fake-upstream.js")], stderr: "pipe", cwd: __dirname,
  env: { ...getDefaultEnvironment(), UPSTREAM_TEST_SCENARIO: "normal", UPSTREAM_TEST_CALLS: process.env.UPSTREAM_TEST_CALLS }
}) }));
