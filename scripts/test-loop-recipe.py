#!/usr/bin/env python3
"""Exercise the starter's actual frame statements in an independent package consumer.

Use an already completed test-starter.sh proof (its isolated feed/cache and exact
copied sources). No original fixture is edited; no engine source or native rebuild.
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


def between(source, start, end):
    return textwrap.dedent(source[source.index(start):source.index(end, source.index(start))])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--starter-proof', type=Path, required=True)
    parser.add_argument('--dotnet', required=True)
    args = parser.parse_args()
    proof = args.starter_proof.resolve()
    source = proof / 'app'
    output = proof / 'loop-policy'
    output.mkdir()  # Never overwrite a prior result.
    host = (source / 'Program.cs').read_text()
    policy = between(host, '        if (action.IsPressed(controls.Pause))', '        frames++;')
    bindings = 'private readonly StarterInput controls = new();\nprivate InputActionMap actions => controls.Map;\n'
    for name in ('FixedStepInput.cs', 'StarterGame.cs', 'StarterInput.cs', 'StarterOptions.cs'):
        shutil.copy2(source / name, output / name)
    shutil.copytree(source / 'assets', output / 'assets')
    shutil.copy2(source / 'Starter.csproj', output / 'LoopPolicy.csproj')
    driver = '''
internal sealed class Driver : IDisposable
{
    private readonly EngineHost engine;
    private readonly TextureLease texture;
    private readonly Camera camera = new() { Zoom = 1 };
    private readonly SpriteCommand[] draws = new SpriteCommand[1];
    private readonly FixedStepInput fixedInput = new(StarterGame.StepSeconds);
    private readonly StarterOptions options;
    private readonly StarterGame game;
    public StarterGame Game => game;
    public double Alpha => fixedInput.InterpolationAlpha;
    public Vector2 RenderPosition => game.PresentationPosition(Alpha);
    public double MenuRealSeconds { get; private set; }
    public ulong Draws => engine.GetStats().Frames;
''' + textwrap.indent(bindings, '    ') + '''
    public Driver(bool physics, bool headlessClock = false)
    {
        engine = EngineHost.Create(headless: true, maxSprites: 16);
        texture = engine.Textures.Acquire(new AssetRoot(Path.Combine(AppContext.BaseDirectory, "assets")), "white.png");
        game = new StarterGame(engine, physics, controls);
        options = StarterOptions.Parse(headlessClock ? ["--headless"] : []);
    }

    public TimingStep Frame(double elapsed, SyntheticInput input)
    {
        InputAction[] Tokens(params PhysicalKey?[] keys) => keys.Where(key => key is not null).Select(key => key switch
        {
            PhysicalKey.D => controls.Right, PhysicalKey.Space => controls.Pulse,
            PhysicalKey.Escape => controls.Pause, PhysicalKey.T => controls.Restart,
            _ => throw new ArgumentException("Undeclared synthetic action")
        }).ToArray();
        var action = actions.CreateState(down: Tokens(input.Held), pressed: Tokens(input.Tap, input.Tap2),
            released: Tokens(input.Tap, input.Tap2));
''' + textwrap.indent(policy, '        ') + '''
        // A recording stand-in for outer-frame menu/timer work, not a native UI test.
        MenuRealSeconds += frame.RealSeconds;
        return frame;
    }

    public void Dispose()
    {
        game.Dispose();
        texture.Dispose();
        engine.Dispose();
    }
}
'''
    tests = r'''using System.Numerics;
using Dotnet2DStarter;
using GameAuthoringLab;

[Flags] internal enum WindowFlags { Focused = 1, Drawable = 2 }
internal readonly record struct SyntheticInput(PhysicalKey? Held, PhysicalKey? Tap, PhysicalKey? Tap2, WindowFlags Flags)
{
    public bool Focused => (Flags & WindowFlags.Focused) != 0;
    public bool Drawable => (Flags & WindowFlags.Drawable) != 0;
}

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
        checks++;
    }
    private static void Near(Vector2 a, Vector2 b, string label) => Check(Vector2.Distance(a, b) < .002f, label);
    private static SyntheticInput Input(PhysicalKey? held = null, PhysicalKey? tap = null,
        PhysicalKey? tap2 = null, WindowFlags flags = WindowFlags.Focused | WindowFlags.Drawable)
        => new(held, tap, tap2, flags);
    private static void Main()
    {
        double step = StarterGame.StepSeconds;
        Vector2 delta = new(160 * StarterGame.StepSeconds, 0);
        foreach (bool physics in new[] { false, true })
        {
            using (var driver = new Driver(physics))
            {
                driver.Frame(step / 4, Input(tap: PhysicalKey.Space));
                Check(driver.Game.Steps == 0 && driver.Game.Pulses == 0, "zero-step frame queues a press");
                driver.Frame(step * 2.25, Input(held: PhysicalKey.D));
                Check(driver.Game.Steps == 2 && driver.Game.Pulses == 1, "catch-up consumes queued press once");
                Near(driver.RenderPosition, driver.Game.Position - delta / 2, "final two poses at residual midpoint");
                Vector2 current = driver.Game.Position;
                driver.Frame(step / 4, Input(held: PhysicalKey.D));
                Near(driver.Game.Position, current, "no-step frame does not move simulation");
                Near(driver.RenderPosition, current - delta / 4, "no-step frame interpolates only presentation");
                Check(driver.Draws == 3, "draw once per outer frame, not per physics step");
                double real = driver.MenuRealSeconds;
                TimingStep paused = driver.Frame(.1, Input(tap: PhysicalKey.Escape, tap2: PhysicalKey.Space));
                Check(driver.Game.Paused && driver.Game.Steps == 2 && driver.Game.Pulses == 1, "pause prevents simulation and action");
                Near(driver.RenderPosition, current, "pause snaps to authoritative pose, no rewind");
                Check(paused.GameSeconds == 0 && driver.MenuRealSeconds > real && driver.Draws == 4,
                    "outer-frame menu clock and drawing continue during pause");
                driver.Frame(step / 4, Input(tap: PhysicalKey.Escape));
                Check(!driver.Game.Paused && driver.Game.Steps == 2, "zero-step resume");
                Near(driver.RenderPosition, current, "zero-step resume does not revive pre-pause history");
                driver.Frame(step * 3.75, Input(held: PhysicalKey.D, tap: PhysicalKey.Space));
                Check(driver.Game.Steps == 6 && driver.Game.Pulses == 2, "resumed catch-up consumes only new action");
                Near(driver.RenderPosition, driver.Game.Position - delta, "exact boundary renders previous pose");
                driver.Frame(step / 4, Input(tap: PhysicalKey.Space));
                driver.Frame(.1, Input(tap: PhysicalKey.T, tap2: PhysicalKey.Space));
                Check(driver.Game.Steps == 0 && driver.Game.Pulses == 0, "restart discards debt and simultaneous presses");
                Near(driver.RenderPosition, new(160, 120), "restart snaps both poses");
                driver.Frame(step, Input());
                Check(driver.Game.Steps == 1 && driver.Game.Pulses == 0, "restart leaves no stale press");
                Near(driver.Game.Position, new(160, 120), "restart clears old velocity");
                driver.Frame(step, Input(tap: PhysicalKey.Escape, tap2: PhysicalKey.T));
                Check(driver.Game.Paused && driver.Game.Steps == 0, "simultaneous pause/restart preserves pause choice");
            }
            foreach (WindowFlags flags in new[] { WindowFlags.Focused, WindowFlags.Drawable })
            {
                using var driver = new Driver(physics);
                driver.Frame(step * 1.5, Input(held: PhysicalKey.D, tap: PhysicalKey.Space));
                driver.Frame(step / 4, Input(tap: PhysicalKey.Space));
                Vector2 current = driver.Game.Position;
                ulong draws = driver.Draws;
                driver.Frame(2, Input(tap: PhysicalKey.Space, flags: flags));
                Check(driver.Game.Steps == 1 && driver.Game.Pulses == 1 && driver.Alpha == 0,
                    "focus/no-draw suspension clears pending input and debt");
                Near(driver.RenderPosition, current, "focus/no-draw suspension snaps history");
                Check(driver.Draws == draws + (flags == WindowFlags.Drawable ? 1ul : 0),
                    "only nondrawable frames skip drawing");
                driver.Frame(step / 4, Input());
                Near(driver.RenderPosition, current, "focus/no-draw zero-step resume is stable");
                driver.Frame(step * .75, Input());
                Check(driver.Game.Steps == 2 && driver.Game.Pulses == 1, "resume never replays a stale press");
            }
            using (var driver = new Driver(physics, headlessClock: true))
            {
                driver.Frame(20, Input(flags: 0));
                Check(driver.Game.Steps == 1 && driver.Draws == 1, "explicit headless test clock ignores absent window state");
            }
        }
        Console.WriteLine($"LOOP POLICY PASS checks={checks}; extracted starter frame; plain + native physics; synthetic typed actions/window state, no physical-input/native-UI acceptance");
    }
}
'''
    (output / 'Program.cs').write_text(tests + driver)
    project = output / 'LoopPolicy.csproj'
    project.write_text(project.read_text().replace('<Nullable>enable</Nullable>', '<Nullable>enable</Nullable>\n    <AllowUnsafeBlocks>false</AllowUnsafeBlocks>'))
    env = dict(os.environ, DOTNET_CLI_HOME=str(proof / 'dotnet-home'), NUGET_PACKAGES=str(proof / 'nuget-cache'),
               NUGET_HTTP_CACHE_PATH=str(proof / 'http-cache'), DOTNET_CLI_TELEMETRY_OPTOUT='1')
    for name in ('LD_LIBRARY_PATH', 'LD_PRELOAD', 'LD_AUDIT', 'GAL_ASSET_ROOT'):
        env.pop(name, None)
    commands = [
        [args.dotnet, 'restore', str(project), '--configfile', str(source / 'NuGet.Config')],
        [args.dotnet, 'build', str(project), '-c', 'Release', '--no-restore', '-p:UseSharedCompilation=false'],
        [args.dotnet, str(output / 'bin/Release/net10.0/LoopPolicy.dll')]]
    for name, command in zip(('restore', 'build', 'run'), commands):
        log = proof / 'logs' / ('loop-policy-' + name + '.log')
        with log.open('w') as stream:
            result = subprocess.run(command, cwd=output, env=env, stdout=stream, stderr=subprocess.STDOUT)
        if result.returncode:
            raise RuntimeError(f'{name} failed: {log}')
    report = (proof / 'logs/loop-policy-run.log').read_text()
    print(report, end='')
    match = re.search(r'LOOP POLICY PASS checks=(\d+)', report)
    assert match, 'Missing final host-policy result'
    for name in ('restore', 'build'):
        assert not re.search(r'\b(?:warning|error) [A-Z]+\d+', (proof / 'logs' / ('loop-policy-' + name + '.log')).read_text())
    result = {'copied_host_sha256': hashlib.sha256((source / 'Program.cs').read_bytes()).hexdigest(),
              'extracted_policy_sha256': hashlib.sha256(policy.encode()).hexdigest(), 'checks': int(match[1]),
              'physical_input_or_native_ui_acceptance': False}
    (proof / 'loop-policy-results.json').write_text(json.dumps(result, indent=2) + '\n')


if __name__ == '__main__':
    main()
