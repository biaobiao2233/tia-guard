"use strict";
// Portable mailbox fixture, explicitly not a vendor compiler/runtime.
const fs=require('node:fs/promises');const path=require('node:path');const crypto=require('node:crypto');
const root=process.env.CODESYS_GUARD_SESSION;const key=process.env.CODESYS_GUARD_KEY;const mode=process.env.CODESYS_TEST_MODE;
const pause=ms=>new Promise(r=>setTimeout(r,ms));
async function write(name,value){await fs.writeFile(path.join(root,name+'.tmp'),JSON.stringify(value));await fs.rename(path.join(root,name+'.tmp'),path.join(root,name));}
(async()=>{
  await write('ready.json',{ready:true,pid:process.pid,simulationOnly:true});const operations=[];
  while(true){
    const files=(await fs.readdir(root)).filter(n=>n.endsWith('.request.json'));
    for(const name of files){
      const envelope=JSON.parse(await fs.readFile(path.join(root,name),'utf8'));await fs.unlink(path.join(root,name));
      const signature=crypto.createHmac('sha256',key).update(envelope.payload).digest('hex');if(signature!==envelope.signature)throw Error('fixture-signature');
      const {request,context}=JSON.parse(envelope.payload);operations.push(request.operation);await write('operations.json',operations);
      if(mode==='hang')continue;
      if(mode==='exit'&&request.operation!=='dispose')process.exit(3);
      if(request.operation==='dispose'){
        const cleanup={success:mode!=='cleanup-failure',cleanup:[{operation:'stop',success:mode!=='cleanup-failure'}]};
        await write(name.replace('.request.','.result.'),{...cleanup,operation:'dispose',context});
        await write('cleanup.json',cleanup);await pause(20);process.exit(0);
      }
      await write(name.replace('.request.','.result.'),{operation:mode==='wrong-operation'?'other':request.operation,success:true,
        context:mode==='wrong-context'?{...context,requestHash:'0'.repeat(64)}:context});
    }
    await pause(5);
  }
})().catch(e=>{process.stderr.write(e.stack);process.exit(1);});
