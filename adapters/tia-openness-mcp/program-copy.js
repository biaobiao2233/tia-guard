"use strict";
const fs=require("node:fs"),path=require("node:path");
const {AdapterError,sha256,canonical,assertDisconnected}=require("./contract.js");
const {prepareCopy,binding,inventory,sameFiles}=require("./owned-read.js");
const {completeResult}=require("./pages.js");
const tools=["GetState","ConnectIsolated","OpenProject","GetProject","GetProjectTree","GetBlocks","ImportPlcExternalSource","GenerateBlocksFromExternalSource","CompileSoftware","ExportBlock","SaveProject","CloseProject","Disconnect","GetExport"];
function contract(toolsList){return sha256(JSON.stringify(canonical(tools.map(name=>{
 const found=toolsList.filter(t=>t.name===name);if(found.length!==1)throw new AdapterError("program-tool-missing","contract");
 const t=found[0];return {name,inputSchema:t.inputSchema,...(t.outputSchema?{outputSchema:t.outputSchema}:{})};
}))));}
function validatePlan(plan){
 if(!plan||!path.isAbsolute(plan.sourceFile??"")||!/^FC_TG_[A-Za-z0-9_]{1,60}$/.test(plan.blockName??"")||! /^[a-f0-9]{64}$/.test(plan.sourceSha256??""))throw new AdapterError("invalid-program-plan","policy");
 if(fs.lstatSync(plan.sourceFile).isSymbolicLink())throw new AdapterError("program-source-link","policy");
 const bytes=fs.readFileSync(plan.sourceFile);if(bytes.length>100000||sha256(bytes)!==plan.sourceSha256)throw new AdapterError("program-source-hash-mismatch","policy");
 const text=bytes.toString("utf8").replace(/^\uFEFF/,"");
 // Deliberately narrow: one new FC, no comments/extra declarations or included files.
 if(/\/\/|\(\*|\*\)|\b(?:FUNCTION_BLOCK|DATA_BLOCK|ORGANIZATION_BLOCK|TYPE|INCLUDE)\b/i.test(text)||
   (text.match(/\bFUNCTION\b/gi)??[]).length!==1||(text.match(/\bEND_FUNCTION\b/gi)??[]).length!==1||
   !text.startsWith('FUNCTION "'+plan.blockName+'" :')||! /END_FUNCTION\s*$/i.test(text))throw new AdapterError("unsupported-program-source","policy");
 return {blockName:plan.blockName,bytes,sourceSha256:plan.sourceSha256};
}
function network(directory){
 const files=fs.readdirSync(directory).filter(n=>n.endsWith(".xml"));if(files.length!==1)throw new AdapterError("program-export-file-count","verification");
 const text=fs.readFileSync(path.join(directory,files[0]),"utf8");
 const nodes=[...text.matchAll(/<NetworkSource(?:\s[^>]*)?>([\s\S]*?)<\/NetworkSource>/g)].map(m=>m[1]);
 if(nodes.length!==1)throw new AdapterError("unsupported-program-export","verification");return sha256(nodes[0]);
}
async function programCopy(client,settings,plan){
 const validated=validatePlan(plan),copy=prepareCopy(settings),r={scope:"new-whitelisted-FC-in-owned-copy",sourceSha256:validated.sourceSha256,blockName:validated.blockName,projectFile:copy.projectFile,programmingVerified:false,runtimeQualified:false,calls:[]};
 let attempted=false,opened=false,softwarePath;
 const raw=async(name,args={})=>{const step={name};r.calls.push(step);try{step.result=await client.callTool({name,arguments:args},{timeout:180000});return step.result;}catch(e){step.error=e.message;throw e;}};
 const call=async(name,args={})=>completeResult(await raw(name,args),(exportId,offset)=>raw("GetExport",{exportId,offset}));
 const confirm=async()=>binding(await call("GetProject"),copy.projectFile);
 const mutate=async(name,args={})=>{await confirm();return call(name,args);};
 try{
  assertDisconnected(await call("GetState"));attempted=true;
  const c=await call("ConnectIsolated");if(c.meta?.isolated!==true||c.meta?.verified!==true)throw new AdapterError("program-isolation-unverified","ownership");
  const empty=await call("GetState");if(empty.isConnected!==true||empty.project!=="-"||empty.session!=="-")throw new AdapterError("program-instance-not-empty","ownership");
  await call("OpenProject",{path:copy.projectFile,closeForeignProject:false});opened=true;await confirm();
  const tree=await call("GetProjectTree"),names=[...new Set([...String(tree.tree??"").matchAll(/PlcSoftware: (.+?) \[PLC Program\]/g)].map(m=>m[1]))];
  if(names.length!==1)throw new AdapterError("program-plc-ambiguous","ownership");softwarePath=names[0];
  const existing=await call("GetBlocks",{softwarePath,regexName:"^"+validated.blockName+"$"});
  if(!Array.isArray(existing.items)||existing.items.length!==0)throw new AdapterError("program-block-already-exists","ownership");
  r.baselineCompile=await mutate("CompileSoftware",{softwarePath});if(r.baselineCompile.errorCount!==0)throw new AdapterError("program-baseline-errors","compiler");
  const filePath=path.join(copy.capsule,validated.blockName+".scl");fs.writeFileSync(filePath,validated.bytes,{flag:"wx"});
  await mutate("ImportPlcExternalSource",{softwarePath,groupPath:"",filePath});
  await mutate("GenerateBlocksFromExternalSource",{softwarePath,externalSourceName:validated.blockName+".scl"});
  r.compile=await mutate("CompileSoftware",{softwarePath});if(r.compile.errorCount!==0)throw new AdapterError("program-compile-errors","compiler");
  await mutate("ExportBlock",{softwarePath,blockPath:validated.blockName,exportPath:copy.exportDirectory,preservePath:false});
  const before=network(copy.exportDirectory);
  await mutate("SaveProject");await mutate("CloseProject",{closeForeignProject:false});opened=false;
  await call("OpenProject",{path:copy.projectFile,closeForeignProject:false});opened=true;await confirm();
  const blocks=await call("GetBlocks",{softwarePath,regexName:"^"+validated.blockName+"$"});
  if(blocks.items?.length!==1||blocks.items[0].name!==validated.blockName||blocks.items[0].isConsistent!==true)throw new AdapterError("program-reopen-block-mismatch","verification");
  const output=path.join(copy.capsule,"reopened-export");fs.mkdirSync(output);
  await mutate("ExportBlock",{softwarePath,blockPath:validated.blockName,exportPath:output,preservePath:false});
  if(network(output)!==before)throw new AdapterError("program-reopen-logic-mismatch","verification");
  r.logicSha256=before;r.reopenedCompile=await mutate("CompileSoftware",{softwarePath});if(r.reopenedCompile.errorCount!==0)throw new AdapterError("program-reopen-errors","compiler");
  r.reopenVerified=true;
 }catch(e){
  r.error={code:e.code??"program-operation-failed",message:e.message};
  const last=r.calls.filter(c=>c.name==="CompileSoftware"&&c.result).at(-1);
  if(last){try{
    const result=last.result,v=result.structuredContent??JSON.parse(result.content.filter(c=>c.type==="text")[0].text);
    if(Number.isInteger(v.errorCount))r.compilerDiagnostics={state:v.state,errorCount:v.errorCount,warningCount:v.warningCount,messages:Array.isArray(v.messages)?v.messages.slice(0,50):[],truncated:v.meta?.truncated===true||(v.messages?.length??0)>50};
  }catch{r.compilerDiagnostics={truncated:true,unavailable:true};}}
  const failure=e instanceof AdapterError?e:new AdapterError("program-operation-failed","engineering");
  failure.artifactDirectory=copy.capsule;failure.compilerDiagnostics=r.compilerDiagnostics;throw failure;
 }
 finally{
  try{if(opened)await mutate("CloseProject",{closeForeignProject:false});if(attempted)await call("Disconnect");assertDisconnected(await call("GetState"));r.disconnected=true;}
  catch(e){r.cleanupError=e.message;throw e;}
  finally{r.sourceUnchanged=sameFiles(copy.before,inventory(copy.source));r.programmingVerified=r.reopenVerified===true&&r.disconnected===true&&r.sourceUnchanged&&!r.error&&!r.cleanupError;fs.writeFileSync(path.join(copy.capsule,"program-receipt.json"),JSON.stringify(r,null,2)+"\n",{flag:"wx"});}
 }
 if(!r.programmingVerified)throw new AdapterError("program-verification-incomplete","verification");return r;
}
module.exports={validatePlan,contract,programCopy};
