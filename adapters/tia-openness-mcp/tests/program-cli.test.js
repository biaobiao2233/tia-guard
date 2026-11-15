"use strict";
const test=require("node:test"),assert=require("node:assert/strict"),fs=require("node:fs"),os=require("node:os"),path=require("node:path");
const {spawnSync}=require("node:child_process");
test("native programming probe refuses absent configuration before creating an engine",()=>{
 const env={...process.env};delete env.TG_TIA_PROGRAM_PROBE_CONFIG;
 const p=spawnSync(process.execPath,[path.join(__dirname,"../native-program-smoke.js")],{env,encoding:"utf8",timeout:10000});
 assert.equal(p.status,1);assert.match(p.stderr,/TG_TIA_PROGRAM_PROBE_CONFIG is required/);
});
test("native programming probe requires separate explicit write opt-in",t=>{
 const root=fs.mkdtempSync(path.join(os.tmpdir(),"tg-program-gate-"));
 t.after(()=>{assert.ok(path.basename(root).startsWith("tg-program-gate-"));fs.rmSync(root,{recursive:true});});
 const config=path.join(root,"config.json");fs.writeFileSync(config,JSON.stringify({allowOwnedProgramProbe:false}));
 const p=spawnSync(process.execPath,[path.join(__dirname,"../native-program-smoke.js")],{env:{...process.env,TG_TIA_PROGRAM_PROBE_CONFIG:config},encoding:"utf8",timeout:10000});
 assert.equal(p.status,1);assert.match(p.stderr,/allowOwnedProgramProbe=true is required/);assert.deepEqual(fs.readdirSync(root),["config.json"]);
});
