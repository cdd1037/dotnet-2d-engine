#!/usr/bin/env python3
"""ARCHIVED reproduction for baseline a6a5a6e (2026-10-02), not a current API gate.

Copy two existing prototypes, adapt only their hosts, count, and optionally check.

Run from the a6a5a6e checkout with its original package feed and matching external
fixtures; current packages intentionally removed the ABI-shaped APIs used here.
Never edits the input prototypes. Requires the external authoring-comparison tree
and already prepared SDK/feed. Logs and generated harness live under --output.
"""
import argparse
import hashlib
import json
import os
import re
from pathlib import Path
import shutil
import subprocess
import textwrap
import xml.sax.saxutils

REPO = Path(__file__).resolve().parents[1]


def replace_once(source, before, after):
    if source.count(before) != 1:
        raise ValueError(f"Expected exactly one source fragment: {before[:90]!r}")
    return source.replace(before, after, 1)


def adapt_platformer(source):
    source = replace_once(source, 'using Platformer;\n', 'using Platformer;\nusing Dotnet2DStarter;\n')
    source = replace_once(source, 'double previous = 0, accumulator = 0;\nbool pendingJump = false;',
        'double previous = 0;\nvar fixedInput = new FixedStepInput(Simulation.StepSeconds);')
    source = replace_once(source, 'double elapsed = Math.Min(now - previous, .1);',
        'double elapsed = now - previous;')
    start = source.index('    if ((action.Pressed & 8) != 0)')
    end = source.index('    if (input.Drawable) presentation.Draw(game);', start)
    body = textwrap.dedent('''\
        if ((action.Pressed & 8) != 0) game.Paused = !game.Paused;
        bool restart = (action.Pressed & 16) != 0;
        if (restart)
        {
            game.Restart();
            fixedInput.Reset();
        }
        bool suspended = game.Paused || restart || !input.Focused || !input.Drawable;
        fixedInput.BeginFrame(elapsed, action, suspended);
        while (fixedInput.TryTakeStep(out var stepInput))
        {
            float axis = ((stepInput.Down & 2) != 0 ? 1 : 0) - ((stepInput.Down & 1) != 0 ? 1 : 0);
            game.Tick(axis, (stepInput.Pressed & 4) != 0, input.Viewport);
        }
    ''')
    return source[:start] + textwrap.indent(body, '    ') + source[end:]


def adapt_topdown(source):
    source = replace_once(source, 'using TopdownOurs;\n', 'using TopdownOurs;\nusing Dotnet2DStarter;\n')
    source = replace_once(source, '''    double accumulator = 0;
    bool pendingAttack = false;
    bool pendingInteract = false;''', '''    var actions = new InputActionMap(
        InputBinding.Key(1, PhysicalKey.A), InputBinding.Key(2, PhysicalKey.D),
        InputBinding.Key(4, PhysicalKey.W), InputBinding.Key(8, PhysicalKey.S),
        InputBinding.Key(16, PhysicalKey.Space), InputBinding.Key(32, PhysicalKey.E),
        InputBinding.Key(64, PhysicalKey.Escape), InputBinding.Key(128, PhysicalKey.T));
    var fixedInput = new FixedStepInput();''')
    start = source.index('        if (input.KeyPressed(PhysicalKey.Escape))')
    end = source.index('        Thread.Sleep(1);', start)
    body = '''var action = actions.Update(input);
double now = clock.Elapsed.TotalSeconds;
double elapsed = now - previous;
previous = now;
if ((action.Pressed & 64) != 0)
    arena.Rules.TogglePause();
bool restart = (action.Pressed & 128) != 0;
if (restart)
{
    arena.Dispose();
    arena = new Arena(engine, assets);
    fixedInput.Reset();
}
bool suspended = arena.Rules.Paused || restart || !input.Focused || !input.Drawable;
TimingStep frame = fixedInput.BeginFrame(elapsed, action, suspended);
while (fixedInput.TryTakeStep(out var stepInput))
{
    Vector2 direction = new(
        ((stepInput.Down & 2) != 0 ? 1 : 0) - ((stepInput.Down & 1) != 0 ? 1 : 0),
        ((stepInput.Down & 8) != 0 ? 1 : 0) - ((stepInput.Down & 4) != 0 ? 1 : 0));
    arena.Step(direction, (stepInput.Pressed & 32) != 0, (stepInput.Pressed & 16) != 0);
}
if (input.Drawable) arena.Draw(input.Viewport, frame.GameSeconds);
'''
    return source[:start] + textwrap.indent(body, '        ') + source[end:]


def slice_between(source, start, end, include_end=False):
    left = source.index(start)
    right = source.index(end, left)
    return textwrap.dedent(source[left:right + (len(end) if include_end else 0)])


def harness(platformer, topdown):
    # Bodies below are copied from generated Program.cs, not independently
    # reimplemented. Stand-ins record step/draw calls, avoiding a native window.
    pmap = slice_between(platformer, 'var actions = new InputActionMap(', 'var watch =')
    tmap = slice_between(topdown, '    var actions = new InputActionMap(', '    while (true)')
    pbody = slice_between(platformer, '    if ((action.Pressed & 8)',
                          '    if (input.Drawable) presentation.Draw(game);', True)
    tbody = slice_between(topdown, '        if ((action.Pressed & 64)',
                          '        if (input.Drawable) arena.Draw(input.Viewport, frame.GameSeconds);', True)
    # A field initializer cannot use var. All following policy bodies stay exact.
    pmap = pmap.replace('var actions =', 'private readonly InputActionMap actions =')
    tmap = tmap.replace('var actions =', 'private readonly InputActionMap actions =')
    tmap = tmap.replace('var fixedInput =', 'private readonly FixedStepInput fixedInput =')
    return '''using System.Numerics;
using Dotnet2DStarter;
using GameAuthoringLab;

internal static unsafe class Program
{
    private static int checks;
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        checks++;
    }

    private static InputSnapshot Input(InputFlags flags = InputFlags.Focused | InputFlags.Drawable,
        PhysicalKey? down = null, PhysicalKey? tap = null, PhysicalKey? tap2 = null)
    {
        var input = new InputSnapshot { Flags = flags, WindowWidth = 960,
            WindowHeight = 540, PixelWidth = 960, PixelHeight = 540 };
        if (down is PhysicalKey held)
        {
            int code = (int)held;
            input.KeysDown[code / 64] |= 1ul << (code % 64);
            input.GameKeysDown[code / 64] |= 1ul << (code % 64);
        }
        foreach (var candidate in new[] { tap, tap2 })
        {
            if (candidate is PhysicalKey pressed)
            {
                int code = (int)pressed;
                input.KeysPressed[code / 64] |= 1ul << (code % 64);
                input.GameKeysPressed[code / 64] |= 1ul << (code % 64);
                input.KeysReleased[code / 64] |= 1ul << (code % 64);
                input.GameKeysReleased[code / 64] |= 1ul << (code % 64);
            }
        }
        return input;
    }

    private static void Main()
    {
        foreach (bool platformer in new[] { true, false })
        {
            Driver New() => platformer ? new PlatformerDriver() : new TopdownDriver();
            double step = platformer ? Simulation.StepSeconds : 1d / 60;
            string genre = platformer ? "platformer" : "topdown";
            var driver = New();
            driver.Frame(step / 4, Input(tap: PhysicalKey.Space));
            Check(driver.State.Steps == 0, genre + " zero-step frame");
            driver.Frame(step * 3 / 4, Input());
            Check(driver.State.Actions == 1, genre + " queued edge survives zero-step frame");
            driver.Frame(step * 3, Input(down: PhysicalKey.D, tap: PhysicalKey.Space));
            Check(driver.State.Steps == 4 && driver.State.Actions == 2,
                genre + " one edge across three catch-up steps");
            Check(driver.State.Moves == 3, genre + " held direction reaches each step");

            driver = New();
            driver.Frame(step / 4, Input(tap: PhysicalKey.Space));
            driver.Frame(.1, Input(tap: PhysicalKey.Escape));
            driver.Frame(.1, Input(tap: PhysicalKey.Space));
            Check(driver.State.Steps == 0, genre + " paused gameplay never steps");
            driver.Frame(step, Input(tap: PhysicalKey.Escape));
            Check(driver.State.Steps == 1 && driver.State.Actions == 0,
                genre + " resume discards earlier and paused presses");

            driver = New();
            driver.Frame(step / 4, Input(tap: PhysicalKey.Space));
            driver.Frame(.1, Input(tap: PhysicalKey.T, tap2: PhysicalKey.Space));
            Check(driver.State.Steps == 0, genre + " restart frame is suspended");
            driver.Frame(step, Input());
            Check(driver.State.Steps == 1 && driver.State.Actions == 0,
                genre + " restart discards queued edges and catch-up time");

            foreach (var missing in new[] { InputFlags.Drawable, InputFlags.Focused })
            {
                driver = New();
                driver.Frame(step / 4, Input(tap: PhysicalKey.Space));
                int draws = driver.State.Draws;
                driver.Frame(.1, Input(missing, tap: PhysicalKey.Space));
                Check(driver.State.Steps == 0, genre + " suspension clears queued steps");
                if (missing == InputFlags.Focused)
                    Check(driver.State.Draws == draws, genre + " nondrawable does not draw");
                else if (!platformer)
                    Check(driver.State.LastDrawSeconds == 0,
                        "topdown unfocused camera receives zero gameplay delta");
                driver.Frame(step, Input());
                Check(driver.State.Steps == 1 && driver.State.Actions == 0,
                    genre + " suspension does not replay old presses");
                driver.Frame(step, Input(tap: PhysicalKey.Space));
                Check(driver.State.Actions == 1, genre + " new press works after resume");
            }

            driver = New();
            driver.Frame(1, Input(tap: PhysicalKey.Space));
            int expected = (int)Math.Floor((.1 + step * 1e-10) / step);
            Check(driver.State.Steps == expected && driver.State.Actions == 1,
                genre + " long frame is capped, without repeating one-shot input");

            driver = New();
            driver.Frame(step, Input(InputFlags.Drawable));
            driver.Frame(step, Input(down: PhysicalKey.Space));
            Check(driver.State.Actions == 0, genre + " focus regain requires neutral controls");
            driver.Frame(step, Input());
            driver.Frame(step, Input(tap: PhysicalKey.Space));
            Check(driver.State.Actions == 1, genre + " focus-neutral gate releases");
            driver = New();
            var controls = platformer
                ? new[] { (PhysicalKey.A, -Vector2.UnitX), (PhysicalKey.D, Vector2.UnitX),
                    (PhysicalKey.Left, -Vector2.UnitX), (PhysicalKey.Right, Vector2.UnitX) }
                : new[] { (PhysicalKey.A, -Vector2.UnitX), (PhysicalKey.D, Vector2.UnitX),
                    (PhysicalKey.W, -Vector2.UnitY), (PhysicalKey.S, Vector2.UnitY) };
            foreach (var (key, direction) in controls)
            {
                driver.Frame(step, Input(down: key));
                Check(driver.State.Direction == direction, genre + " direction for " + key);
            }
            if (!platformer)
            {
                driver.Frame(step / 4, Input(tap: PhysicalKey.E));
                driver.Frame(step * 3 / 4, Input());
                Check(driver.State.Interactions == 1 && driver.State.Actions == 0,
                    "topdown E interaction survives zero-step frame without attacking");
                driver.Frame(step, Input(tap: PhysicalKey.Space));
                Check(driver.State.Interactions == 1 && driver.State.Actions == 1,
                    "topdown Space attacks without interacting");
            }
            Console.WriteLine("HOST POLICY PASS " + genre);
        }
        Console.WriteLine($"HOST POLICY PASS checks={checks}");
    }
}

internal abstract class Driver
{
    public abstract State State { get; }
    public abstract void Frame(double elapsed, InputSnapshot input);
}

internal sealed class PlatformerDriver : Driver
{
''' + textwrap.indent(pmap, '    ') + '''    private readonly FixedStepInput fixedInput = new(Simulation.StepSeconds);
    private readonly Simulation game = new();
    private readonly Presentation presentation = new();
    public override State State => game.State;
    public override void Frame(double elapsed, InputSnapshot input)
    {
        var action = actions.Update(input);
''' + textwrap.indent(pbody, '        ') + '''
    }
}

internal sealed class TopdownDriver : Driver
{
''' + textwrap.indent(tmap, '    ') + '''    private readonly object engine = new();
    private readonly object assets = new();
    private Arena arena = new(new object(), new object());
    public override State State => arena.State;
    public override void Frame(double elapsed, InputSnapshot input)
    {
        var action = actions.Update(input);
''' + textwrap.indent(tbody, '        ') + '''
    }
}

internal sealed class State
{
    public int Steps, Actions, Interactions, Moves, Draws;
    public Vector2 Direction;
    public double LastDrawSeconds;
    public void Step(Vector2 direction, bool action)
    {
        Steps++;
        Direction = direction;
        if (action) Actions++;
        if (direction != Vector2.Zero) Moves++;
    }
}

internal sealed class Simulation
{
    public const float StepSeconds = 1f / 60;
    public bool Paused { get; set; }
    public State State { get; } = new();
    public void Restart() { }
    public void Tick(float axis, bool jump, Viewport viewport) => State.Step(new(axis, 0), jump);
}

internal sealed class Presentation
{
    public void Draw(Simulation game) => game.State.Draws++;
}

internal sealed class Rules
{
    public bool Paused { get; private set; }
    public void TogglePause() => Paused = !Paused;
}

internal sealed class Arena : IDisposable
{
    public Arena(object engine, object assets) { }
    public State State { get; } = new();
    public Rules Rules { get; } = new();
    public void Step(Vector2 direction, bool interact, bool attack)
    {
        State.Step(direction, attack);
        if (interact) State.Interactions++;
    }
    public void Draw(Viewport viewport, double seconds)
    {
        State.Draws++;
        State.LastDrawSeconds = seconds;
    }
    public void Dispose() { }
}
'''


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def stats(path):
    data = path.read_bytes()
    return dict(nonblank=sum(bool(line.strip()) for line in data.splitlines()),
                physical=len(data.splitlines()), bytes=len(data), sha256=sha(path))


def run(command, name, root, env):
    with (root / 'logs' / (name + '.log')).open('w') as log:
        log.write('$ ' + ' '.join(str(part) for part in command) + '\n')
        log.flush()
        result = subprocess.run(command, cwd=root, env=env, stdout=log, stderr=subprocess.STDOUT)
    if result.returncode:
        raise RuntimeError(f'{name} failed; see {root / "logs" / (name + ".log")}')
    return dict(name=name, exit_code=result.returncode)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=REPO.parent / 'authoring-comparison')
    parser.add_argument('--output', type=Path, required=True, help='New, nonexistent output directory')
    parser.add_argument('--feed', type=Path, default=REPO / 'build-packages/feed')
    parser.add_argument('--dotnet', default=os.environ.get('DOTNET', 'dotnet'))
    parser.add_argument('--run', action='store_true', help='Build both snapshots and run focused tests')
    args = parser.parse_args()
    print("ARCHIVED STUDY: requires checkout a6a5a6e, its original feed and external fixtures; not a current validation gate.")
    source, root, feed = (p.resolve() for p in (args.source, args.output, args.feed))
    if root.exists():
        parser.error('--output must not exist; use a new isolated directory')
    if root == source or source in root.parents or root == REPO or REPO in root.parents:
        parser.error('--output must be outside the source prototypes and tracked repository')
    helper = REPO / 'templates/Starter/FixedStepInput.cs'
    for path in (source / 'platformer/ours/Program.cs', source / 'topdown/ours/Program.cs',
                 source / 'card-ui/ours/Program.cs', helper):
        if not path.is_file():
            parser.error(f'Missing required source: {path}')
    if not feed.is_dir():
        parser.error(f'Missing prepared package feed: {feed}')
    originals = {p: sha(p) for p in source.rglob('*') if p.is_file()
                 and not any(part.startswith('.') or part in ('bin', 'obj', 'evidence')
                             for part in p.relative_to(source).parts)
                 and p.suffix in ('.cs', '.csproj', '.json', '.rml', '.rcss', '.tscn', '.gd', '.tres')}
    root.mkdir(parents=True)
    (root / 'logs').mkdir()
    config = '<configuration><packageSources><clear /><add key="prepared-local" value=' + xml.sax.saxutils.quoteattr(str(feed)) + ' /></packageSources></configuration>\n'
    (root / 'NuGet.Config').write_text(config)
    hosts = {}
    for phase in ('before', 'after'):
        for genre, folders in (('platformer', ('ours', 'common', 'tests')), ('topdown', ('ours', 'shared', 'tests'))):
            for folder in folders:
                shutil.copytree(source / genre / folder, root / phase / genre / folder,
                    ignore=shutil.ignore_patterns('bin', 'obj', '.godot', 'NuGet.Config', '__pycache__'))
            host = root / phase / genre / 'ours/Program.cs'
            if phase == 'after':
                host.write_text((adapt_platformer if genre == 'platformer' else adapt_topdown)(host.read_text()))
                copied = host.parent / helper.name
                shutil.copy2(helper, copied)
                assert copied.read_bytes() == helper.read_bytes()
                hosts[genre] = host.read_text()
    harness_root = root / 'host-policy'
    harness_root.mkdir()
    shutil.copy2(helper, harness_root / helper.name)
    (harness_root / 'Program.cs').write_text(harness(hosts['platformer'], hosts['topdown']))
    (harness_root / 'HostPolicy.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks><TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup><PackageReference Include="Dotnet2D.Engine" Version="0.1.0-preview.1" /></ItemGroup>
</Project>
''')
    results = dict(source=str(source), output=str(root), feed=str(feed), helper=stats(helper),
                   card_ui_host=stats(source / 'card-ui/ours/Program.cs'), hosts={}, checks=[])
    for genre in ('platformer', 'topdown'):
        results['hosts'][genre] = {phase: stats(root / phase / genre / 'ours/Program.cs') for phase in ('before', 'after')}
    results['packages'] = {p.name: sha(p) for p in feed.glob('*.nupkg')}
    if args.run:
        env = dict(os.environ, DOTNET_CLI_HOME=str(root / '.dotnet-home'),
                   NUGET_PACKAGES=str(root / '.nuget'), DOTNET_CLI_TELEMETRY_OPTOUT='1',
                   DOTNET_GENERATE_ASPNET_CERTIFICATE='false',
                   XDG_DATA_HOME=str(root / '.xdg/data'), XDG_CONFIG_HOME=str(root / '.xdg/config'),
                   XDG_CACHE_HOME=str(root / '.xdg/cache'))
        for variable in ('LD_LIBRARY_PATH', 'LD_PRELOAD', 'LD_AUDIT', 'GAL_ASSET_ROOT'):
            env.pop(variable, None)
        sdk = Path(args.dotnet)
        if sdk.is_absolute():
            env['DOTNET_ROOT'] = str(sdk.parent)
        commands = [(args.dotnet, '--info'), (args.dotnet, 'nuget', 'list', 'source', '--configfile', str(root / 'NuGet.Config'))]
        for command, name in zip(commands, ('sdk-info', 'package-sources')):
            results['checks'].append(run(command, name, root, env))
        for phase in ('before', 'after'):
            for genre, project in (('platformer', 'Platformer'), ('topdown', 'Topdown')):
                base = root / phase / genre / 'ours'
                results['checks'].append(run([args.dotnet, 'build', str(base / (project + '.csproj')), '-c', 'Release',
                    '--configfile', str(root / 'NuGet.Config'), '-p:UseSharedCompilation=false'], f'{phase}-{genre}-build', root, env))
                app = base / 'bin/Release/net10.0' / (project + '.dll')
                results['checks'].append(run([args.dotnet, str(app), '--test'], f'{phase}-{genre}-native', root, env))
                if genre == 'platformer':
                    results['checks'].append(run([args.dotnet, str(app), '--test', '--iterated'], f'{phase}-{genre}-iterated-native', root, env))
        results['checks'].append(run([args.dotnet, 'run', '--project', str(harness_root / 'HostPolicy.csproj'), '-c', 'Release',
            '-p:UseSharedCompilation=false'], 'host-policy', root, env))
        for log in (root / 'logs').glob('*-build.log'):
            assert not re.search(r'\b(?:warning|error) [A-Z]+\d+', log.read_text()), f'Diagnostics in {log}'
        for log in (root / 'logs').glob('*-native.log'):
            expected = 'NATIVE_INTEGRATION_PASSED 13' if 'platformer' in log.name else 'TOPDOWN OURS PASS checks=14'
            assert expected in log.read_text(), f'Missing contract result in {log}'
        assert 'HOST POLICY PASS checks=47' in (root / 'logs/host-policy.log').read_text()
    assert all(sha(path) == digest for path, digest in originals.items()), 'An original source changed during comparison'
    assert helper.read_bytes() == (harness_root / helper.name).read_bytes(), 'Helper changed during comparison; rerun from new output'
    results['original_sources_unchanged'] = len(originals)
    (root / 'results.json').write_text(json.dumps(results, indent=2) + '\n')
    print(json.dumps(results, indent=2))


if __name__ == '__main__':
    main()
