"use strict";
const test=require('node:test');const assert=require('node:assert/strict');
const {compilationPassed,validateResult,snapshotMatches,typedValue}=require('../results');
const {Provider}=require('../provider');const f=require('./fixtures');
const source=require('../examples/counter.json');
function snapshot(){return {simulation:true,device:{type:4097,id:'0000 000a',version:'3.5.22.40'},
  objects:source.objects.map(o=>({...o,variableTypes:o.name==='GVL_Test'?{Enable:'BOOL',Step:'DINT'}:{}})),
  tasks:[{name:'MainTask',kind:'Cyclic',interval:'t#20ms',calls:['PLC_PRG']}]};}
test('expected task/object/type contract rejects identically wrong reopen snapshots',()=>{
  assert.equal(snapshotMatches(snapshot(),source),true);
  for(const change of [s=>s.tasks=[],s=>s.tasks[0].calls=['FB_Add'],s=>s.tasks[0].interval='t#200ms',s=>s.tasks[0].kind='Freewheeling',
    s=>s.objects.pop(),s=>s.objects[0].kind='functionBlock',s=>s.objects[2].variableTypes.Step='INT']) {
    const s=snapshot();change(s);assert.equal(snapshotMatches(s,source),false);
    const request={operation:'reopen'},state={target:f.target,expected:source,compilation:{sourceFingerprint:'b'.repeat(64)}};
    assert.throws(()=>validateResult(request,{operation:'reopen',success:true,target:f.target,context:f.context(request),
      verification:{complete:true,closedAndReopened:true,verdict:'pass',requestTextMatches:true,expectedStructureMatches:true,differences:[],sourceFingerprint:'b'.repeat(64),before:s,after:s}},state),/invalid-native-result/);
  }
});
test('compile proof rejects foreign target, phase, request, missing summary and unrelated Text',()=>{
  const proof=f.compilation();const check=c=>compilationPassed(c,proof.context,proof.target);
  assert.equal(check(proof.compilation),true);
  for(const mutate of [c=>c.buildId='other',c=>c.requestHash='c'.repeat(64),c=>c.target={...f.target,applicationName:'Other'},
    c=>c.diagnostics[0].correlated=false,c=>c.diagnostics[0].foreignOrigin=true,c=>c.diagnostics[0].requestId='old',
    c=>c.diagnostics[0].targetHeader=false,c=>c.diagnostics[3].buildSummary=false,c=>c.diagnostics[3].message='unrelated Text',
    c=>c.diagnostics[3].category='other',c=>c.phases[1].completed=false,c=>c.phases[0].diagnosticCount=99]) {
    const c=structuredClone(proof.compilation);mutate(c);assert.equal(check(c),false);
  }
});
test('every success operation needs specific evidence, not success:true alone',()=>{
  for(const request of [{operation:'create',...source},{operation:'edit',object:source.objects[0]},{operation:'reopen'},{operation:'login'},
    {operation:'run'},{operation:'stop'},{operation:'write',variable:'GVL_Test.Step',value:7},{operation:'read',variables:['PLC_PRG.Counter']}]) {
    assert.throws(()=>validateResult(request,{operation:request.operation,context:f.context(request),target:f.target,success:true},
      {target:f.target,expected:source,verified:true}),/invalid-native-result/);
  }
  assert.throws(()=>typedValue('GVL_Test.Step','INT#7'),/invalid-native-result/);
  assert.equal(typedValue('GVL_Test.Step','DINT#7'),7);
});
test('native timeout closes all grants and prevents another transport call even with a new preview',async()=>{
  let calls=0,cleaned=0;
  const p=new Provider({writes:true},{call:async()=>{calls++;throw Error('native-timeout');},abort:async()=>{cleaned++;return {success:true,ownedProcessExited:true};}});
  const a=await p.dispatch('preview_codesys',{request:{operation:'run'}});const b=await p.dispatch('preview_codesys',{request:{operation:'stop'}});
  const ga=await p.dispatch('authorize_codesys',{previewId:a.previewId});const gb=await p.dispatch('authorize_codesys',{previewId:b.previewId});
  const failed=await p.dispatch('execute_codesys',{previewId:a.previewId,grantId:ga.grantId});
  assert.equal(failed.success,false);assert.equal(failed.sessionFaulted,true);assert.equal(failed.cleanup.ownedProcessExited,true);
  assert.equal((await p.dispatch('preview_codesys',{request:{operation:'run'}})).success,false);
  assert.equal((await p.dispatch('execute_codesys',{previewId:b.previewId,grantId:gb.grantId})).success,false);
  assert.equal(calls,1);assert.equal(cleaned,1);
});
