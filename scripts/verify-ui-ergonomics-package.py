#!/usr/bin/env python3
"""Match the independent consumer outputs to their final local NuGet packages."""
from pathlib import Path
import hashlib
import json
import sys
import zipfile

proof = Path(sys.argv[1]).resolve()
sha = lambda data: hashlib.sha256(data).hexdigest()
engine = next(proof.glob('feed/Dotnet2D.Engine.*.nupkg'))
native = next(proof.glob('feed/Dotnet2D.Native.Linux.x64.*.nupkg'))
with zipfile.ZipFile(engine) as archive:
    assert 'analyzers/dotnet/cs/Dotnet2D.Ui.Generator.dll' in archive.namelist(), 'generator must reach package consumers'
    payload = archive.read('lib/net10.0/Dotnet2D.Engine.dll')
    assert payload == (proof / 'publish/jit/Dotnet2D.Engine.dll').read_bytes(), 'JIT engine must match package'
with zipfile.ZipFile(native) as archive:
    dsos = {Path(name).name: archive.read(name) for name in archive.namelist()
            if name.startswith('runtimes/linux-x64/native/') and not name.endswith('/')}
for mode in ['jit', 'aot']:
    runtime = proof / 'publish' / mode
    assert not list(runtime.rglob('Microsoft.CodeAnalysis*.dll')), 'Roslyn must stay build-only'
    assert not list(runtime.rglob('Dotnet2D.Ui.Generator.dll')), 'analyzer must not ship in application output'
    paths = (proof / 'captures' / mode / 'package-native-path.txt').read_text().splitlines()
    assert len(paths) == 1, (mode, paths)
    loaded = Path(paths[0]).resolve()
    assert loaded.is_relative_to(proof / 'publish' / mode) and loaded.name == 'libgal.so', (mode, loaded)
    for name, data in dsos.items():
        assert (loaded.parent / name).read_bytes() == data, (mode, name)
report = {'engine_package_sha256': sha(engine.read_bytes()), 'native_package_sha256': sha(native.read_bytes()),
          'engine_dll_sha256': sha(payload), 'native_files': {name: sha(data) for name, data in dsos.items()},
          'modes': ['jit', 'aot'], 'scope': 'focused correctness, not timing benchmark'}
(proof / 'verification.json').write_text(json.dumps(report, indent=2) + '\n')
print('PACKAGE UI ERGONOMICS MATCH PASS native_files=' + str(len(dsos)))
