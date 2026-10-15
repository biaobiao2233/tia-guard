"use strict";
const test=require('node:test');const assert=require('node:assert/strict');
const {Guard}=require('../guard');const {Provider,compilationPassed}=require('../provider');const {tools}=require('../schemas');
test('default read-only permits preview but blocks all mutation grants',()=>{
  const g=new Guard();const p=g.preview({operation:'compile'});
  assert.throws(()=>g.authorize(p.previewId),/read-only/);
  assert.throws(()=>g.consume(p.previewId,'unknown'),/read-only/);
});
test('grants are single-use, revision-bound, expiring and session-bound',()=>{
  let now=10;const g=new Guard({writes:true,clock:()=>now});
  const p=g.preview({operation:'compile'}), other=g.preview({operation:'run'});
  const grant=g.authorize(p.previewId);assert.throws(()=>g.consume(p.previewId,'foreign'),/invalid-grant/);
  g.consume(p.previewId,grant.grantId);
  assert.throws(()=>g.consume(p.previewId,grant.grantId),/stale/);
  assert.throws(()=>g.authorize(other.previewId),/stale/);
  const next=g.preview({operation:'stop'});now+=120000;assert.throws(()=>g.authorize(next.previewId),/stale/);
  const fresh=g.preview({operation:'run'});g.close();assert.throws(()=>g.authorize(fresh.previewId),/stale/);
});
test('frozen requests resist caller mutation and native failures invalidate earlier previews',async()=>{
  const observed=[];const transport={call:async r=>{observed.push(r);return {operation:r.operation,success:false,error:'real-compile-failure'};}};
  const p=new Provider({writes:true},transport);const request={operation:'stop'};
  const preview=await p.dispatch('preview_codesys',{request});request.operation='run';
  const grant=await p.dispatch('authorize_codesys',{previewId:preview.previewId});
  const r=await p.dispatch('execute_codesys',{previewId:preview.previewId,grantId:grant.grantId});
  assert.equal(r.success,false);assert.equal(observed[0].operation,'stop');
  assert.equal((await p.dispatch('execute_codesys',{previewId:preview.previewId,grantId:grant.grantId})).success,false);
});
test('no arbitrary project, controller, script, network target or output write parameters',()=>{
  for(const req of [{operation:'create',projectPath:'C:/other.project'}, {operation:'login',target:'127.0.0.1'},
    {operation:'write',variable:'PLC_PRG.Counter',value:1},{operation:'write',variable:'GVL_Test.Step',value:11},
    {operation:'run',script:'import os'},{operation:'read',variables:['x']}]) {
    assert.equal(tools.preview_codesys.schema.safeParse({request:req}).success,false);
  }
  const g=new Guard({writes:true});assert.throws(()=>g.preview({operation:'write',variable:'GVL_Test.Enable',value:1}),/type-mismatch/);
  assert.equal(tools.read_codesys.schema.safeParse({variables:['PLC_PRG.Counter + 1']}).success,false);
});
test('compiler empty success does not qualify and complete failure diagnostics survive',async()=>{
  let answer={operation:'compile',success:true};
  const p=new Provider({writes:true},{call:async()=>answer});
  async function execute(){const v=await p.dispatch('preview_codesys',{request:{operation:'compile'}});const g=await p.dispatch('authorize_codesys',{previewId:v.previewId});return p.dispatch('execute_codesys',{previewId:v.previewId,grantId:g.grantId});}
  assert.equal((await execute()).success,false);
  answer={operation:'compile',success:false,compilation:{errorCount:1,diagnostics:[{code:'C0046',message:'unknown identifier'}]}};
  assert.deepEqual((await execute()).compilation,answer.compilation);
});
test('unsupported object names, case collisions and missing program entry are rejected',()=>{
  const g=new Guard();const object={kind:'program',name:'MAIN',declaration:'PROGRAM MAIN',implementation:''};
  assert.throws(()=>g.preview({operation:'create',entry:'Other',objects:[object]}),/invalid/);
  assert.throws(()=>g.preview({operation:'create',entry:'MAIN',objects:[object,{...object,name:'main'}]}),/invalid/);
  assert.throws(()=>g.preview({operation:'edit',object:{...object,name:'../escape'}}));
});
test('unknown severity, contradictory counts, exceptions and incomplete compilation fail closed',()=>{
  const good={buildStarted:true,buildCompleted:true,diagnosticsComplete:true,nativeCodeGenerated:true,errorCount:0,warningCount:0,diagnostics:[{severity:'Text'}]};
  assert.equal(compilationPassed(good),true);
  for(const bad of [{buildStarted:'true'},{buildCompleted:false},{diagnosticsComplete:false},{warningCount:1},
    {buildException:'vendor failure'},{diagnostics:[{severity:'Unknown'}]},{diagnostics:[{severity:'Error'}]},{diagnostics:[]}]) {
    assert.equal(compilationPassed({...good,...bad}),false);
  }
});
