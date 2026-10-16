# -*- coding: utf-8 -*-
"""Vendor-independent policy shared by the actual IronPython worker and fault tests."""
import re
import xml.etree.ElementTree as ET

COMPILER = '97f48d64-a2a3-4856-b640-75c046e37ea9'
ANALYSIS = '220493a1-f49b-4416-9a3f-a545db707cbe'
SIM_DEVICE = {'type': 4097, 'id': '0000 000a', 'version': '3.5.22.40'}
INPUTS = {'GVL_Test.Enable': 'BOOL', 'GVL_Test.Step': 'DINT'}

def normalize(text):
    return text.replace('\r\n', '\n').replace('\r', '\n').rstrip('\n')

def local_name(element):
    return element.tag.split('}')[-1]

def object_metadata(xml, name):
    # IronPython's bundled Expat cannot feed a Unicode BOM/non-ASCII string directly.
    if not isinstance(xml, bytes): xml = xml.encode('utf-8')
    root = ET.fromstring(xml)
    pous = [e for e in root.iter() if local_name(e) == 'pou' and e.get('name') == name]
    gvls = [e for e in root.iter() if local_name(e) == 'globalVars' and e.get('name') == name]
    if len(pous) == 1 and not gvls:
        kind = {'program': 'program', 'functionBlock': 'functionBlock'}.get(pous[0].get('pouType'))
        if not kind: raise ValueError('unsupported-native-pou-kind')
        return kind, {}
    if len(gvls) != 1 or pous: raise ValueError('ambiguous-native-object-kind')
    types = {}
    for var in gvls[0]:
        if local_name(var) != 'variable': continue
        key = var.get('name')
        if key in types: raise ValueError('duplicate-native-variable')
        type_nodes = [e for e in var if local_name(e) == 'type']
        children = list(type_nodes[0]) if len(type_nodes) == 1 else []
        types[key] = local_name(children[0]) if len(children) == 1 and not list(children[0]) else 'unsupported'
    return 'gvl', types

def check_input_declaration(spec):
    if spec['name'].lower() != 'gvl_test': return
    if spec['name'] != 'GVL_Test' or spec['kind'] != 'gvl': raise ValueError('reserved-input-object')
    # The reserved writable test interface intentionally accepts a small declaration grammar.
    text = spec['declaration'].strip()
    body = re.match(r'\AVAR_GLOBAL\s+(.*?)\s*END_VAR\s*\Z', text, re.I | re.S)
    if not body: raise ValueError('unsupported-input-declaration')
    fields = {}
    for item in body.group(1).split(';'):
        if not item.strip(): continue
        match = re.match(r'\A\s*(Enable|Step)\s*:\s*(BOOL|DINT)\s*(?::=\s*(TRUE|FALSE|[+-]?\d+)\s*)?\Z', item, re.I)
        if not match: raise ValueError('unsupported-input-declaration')
        name, kind, initial = match.groups()
        if name in fields: raise ValueError('duplicate-input-declaration')
        fields[name] = kind.upper()
        if kind.upper() == 'BOOL' and initial and initial.upper() not in ('TRUE', 'FALSE'): raise ValueError('input-initial-type')
        if kind.upper() == 'DINT' and initial and not re.match(r'\A[+-]?\d+\Z', initial): raise ValueError('input-initial-type')
    if fields != {'Enable': 'BOOL', 'Step': 'DINT'}: raise ValueError('input-declaration-type-mismatch')

def structure_differences(snapshot, expected):
    differences = []
    if snapshot.get('device') != SIM_DEVICE or snapshot.get('simulation') is not True: differences.append('device')
    actual = snapshot.get('objects', [])
    wanted = expected['objects']
    if sorted(o['name'] for o in actual) != sorted(o['name'] for o in wanted): differences.append('object-set')
    for spec in wanted:
        matches = [o for o in actual if o['name'] == spec['name']]
        if len(matches) != 1: continue
        obj = matches[0]
        if obj.get('kind') != spec['kind']: differences.append('object-kind:' + spec['name'])
        for field in ('declaration', 'implementation'):
            if normalize(obj.get(field, '')) != normalize(spec[field]): differences.append(field + ':' + spec['name'])
        if spec['name'] == 'GVL_Test' and obj.get('variableTypes') != {'Enable': 'BOOL', 'Step': 'DINT'}:
            differences.append('input-types')
    wanted_task = {'name': 'MainTask', 'kind': 'Cyclic', 'interval': 't#20ms', 'calls': [expected['entry']]}
    if snapshot.get('tasks') != [wanted_task]: differences.append('task-configuration')
    return differences

def zero_codegen_summary(message):
    return bool(re.match(u'\A(?:构建完整|Build complete)\\s*--\\s*0\\s*(?:错误|errors?)\\s*,\\s*\\d+\\s*(?:警告|warnings?)(?::.*)?\\s*\\Z', message, re.I))

def scalar_value(variable, observed):
    if not isinstance(observed, str) or not observed: raise ValueError('missing-monitor-value')
    if INPUTS[variable] == 'BOOL':
        if observed not in ('TRUE', 'FALSE'): raise ValueError('online-input-type-mismatch')
        return observed == 'TRUE'
    if not re.match(r'\ADINT#[+-]?\d+\Z', observed): raise ValueError('online-input-type-mismatch')
    value = int(observed[5:])
    if not -2147483648 <= value <= 2147483647: raise ValueError('online-input-value-range')
    return value

def checked_monitor_value(value):
    if value is None or not str(value) or str(value) in ('None', '<unknown>'): raise ValueError('missing-monitor-value')
    return str(value)
