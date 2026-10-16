"use strict";
// Opt-in qualification through actual stdio MCP. Never run in portable CI.
const assert=require('node:assert/strict');const fs=require('node:fs/promises');const path=require('node:path');const crypto=require('node:crypto');
const {Client}=require('@modelcontextprotocol/client');const {StdioClientTransport}=require('@modelcontextprotocol/client/stdio');
async function main(){
  if(process.env.CODESYS_GUARD_WRITES!=='1' || process.platform!=='win32') throw Error('explicit-owned-native-environment-required');
  const evidencePath=process.env.CODESYS_GUARD_EVIDENCE;
  if(!evidencePath || !path.isAbsolute(evidencePath)) throw Error('private-evidence-path-required');
  const report={startedAt:new Date().toISOString(),kind:'native-CODESYS-IDE-simulation-via-stdio-MCP',steps:[],success:false};
  for(const file of ['native.py','native_policy.py','transport.js','provider.js','results.js','guard.js','schemas.js','index.js']) report[file]=crypto.createHash('sha256').update(await fs.readFile(path.join(__dirname,'..',file))).digest('hex');
  const transport=new StdioClientTransport({command:process.execPath,args:[path.join(__dirname,'..','index.js')],env:process.env,stderr:'pipe'});
  let stderr='';transport.stderr?.on('data',b=>stderr+=b.toString());
  const client=new Client({name:'tia-guard-native-codesys-qualification',version:'1.0'});
  let disposed=false;
  async function call(name,args={}){
    const r=await client.callTool({name,arguments:args},undefined,{timeout:180000});
    report.steps.push({name,args,result:r.structuredContent,isError:r.isError});
    process.stdout.write(`${name}: ${r.isError?'FAIL':'OK'}\n`);
    return r;
  }
  async function execute(request,expectSuccess=true){
    const p=await call('preview_codesys',{request});assert.equal(p.isError,false);
    const g=await call('authorize_codesys',{previewId:p.structuredContent.previewId});assert.equal(g.isError,false);
    const r=await call('execute_codesys',{previewId:p.structuredContent.previewId,grantId:g.structuredContent.grantId});
    assert.equal(r.isError,!expectSuccess,JSON.stringify(r.structuredContent));return r.structuredContent;
  }
  const number=value=>{assert.match(value,/^(?:DINT#)?-?\d+$/);return Number(value.split('#').at(-1));};
  const read=async variables=>{const r=await call('read_codesys',{variables,delayMs:350});assert.equal(r.isError,false);return r.structuredContent;};
  try{
    await client.connect(transport);const list=await client.listTools();assert.equal(list.tools.length,7);
    assert.equal((await call('doctor_codesys')).isError,false);
    const source=JSON.parse(await fs.readFile(path.join(__dirname,'..','examples','counter.json'),'utf8'));
    const broken=structuredClone(source);broken.objects[0].implementation='Counter := DefinitelyMissing;';
    broken.objects[1].implementation='Result := Value;';
    broken.objects[2].declaration=broken.objects[2].declaration.replace('Step : DINT := 1','Step : DINT := 0');
    await execute(broken);
    const bad=await execute({operation:'compile'},false);
    assert.ok(bad.compilation.errorCount>=1);assert.ok(bad.compilation.diagnostics.some(d=>d.message.includes('DefinitelyMissing')));
    for(const object of source.objects) await execute({operation:'edit',object});
    const snapshot=await call('inspect_codesys');assert.equal(snapshot.isError,false);
    for(const object of source.objects){const stored=snapshot.structuredContent.snapshot.objects.find(o=>o.name===object.name);assert.equal(stored.declaration,object.declaration);assert.equal(stored.implementation,object.implementation);}
    report.allThreeObjectKindsChanged=true;
    const wrongType={...source.objects[2],declaration:source.objects[2].declaration.replace('Step : DINT','Step : INT')};
    const deniedType=await execute({operation:'edit',object:wrongType},false);assert.match(deniedType.error,/input-declaration-type-mismatch|unsupported-input-declaration/);
    report.wrongInputTypeRejected=true;
    const compiled=await execute({operation:'compile'});assert.equal(compiled.compilation.nativeCodeGenerated,true);
    assert.equal(compiled.compilation.errorCount,0);
    const reopened=await execute({operation:'reopen'});assert.equal(reopened.verification.verdict,'pass');
    await execute({operation:'login'});await execute({operation:'run'});
    const unavailable=await call('read_codesys',{variables:['PLC_PRG.DoesNotExist'],delayMs:0});assert.equal(unavailable.isError,true);report.invalidVariableReadRejected=true;
    const first=await read(['PLC_PRG.Counter']);const second=await read(['PLC_PRG.Counter']);
    assert.ok(number(second.values['PLC_PRG.Counter'])>number(first.values['PLC_PRG.Counter']));
    report.counter={before:first.values['PLC_PRG.Counter'],after:second.values['PLC_PRG.Counter']};
    await execute({operation:'write',variable:'GVL_Test.Enable',value:true});
    await execute({operation:'write',variable:'GVL_Test.Step',value:7});
    await read(['GVL_Test.Enable','GVL_Test.Step']);await execute({operation:'stop'});
    const stopped1=await read(['PLC_PRG.Counter','PLC_PRG.Result']);const stopped2=await read(['PLC_PRG.Counter']);
    assert.equal(stopped1.values['PLC_PRG.Counter'],stopped2.values['PLC_PRG.Counter']);
    assert.equal(number(stopped1.values['PLC_PRG.Result'])-number(stopped1.values['PLC_PRG.Counter']),7);
    report.stopped={before:stopped1.values['PLC_PRG.Counter'],after:stopped2.values['PLC_PRG.Counter'],result:stopped1.values['PLC_PRG.Result']};
    await execute({operation:'run'});
    const resumed1=await read(['PLC_PRG.Counter']);const resumed2=await read(['PLC_PRG.Counter']);
    assert.ok(number(resumed2.values['PLC_PRG.Counter'])>number(resumed1.values['PLC_PRG.Counter']));
    report.resumed={before:resumed1.values['PLC_PRG.Counter'],after:resumed2.values['PLC_PRG.Counter']};
    const invalid=await call('preview_codesys',{request:{operation:'write',variable:'PLC_PRG.Counter',value:0}}).catch(e=>({isError:true,error:e.message}));
    assert.equal(invalid.isError,true);report.outputWriteRejected=true;
    const cleanup=await call('dispose_codesys');assert.equal(cleanup.isError,false);assert.equal(cleanup.structuredContent.ownedProcessExited,true);disposed=true;
    const stale=await call('read_codesys',{variables:['PLC_PRG.Counter'],delayMs:0});assert.equal(stale.isError,true);
    report.success=true;
  }catch(e){report.error=e.stack;throw e;}
  finally{
    if(!disposed){try{report.emergencyCleanup=(await call('dispose_codesys')).structuredContent;}catch(e){report.cleanupError=e.message;}}
    await client.close();report.stderr=stderr;report.endedAt=new Date().toISOString();await fs.writeFile(evidencePath,JSON.stringify(report,null,2));
  }
}
main().catch(e=>{process.stderr.write(e.stack+'\n');process.exitCode=1;});
