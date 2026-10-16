# -*- coding: utf-8 -*-
# TIA-Guard: runs ONLY inside official CODESYS ScriptEngine (IronPython 2.7).
from __future__ import print_function
import os, io, json, time, hashlib, hmac, traceback, re, sys
from scriptengine import *

root = os.environ['CODESYS_GUARD_SESSION']
sys.path.insert(0, root)
from native_policy import (COMPILER, ANALYSIS, INPUTS, object_metadata, check_input_declaration,
    structure_differences, parse_summary_counts, phase_counts, scalar_value, checked_monitor_value)
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
current_context = None
compiled_fingerprint = None
cleanup_report = None

def target_identity():
    return {'projectId':hashlib.sha256(project_path.encode('utf-8')).hexdigest(),
        'applicationId':str(app.guid),'applicationName':app.get_name(),'deviceId':str(device.guid)}

def fingerprint(value):
    return hashlib.sha256(json.dumps(value,sort_keys=True,ensure_ascii=True).encode('ascii')).hexdigest()

def require_structure(value):
    differences=structure_differences(value,expected)
    if differences: raise ValueError('structure-mismatch:' + ','.join(differences))

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
    if projects.primary is None or projects.primary.handle!=project.handle or project.active_application.guid!=app.guid:
        raise ValueError('active-application-identity-changed')
    identity=device.get_device_identification()
    # The official IDE switches to a distinct simulation device identification.
    if (identity.type,str(identity.id),str(identity.version)) != (4097,'0000 000a','3.5.22.40'):
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
            kind,types=object_metadata(obj.export_xml(recursive=False),obj.get_name())
            objects.append({'name':obj.get_name(), 'kind':kind,'variableTypes':types,
                'declaration':obj.textual_declaration.text,
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
    check_input_declaration(spec)
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

def diagnostics(phase):
    result=[]; target=target_identity()
    allowed=set(str(x.guid) for x in app.get_children(True)); allowed.add(str(app.guid))
    qualified=device.get_name()+'.Sim.'+device.get_name()+'.'+app.get_name()
    for category in system.get_message_categories():
        for m in system.get_message_objects(category):
            category_id=str(category).lower().strip('{}')
            foreign_project=m.project is not None and m.project.handle != project.handle
            foreign_object=m.object is not None and str(m.object.guid) not in allowed
            result.append({'category':category_id,'severity':str(m.severity),'code':str(m.prefix or '')+str(m.number or 0),
                'message':str(m.text),'object':m.object.get_name() if m.object else None,
                'position':str(m.position_text) if m.position_text else None,
                'phase':phase,'requestId':current_context['requestId'],'buildId':current_context['requestId'],
                'target':target,'foreignOrigin':foreign_project or foreign_object})
    headers=[m for m in result if m['category']==COMPILER and m['code']=='C0' and
        m['message'].startswith('------ ') and qualified in m['message'].split()]
    for m in result:
        m['targetHeader']=m in headers
        m['correlated']=bool(headers) and not m['foreignOrigin'] and m['category'] in (COMPILER,ANALYSIS)
        # Reject another application's build header even in this owned IDE.
        if m['category']==COMPILER and m['message'].startswith('------ ') and not m['targetHeader']: m['correlated']=False
        counts=parse_summary_counts(m['message'],phase) if m['category']==COMPILER and m['code']=='C0' else None
        m['buildSummary']=phase=='generate' and counts is not None and counts['errorCount']==0
    return result

def compile_project():
    global compiled, verified, compiled_fingerprint
    simulation_only()
    if connection is not None: raise ValueError('logout-required-before-compile')
    compiled=False; verified=False; compiled_fingerprint=None
    source=snapshot(); require_structure(source)
    # Rebuild does type checking. generate_code additionally invokes the native code generator.
    messages=[]; phases=[]; build_exception=None; generated=False
    for phase,operation in [('rebuild',app.rebuild),('generate',app.generate_code)]:
        if phase=='generate' and any(m['severity'] in ('Error','FatalError') for m in messages): break
        for category in system.get_message_categories(): system.clear_messages(category)
        completed=False
        try:
            returned=operation()
            if returned is False: raise ValueError('unexpected-false-compiler-return')
            completed=True
        except:
            build_exception=traceback.format_exc()
        captured=diagnostics(phase); messages.extend(captured)
        phase_report={'name':phase,'completed':completed,'headerSeen':any(m['targetHeader'] for m in captured),
            'diagnosticCount':len(captured)}
        phase_report.update(phase_counts(captured,phase)); phases.append(phase_report)
        if build_exception or not phase_report['countsMatchSummary']: break
        if phase=='generate': generated=completed
    errors=[m for m in messages if m['severity'] in ('Error','FatalError')]
    warnings=[m for m in messages if m['severity']=='Warning']
    # C0 is a build summary, not a successful process exit. Missing summary fails closed.
    summaries=[m for m in messages if m['buildSummary'] and m['correlated']]
    known=bool(messages) and all(m['severity'] in ('Error','FatalError','Warning','Information','Text') and m['correlated'] for m in messages) and all(p['countsMatchSummary'] for p in phases)
    compiled=generated and not build_exception and known and not errors and bool(summaries)
    try: project.save()
    except: compiled=False; build_exception=traceback.format_exc()
    if compiled: compiled_fingerprint=fingerprint(source)
    return {'success':compiled,'target':target_identity(),'compilation':{'buildStarted':True,'buildCompleted':build_exception is None,
        'buildId':current_context['requestId'],'requestHash':current_context['requestHash'],
        'target':target_identity(),'sourceFingerprint':fingerprint(source),'phases':phases,
        'countSemantics':'phase-diagnostic-occurrences',
        'diagnosticsComplete':known,'errorCount':len(errors),'warningCount':len(warnings),
        'nativeCodeGenerated':generated,'diagnostics':messages,'buildException':build_exception}}

def do(request):
    global project, device, app, connection, compiled, verified, expected, entry, compiled_fingerprint
    op=request['operation']
    # Revoke qualifications before ANY precondition or vendor call can fail.
    if op in ('create','edit','compile'):
        compiled=False; verified=False; compiled_fingerprint=None
    if op=='reopen': verified=False
    if op=='create':
        if project is not None or os.path.exists(project_path): raise ValueError('project-collision')
        for obj in request['objects']: check_input_declaration(obj)
        expected=request; entry=request['entry']
        project=projects.create(project_path)
        project.add('OwnedPLC',4096,'0000 0001','3.5.22.40')
        bind()
        identity=device.get_device_identification()
        if (identity.type,str(identity.id),str(identity.version)) != (4096,'0000 0001','3.5.22.40'):
            raise ValueError('source-device-identity-mismatch')
        device.set_simulation_mode(True)
        for obj in request['objects']: put_object(obj,True)
        task=app.create_task_configuration().create_task('MainTask')
        task.kind_of_task=KindOfTask.Cyclic; task.interval='t#20ms'; task.pous.add(entry)
        project.save()
        created=snapshot(); require_structure(created)
        return {'success':True,'snapshot':created,'target':target_identity()}
    if op=='dispose': return cleanup()
    simulation_only()
    if op=='inspect': return {'success':True,'snapshot':snapshot(),'target':target_identity()}
    if op=='edit':
        put_object(request['object'],False)
        for i,obj in enumerate(expected['objects']):
            if obj['name']==request['object']['name']: expected['objects'][i]=request['object']
        project.save()
        changed=snapshot(); require_structure(changed)
        return {'success':True,'snapshot':changed,'target':target_identity()}
    if op=='compile': return compile_project()
    if op=='reopen':
        verified=False
        if connection is not None: raise ValueError('logout-required-before-reopen')
        if not compiled: raise ValueError('successful-compile-required')
        before=snapshot(); require_structure(before)
        if fingerprint(before)!=compiled_fingerprint: raise ValueError('compiled-source-drift')
        project.save(); project.close(); project=None
        project=projects.open(project_path); bind(); after=snapshot()
        differences=structure_differences(after,expected)
        if before!=after: differences.append('reopen-drift')
        verified=not differences
        return {'success':verified,'target':target_identity(),'verification':{'complete':True,'verdict':'pass' if verified else 'fail',
            'closedAndReopened':True,'requestTextMatches':verified,'expectedStructureMatches':verified,
            'sourceFingerprint':fingerprint(after),'differences':differences,'before':before,'after':after}}
    if op=='login':
        if not compiled or not verified: raise ValueError('compile-and-reopen-required')
        live=snapshot(); require_structure(live)
        if fingerprint(live)!=compiled_fingerprint: raise ValueError('compiled-source-drift')
        if connection is not None: raise ValueError('already-logged-in')
        simulation_only()
        connection=online.create_online_application(app)
        connection.timeout=5000
        connection.login(OnlineChangeOption.Never,False)
        if not connection.is_logged_in: raise ValueError('login-not-observed')
        state=str(connection.application_state)
        return {'success':state in ('run','stop'),'loggedIn':bool(connection.is_logged_in),
            'state':state,'simulation':True,'target':target_identity()}
    if connection is None or not connection.is_logged_in: raise ValueError('owned-login-required')
    simulation_only()
    if op in ('run','stop'):
        if op=='run': connection.start()
        else: connection.stop()
        system.delay(100)
        state=str(connection.application_state)
        return {'success':state==op,'state':state,'target':target_identity()}
    if op=='read':
        if not 0<=request.get('delayMs',0)<=2000: raise ValueError('invalid-delay')
        system.delay(request.get('delayMs',0))
        variables=request['variables']
        if not 1<=len(variables)<=32 or any(not re.match(r'^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*){1,5}$',v) for v in variables): raise ValueError('invalid-variable')
        return {'success':True,'state':str(connection.application_state),'target':target_identity(),
            'values':dict((v,checked_monitor_value(connection.read_value(v))) for v in variables)}
    if op=='write':
        var=request['variable']; value=request['value']
        if var=='GVL_Test.Enable' and type(value)==bool: literal='TRUE' if value else 'FALSE'
        elif var=='GVL_Test.Step' and type(value) in (int,long) and 0<=value<=10: literal=str(value)
        else: raise ValueError('input-not-allowlisted')
        interface=snapshot(); require_structure(interface)
        # XML metadata validates declaration type; native monitoring validates the online type before any write.
        before=checked_monitor_value(connection.read_value(var)); scalar_value(var,before)
        connection.set_prepared_value(var,literal)
        try: connection.write_prepared_values()
        finally: connection.set_prepared_value(var,None)
        system.delay(100)
        observed=checked_monitor_value(connection.read_value(var))
        return {'success':scalar_value(var,observed)==value,'variable':var,'plcType':INPUTS[var],
            'before':before,'observed':observed,'target':target_identity()}
    raise ValueError('unsupported-operation')

def cleanup():
    global connection, project, cleanup_report
    if cleanup_report is not None: return cleanup_report
    results=[]
    def attempt(operation, action):
        try:
            action(); results.append({'operation':operation,'success':True})
        except:
            results.append({'operation':operation,'success':False,'error':traceback.format_exc()})
    if connection is not None:
        def stop_owned():
            simulation_only()
            if connection.is_logged_in:
                connection.stop()
                if str(connection.application_state)!='stop': raise ValueError('cleanup-stop-not-observed')
        def logout_owned():
            connection.logout()
            if connection.is_logged_in: raise ValueError('cleanup-logout-not-observed')
        attempt('stop',stop_owned)
        attempt('logout',logout_owned)
        attempt('dispose-online',connection.Dispose)
    if project is not None:
        def close_owned():
            if os.path.normcase(os.path.abspath(project.path))!=os.path.normcase(project_path): raise ValueError('cleanup-project-identity-changed')
            if device is not None and device.get_simulation_mode(): device.set_simulation_mode(False)
            # Keep the saved artifact in simulation mode; this change is only session shutdown.
            project.close()
        attempt('close-project',close_owned)
    # Terminal, cumulative evidence: the finally call returns this exact outcome, including failures.
    cleanup_report={'success':all(x['success'] for x in results),'cleanup':results}
    return cleanup_report

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
                message=json.loads(payload)
                request=message['request']; current_context=message['context']
                if current_context['requestId']!=filename.split('.')[0]: raise ValueError('request-identity-mismatch')
                stop=request['operation']=='dispose'
                result=do(request)
            except:
                result={'success':False,'error':traceback.format_exc()}
            result['operation']=request.get('operation','unknown')
            result['context']=current_context
            write_json(os.path.join(root,filename.replace('.request.','.result.')),result)
            if stop: break
        if requests and stop: break
        system.delay(50)
finally:
    write_json(os.path.join(root,'cleanup.json'),cleanup())
