#!/usr/bin/env node
"use strict";
const {McpServer}=require('@modelcontextprotocol/server');
const {serveStdio}=require('@modelcontextprotocol/server/stdio');
const {tools}=require('./schemas');
const {fromEnvironment}=require('./provider');
function buildServer(provider) {
  const server=new McpServer({name:'tia-guard-codesys',version:'0.1.0'},{capabilities:{tools:{}}});
  for(const [name,tool] of Object.entries(tools)) server.registerTool(name,{
    description:tool.description,inputSchema:tool.schema,
    annotations:{readOnlyHint:tool.readOnly,destructiveHint:!tool.readOnly,idempotentHint:tool.readOnly,openWorldHint:false}
  },async args=>{
    const parsed=tool.schema.safeParse(args);
    const report=parsed.success?await provider.dispatch(name,parsed.data):{schemaVersion:'1.0',operation:name,success:false,error:'invalid-arguments'};
    return {content:[{type:'text',text:JSON.stringify(report)}],structuredContent:report,isError:report.success!==true};
  });
  return server;
}
function main() {
  const provider=fromEnvironment();
  const handle=serveStdio(()=>buildServer(provider),{onerror:()=>{}});
  let closing;
  const close=()=>closing||=(async()=>{
    const result=await provider.dispose();
    if(!result?.success){process.stderr.write('CODESYS owned cleanup incomplete\n');process.exitCode=1;}
    await handle.close();
  })().catch(()=>{process.stderr.write('CODESYS owned cleanup failed\n');process.exitCode=1;});
  process.stdin.once('end',close);process.once('SIGINT',close);process.once('SIGTERM',close);
}
if(require.main===module) main();
module.exports={buildServer,main};
