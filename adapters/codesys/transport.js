"use strict";
const fs = require('node:fs/promises');
const path = require('node:path');
const crypto = require('node:crypto');
const { spawn } = require('node:child_process');
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
class NativeTransport {
  constructor(config) {this.config=config; this.child=null;this.root=null;this.exit=null;this.closed=false;this.key=crypto.randomBytes(32).toString('hex');}
  async start() {
    if(this.closed) throw Error('transport-closed');
    if(this.child) return;
    if(process.platform!=='win32') throw Error('native-windows-required');
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
    this.child=spawn(c.exe,[`--profile="${c.profile}"`,`--runscript="${script}"`,'--noUI'],{
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
      await fs.writeFile(path.join(this.root,'process.log'),log);
      await fs.writeFile(path.join(this.root,'process.json'),JSON.stringify({pid:this.child.pid,...result}));
      return result;
    });
    const ready=await this.waitFile('ready.json',150000);
    if(ready.pid!==this.child.pid || ready.simulationOnly!==true) throw Error('native-identity-mismatch');
  }
  async waitFile(name,timeout) {
    const end=Date.now()+timeout;
    while(Date.now()<end) {
      try{return JSON.parse(await fs.readFile(path.join(this.root,name),'utf8'));}
      catch(e){if(e.code!=='ENOENT') throw e;}
      if(this.exited) throw Error('native-process-exited');
      await sleep(50);
    }
    throw Error('native-timeout');
  }
  async call(request) {
    await this.start();
    const id=crypto.randomUUID(); const payload=JSON.stringify(request);
    const envelope={payload,signature:crypto.createHmac('sha256',this.key).update(payload).digest('hex')};
    const name=id+'.request.json';
    await fs.writeFile(path.join(this.root,name+'.tmp'),JSON.stringify(envelope));
    await fs.rename(path.join(this.root,name+'.tmp'),path.join(this.root,name));
    const result=await this.waitFile(id+'.result.json',150000);
    if(result.operation!==request.operation || typeof result.success!=='boolean') throw Error('invalid-native-result');
    return result;
  }
  async dispose() {
    if(this.closed) return this.cleanup;
    this.closed=true;
    if(!this.child) return this.cleanup={success:true,ownedProcessExited:true,cleanup:[]};
    let native=null; let failure=null;
    if(!this.exited) {
      // call uses start(), which must still recognize the live child during cleanup.
      this.closed=false;
      try{native=await this.call({operation:'dispose'});}catch(e){failure=e.message;}
      finally{this.closed=true;}
    }
    let exit=await Promise.race([this.exit,sleep(10000).then(()=>null)]);
    if(!exit){this.child.kill();exit=await Promise.race([this.exit,sleep(5000).then(()=>null)]);failure=failure||'forced-owned-process-exit';}
    let final=null;
    try{final=JSON.parse(await fs.readFile(path.join(this.root,'cleanup.json'),'utf8'));}catch{}
    this.key='';
    return this.cleanup={success:!failure && native?.success===true && final?.success===true && exit?.code===0,
      ownedProcessExited:!!exit,cleanup:native?.cleanup||[],failure};
  }
}
module.exports={NativeTransport};
