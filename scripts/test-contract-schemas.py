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
with_identity = deepcopy(manifest)
with_identity["project"].update({
    "originalFileName": "Demo.ap21",
    "originalSizeBytes": 151394,
    "originalSha256": "b0ca8738b321e15074ff48019a4cf53b0918914d5f9f1138a967e25c4f30caae",
})
validate(manifest_schema, with_identity)
for path in (fixture / "tia").rglob("*.json"):
    validate(descriptor_schema, read(path))

for mutation in ("numeric-name", "missing-projectVersion", "missing-reason", "ready-diagnostic", "incomplete-original-identity"):
    invalid = deepcopy(manifest)
    if mutation == "numeric-name":
        invalid["project"]["name"] = 123
    elif mutation == "missing-projectVersion":
        del invalid["project"]["projectVersion"]
    elif mutation == "missing-reason":
        del invalid["capabilities"][0]["reason"]
    elif mutation == "ready-diagnostic":
        invalid["diagnostics"] = [{"code": "BLOCKED", "message": "incomplete", "objectRef": None}]
    else:
        invalid["project"]["originalFileName"] = "Demo.ap21"
    assert list(Draft202012Validator(manifest_schema).iter_errors(invalid)), mutation
print("PASS: Snapshot, canonical manifest/descriptors, original file identity, and five schema negative cases")

ai_schema = read(schemas / "ai-engineering-v1.schema.json")
ai_view = read(repo / "examples/ai-view-fixture/project.json")
validate(ai_schema, ai_view)
for mutation in ("authority", "coverage", "missing-source", "wrong-number", "extra-blob"):
    invalid = deepcopy(ai_view)
    if mutation == "authority":
        invalid["authority"] = "build-input"
    elif mutation == "coverage":
        invalid["coverage"][0]["status"] = "probably-empty"
    elif mutation == "missing-source":
        del invalid["project"]["sourceRef"]
    elif mutation == "wrong-number":
        invalid["blocks"][0]["number"] = 2
    else:
        invalid["blob"] = "unrecognized payload"
    assert list(Draft202012Validator(ai_schema).iter_errors(invalid)), mutation
print("PASS: derived AI engineering schema and five negative cases")

lad_schema = read(schemas / "ai-engineering-v2.schema.json")
lad_view = read(repo / "examples/lad-v21-fixture/ai/project.json")
validate(lad_schema, lad_view)
for mutation in ("partial-with-writes", "unsupported-with-reads", "unknown-expression", "bad-port",
                 "missing-selector", "interlock-without-evidence", "unavailable-with-interlock"):
    invalid = deepcopy(lad_view)
    network = invalid["blocks"][0]["networks"][0]
    relationship = invalid["blocks"][0]["relationships"]
    if mutation == "partial-with-writes":
        network["analysis"]["status"] = "unsupported"
    elif mutation == "unsupported-with-reads":
        network["analysis"]["status"] = "unknown"
        network["analysis"]["writes"] = []
    elif mutation == "unknown-expression":
        network["analysis"]["writes"][0]["expression"]["op"] = "or"
    elif mutation == "bad-port":
        network["analysis"]["flows"][0]["toPort"] = "operand"
    elif mutation == "missing-selector":
        del network["graph"]["nodes"][0]["sourceSelector"]
    elif mutation == "interlock-without-evidence":
        relationship["interlocks"][0]["evidence"] = []
    else:
        relationship["status"] = "unavailable"
    assert list(Draft202012Validator(lad_schema).iter_errors(invalid)), mutation
print("PASS: LAD v2 schema and seven fail-closed negative cases")
