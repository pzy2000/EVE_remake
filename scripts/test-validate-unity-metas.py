#!/usr/bin/env python3

from pathlib import Path
import importlib.util
import tempfile


SCRIPT = Path(__file__).with_name("validate-unity-metas.py")
SPEC = importlib.util.spec_from_file_location("validate_unity_metas", SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


def touch(path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.touch()


with tempfile.TemporaryDirectory() as temporary:
    assets = Path(temporary) / "Assets"
    assets.mkdir()
    touch(Path(f"{assets}.meta"))
    (assets / "Runtime").mkdir()
    touch(assets / "Runtime.meta")
    touch(assets / "Runtime" / "Game.cs")
    touch(assets / "Runtime" / "Game.cs.meta")
    android_library = assets / "Plugins" / "Mobile.androidlib"
    (assets / "Plugins").mkdir()
    touch(assets / "Plugins.meta")
    android_library.mkdir()
    touch(Path(f"{android_library}.meta"))
    touch(android_library / "build.gradle")
    touch(android_library / "src" / "main" / "AndroidManifest.xml")

    missing, orphaned = MODULE.validate(assets)
    assert missing == [], missing
    assert orphaned == [], orphaned

    (assets / "Runtime" / "Game.cs.meta").unlink()
    touch(assets / "Runtime" / "Deleted.asset.meta")
    missing, orphaned = MODULE.validate(assets)
    assert missing == [assets / "Runtime" / "Game.cs"], missing
    assert orphaned == [assets / "Runtime" / "Deleted.asset.meta"], orphaned

print("Unity meta validator tests passed.")
