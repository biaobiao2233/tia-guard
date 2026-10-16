"use strict";
const {digest}=require('../guard');const crypto=require('node:crypto');
const {COMPILER}=require('../results');
const target={projectId:'a'.repeat(64),applicationId:'11111111-1111-4111-8111-111111111111',applicationName:'Application',deviceId:'22222222-2222-4222-8222-222222222222'};
const context=request=>({requestId:crypto.randomUUID(),requestHash:digest(request)});
function compilation(request={operation:'compile'}) {
  const ctx=context(request);
  const row=(phase,message,extra={})=>({phase,message,severity:'Text',category:COMPILER,code:'C0',requestId:ctx.requestId,buildId:ctx.requestId,target,foreignOrigin:false,correlated:true,targetHeader:false,buildSummary:false,...extra});
  const diagnostics=['rebuild','generate'].flatMap(phase=>[row(phase,'------ 开始构建应用程序 OwnedPLC.Sim.OwnedPLC.Application -------',{targetHeader:true}),
    row(phase,phase==='generate'?'构建完整--0错误,0警告:准备下载':'编译完成--0错误,0警告',{buildSummary:phase==='generate'})]);
  return {context:ctx,target,compilation:{buildStarted:true,buildCompleted:true,diagnosticsComplete:true,nativeCodeGenerated:true,errorCount:0,warningCount:0,
    buildId:ctx.requestId,requestHash:ctx.requestHash,target,sourceFingerprint:'b'.repeat(64),buildException:null,diagnostics,
    countSemantics:'phase-diagnostic-occurrences',
    phases:['rebuild','generate'].map(name=>({name,completed:true,headerSeen:true,diagnosticCount:2,errorCount:0,warningCount:0,summaryCounts:{errorCount:0,warningCount:0},countsMatchSummary:true}))}};
}
module.exports={target,context,compilation};
