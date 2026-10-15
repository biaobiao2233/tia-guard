"use strict";
const fs=require('node:fs/promises');
const path=require('node:path');
const {Guard,digest}=require('./guard');
const {NativeTransport}=require('./transport');
function fromEnvironment(env=process.env) {
  return new Provider({writes:env.CODESYS_GUARD_WRITES==='1',exe:env.CODESYS_GUARD_EXE||'',
    profile:env.CODESYS_GUARD_PROFILE||'CODESYS V3.5 SP22 Patch 4',workspace:env.CODESYS_GUARD_WORKSPACE||''});
}
class Provider {
  constructor(config,transport) {this.config=config;this.guard=new Guard(config);this.transport=transport||new NativeTransport(config);this.chain=Promise.resolve();this.executionId=require('node:crypto').randomUUID();}
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
          const r=await this.transport.call(frozen.request);
          result={...r,operation:name,nativeOperation:r.operation,inputHash:frozen.inputHash,revision:this.guard.revision};
          // A native result is required; a process exit, empty diagnostics or mock cannot qualify.
          if(frozen.request.operation==='compile' && r.success && !(r.compilation?.buildStarted && r.compilation?.buildCompleted && r.compilation?.diagnosticsComplete && r.compilation?.nativeCodeGenerated && r.compilation?.errorCount===0 && r.compilation?.diagnostics?.length>0)) result.success=false;
          break;
        }
        case 'inspect_codesys':
        case 'read_codesys':
          if(!this.transport.child) throw Error('no-owned-session');
          result=await this.transport.call(name==='inspect_codesys'?{operation:'inspect'}:{operation:'read',...args});
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
    }catch(e){return {...base,success:false,error:e.message};}
  }
  async dispose(){await this.chain;this.guard.close();return this.transport.dispose();}
}
module.exports={Provider,fromEnvironment};
