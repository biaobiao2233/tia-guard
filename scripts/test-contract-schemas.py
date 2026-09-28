"""Validate sanitized repository fixtures; no Siemens installation or project needed."""
from copy import deepcopy
import json
from pathlib import Path
from jsonschema import Draft202012Validator

repo = Path(__file__).resolve().parents[1]
schemas = repo / "docs/contracts"
def read(path):
    return json.loads(path.read_text(encoding="utf-8"))
def validate(schema, value):
    Draft202012Validator.check_schema(schema)
    Draft202012Validator(schema).validate(value)

validate(read(schemas / "snapshot-v1.schema.json"), read(repo / "examples/snapshot-fixture/snapshot.json"))
manifest_schema = read(schemas / "roundtrip-source-v1.schema.json")
descriptor_schema = read(schemas / "roundtrip-descriptors-v1.schema.json")
fixture = repo / "examples/roundtrip-fixture"
manifest = read(fixture / "tia-guard.json")
validate(manifest_schema, manifest)
for path in (fixture / "tia").rglob("*.json"):
    validate(descriptor_schema, read(path))

for mutation in ("numeric-name", "missing-projectVersion", "missing-reason", "ready-diagnostic"):
    invalid = deepcopy(manifest)
    if mutation == "numeric-name":
        invalid["project"]["name"] = 123
    elif mutation == "missing-projectVersion":
        del invalid["project"]["projectVersion"]
    elif mutation == "missing-reason":
        del invalid["capabilities"][0]["reason"]
    else:
        invalid["diagnostics"] = [{"code": "BLOCKED", "message": "incomplete", "objectRef": None}]
    assert list(Draft202012Validator(manifest_schema).iter_errors(invalid)), mutation
print("PASS: Snapshot, canonical manifest/descriptors, and four schema negative cases")
