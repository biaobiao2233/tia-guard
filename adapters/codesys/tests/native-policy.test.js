"use strict";
const test=require('node:test');const assert=require('node:assert/strict');const {spawnSync}=require('node:child_process');const path=require('node:path');
test('production IronPython functions with simulated vendor APIs: review failure regressions',()=>{
  const r=spawnSync('python',[path.join(__dirname,'native_policy_test.py')],{encoding:'utf8',env:{...process.env,PYTHONDONTWRITEBYTECODE:'1'}});
  assert.equal(r.status,0,`${r.error||''}\n${r.stdout}\n${r.stderr}`);
  process.stdout.write(r.stderr);
});
