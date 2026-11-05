"""Validate real-device preset round trip and Java/C# DSP reference vectors."""
import json
import pathlib
import sys

root = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(root / "build/pythonlibs"))
import jsonschema

def read(path):
    return json.loads((root / path).read_text(encoding="utf-8-sig"))

schema = read("spec/preset.schema.json")
desktop = read("evidence/desktop-export.json")
android = read("evidence/android-roundtrip.json")
assert desktop == android, "Cross-platform config parameters changed"
jsonschema.validate(desktop, schema)
jsonschema.validate(read("presets/flat.json"), schema)
java = read("evidence/android-dsp-vectors.json")
csharp = read("evidence/desktop-dsp-vectors.json")
assert len(java) == len(csharp) == 28
details = []
for a, b in zip(java, csharp):
    assert (a["type"], a["rate"]) == (b["type"], b["rate"])
    errors = []
    for key in ("coefficients", "impulse"):
        assert len(a[key]) == len(b[key])
        errors.extend(abs(x - y) for x, y in zip(a[key], b[key]))
    error = max(errors)
    assert error < 1e-12, (a["type"], a["rate"], error)
    details.append(dict(type=a["type"], rate=a["rate"], max_error=error))
report = dict(result="PASS", preset_roundtrip="exact semantic equality", schema="PASS",
              vectors=len(details), max_absolute_error=max(d["max_error"] for d in details), details=details)
(root / "evidence/dsp-parity.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print(f'PASS: preset round trip, schema, {len(details)} Java/C# coefficient and impulse vectors; max error {report["max_absolute_error"]}')
