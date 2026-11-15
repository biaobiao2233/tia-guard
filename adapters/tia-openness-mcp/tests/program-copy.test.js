"use strict";
const test=require("node:test"),assert=require("node:assert/strict"),fs=require("node:fs"),path=require("node:path"),os=require("node:os");
const {validatePlan,programCopy}=require("../program-copy.js");
const {sha256}=require("../contract.js");
const {fixture}=require("./support.js");
function planFixture(t){
 const root=fs.mkdtempSync(path.join(os.tmpdir(),"tg-program-plan-"));t.after(()=>{assert.ok(path.basename(root).startsWith("tg-program-plan-"));fs.rmSync(root,{recursive:true});});
 const file=path.join(root,"Source.scl"),text='FUNCTION "FC_TG_Example" : DInt\nBEGIN\n #FC_TG_Example := 1;\nEND_FUNCTION\n';fs.writeFileSync(file,text);
 return {root,text,plan:{sourceFile:file,sourceSha256:sha256(text),blockName:"FC_TG_Example"}};
}
test("only hash-pinned single new whitelisted FC source is accepted",t=>{
 const f=planFixture(t);assert.equal(validatePlan(f.plan).blockName,"FC_TG_Example");
 assert.throws(()=>validatePlan({...f.plan,blockName:"Main"}),/invalid-program-plan/);
 assert.throws(()=>validatePlan({...f.plan,sourceSha256:"0".repeat(64)}),/hash-mismatch/);
 for(const text of [f.text+'FUNCTION "FC_TG_Other" : DInt\nEND_FUNCTION',f.text+'\nTYPE "Extra"',f.text.replace(' := 1',' := 1 //hidden')]){
  fs.writeFileSync(f.plan.sourceFile,text);assert.throws(()=>validatePlan({...f.plan,sourceSha256:sha256(text)}),/unsupported/);
 }
});
test("a diagnostic engine cannot silently become a write engine",async t=>{
 const f=await fixture(t),p=planFixture(t);f.provider.config.ownedProgram=p.plan;
 await f.provider.invoke("capabilities");const calls=(await f.readCalls()).length;
 await assert.rejects(f.provider.programCopy(),/fresh-session/);assert.equal((await f.readCalls()).length,calls);
 assert.ok(f.launches[0].args.includes("--read-only"));
});
test("failed native compilation prevents save/reopen and records failure with cleanup",async t=>{
 const f=planFixture(t),source=path.join(f.root,"source");fs.mkdirSync(source);const project=path.join(source,"Owned.ap21");fs.writeFileSync(project,"sanitized native-free fixture");
 let connected=false,target=null;const calls=[];let compiles=0;
 const env=v=>({structuredContent:v});const client={callTool:async({name,arguments:a})=>{
  calls.push(name);
  if(name==="GetState")return env({isConnected:connected,project:target?"Owned":"-",session:"-"});
  if(name==="ConnectIsolated"){connected=true;return env({meta:{verified:true,isolated:true}});}
  if(name==="OpenProject"){target=a.path;return env({});}
  if(name==="GetProject")return env({items:[{name:"Owned",attributes:[{name:"Path",value:target}]}]});
  if(name==="GetProjectTree")return env({tree:"PlcSoftware: PLC_1 [PLC Program]"});
  if(name==="GetBlocks")return env({items:[]});
  if(name==="CompileSoftware")return env(++compiles===1?{errorCount:0}:{errorCount:1,state:"Error",messages:["Undefined test symbol"],meta:{success:false}});
  if(name==="ImportPlcExternalSource"||name==="GenerateBlocksFromExternalSource")return env({meta:{success:true}});
  if(name==="CloseProject"){target=null;return env({});}
  if(name==="Disconnect"){connected=false;return env({});}
  throw Error("Unexpected tool "+name);
 }};
 const artifactRoot=path.join(f.root,"artifacts");await assert.rejects(programCopy(client,{sourceProjectFile:project,artifactRoot},f.plan),e=>{assert.equal(e.compilerDiagnostics.errorCount,1);assert.ok(e.artifactDirectory.startsWith(artifactRoot));return true;});
 assert.equal(calls.includes("SaveProject"),false);assert.equal(calls.includes("ExportBlock"),false);assert.equal(connected,false);
 const receipt=JSON.parse(fs.readFileSync(path.join(artifactRoot,fs.readdirSync(artifactRoot)[0],"program-receipt.json")));assert.equal(receipt.programmingVerified,false);assert.equal(receipt.sourceUnchanged,true);
});
