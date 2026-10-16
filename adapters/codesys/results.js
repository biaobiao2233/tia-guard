"use strict";
const {z}=require('zod');
const {digest}=require('./guard');
const hex=z.string().regex(/^[0-9a-f]{64}$/);
const guid=z.string().regex(/^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i);
const targetSchema=z.strictObject({projectId:hex,applicationId:guid,applicationName:z.literal('Application'),deviceId:guid});
const contextSchema=z.strictObject({requestId:z.string().uuid(),requestHash:hex});
const snapshotSchema=z.strictObject({
  objects:z.array(z.strictObject({name:z.string(),kind:z.enum(['program','functionBlock','gvl']),variableTypes:z.record(z.string(),z.string()),declaration:z.string(),implementation:z.string()})),
  tasks:z.array(z.strictObject({name:z.string(),kind:z.string(),interval:z.string(),calls:z.array(z.string())})),
  device:z.strictObject({type:z.literal(4097),id:z.literal('0000 000a'),version:z.literal('3.5.22.40')}),simulation:z.literal(true)
});
const COMPILER='97f48d64-a2a3-4856-b640-75c046e37ea9';
const ANALYSIS='220493a1-f49b-4416-9a3f-a545db707cbe';
const summaryCandidate=/^(?:编译完成|构建完整|Compile complete|Compilation complete|Build complete)/i;
function parseSummaryCounts(message,phase) {
  const prefix=phase==='rebuild'?'(?:编译完成|Compile complete|Compilation complete)':'(?:构建完整|Build complete)';
  const match=new RegExp('^'+prefix+'\\s*--\\s*(\\d+)\\s*(?:错误|errors?)\\s*,\\s*(\\d+)\\s*(?:警告|warnings?)(?::.*)?\\s*$','i').exec(message);
  if(!match) return null;
  const [errorCount,warningCount]=match.slice(1).map(Number);
  return [errorCount,warningCount].every(n=>Number.isInteger(n)&&n>=0&&n<=2147483647)?{errorCount,warningCount}:null;
}
const sameTarget=(a,b)=>!!a&&!!b&&['projectId','applicationId','applicationName','deviceId'].every(k=>a[k]===b[k]);
const normalize=s=>s.replace(/\r\n?/g,'\n').replace(/\n+$/,'');
function snapshotMatches(snapshot,expected) {
  if(!snapshotSchema.safeParse(snapshot).success || !expected) return false;
  const objects=snapshot.objects;
  if(objects.length!==expected.objects.length || new Set(objects.map(o=>o.name)).size!==objects.length) return false;
  for(const spec of expected.objects) {
    const o=objects.find(o=>o.name===spec.name);
    if(!o || o.kind!==spec.kind || ['declaration','implementation'].some(k=>normalize(o[k])!==normalize(spec[k]))) return false;
    if(o.name==='GVL_Test' && !(Object.keys(o.variableTypes).length===2 && o.variableTypes.Enable==='BOOL' && o.variableTypes.Step==='DINT')) return false;
  }
  return snapshot.tasks.length===1 && snapshot.tasks[0].name==='MainTask' && snapshot.tasks[0].kind==='Cyclic' &&
    snapshot.tasks[0].interval==='t#20ms' && snapshot.tasks[0].calls.length===1 && snapshot.tasks[0].calls[0]===expected.entry;
}
function compilationPassed(c,context,target) {
  if(!contextSchema.safeParse(context).success || !targetSchema.safeParse(target).success || !c ||
    c.buildStarted!==true || c.buildCompleted!==true || c.diagnosticsComplete!==true || c.nativeCodeGenerated!==true ||
    c.errorCount!==0 || !Number.isInteger(c.warningCount) || c.warningCount<0 || c.buildException ||
    c.buildId!==context.requestId || c.requestHash!==context.requestHash || !sameTarget(c.target,target) ||
    !hex.safeParse(c.sourceFingerprint).success || !Array.isArray(c.diagnostics) || !c.diagnostics.length ||
    !Array.isArray(c.phases) || c.phases.length!==2 || c.countSemantics!=='phase-diagnostic-occurrences') return false;
  for(const name of ['rebuild','generate']) {
    const phases=c.phases.filter(p=>p.name===name);const rows=c.diagnostics.filter(d=>d.phase===name);
    const errorCount=rows.filter(d=>['Error','FatalError'].includes(d.severity)).length,warningCount=rows.filter(d=>d.severity==='Warning').length;
    const summaries=rows.filter(d=>d.category===COMPILER&&d.code==='C0'&&summaryCandidate.test(d.message));
    const counts=summaries.length===1?parseSummaryCounts(summaries[0].message,name):null;
    if(!counts || counts.errorCount!==errorCount || counts.warningCount!==warningCount ||
      phases.length!==1 || phases[0].errorCount!==errorCount || phases[0].warningCount!==warningCount || phases[0].countsMatchSummary!==true ||
      phases[0].summaryCounts?.errorCount!==errorCount || phases[0].summaryCounts?.warningCount!==warningCount ||
      phases[0].completed!==true || phases[0].headerSeen!==true || phases[0].diagnosticCount!==rows.length ||
      !rows.some(d=>d.category===COMPILER && d.code==='C0' && d.targetHeader===true && /^------ /.test(d.message) && d.message.split(/\s+/).includes('OwnedPLC.Sim.OwnedPLC.Application'))) return false;
  }
  if(!c.diagnostics.every(d=>['Warning','Information','Text'].includes(d.severity) && [COMPILER,ANALYSIS].includes(d.category) &&
    d.requestId===context.requestId && d.buildId===c.buildId && sameTarget(d.target,target) && d.correlated===true && d.foreignOrigin===false &&
    ['rebuild','generate'].includes(d.phase) && typeof d.message==='string')) return false;
  return c.warningCount===c.diagnostics.filter(d=>d.severity==='Warning').length &&
    c.diagnostics.some(d=>d.phase==='generate' && d.category===COMPILER && d.code==='C0' && d.buildSummary===true && parseSummaryCounts(d.message,'generate')?.errorCount===0);
}
function typedValue(variable,value) {
  if(variable==='GVL_Test.Enable') {if(value!=='TRUE'&&value!=='FALSE') throw Error('invalid-native-result');return value==='TRUE';}
  if(variable!=='GVL_Test.Step' || typeof value!=='string' || !/^DINT#[+-]?\d+$/.test(value)) throw Error('invalid-native-result');
  const number=Number(value.slice(5));if(!Number.isInteger(number)||number< -2147483648||number>2147483647) throw Error('invalid-native-result');return number;
}
function validateResult(request,r,state) {
  const invalid=()=>{throw Error('invalid-native-result');};
  if(!r || r.operation!==request.operation || typeof r.success!=='boolean' || !contextSchema.safeParse(r.context).success || r.context.requestHash!==digest(request)) invalid();
  if(!r.success) return; // Negative results never qualify; native error/partial diagnostics are preserved.
  if(!targetSchema.safeParse(r.target).success || (request.operation!=='create' && !state.target) || (state.target && !sameTarget(r.target,state.target))) invalid();
  switch(request.operation) {
    case 'create': case 'edit': case 'inspect': if(!snapshotMatches(r.snapshot,state.expected)) invalid();break;
    case 'compile': if(!compilationPassed(r.compilation,r.context,r.target)) invalid();break;
    case 'reopen': {
      const v=r.verification;
      if(!v || v.complete!==true || v.closedAndReopened!==true || v.verdict!=='pass' || v.requestTextMatches!==true || v.expectedStructureMatches!==true ||
        !Array.isArray(v.differences) || v.differences.length || !snapshotMatches(v.before,state.expected) || !snapshotMatches(v.after,state.expected) ||
        JSON.stringify(v.before)!==JSON.stringify(v.after) || !state.compilation || v.sourceFingerprint!==state.compilation.sourceFingerprint) invalid();
      break;
    }
    case 'login': if(r.loggedIn!==true || r.simulation!==true || !['run','stop'].includes(r.state) || !state.verified) invalid();break;
    case 'run': case 'stop': if(r.state!==request.operation) invalid();break;
    case 'write':
      if(r.variable!==request.variable || r.plcType!==(request.variable==='GVL_Test.Enable'?'BOOL':'DINT') || typedValue(r.variable,r.observed)!==request.value) invalid();
      typedValue(r.variable,r.before);break;
    case 'read':
      if(!['run','stop'].includes(r.state) || !r.values || Object.keys(r.values).length!==request.variables.length ||
        !request.variables.every(v=>typeof r.values[v]==='string' && r.values[v].length>0 && !['None','<unknown>'].includes(r.values[v]))) invalid();break;
    default: invalid();
  }
}
module.exports={validateResult,compilationPassed,snapshotMatches,typedValue,COMPILER,ANALYSIS};
