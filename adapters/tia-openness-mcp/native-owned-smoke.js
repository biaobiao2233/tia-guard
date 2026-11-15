#!/usr/bin/env node
"use strict";
// Opt-in native MCP acceptance; machine-specific receipts must remain local.
const path=require("node:path"),assert=require("node:assert/strict");
const {Client}=require("@modelcontextprotocol/client");
const {StdioClientTransport,getDefaultEnvironment}=require("@modelcontextprotocol/client/stdio");
async function main(){
  if(!process.env.TG_TIA_UPSTREAM_CONFIG)throw Error("Explicit owned-read configuration required");
  const transport=new StdioClientTransport({command:process.execPath,args:[path.join(__dirname,"index.js")],cwd:__dirname,stderr:"pipe",env:{...getDefaultEnvironment(),TG_TIA_UPSTREAM_CONFIG:process.env.TG_TIA_UPSTREAM_CONFIG}});
  let stderr="";transport.stderr.on("data",b=>{stderr=(stderr+b).slice(-8192);});
  const client=new Client({name:"tia-guard-native-owned-read",version:"0.1.0"},{mode:"legacy"});
  const receipt={startedAt:new Date().toISOString(),ownedReadVerified:false,engineeringQualified:false,runtimeQualified:false};
  try{
    await client.connect(transport,{timeout:20000});
    const tools=(await client.listTools()).tools;
    assert.equal(tools.filter(t=>t.name==="upstream_inspect_copy").length,1);
    const result=await client.callTool({name:"upstream_inspect_copy",arguments:{}},{timeout:600000});
    if(result.isError)throw Error(result.structuredContent?.code??"Owned inspection failed");
    receipt.result=result.structuredContent;
    assert.equal(receipt.result.sourceUnchanged,true);assert.equal(receipt.result.reopenVerified,true);
    assert.equal(receipt.result.disconnected,true);assert.ok(receipt.result.blocks.length>0);
    assert.ok(Object.keys(receipt.result.exportFiles).length>0);
    const close=await client.callTool({name:"upstream_close",arguments:{}});
    assert.equal(close.structuredContent.cleanupConfirmed,true);receipt.cleanupConfirmed=true;
    const ended=await client.callTool({name:"upstream_state",arguments:{}});
    assert.equal(ended.isError,true);receipt.ownedReadVerified=true;
  }catch(error){receipt.error=error.message;receipt.stderr=stderr;process.exitCode=1;}
  finally{await client.close();receipt.endedAt=new Date().toISOString();process.stdout.write(JSON.stringify(receipt,null,2)+"\n");}
}
main().catch(error=>{process.stderr.write(error.message+"\n");process.exitCode=1;});
