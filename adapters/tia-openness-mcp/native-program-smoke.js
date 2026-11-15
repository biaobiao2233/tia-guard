#!/usr/bin/env node
// Opt-in native acceptance only; not a general engineering command proxy.
"use strict";
const fs=require('node:fs'),path=require('node:path');
const {Client}=require('@modelcontextprotocol/client');
const {StdioClientTransport}=require('@modelcontextprotocol/client/stdio');
const {launchParameters,readJson}=require('./upstream.js');
const {prepareCopy,binding,inventory,sameFiles}=require('./owned-read.js');
const {completeResult}=require('./pages.js');
const {payload,assertDisconnected}=require('./contract.js');
const pin=require('./upstream.lock.json').candidate;
if(!process.env.TG_TIA_PROGRAM_PROBE_CONFIG)throw Error('TG_TIA_PROGRAM_PROBE_CONFIG is required');
const settings=readJson(process.env.TG_TIA_PROGRAM_PROBE_CONFIG);
if(settings.allowOwnedProgramProbe!==true)throw Error('Explicit allowOwnedProgramProbe=true is required');
const copy=prepareCopy({...settings.ownedRead,artifactRoot:settings.ownedRead.artifactRoot});
const report=path.join(copy.capsule,'program-receipt.json');
const receipt={startedAt:new Date().toISOString(),projectFile:copy.projectFile,calls:[],programmingVerified:false,runtimeQualified:false};
function save(){fs.writeFileSync(report,JSON.stringify(receipt,null,2)+'\n');}
const block='FC_TG_IntegrationProbe';
function source(bad){return `FUNCTION "${block}" : DInt\n{ S7_Optimized_Access := 'TRUE' }\nVERSION : 0.1\nVAR_INPUT\n Start : Bool;\n Stop : Bool;\n Reset : Bool;\n ItemCount : DInt;\nEND_VAR\nBEGIN\n IF #Reset THEN\n  #${block} := 0;\n ELSIF #Stop THEN\n  #${block} := #ItemCount;\n ELSIF #Start THEN\n  #${block} := #ItemCount + ${bad?'#TG_UNDECLARED_ERROR':1};\n ELSE\n  #${block} := #ItemCount;\n END_IF;\nEND_FUNCTION\n`;}
async function main(){
 const params=launchParameters(settings,pin);
 params.args=params.args.filter(x=>x!=='--read-only');params.env.TIA_MCP_READ_ONLY='0';
 const transport=new StdioClientTransport(params);let stderr='';transport.stderr.on('data',b=>{stderr=(stderr+b).slice(-12000);});
 const client=new Client({name:'tia-guard-owned-program-probe',version:'0.1.0'},{mode:'legacy'});
 let attempted=false,opened=false;
 const raw=async(name,args={})=>{
  const step={name,arguments:args,startedAt:new Date().toISOString()};receipt.calls.push(step);save();
  try{step.result=await client.callTool({name,arguments:args},{timeout:180000});step.endedAt=new Date().toISOString();save();return step.result;}catch(e){step.error=e.message;save();throw e;}
 };
 const call=async(name,args={})=>completeResult(await raw(name,args),(exportId,offset)=>raw('GetExport',{exportId,offset}));
 const confirm=async()=>binding(await call('GetProject'),copy.projectFile);
 try{
  await client.connect(transport,{timeout:20000});receipt.enginePid=transport.pid;
  receipt.server=client.getServerVersion();if(receipt.server.version!==pin.protocol.serverVersion)throw Error('Engine version mismatch');
  receipt.schemas=(await client.listTools()).tools.filter(t=>['ConnectIsolated','OpenProject','ImportPlcExternalSource','GenerateBlocksFromExternalSource','DeletePlcExternalSource','CompileSoftware','SaveProject','CloseProject','ExportBlock','GetBlocks'].includes(t.name));save();
  assertDisconnected(await call('GetState'));attempted=true;
  const isolated=await call('ConnectIsolated');if(isolated.meta?.verified!==true||isolated.meta?.isolated!==true)throw Error('Isolation unverified');
  const empty=await call('GetState');if(empty.project!=='-'||empty.session!=='-')throw Error('Instance not empty');
  await call('OpenProject',{path:copy.projectFile,closeForeignProject:false});opened=true;await confirm();
  const tree=await call('GetProjectTree');const names=[...new Set([...tree.tree.matchAll(/PlcSoftware: (.+?) \[PLC Program\]/g)].map(m=>m[1]))];if(names.length!==1)throw Error('PLC ambiguous');
  const softwarePath=names[0];const filePath=path.join(copy.capsule,'TG_IntegrationProbe.scl');
  receipt.baselineCompile=await call('CompileSoftware',{softwarePath});if(receipt.baselineCompile.errorCount!==0)throw Error('Baseline compilation not clean');
  const before=await call('GetBlocks',{softwarePath,regexName:'^'+block+'$'});if(before.items?.length!==0)throw Error('Test block already exists');
  fs.writeFileSync(filePath,source(true),'utf8');await confirm();
  await call('ImportPlcExternalSource',{softwarePath,groupPath:'',filePath});
  const bad=await raw('GenerateBlocksFromExternalSource',{softwarePath,externalSourceName:'TG_IntegrationProbe.scl'});
  receipt.badGenerationReportedSuccess=JSON.stringify(bad).includes('success');
  const badCompile=await raw('CompileSoftware',{softwarePath});
  const value=badCompile.structuredContent??JSON.parse(badCompile.content.filter(c=>c.type==='text')[0].text);receipt.negativeCompile=value;receipt.negativeSourceRejected=Number.isInteger(value.errorCount)&&value.errorCount>0;save();
  if(!receipt.negativeSourceRejected)throw Error('Negative compiler rejection not established');
  await confirm();await call('DeletePlcExternalSource',{softwarePath,externalSourceName:'TG_IntegrationProbe.scl'});
  fs.writeFileSync(filePath,source(false),'utf8');await call('ImportPlcExternalSource',{softwarePath,groupPath:'',filePath});
  await call('GenerateBlocksFromExternalSource',{softwarePath,externalSourceName:'TG_IntegrationProbe.scl'});
  receipt.compile=await call('CompileSoftware',{softwarePath});if(receipt.compile.errorCount!==0)throw Error('Compile not clean');
  await confirm();await call('ExportBlock',{softwarePath,blockPath:block,exportPath:copy.exportDirectory,preservePath:false});
  await call('SaveProject');await call('CloseProject',{closeForeignProject:false});opened=false;
  await call('OpenProject',{path:copy.projectFile,closeForeignProject:false});opened=true;await confirm();
  receipt.reopened=await call('GetBlocks',{softwarePath,regexName:'^'+block+'$'});
  if(receipt.reopened.items?.length!==1||receipt.reopened.items[0].isConsistent!==true)throw Error('Reopened block not consistent');
  const reopenedExport=path.join(copy.capsule,'reopened-export');fs.mkdirSync(reopenedExport);
  await call('ExportBlock',{softwarePath,blockPath:block,exportPath:reopenedExport,preservePath:false});
  receipt.reopenedCompile=await call('CompileSoftware',{softwarePath});if(receipt.reopenedCompile.errorCount!==0)throw Error('Reopened compilation failed');
  receipt.programmingVerified=true;
 }catch(e){receipt.error=e.message;receipt.stderr=stderr;process.exitCode=1;}
 finally{
  try{if(opened){await confirm();await call('CloseProject',{closeForeignProject:false});}if(attempted)await call('Disconnect');assertDisconnected(await call('GetState'));receipt.disconnected=true;}
  catch(e){receipt.cleanupError=e.message;receipt.programmingVerified=false;process.exitCode=1;}
  await client.close();receipt.sourceUnchanged=sameFiles(copy.before,inventory(copy.source));if(!receipt.sourceUnchanged){receipt.programmingVerified=false;process.exitCode=1;}
  receipt.endedAt=new Date().toISOString();save();console.log(JSON.stringify({report,verified:receipt.programmingVerified,error:receipt.error,disconnected:receipt.disconnected,sourceUnchanged:receipt.sourceUnchanged}));
 }
}
main().catch(e=>{receipt.error=e.message;save();console.error(e.message);process.exitCode=1;});
