"use strict";
const { z } = require('zod');
const name = z.string().regex(/^[A-Za-z][A-Za-z0-9_]{0,63}$/);
const object = z.strictObject({ name, kind: z.enum(['program','functionBlock','gvl']),
  declaration: z.string().min(1).max(64000), implementation: z.string().max(64000).default('') });
const identifier = z.string().regex(/^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*){1,5}$/).max(256);
const action = z.discriminatedUnion('operation', [
  z.strictObject({ operation:z.literal('create'), objects:z.array(object).min(1).max(32), entry:name }),
  z.strictObject({ operation:z.literal('edit'), object }),
  z.strictObject({ operation:z.literal('compile') }),
  z.strictObject({ operation:z.literal('reopen') }),
  z.strictObject({ operation:z.literal('login') }),
  z.strictObject({ operation:z.literal('run') }),
  z.strictObject({ operation:z.literal('stop') }),
  z.strictObject({ operation:z.literal('write'), variable:z.enum(['GVL_Test.Enable','GVL_Test.Step']), value:z.union([z.boolean(),z.number().int().min(0).max(10)]) })
]);
const tools = {
  doctor_codesys:{ readOnly:true, schema:z.strictObject({}), description:'Inspect configured local components without starting CODESYS; license activation is never automatic.' },
  preview_codesys:{ readOnly:true, schema:z.strictObject({ request:action }), description:'Freeze an operation against this session revision. Only newly owned projects and IDE simulation are supported.' },
  authorize_codesys:{ readOnly:false, schema:z.strictObject({ previewId:z.string().uuid() }), description:'Authorize a single frozen operation. Requires trusted host CODESYS_GUARD_WRITES=1.' },
  execute_codesys:{ readOnly:false, schema:z.strictObject({ previewId:z.string().uuid(), grantId:z.string().uuid() }), description:'Execute one authorized operation through official ScriptEngine. Returned diagnostics and evidence determine success.' },
  inspect_codesys:{ readOnly:true, schema:z.strictObject({}), description:'Read the adapter-owned in-memory project and task configuration; never open an arbitrary project.' },
  read_codesys:{ readOnly:true, schema:z.strictObject({ variables:z.array(identifier).min(1).max(32), delayMs:z.number().int().min(0).max(2000).default(0) }), description:'Read real simulation values; optional delay serves the native IDE event loop.' },
  dispose_codesys:{ readOnly:false, schema:z.strictObject({}), description:'Stop/logout/dispose the owned simulation, close the owned project, and await the exact child process exit. Safety cleanup needs no grant.' }
};
module.exports={tools,action};
