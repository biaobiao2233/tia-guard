"use strict";
const test=require("node:test"),assert=require("node:assert/strict"),fs=require("node:fs"),path=require("node:path"),os=require("node:os");
const {completeResult}=require("../pages.js");
const {prepareCopy,binding,blockIdentity,inventory,sameFiles,inspectOwnedCopy,verifyReadContract}=require("../owned-read.js");
const {canonical,sha256}=require("../contract.js");
const envelope=value=>({structuredContent:value});
function page(text,id,offset,total){const end=offset+text.length;return envelope({message:text,meta:{ok:true,exportId:id,offset,returned:text.length,totalLength:total,nextOffset:end===total?null:end,eof:end===total}});}
test("public GetExport character pages are assembled before parsing",async()=>{
  const text=JSON.stringify({items:[{name:"测试🙂"}],meta:{success:true}}),cut=17,id="ex_fixture_1";
  const first=page(text.slice(0,cut),id,0,text.length);first.structuredContent.meta.truncated=true;
  const calls=[];
  const result=await completeResult(first,async(got,offset)=>{calls.push([got,offset]);return page(text.slice(offset),id,offset,text.length);});
  assert.equal(result.items[0].name,"测试🙂");assert.deepEqual(calls,[[id,cut]]);
});
test("pagination rejects wrong offsets/identities, excessive length, partial and failed inner data",async()=>{
  const text='{"items":[],"meta":{"complete":false}}',id="ex_fixture_1";
  const first=page(text.slice(0,7),id,0,text.length);first.structuredContent.meta.truncated=true;
  for(const change of [{offset:6},{exportId:"ex_other"},{totalLength:999},{eof:false},{returned:1}]){
    await assert.rejects(completeResult(first,async()=>{const p=page(text.slice(7),id,7,text.length);Object.assign(p.structuredContent.meta,change);return p;}));
  }
  await assert.rejects(completeResult(first,async()=>page(text.slice(7),id,7,text.length)),/incomplete/);
  await assert.rejects(completeResult(first,async()=>{throw Error("must not page");},1),/pagination/);
  for(const data of [{meta:{complete:false}},{meta:{truncated:true}},{data:{complete:false}},{meta:{ok:false}}]){
    await assert.rejects(completeResult(envelope(data),async()=>{}));
  }
});
test("owned copies never modify the source and reject outputs nested in it",t=>{
  const base=fs.mkdtempSync(path.join(os.tmpdir(),"tg-owned-read-"));
  t.after(()=>{const actual=fs.realpathSync(base);assert.ok(path.basename(actual).startsWith("tg-owned-read-"));fs.rmSync(actual,{recursive:true});});
  const source=path.join(base,"source"),out=path.join(base,"artifacts");fs.mkdirSync(source);
  const project=path.join(source,"Example.ap21");fs.writeFileSync(project,"sanitized non-native fixture");
  const initial=inventory(source);const copy=prepareCopy({sourceProjectFile:project,artifactRoot:out});
  assert.notEqual(copy.projectFile,project);assert.equal(sameFiles(initial,inventory(source)),true);
  assert.equal(sameFiles(initial,inventory(copy.projectDirectory)),true);
  fs.writeFileSync(copy.projectFile,"copy only");assert.equal(sameFiles(initial,inventory(source)),true);
  assert.throws(()=>prepareCopy({sourceProjectFile:project,artifactRoot:path.join(source,"output")}),/inside-source/);
  assert.throws(()=>prepareCopy({sourceProjectFile:"relative.ap21",artifactRoot:out}),/configuration/);
});
test("project binding requires exactly one actual Path attribute and native blocks",()=>{
  const expected=path.resolve("Owned.ap21");
  const p={items:[{name:"Owned",attributes:[{name:"Path",value:expected}]}]};
  assert.equal(binding(p,expected),"Owned");
  assert.throws(()=>binding(p,path.resolve("Original.ap21")),/mismatch/);
  assert.throws(()=>binding({items:[...p.items,...p.items]},expected),/ambiguous/);
  assert.throws(()=>blockIdentity({items:[]}),/missing/);
});
test("partial native tags abort export, disconnect, and retain a failed receipt",async t=>{
  const base=fs.mkdtempSync(path.join(os.tmpdir(),"tg-read-failure-"));
  t.after(()=>{assert.ok(path.basename(base).startsWith("tg-read-failure-"));fs.rmSync(base,{recursive:true});});
  const source=path.join(base,"source");fs.mkdirSync(source);const project=path.join(source,"Owned.ap21");fs.writeFileSync(project,"fixture");
  let connected=false,target=null;const calls=[];
  const client={callTool:async({name,arguments:args})=>{
    calls.push(name);
    if(name==="GetState")return envelope({isConnected:connected,project:target?"Owned":"-",session:"-"});
    if(name==="ConnectIsolated"){connected=true;return envelope({meta:{verified:true,isolated:true}});}
    if(name==="OpenProject"){target=args.path;return envelope({meta:{success:true}});}
    if(name==="GetProject")return envelope({items:[{name:"Owned",attributes:[{name:"Path",value:target}]}]});
    if(name==="GetProjectTree")return envelope({tree:"PlcSoftware: PLC_1 [PLC Program]"});
    if(name==="GetBlocks")return envelope({items:[{name:"Main",typeName:"OB",programmingLanguage:"SCL"}]});
    if(name==="GetPlcTags")return envelope({ok:true,data:{complete:false}});
    if(name==="CloseProject"){target=null;return envelope({meta:{success:true}});}
    if(name==="Disconnect"){connected=false;return envelope({meta:{success:true}});}
    throw Error("Unexpected tool:"+name);
  }};
  const artifactRoot=path.join(base,"artifacts");
  await assert.rejects(inspectOwnedCopy(client,{sourceProjectFile:project,artifactRoot}),/incomplete/);
  assert.equal(calls.includes("ExportBlocks"),false);assert.equal(connected,false);
  const dir=fs.readdirSync(artifactRoot)[0],receipt=JSON.parse(fs.readFileSync(path.join(artifactRoot,dir,"receipt.json")));
  assert.equal(receipt.ownedReadVerified,false);assert.equal(receipt.sourceUnchanged,true);assert.ok(receipt.error);
});
test("owned-read schema pin rejects additional required parameters and missing tools",()=>{
  const names=["ConnectIsolated","GetState","OpenProject","GetProject","GetProjectTree","GetBlocks","GetPlcTags","ExportBlocks","GetExport","CloseProject","Disconnect"];
  const tools=names.map(name=>({name,inputSchema:{type:"object",properties:{}}}));
  const digest=sha256(JSON.stringify(canonical(tools)));verifyReadContract(tools,digest);
  assert.throws(()=>verifyReadContract(tools.slice(1),digest),/missing/);
  tools[0].inputSchema.required=["newParameter"];assert.throws(()=>verifyReadContract(tools,digest),/drift/);
});
