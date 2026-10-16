"use strict";
const fs=require('node:fs/promises');
const path=require('node:path');
const {Guard,digest}=require('./guard');
const {NativeTransport}=require('./transport');
const {validateResult,compilationPassed}=require('./results');
function fromEnvironment(env=process.env) {
  return new Provider({writes:env.CODESYS_GUARD_WRITES==='1',exe:env.CODESYS_GUARD_EXE||'',
    profile:env.CODESYS_GUARD_PROFILE||'CODESYS V3.5 SP22 Patch 4',workspace:env.CODESYS_GUARD_WORKSPACE||''});
}
class Provider {
  constructor(config,transport) {
    this.config=config;this.guard=new Guard(config);this.transport=transport||new NativeTransport(config);this.chain=Promise.resolve();this.executionId=require('node:crypto').randomUUID();
    this.nativeState={target:null,expected:null,compilation:null,verified:false};this.integrityFault=null;
  }
  async nativeCall(request) {
    if(this.integrityFault) throw Error('native-session-faulted');
    try {
      const result=await this.transport.call(request);
      validateResult(request,result,this.nativeState);
      return result;
    }catch(error) {
      this.integrityFault=error.message;this.guard.close();this.nativeState.verified=false;this.nativeState.compilation=null;
      try{this.faultCleanup=await (this.transport.abort?this.transport.abort(error.message):this.transport.dispose());}
      catch(e){this.faultCleanup={success:false,ownedProcessExited:false,error:e.message};}
      error.cleanup=this.faultCleanup;throw error;
    }
  }
  dispatch(name,args) {
    const next=this.chain.then(()=>this.perform(name,args));
    this.chain=next.catch(()=>{});return next;
  }
  async perform(name,args) {
    const base={schemaVersion:'1.0',executionId:this.executionId,operation:name,success:true};
    try {
      let result;
      switch(name) {
        case 'doctor_codesys': {
          const exists=async p=>!!p && !!await fs.stat(p).catch(()=>null);
          const home=path.dirname(path.dirname(this.config.exe));
          result={nativeAvailable:process.platform==='win32' && await exists(this.config.exe),writesEnabled:this.config.writes,
            profile:this.config.profile,scriptEngineInstalled:await exists(path.join(home,'Common','ScriptEngine.dll')),
            onlineScriptInstalled:await exists(path.join(home,'ScriptLib','Stubs','scriptengine','ScriptOnline.pyi')),
            ideSimulationInstalled:await exists(path.join(home,'Common','SimulationRts.dll')),
            officialMcpLicense:'not-verified',runtimeLicense:'not-verified',
            scope:'new-owned-project-and-IDE-simulation',hardwareSupported:false};break;
        }
        case 'preview_codesys': result=this.guard.preview(args.request);break;
        case 'authorize_codesys': result=this.guard.authorize(args.previewId);break;
        case 'execute_codesys': {
          const frozen=this.guard.consume(args.previewId,args.grantId);
          const request=frozen.request;
          if(['create','edit','compile'].includes(request.operation)) {this.nativeState.compilation=null;this.nativeState.verified=false;}
          if(request.operation==='reopen') this.nativeState.verified=false;
          const previous=this.nativeState.expected;
          if(request.operation==='create') this.nativeState.expected=structuredClone(request);
          if(request.operation==='edit' && previous) this.nativeState.expected={...previous,objects:previous.objects.map(o=>o.name===request.object.name?request.object:o)};
          const r=await this.nativeCall(request);
          if(!r.success && ['create','edit'].includes(request.operation)) this.nativeState.expected=previous;
          if(r.success && request.operation==='create') this.nativeState.target=r.target;
          if(r.success && request.operation==='compile') this.nativeState.compilation=r.compilation;
          if(r.success && request.operation==='reopen') this.nativeState.verified=true;
          result={...r,operation:name,nativeOperation:r.operation,inputHash:frozen.inputHash,revision:this.guard.revision};
          break;
        }
        case 'inspect_codesys':
        case 'read_codesys':
          if(!this.transport.child) throw Error('no-owned-session');
          result=await this.nativeCall(name==='inspect_codesys'?{operation:'inspect'}:{operation:'read',...args});
          result={...result,nativeOperation:result.operation,operation:name};break;
        case 'dispose_codesys': this.guard.close();result=await this.transport.dispose();break;
        default: throw Error('unknown-tool');
      }
      const report={...base,...result};
      // Private raw receipt; never include installation/project paths in public evidence.
      if(this.transport.root) {
        const file=path.join(this.transport.root,`receipt-${require('node:crypto').randomUUID()}.json`);
        await fs.writeFile(file,JSON.stringify({...report,resultHash:digest(report)},null,2));
      }
      return report;
    }catch(e){return {...base,success:false,error:e.message,...(e.cleanup?{cleanup:e.cleanup,sessionFaulted:true}:{})};}
  }
  async dispose(){await this.chain;this.guard.close();return this.transport.dispose();}
}
module.exports={Provider,fromEnvironment,compilationPassed};
