"use strict";
const test=require('node:test');const assert=require('node:assert/strict');const fs=require('node:fs/promises');
const os=require('node:os');const path=require('node:path');const {spawn}=require('node:child_process');const {NativeTransport}=require('../transport');
async function fixture(t,mode){
  const workspace=await fs.mkdtemp(path.join(os.tmpdir(),'codesys-transport-'));
  const transport=new NativeTransport({writes:true,exe:process.execPath,workspace,profile:'CODESYS V3.5 SP22 Patch 4'},{platform:'win32',
    spawn:(_exe,_args,options)=>spawn(process.execPath,[path.join(__dirname,'fake-native-process.js')],{...options,windowsVerbatimArguments:false,env:{...options.env,CODESYS_TEST_MODE:mode}}),
    timeouts:{startup:5000,operation:200,cleanup:200,exit:200,kill:5000,poll:5}});
  t.after(async()=>{await transport.dispose();await fs.rm(workspace,{recursive:true,force:true});});return transport;
}
test('real child: timed-out command is terminal, bounded cleanup kills only owned child',async t=>{
  const transport=await fixture(t,'hang');await assert.rejects(transport.call({operation:'run'}),/native-timeout/);
  assert.equal(transport.closed,true);assert.equal(transport.faulted,'native-timeout');assert.equal(transport.cleanup.ownedProcessExited,true);
  assert.equal(transport.cleanup.success,false);assert.equal(transport.child.exitCode!==null||transport.child.signalCode!==null,true);
  await assert.rejects(transport.call({operation:'write'}),/native-session-faulted/);
  const operations=JSON.parse(await fs.readFile(path.join(transport.root,'operations.json'),'utf8'));assert.deepEqual(operations,['run','dispose']);
});
for(const mode of ['wrong-operation','wrong-context','exit'])test(`real child: ${mode} invalidates session and awaits cleanup`,async t=>{
  const transport=await fixture(t,mode);await assert.rejects(transport.call({operation:'run'}),/invalid-native-result|native-process-exited/);
  assert.equal(transport.closed,true);assert.equal(transport.cleanup.ownedProcessExited,true);
  await assert.rejects(transport.call({operation:'run'}),/native-session-faulted/);
});
test('real child: failed native cleanup cannot become successful through repeated dispose',async t=>{
  const transport=await fixture(t,'cleanup-failure');await transport.start();
  const first=await transport.dispose();const second=await transport.dispose();assert.equal(first.success,false);assert.deepEqual(second,first);
  assert.equal(first.ownedProcessExited,true);assert.equal(first.cleanup[0].success,false);
});
