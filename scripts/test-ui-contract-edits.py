#!/usr/bin/env python3
"""Real edit/rebuild checks in an already-restored, independent package consumer.

The caller supplies a disposable consumer copy. Every source is restored in finally.
This is a development correctness check, not part of an application's startup.
"""
from pathlib import Path
import json
import os
import subprocess
import sys
import time

consumer = Path(sys.argv[1]).resolve()
output = Path(sys.argv[2]).resolve()
output.mkdir(parents=True, exist_ok=True)
dotnet = os.environ.get('DOTNET', 'dotnet')
paths = {name: consumer / name for name in ['CardUi.cs', 'CardGame.cs', 'Sample.csproj', 'assets/ui/cards.rml']}
original = {name: path.read_text() for name, path in paths.items()}
checks = []

def build(name, edits=None, expected=None, detail=None):
    for filename, path in paths.items():
        path.write_text((edits or {}).get(filename, original[filename]))
    start = time.monotonic()
    result = subprocess.run([dotnet, 'build', str(consumer / 'Sample.csproj'), '-c', 'Release', '--no-restore',
        '-m:1', '-nr:false', '-p:UseSharedCompilation=false'], text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    seconds = time.monotonic() - start
    output.joinpath(name + '.log').write_text(result.stdout)
    if expected is None:
        assert result.returncode == 0, (name, result.stdout)
    else:
        assert result.returncode != 0 and ('error ' + expected + ':') in result.stdout, (name, result.stdout)
        assert 'CS8785' not in result.stdout, ('generator must diagnose, not throw', name, result.stdout)
        if expected == 'DUI005':
            assert 'cards.rml(' in result.stdout, ('original RML location missing', name, result.stdout)
        if detail:
            assert detail in result.stdout, (name, detail, result.stdout)
    checks.append({'case': name, 'expected': expected or 'success', 'seconds': round(seconds, 3)})
    print('UI CONTRACT EDIT PASS', name, flush=True)

try:
    build('baseline')
    # All files unchanged: standard MSBuild should skip compile, while the driver suite
    # separately verifies generator-node caching inside a retained Roslyn driver.
    start = time.monotonic()
    unchanged = subprocess.run([dotnet, 'build', str(consumer / 'Sample.csproj'), '-c', 'Release', '--no-restore',
        '-m:1', '-nr:false', '-p:UseSharedCompilation=false'], text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    assert unchanged.returncode == 0, unchanged.stdout
    output.joinpath('unchanged.log').write_text(unchanged.stdout)
    checks.append({'case': 'unchanged', 'expected': 'success', 'seconds': round(time.monotonic() - start, 3)})
    build('rename_command', {'CardUi.cs': original['CardUi.cs'].replace('void Discard(ulong id)', 'void Toss(ulong id)')}, 'DUI005', "Unknown registered command 'Discard'")
    build('rename_property', {'CardGame.cs': original['CardGame.cs'].replace('record Stat(string Label,', 'record Stat(string Caption,')}, 'DUI005', 'stat.Label')
    build('wrong_key_kind', {'assets/ui/cards.rml': original['assets/ui/cards.rml'].replace('Discard(item.Id)', 'Discard(item.Count)')}, 'DUI005', 'expects Key')
    build('wrong_arity', {'assets/ui/cards.rml': original['assets/ui/cards.rml'].replace('Discard(item.Id)', 'Discard(item.Id, item.Count)')}, 'DUI005', 'expects 1 argument')
    build('empty_loop_path', {'assets/ui/cards.rml': original['assets/ui/cards.rml'].replace('section.Items', 'section.Missing')}, 'DUI005', 'section.Missing')
    build('wrong_case', {'assets/ui/cards.rml': original['assets/ui/cards.rml'].replace('item.CanDiscard', 'item.canDiscard')}, 'DUI005', 'item.canDiscard')
    build('missing_additional_file', {'Sample.csproj': original['Sample.csproj'].replace('assets/ui/cards.rml;', '')}, 'DUI004', 'missing from AdditionalFiles')
    build('unsupported_command_type', {'CardUi.cs': original['CardUi.cs'].replace('void Discard(ulong id)', 'void Discard(int id)')}, 'DUI002')
    build('coherent_rename', {'CardUi.cs': original['CardUi.cs'].replace('void Discard(ulong id)', 'void Toss(ulong id)'),
        'CardGame.cs': original['CardGame.cs'].replace('record Stat(string Label,', 'record Stat(string Caption,'),
        'assets/ui/cards.rml': original['assets/ui/cards.rml'].replace('Discard(item.Id)', 'Toss(item.Id)').replace('stat.Label', 'stat.Caption')})
    build('restored')
finally:
    for filename, path in paths.items():
        path.write_text(original[filename])
output.joinpath('results.json').write_text(json.dumps(checks, indent=2) + '\n')
print('UI CONTRACT EDITS PASS cases=' + str(len(checks)))
