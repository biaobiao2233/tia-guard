"""Fault injection into actual worker functions; no CODESYS/PLC is started."""
import ast
import copy
import json
import os
from pathlib import Path
import sys
import tempfile
import types
import unittest
from unittest.mock import patch

BASE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(BASE))
import native_policy as policy

SOURCE = json.loads((BASE / 'examples/counter.json').read_text())
TARGET = dict(projectId='a'*64, applicationId='11111111-1111-4111-8111-111111111111', applicationName='Application', deviceId='22222222-2222-4222-8222-222222222222')
def snapshot():
    return dict(objects=[dict(o,variableTypes={'Enable':'BOOL','Step':'DINT'} if o['name']=='GVL_Test' else {}) for o in SOURCE['objects']],
        tasks=[dict(name='MainTask',kind='Cyclic',interval='t#20ms',calls=['PLC_PRG'])],device=dict(policy.SIM_DEVICE),simulation=True)

class WorkerTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup)
        code=ast.parse((BASE/'native.py').read_text(encoding='utf-8'))
        # Exclude only the top-level mailbox loop, not the production functions being tested.
        body=code.body[:next(i for i,node in enumerate(code.body) if isinstance(node,ast.Try))]
        scope={'unicode':str,'long':int}
        with patch.dict(sys.modules,{'scriptengine':types.ModuleType('scriptengine')}), patch.dict(os.environ,{'CODESYS_GUARD_SESSION':self.temp.name,'CODESYS_GUARD_KEY':'test-key'}):
            exec(compile(ast.Module(body=body,type_ignores=[]),str(BASE/'native.py'),'exec'),scope)
        self.n=scope;scope['expected']=copy.deepcopy(SOURCE);scope['simulation_only']=lambda:None
        scope['target_identity']=lambda:dict(TARGET);scope['snapshot']=snapshot
        scope['compiled']=True;scope['verified']=True;scope['compiled_fingerprint']=scope['fingerprint'](snapshot())
        scope['current_context']={'requestId':'11111111-1111-4111-8111-111111111111','requestHash':'b'*64}
        scope['project']=types.SimpleNamespace(path=scope['project_path'],handle=1,save=lambda:None,close=lambda:None)
        scope['app']=types.SimpleNamespace(guid='app',get_name=lambda:'Application',get_children=lambda *a:[])
        scope['device']=types.SimpleNamespace(get_name=lambda:'OwnedPLC',get_simulation_mode=lambda:True,set_simulation_mode=lambda _:None)
        scope['system']=types.SimpleNamespace(delay=lambda _:None)

    def test_missing_or_wrong_task_cannot_verify_identical_snapshots(self):
        for field,value in [('tasks',[]),('tasks',[dict(name='MainTask',kind='Cyclic',interval='t#200ms',calls=['PLC_PRG'])])]:
            s=snapshot();s[field]=value;self.n['snapshot']=lambda:copy.deepcopy(s);self.n['verified']=True
            with self.assertRaisesRegex(ValueError,'structure-mismatch'):self.n['do']({'operation':'reopen'})
            self.assertFalse(self.n['verified'])

    def test_failed_save_revokes_previous_reopen_and_prevents_login(self):
        self.n['project'].save=lambda:(_ for _ in ()).throw(OSError('save failed'))
        with self.assertRaisesRegex(OSError,'save failed'):self.n['do']({'operation':'reopen'})
        self.assertFalse(self.n['verified'])
        with self.assertRaisesRegex(ValueError,'compile-and-reopen-required'):self.n['do']({'operation':'login'})

    def test_failed_open_revokes_previous_reopen(self):
        self.n['projects']=types.SimpleNamespace(open=lambda _:(_ for _ in ()).throw(OSError('open failed')))
        with self.assertRaisesRegex(OSError,'open failed'):self.n['do']({'operation':'reopen'})
        self.assertFalse(self.n['verified'])

    def test_changed_object_kind_and_input_type_cannot_verify(self):
        for mutate in [lambda s:s['objects'][0].update(kind='functionBlock'),lambda s:s['objects'][2]['variableTypes'].update(Step='INT')]:
            s=snapshot();mutate(s);self.assertTrue(policy.structure_differences(s,SOURCE))

    def online(self,read):
        self.writes=[]
        self.n['connection']=types.SimpleNamespace(is_logged_in=True,application_state='run',read_value=read,
            set_prepared_value=lambda v,x:self.writes.append((v,x)),write_prepared_values=lambda:None)

    def test_wrong_online_type_is_rejected_before_writing(self):
        self.online(lambda _:'INT#7')
        with self.assertRaisesRegex(ValueError,'online-input-type-mismatch'):self.n['do']({'operation':'write','variable':'GVL_Test.Step','value':7})
        self.assertEqual(self.writes,[])

    def test_wrong_online_type_after_writing_cannot_return_success(self):
        values=iter(['DINT#0','INT#7']);self.online(lambda _:next(values))
        with self.assertRaisesRegex(ValueError,'online-input-type-mismatch'):self.n['do']({'operation':'write','variable':'GVL_Test.Step','value':7})

    def test_missing_monitor_value_cannot_return_success(self):
        self.online(lambda _:None)
        with self.assertRaisesRegex(ValueError,'missing-monitor-value'):self.n['do']({'operation':'read','variables':['PLC_PRG.Counter'],'delayMs':0})

    def test_reserved_input_declarations_reject_changed_type(self):
        spec=copy.deepcopy(SOURCE['objects'][2]);policy.check_input_declaration(spec)
        for decl in [spec['declaration'].replace('DINT','INT'),spec['declaration'].replace('BOOL','STRING')]:
            with self.assertRaises(ValueError):policy.check_input_declaration(dict(spec,declaration=decl))

    def test_metadata_uses_exported_native_object_and_type(self):
        self.assertEqual(policy.object_metadata('<project><pou name="X" pouType="functionBlock"/></project>','X'),('functionBlock',{}))
        self.assertEqual(policy.object_metadata('<globalVars name="GVL_Test"><variable name="Step"><type><INT/></type></variable></globalVars>','GVL_Test'),('gvl',{'Step':'INT'}))
        self.assertEqual(policy.object_metadata('\ufeff<?xml version="1.0" encoding="utf-8"?><project><!-- 中文 --><pou name="X" pouType="program"/></project>','X'),('program',{}))

    def test_cleanup_retains_failure_and_attempts_logout_even_after_stop_failure(self):
        calls=[]
        def stop():calls.append('stop');raise RuntimeError('stop failed')
        def logout():calls.append('logout');self.n['connection'].is_logged_in=False
        def dispose():calls.append('dispose');raise RuntimeError('dispose failed')
        def close():calls.append('close');raise RuntimeError('close failed')
        self.n['connection']=types.SimpleNamespace(is_logged_in=True,stop=stop,logout=logout,Dispose=dispose)
        self.n['project'].close=close
        first=self.n['cleanup']();second=self.n['cleanup']()
        self.assertFalse(first['success']);self.assertEqual(first,second);self.assertEqual(calls,['stop','logout','dispose','close'])
        self.assertEqual([r['operation'] for r in first['cleanup'] if not r['success']],['stop','dispose-online','close-project'])

    def compile_fixture(self,foreign=False,exception=False,no_header=False,false_return=False,
                        warnings=0,summary_warnings=None,summary_errors=None,duplicate_summary=False):
        messages=[]
        def message(text,project=None,severity='Text'):return types.SimpleNamespace(prefix='C',number=0,text=text,severity=severity,object=None,project=project,position_text=None)
        def phase(name):
            if not no_header:messages.append(message('------ 开始构建应用程序 OwnedPLC.Sim.OwnedPLC.Application -------'))
            for _ in range(warnings):messages.append(message('same warning in both phases',severity='Warning'))
            count=(summary_warnings or {}).get(name,warnings)
            errors=(summary_errors or {}).get(name,0)
            text=('编译完成' if name=='rebuild' else '构建完整')+'--%d错误,%d警告'%(errors,count)
            messages.append(message(text,types.SimpleNamespace(handle=2) if foreign else None))
            if duplicate_summary:messages.append(message(text))
            if exception:raise RuntimeError('compiler exception')
            if false_return:return False
        self.n['system']=types.SimpleNamespace(get_message_categories=lambda:[policy.COMPILER],clear_messages=lambda _:messages.clear(),get_message_objects=lambda _:list(messages))
        self.n['app'].rebuild=lambda:phase('rebuild');self.n['app'].generate_code=lambda:phase('generate')

    def test_foreign_build_message_cannot_prove_native_compilation(self):
        self.compile_fixture(foreign=True);result=self.n['compile_project']()
        self.assertFalse(result['success']);self.assertFalse(result['compilation']['diagnosticsComplete'])

    def test_fresh_target_phases_generate_complete_bound_proof(self):
        self.compile_fixture();result=self.n['compile_project']()
        self.assertTrue(result['success']);self.assertEqual(result['target'],TARGET)
        report=result['compilation'];self.assertEqual(report['buildId'],self.n['current_context']['requestId'])
        self.assertEqual([p['name'] for p in report['phases']],['rebuild','generate'])
        self.assertTrue(all(d['correlated'] for d in report['diagnostics']))
        self.assertTrue(any(d['buildSummary'] for d in report['diagnostics'] if d['phase']=='generate'))

    def test_missing_target_header_cannot_prove_native_compilation(self):
        self.compile_fixture(no_header=True);self.assertFalse(self.n['compile_project']()['success'])

    def test_compiler_exception_preserves_diagnostics_without_completed_flag(self):
        self.compile_fixture(exception=True);result=self.n['compile_project']()
        self.assertFalse(result['success']);self.assertFalse(result['compilation']['buildCompleted']);self.assertTrue(result['compilation']['diagnostics'])

    def test_unexpected_false_compiler_return_is_not_codegen_success(self):
        self.compile_fixture(false_return=True);result=self.n['compile_project']()
        self.assertFalse(result['success']);self.assertFalse(result['compilation']['nativeCodeGenerated'])

    def test_generate_summary_nine_warnings_with_zero_warning_rows_fails(self):
        self.compile_fixture(summary_warnings={'generate':9});result=self.n['compile_project']()
        self.assertFalse(result['success']);self.assertFalse(result['compilation']['diagnosticsComplete'])
        phase=result['compilation']['phases'][1]
        self.assertEqual(phase['warningCount'],0);self.assertEqual(phase['summaryCounts']['warningCount'],9)
        self.assertFalse(phase['countsMatchSummary'])

    def test_rebuild_summary_count_mismatch_blocks_codegen(self):
        self.compile_fixture(summary_warnings={'rebuild':9});result=self.n['compile_project']()
        self.assertFalse(result['success']);self.assertFalse(result['compilation']['nativeCodeGenerated'])
        self.assertEqual(len(result['compilation']['phases']),1)

    def test_summary_cannot_omit_real_warnings_or_claim_unobserved_errors(self):
        self.compile_fixture(warnings=1,summary_warnings={'generate':0});self.assertFalse(self.n['compile_project']()['success'])
        self.compile_fixture(summary_errors={'generate':1});self.assertFalse(self.n['compile_project']()['success'])

    def test_repeated_warning_across_phases_is_counted_independently(self):
        self.compile_fixture(warnings=1);result=self.n['compile_project']()
        self.assertTrue(result['success']);self.assertEqual(result['compilation']['warningCount'],2)
        self.assertEqual([p['summaryCounts']['warningCount'] for p in result['compilation']['phases']],[1,1])
        self.assertTrue(all(p['countsMatchSummary'] for p in result['compilation']['phases']))

    def test_ambiguous_multiple_phase_summaries_fail_closed(self):
        self.compile_fixture(duplicate_summary=True);self.assertFalse(self.n['compile_project']()['success'])

if __name__=='__main__':unittest.main(verbosity=2)
