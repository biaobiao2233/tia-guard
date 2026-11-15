"""Prepare a pinned, original runtime from a pre-downloaded official ZIP. Never execute it."""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import zipfile


def digest(data):
    return hashlib.sha256(data).hexdigest()


def runtime_digest(files):
    return digest("".join(name + "\0" + files[name] + "\n" for name in sorted(files)).encode())


def prepare(archive, destination, tia_location, lock_path):
    pin = json.loads(lock_path.read_text(encoding="utf-8-sig"))["candidate"]
    if digest(archive.read_bytes()) != pin["assetSha256"]:
        raise ValueError("asset-hash-mismatch")
    config_path = destination.parent / (destination.name + ".config.json")
    if destination.exists() or config_path.exists():
        raise ValueError("output-exists: choose a new isolated directory")
    with zipfile.ZipFile(archive) as bundle:
        payloads = {}
        for info in bundle.infolist():
            if not info.filename.startswith(pin["runtimePrefix"]) or info.is_dir():
                continue
            name = info.filename[len(pin["runtimePrefix"]):]
            parts = PurePosixPath(name)
            if (not name or parts.is_absolute() or ".." in parts.parts or "\\" in name
                    or ":" in name or "\0" in name or name.casefold() in {n.casefold() for n in payloads}):
                raise ValueError("invalid-runtime-path")
            if info.file_size > 30_000_000 or sum(map(len, payloads.values())) > 150_000_000:
                raise ValueError("runtime-size-limit")
            payloads[name] = bundle.read(info)
    hashes = {name: digest(data) for name, data in payloads.items()}
    if runtime_digest(hashes) != pin["runtimeManifestSha256"]:
        raise ValueError("runtime-manifest-mismatch")
    if hashes.get(pin["executable"]) != pin["executableSha256"]:
        raise ValueError("executable-hash-mismatch")
    destination.mkdir(parents=True, exist_ok=False)
    for name, data in payloads.items():
        target = destination.joinpath(*PurePosixPath(name).parts)
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
    config = {
        "schemaVersion": 1,
        "version": pin["version"],
        "runtimeDirectory": str(destination.resolve()),
        "tiaPortalLocation": str(tia_location.resolve()),
        "files": hashes,
    }
    # Exclusive create prevents accidental replacement of an existing configuration.
    with config_path.open("x", encoding="utf-8") as output:
        json.dump(config, output, ensure_ascii=False, indent=2)
    return {"prepared": True, "executed": False, "version": pin["version"],
            "fileCount": len(hashes), "configPath": str(config_path.resolve())}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--archive", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--tia-location", type=Path, required=True)
    options = parser.parse_args()
    try:
        print(json.dumps(prepare(options.archive, options.output, options.tia_location,
                                 Path(__file__).with_name("upstream.lock.json"))))
    except (ValueError, OSError, KeyError, zipfile.BadZipFile) as error:
        parser.exit(1, str(error) + "\n")
