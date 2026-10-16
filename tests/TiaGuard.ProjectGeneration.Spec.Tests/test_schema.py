"""Offline shape checks; semantic/filesystem checks live in the pure C# suite."""
import copy
import json
from pathlib import Path
from jsonschema import Draft202012Validator

root = Path(__file__).resolve().parents[2]
schema = json.loads((root / 'docs/contracts/project-generation-v1.schema.json').read_text(encoding='utf-8'))
Draft202012Validator.check_schema(schema)
validator = Draft202012Validator(schema)
fixtures = list((root / 'examples/project-generation-v1/specs').glob('*/project-generation.json'))
for fixture in fixtures:
    validator.validate(json.loads(fixture.read_text(encoding='utf-8')))
base = json.loads(fixtures[0].read_text(encoding='utf-8'))
negatives = []
for key in ('schemaVersion', 'tiaVersion', 'projectName', 'plc', 'tagTables', 'artifacts'):
    for wrong in (None, True, 21):
        value = copy.deepcopy(base)
        value[key] = wrong
        negatives.append(value)
    value = copy.deepcopy(base)
    del value[key]
    negatives.append(value)
for level in ('root', 'plc', 'artifact'):
    value = copy.deepcopy(base)
    target = value if level == 'root' else value['plc'] if level == 'plc' else value['artifacts'][0]
    target['unknown'] = 'x'
    negatives.append(value)
for key, wrong in (('format', 'simatic-sd'), ('format', 'simaticml'), ('kind', 'HMI'), ('sourcePath', '../Main.scl'), ('sourcePath', 'sources\\Main.scl'), ('dependsOn', [1]), ('dependsOn', ['main', 'main'])):
    value = copy.deepcopy(base)
    value['artifacts'][0][key] = wrong
    negatives.append(value)
for value in negatives:
    assert list(validator.iter_errors(value)), value
print(f'PASS: generation schema, {len(fixtures)} positive fixtures, {len(negatives)} negative cases')
