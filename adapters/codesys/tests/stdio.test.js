"use strict";
const test=require('node:test');const assert=require('node:assert/strict');const path=require('node:path');
const {Client}=require('@modelcontextprotocol/client');const {StdioClientTransport}=require('@modelcontextprotocol/client/stdio');
test('real SDK stdio discovery, default read-only and shutdown without CODESYS',async()=>{
  const transport=new StdioClientTransport({command:process.execPath,args:[path.join(__dirname,'..','index.js')],env:{...process.env,CODESYS_GUARD_WRITES:'0'},stderr:'pipe'});
  const client=new Client({name:'codesys-contract',version:'1.0'});
  try {
    await client.connect(transport);
    const list=await client.listTools();assert.equal(list.tools.length,7);
    const call=async(name,args={})=>client.callTool({name,arguments:args});
    const p=await call('preview_codesys',{request:{operation:'compile'}});assert.equal(p.isError,false);
    const g=await call('authorize_codesys',{previewId:p.structuredContent.previewId});assert.equal(g.isError,true);assert.equal(g.structuredContent.error,'read-only-mode');
    const read=await call('inspect_codesys');assert.equal(read.isError,true);assert.equal(read.structuredContent.error,'no-owned-session');
    const cleanup=await call('dispose_codesys');assert.equal(cleanup.isError,false);assert.equal(cleanup.structuredContent.ownedProcessExited,true);
  }finally{await client.close();}
});
