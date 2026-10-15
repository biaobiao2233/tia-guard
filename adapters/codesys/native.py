# -*- coding: utf-8 -*-
# TIA-Guard: runs ONLY inside official CODESYS ScriptEngine (IronPython 2.7).
from __future__ import print_function
import os, io, json, time, hashlib, hmac, traceback, re
from scriptengine import *

root = os.environ['CODESYS_GUARD_SESSION']
secret = os.environ['CODESYS_GUARD_KEY']
project_path = os.path.join(root, 'Owned.project')
project = None
device = None
app = None
connection = None
compiled = False
verified = False
expected = None
entry = None
last_activity = time.time()

def write_json(path, value):
    temp = path + '.tmp'
    with io.open(temp, 'w', encoding='utf-8') as f:
        f.write(unicode(json.dumps(value, ensure_ascii=True, sort_keys=True)))
    os.rename(temp, path)

def simulation_only():
    if project is None or device is None or app is None:
        raise ValueError('no-owned-project')
    if os.path.normcase(os.path.abspath(project.path)) != os.path.normcase(project_path):
        raise ValueError('project-identity-changed')
    if not device.get_simulation_mode():
        raise ValueError('simulation-required')
    identity=device.get_device_identification()
    if (identity.type,str(identity.id),str(identity.version)) != (4096,'0000 0001','3.5.22.40'):
        raise ValueError('device-identity-changed')

def bind():
    global device, app
    device = project.find('OwnedPLC')[0]
    app = [x for x in device.get_children()[0].get_children() if x.is_application][0]
    project.active_application = app

def snapshot():
    simulation_only()
    objects=[]
    for obj in app.get_children():
        if obj.has_textual_declaration:
            objects.append({'name':obj.get_name(), 'declaration':obj.textual_declaration.text,
                'implementation':obj.textual_implementation.text if obj.has_textual_implementation else ''})
    tasks=[]
    for config in app.get_children():
        if config.is_task_configuration:
            for task in config.get_children():
                tasks.append({'name':task.get_name(), 'kind':str(task.kind_of_task), 'interval':str(task.interval),
                    'calls':[str(task.pous[i][0]) for i in range(len(task.pous))]})
    identity=device.get_device_identification()
    return {'objects': sorted(objects,key=lambda x:x['name']), 'tasks':tasks,
        'device':{'type':identity.type,'id':str(identity.id),'version':str(identity.version)},'simulation':True}

def put_object(spec, create):
    global compiled, verified
    if connection is not None: raise ValueError('logout-required-before-edit')
    name=spec['name']; kind=spec['kind']
    if not re.match(r'^[A-Za-z][A-Za-z0-9_]{0,63}$',name): raise ValueError('invalid-name')
    if kind=='gvl' and spec['implementation']: raise ValueError('gvl-has-no-implementation')
    matches=app.find(name)
    if create:
        if matches: raise ValueError('object-collision')
        if kind=='gvl': obj=app.create_gvl(name)
        elif kind=='program': obj=app.create_pou(name,PouType.Program,ImplementationLanguages.st)
        elif kind=='functionBlock': obj=app.create_pou(name,PouType.FunctionBlock,ImplementationLanguages.st)
        else: raise ValueError('unsupported-object')
    else:
        if len(matches)!=1: raise ValueError('unknown-object')
        obj=matches[0]
        # Object identity/kind is fixed at creation; editing cannot change its type.
        original=[o for o in expected['objects'] if o['name']==name][0]
        if original['kind']!=kind: raise ValueError('object-kind-mismatch')
    # Invalidate BEFORE the first mutation: a vendor exception may leave a partial edit.
    compiled=False; verified=False
    obj.textual_declaration.replace(spec['declaration'])
    if kind!='gvl': obj.textual_implementation.replace(spec['implementation'])

def diagnostics():
    result=[]
    for category in system.get_message_categories():
        for m in system.get_message_objects(category):
            result.append({'category':str(category),'severity':str(m.severity),'code':str(m.prefix or '')+str(m.number or 0),
                'message':str(m.text),'object':m.object.get_name() if m.object else None,
                'position':str(m.position_text) if m.position_text else None})
    return result

def compile_project():
    global compiled, verified
    simulation_only()
    if connection is not None: raise ValueError('logout-required-before-compile')
    compiled=False; verified=False
    for category in system.get_message_categories(): system.clear_messages(category)
    # Rebuild does type checking. generate_code additionally invokes the native code generator.
    messages=[]; build_exception=None; generated=False
    try:
        app.rebuild()
        messages=diagnostics()
        if not any(m['severity'] in ('Error','FatalError') for m in messages):
            app.generate_code(); generated=True
    except:
        build_exception=traceback.format_exc()
    # Some vendor phases replace the message store. Preserve diagnostics from both phases.
    for message in diagnostics():
        if message not in messages: messages.append(message)
    errors=[m for m in messages if m['severity'] in ('Error','FatalError')]
    warnings=[m for m in messages if m['severity']=='Warning']
    # C0 is a build summary, not a successful process exit. Missing summary fails closed.
    summaries=[m for m in messages if m['code']=='C0' and re.search(u'(构建完整|Build complete)\\s*--\\s*0\\s*(错误|errors?)',m['message'],re.I)]
    known=all(m['severity'] in ('Error','FatalError','Warning','Information','Text') for m in messages)
    compiled=generated and not build_exception and known and not errors and bool(summaries)
    project.save()
    return {'success':compiled,'compilation':{'buildStarted':True,'buildCompleted':True,
        'diagnosticsComplete':known,'errorCount':len(errors),'warningCount':len(warnings),
        'nativeCodeGenerated':compiled,'diagnostics':messages,'buildException':build_exception}}

def do(request):
    global project, device, app, connection, compiled, verified, expected, entry
    op=request['operation']
    if op=='create':
        if project is not None or os.path.exists(project_path): raise ValueError('project-collision')
        expected=request; entry=request['entry']
        project=projects.create(project_path)
        project.add('OwnedPLC',4096,'0000 0001','3.5.22.40')
        bind()
        device.set_simulation_mode(True)
        for obj in request['objects']: put_object(obj,True)
        task=app.create_task_configuration().create_task('MainTask')
        task.kind_of_task=KindOfTask.Cyclic; task.interval='t#20ms'; task.pous.add(entry)
        project.save()
        return {'success':True,'snapshot':snapshot()}
    if op=='dispose': return cleanup()
    simulation_only()
    if op=='inspect': return {'success':True,'snapshot':snapshot()}
    if op=='edit':
        put_object(request['object'],False)
        for i,obj in enumerate(expected['objects']):
            if obj['name']==request['object']['name']: expected['objects'][i]=request['object']
        project.save()
        return {'success':True,'snapshot':snapshot()}
    if op=='compile': return compile_project()
    if op=='reopen':
        if connection is not None: raise ValueError('logout-required-before-reopen')
        if not compiled: raise ValueError('successful-compile-required')
        before=snapshot(); project.save(); project.close(); project=None
        project=projects.open(project_path); bind(); after=snapshot()
        verified=before==after
        # Independently compare stored text against the request as well as before/after.
        normalize=lambda text: text.replace('\r\n','\n').replace('\r','\n').rstrip('\n')
        for spec in expected['objects']:
            actual=[o for o in after['objects'] if o['name']==spec['name']]
            if len(actual)!=1 or any(normalize(actual[0][k])!=normalize(spec[k]) for k in ('declaration','implementation')): verified=False
        return {'success':verified,'verification':{'complete':True,'verdict':'pass' if verified else 'fail',
            'closedAndReopened':True,'requestTextMatches':verified,'before':before,'after':after}}
    if op=='login':
        if not compiled or not verified: raise ValueError('compile-and-reopen-required')
        if connection is not None: raise ValueError('already-logged-in')
        simulation_only()
        connection=online.create_online_application(app)
        connection.login(OnlineChangeOption.Never,False)
        if not connection.is_logged_in: raise ValueError('login-not-observed')
        return {'success':True,'state':str(connection.application_state),'simulation':True}
    if connection is None or not connection.is_logged_in: raise ValueError('owned-login-required')
    simulation_only()
    if op in ('run','stop'):
        if op=='run': connection.start()
        else: connection.stop()
        system.delay(100)
        state=str(connection.application_state)
        return {'success':state==op,'state':state}
    if op=='read':
        if not 0<=request.get('delayMs',0)<=2000: raise ValueError('invalid-delay')
        system.delay(request.get('delayMs',0))
        variables=request['variables']
        if not 1<=len(variables)<=32 or any(not re.match(r'^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*){1,5}$',v) for v in variables): raise ValueError('invalid-variable')
        return {'success':True,'state':str(connection.application_state),'values':dict((v,str(connection.read_value(v))) for v in variables)}
    if op=='write':
        var=request['variable']; value=request['value']
        if var=='GVL_Test.Enable' and type(value)==bool: literal='TRUE' if value else 'FALSE'
        elif var=='GVL_Test.Step' and type(value) in (int,long) and 0<=value<=10: literal=str(value)
        else: raise ValueError('input-not-allowlisted')
        connection.set_prepared_value(var,literal)
        try: connection.write_prepared_values()
        finally: connection.set_prepared_value(var,None)
        system.delay(100)
        observed=str(connection.read_value(var))
        scalar=observed.split('#')[-1].upper()
        return {'success':scalar==literal,'variable':var,'observed':observed}
    raise ValueError('unsupported-operation')

def cleanup():
    global connection, project
    results=[]
    if connection is not None:
        try:
            simulation_only()
            if connection.is_logged_in: connection.stop(); connection.logout()
            results.append({'operation':'stop-logout','success':True})
        except: results.append({'operation':'stop-logout','success':False,'error':traceback.format_exc()})
        try: connection.Dispose(); results.append({'operation':'dispose-online','success':True})
        except: results.append({'operation':'dispose-online','success':False,'error':traceback.format_exc()})
        connection=None
    if project is not None:
        try:
            if device is not None and device.get_simulation_mode(): device.set_simulation_mode(False)
            # Keep the saved artifact in simulation mode; this change is only session shutdown.
            project.close(); results.append({'operation':'close-project','success':True})
        except: results.append({'operation':'close-project','success':False,'error':traceback.format_exc()})
        project=None
    return {'success':all(x['success'] for x in results),'cleanup':results}

try:
    write_json(os.path.join(root,'ready.json'),{'ready':True,'pid':os.getpid(),'simulationOnly':True})
    while time.time()-last_activity < 300:
        requests=sorted(f for f in os.listdir(root) if re.match(r'^[0-9a-f-]+\.request\.json$',f))
        for filename in requests:
            source=os.path.join(root,filename)
            with io.open(source,'r',encoding='utf-8') as f: envelope=json.load(f)
            os.remove(source)
            last_activity=time.time()
            stop=False
            try:
                payload=envelope['payload']
                signature=hmac.new(secret.encode('ascii'),payload.encode('utf-8'),hashlib.sha256).hexdigest()
                if not hmac.compare_digest(signature,str(envelope['signature'])): raise ValueError('invalid-signature')
                request=json.loads(payload)
                stop=request['operation']=='dispose'
                result=do(request)
            except:
                result={'success':False,'error':traceback.format_exc()}
            result['operation']=json.loads(envelope['payload']).get('operation','unknown')
            write_json(os.path.join(root,filename.replace('.request.','.result.')),result)
            if stop: break
        if requests and stop: break
        system.delay(50)
finally:
    write_json(os.path.join(root,'cleanup.json'),cleanup())
