"use strict";
const fs = require('node:fs/promises');
const path = require('node:path');
const crypto = require('node:crypto');
const { spawn } = require('node:child_process');
const { digest } = require('./guard');
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
async function bounded(promise,ms) {let timer;try{return await Promise.race([promise,new Promise(r=>{timer=setTimeout(()=>r(null),ms);})]);}finally{clearTimeout(timer);}}
class NativeTransport {
  constructor(config,runtime={}) {
    this.config=config;this.child=null;this.root=null;this.exit=null;this.closed=false;this.faulted=null;
    this.key=crypto.randomBytes(32).toString('hex');this.spawn=runtime.spawn||spawn;this.platform=runtime.platform||process.platform;
    this.timeouts={startup:150000,operation:150000,cleanup:5000,exit:10000,kill:5000,poll:50,...runtime.timeouts};
  }
  async start() {
    if(this.faulted) throw Error('native-session-faulted');
    if(this.closed) throw Error('transport-closed');
    if(this.child) return;
    if(this.platform!=='win32') throw Error('native-windows-required');
    const c=this.config;
    if(!c.writes) throw Error('read-only-mode');
    if(!c.workspace || !path.isAbsolute(c.workspace) || !path.isAbsolute(c.exe)) throw Error('invalid-native-config');
    if(/["\r\n]/.test(c.profile) || !/^CODESYS V3\.5 SP22 Patch 4$/.test(c.profile)) throw Error('unsupported-profile');
    // Never attach to existing processes or project paths. Every native process gets a fresh slot.
    await fs.mkdir(c.workspace,{recursive:true});
    const real=await fs.realpath(c.workspace);
    this.root=await fs.mkdtemp(path.join(real,'codesys-'));
    const script=path.join(this.root,'native.py');
    await fs.copyFile(path.join(__dirname,'native.py'),script);
    await fs.copyFile(path.join(__dirname,'native_policy.py'),path.join(this.root,'native_policy.py'));
    this.child=this.spawn(c.exe,[`--profile="${c.profile}"`,`--runscript="${script}"`,'--noUI'],{
      cwd:this.root,windowsHide:true,windowsVerbatimArguments:true,stdio:['ignore','pipe','pipe'],
      env:{...process.env,CODESYS_GUARD_SESSION:this.root,CODESYS_GUARD_KEY:this.key,TEMP:this.root,TMP:this.root}
    });
    let log='';
    const capture=b=>{ log+=b.toString('utf8'); if(log.length>4*1024*1024) log=log.slice(-4*1024*1024); };
    this.child.stdout.on('data',capture);this.child.stderr.on('data',capture);
    this.exit=new Promise(resolve=>{
      this.child.once('error',error=>resolve({error:error.message,code:null}));
      this.child.once('exit',(code,signal)=>resolve({code,signal}));
    }).then(async result=>{
      this.exited=result;
      try{
        await fs.writeFile(path.join(this.root,'process.log'),log);
        await fs.writeFile(path.join(this.root,'process.json'),JSON.stringify({pid:this.child.pid,...result}));
      }catch(e){result.evidenceWriteError=e.message;}
      return result;
    });
    const ready=await this.waitFile('ready.json',this.timeouts.startup);
    if(ready.pid!==this.child.pid || ready.simulationOnly!==true) throw Error('native-identity-mismatch');
  }
  async waitFile(name,timeout) {
    const end=Date.now()+timeout;
    while(Date.now()<end) {
      try{return JSON.parse(await fs.readFile(path.join(this.root,name),'utf8'));}
      catch(e){if(e.code!=='ENOENT') throw e;}
      if(this.exited) throw Error('native-process-exited');
      await sleep(this.timeouts.poll);
    }
    throw Error('native-timeout');
  }
  async call(request) {
    if(this.closed || this.faulted) throw Error('native-session-faulted');
    try {
      await this.start();return await this.send(request,this.timeouts.operation);
    }catch(error){
      error.integrityFailure=true;
      error.cleanup=await this.abort(error.message);
      throw error;
    }
  }
  async send(request,timeout) {
    const id=crypto.randomUUID(); const context={requestId:id,requestHash:digest(request)};
    const payload=JSON.stringify({request,context});
    const envelope={payload,signature:crypto.createHmac('sha256',this.key).update(payload).digest('hex')};
    const name=id+'.request.json';
    await fs.writeFile(path.join(this.root,name+'.tmp'),JSON.stringify(envelope));
    await fs.rename(path.join(this.root,name+'.tmp'),path.join(this.root,name));
    const result=await this.waitFile(id+'.result.json',timeout);
    if(result.operation!==request.operation || typeof result.success!=='boolean' ||
      result.context?.requestId!==id || result.context?.requestHash!==context.requestHash) throw Error('invalid-native-result');
    return result;
  }
  abort(reason) {this.faulted=this.faulted||reason;return this.dispose();}
  dispose() {
    this.closed=true;
    return this.closing||=(this.shutdown().then(result=>{this.cleanup=result;return result;}));
  }
  async shutdown() {
    if(!this.child) return {success:true,ownedProcessExited:true,cleanup:[],integrityFault:this.faulted};
    let native=null; let failure=null;
    if(!this.exited) {
      // Only this safety command bypasses the terminal state; it never reopens ordinary calls.
      try{native=await this.send({operation:'dispose'},this.timeouts.cleanup);}catch(e){failure=e.message;}
    }
    let exit=await bounded(this.exit,this.timeouts.exit);
    if(!exit){this.child.kill();exit=await bounded(this.exit,this.timeouts.kill);failure=failure||'forced-owned-process-exit';}
    let final=null;
    try{final=JSON.parse(await fs.readFile(path.join(this.root,'cleanup.json'),'utf8'));}catch{}
    this.key='';
    const consistent=!native || JSON.stringify(native.cleanup)===JSON.stringify(final?.cleanup);
    return {success:!failure && (!native || native.success===true) && final?.success===true && consistent && exit?.code===0 && !exit.evidenceWriteError,
      ownedProcessExited:!!exit,cleanup:native?.cleanup||final?.cleanup||[],failure,integrityFault:this.faulted};
  }
}
module.exports={NativeTransport};
