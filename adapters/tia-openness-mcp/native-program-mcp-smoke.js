#!/usr/bin/env node
"use strict";
const path=require("node:path"),assert=require("node:assert/strict");
const {Client}=require("@modelcontextprotocol/client");
const {StdioClientTransport,getDefaultEnvironment}=require("@modelcontextprotocol/client/stdio");
async function main(){
 const t=new StdioClientTransport({command:process.execPath,args:[path.join(__dirname,"index.js")],cwd:__dirname,stderr:"pipe",env:{...getDefaultEnvironment(),TG_TIA_UPSTREAM_CONFIG:process.env.TG_TIA_UPSTREAM_CONFIG}});
 t.stderr.on("data",()=>{});const c=new Client({name:"tia-guard-program-mcp-acceptance",version:"0.1.0"},{mode:"legacy"});
 const receipt={startedAt:new Date().toISOString(),verified:false,runtimeQualified:false};
 try{
  await c.connect(t);assert.ok((await c.listTools()).tools.some(x=>x.name==="upstream_program_copy"));
  let denied=false;try{denied=(await c.callTool({name:"upstream_program_copy",arguments:{sourceFile:"Other.scl"}})).isError===true;}catch{denied=true;}assert.equal(denied,true);receipt.callerOverrideRejected=true;
  const result=await c.callTool({name:"upstream_program_copy",arguments:{}},{timeout:600000});
  if(result.isError)throw Error(result.structuredContent?.code??"MCP programming failed");receipt.result=result.structuredContent;
  assert.equal(receipt.result.programmingVerified,true);
  const closed=await c.callTool({name:"upstream_close",arguments:{}});assert.equal(closed.structuredContent.cleanupConfirmed,true);receipt.cleanupConfirmed=true;receipt.verified=true;
 }catch(e){receipt.error=e.message;process.exitCode=1;}
 finally{await c.close();receipt.endedAt=new Date().toISOString();console.log(JSON.stringify(receipt,null,2));}
}
main().catch(e=>{console.error(e.message);process.exitCode=1;});
