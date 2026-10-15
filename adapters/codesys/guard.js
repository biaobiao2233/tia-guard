"use strict";
const crypto = require('node:crypto');
const { action } = require('./schemas');
const digest = value => crypto.createHash('sha256').update(JSON.stringify(value)).digest('hex');
class Guard {
  constructor({writes=false, clock=Date.now}={}) {
    this.writes=writes; this.clock=clock; this.revision=0; this.closed=false;
    this.previews=new Map(); this.grants=new Map();
  }
  preview(request) {
    if(this.closed) throw Error('session-closed');
    const frozen=action.parse(request);
    if(frozen.operation==='create') {
      const names=frozen.objects.map(o=>o.name.toLowerCase());
      if(new Set(names).size!==names.length || !frozen.objects.some(o=>o.name===frozen.entry && o.kind==='program')) throw Error('invalid-objects-or-entry');
    }
    if(frozen.operation==='write') {
      if(frozen.variable==='GVL_Test.Enable' ? typeof frozen.value!=='boolean' : !Number.isInteger(frozen.value)) throw Error('input-type-mismatch');
    }
    const previewId=crypto.randomUUID();
    const p={request:frozen,inputHash:digest(frozen),revision:this.revision,expiresAt:this.clock()+120000};
    this.previews.set(previewId,p);
    return {previewId,...structuredClone(p), scope:'owned-project/IDE-simulation-only'};
  }
  valid(id) {
    const p=this.previews.get(id);
    if(this.closed || !p || p.revision!==this.revision || this.clock()>=p.expiresAt || digest(p.request)!==p.inputHash) throw Error('stale-preview');
    return p;
  }
  authorize(id) {
    if(!this.writes) throw Error('read-only-mode');
    this.valid(id); const grantId=crypto.randomUUID(); this.grants.set(grantId,id); return {grantId,previewId:id};
  }
  consume(id,grantId) {
    if(!this.writes) throw Error('read-only-mode');
    const p=this.valid(id);
    if(this.grants.get(grantId)!==id) throw Error('invalid-grant');
    this.grants.delete(grantId); this.previews.delete(id);
    // Even a failed native mutation invalidates all old previews.
    this.revision++;
    return structuredClone(p);
  }
  close() {this.closed=true; this.previews.clear();this.grants.clear();}
}
module.exports={Guard,digest};
