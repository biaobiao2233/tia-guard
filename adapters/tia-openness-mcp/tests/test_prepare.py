import hashlib
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

from prepare import prepare, runtime_digest


class PrepareTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="tia-prepare-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.archive = self.root / "fixture.zip"
        self.pin = self.root / "pin.json"
        self.output = self.root / "runtime"

    def make_bundle(self, payloads=None):
        payloads = payloads or {"TiaMcpServer.exe": b"fixture", "library.dll": b"library"}
        with zipfile.ZipFile(self.archive, "w") as bundle:
            for name, content in payloads.items():
                bundle.writestr("runtime/" + name, content)
        hashes = {name: hashlib.sha256(data).hexdigest() for name, data in payloads.items()}
        pin = {"version": "fixture", "runtimePrefix": "runtime/", "executable": "TiaMcpServer.exe",
               "executableSha256": hashes.get("TiaMcpServer.exe"), "runtimeManifestSha256": runtime_digest(hashes),
               "assetSha256": hashlib.sha256(self.archive.read_bytes()).hexdigest()}
        self.pin.write_text(json.dumps({"candidate": pin}), encoding="utf-8")
        return pin

    def test_valid_archive_is_copied_exactly_and_never_executed(self):
        self.make_bundle()
        result = prepare(self.archive, self.output, self.root, self.pin)
        self.assertTrue(result["prepared"])
        self.assertFalse(result["executed"])
        self.assertEqual((self.output / "TiaMcpServer.exe").read_bytes(), b"fixture")
        config = json.loads(Path(result["configPath"]).read_text())
        self.assertEqual(len(config["files"]), 2)
        with self.assertRaisesRegex(ValueError, "output-exists"):
            prepare(self.archive, self.output, self.root, self.pin)

    def test_archive_checksum_failure_creates_nothing(self):
        self.make_bundle()
        with self.archive.open("ab") as output:
            output.write(b"changed")
        with self.assertRaisesRegex(ValueError, "asset-hash-mismatch"):
            prepare(self.archive, self.output, self.root, self.pin)
        self.assertFalse(self.output.exists())

    def test_escaping_and_case_colliding_archive_paths_are_rejected(self):
        for payloads in [{"../escape": b"bad"}, {"TiaMcpServer.exe": b"one", "tiamcpserver.exe": b"two"}]:
            self.make_bundle(payloads)
            with self.assertRaisesRegex(ValueError, "invalid-runtime-path"):
                prepare(self.archive, self.output, self.root, self.pin)
            self.assertFalse(self.output.exists())
            self.assertFalse((self.root / "escape").exists())

    def test_existing_config_is_not_overwritten_or_partially_extracted(self):
        self.make_bundle()
        config = self.root / "runtime.config.json"
        config.write_text("keep", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "output-exists"):
            prepare(self.archive, self.output, self.root, self.pin)
        self.assertEqual(config.read_text(), "keep")
        self.assertFalse(self.output.exists())


if __name__ == "__main__":
    unittest.main()
