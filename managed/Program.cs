using System.Diagnostics;
using System.Globalization;

namespace GameAuthoringLab;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (!Options.TryParse(args, out var options, out string error))
            {
                Console.Error.WriteLine(error);
                PrintUsage();
                return 2;
            }
            if (options.Help)
            {
                PrintUsage();
                return 0;
            }
            if(options.ResourceSelfTest){ResourceTests.Run();return 0;}
            if(options.ResourceGraphics){ResourceTests.Run(true);return 0;}
            if(options.GameSelfTest){MissionTests.Run();GameUiTests.RunAuthoring();return 0;}
            if(args.Contains("--game-ui-self-test",StringComparer.Ordinal)){GameUiTests.RunNative();return 0;}
            if(options.GameDemo){if(options.Headless)throw new ArgumentException("--game-demo/--game-scenario require the UI graphics build; use --game-self-test for CPU checks.");return MissionHost.Run(options.GameScenario,options.Frames,options.SavePath=="two-room-save.json"?(options.GameScenario?"relay-scenario-save.json":"relay-save.json"):options.SavePath);}
            if(options.SceneValidatePath is {} scenePath){var authored=AuthoredScene.LoadFile(scenePath);Console.WriteLine($"VALID AUTHORED SCENE {scenePath} entities={authored.World.EntityCount}");return 0;}
            if(options.AuthoredPath is {} authoredPath)return AuthoredSceneDemo.Run(authoredPath,options.Headless,options.Frames);
            if(options.UiValidatePath is {} uiPath){UiAuthoring.ValidateFiles(uiPath);Console.WriteLine($"VALID UI {uiPath}");return 0;}
            if(options.RoomUi)return RoomUiProbe.Run(options.RoomUiScenario,options.Frames);
            if(options.UiDemo)return UiProbe.Run(options.UiScenario,options.Frames);
            if(options.ValidatePath is {} save)
            {
                RoomGame validated=RoomGame.LoadFile(save,new AssetCatalog());
                Console.WriteLine($"VALID {save} room={validated.RoomIndex+1} entities={validated.World.EntityCount} player={validated.Player.PersistentId} item={validated.Item.PersistentId}");
                return 0;
            }
            return options.SelfTest ? SelfTests.Run() : options.Scenario ? RunRoomScenario(options) : options.RoomDemo ? RunRoomDemo(options) : RunDemo(options);
        }
        catch (DllNotFoundException exception)
        {
            Console.Error.WriteLine("Cannot load the native gal library. Put gal.dll/libgal.so beside the executable, or set the platform library search path.");
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"ERROR: {exception.Message}");
            return 1;
        }
    }

    private static int RunDemo(Options options)
    {
        using var engine = new EngineHost(options.Headless, DemoWorld.SpriteCount);
        Console.WriteLine($"Game Authoring Lab | ABI 1 | backend={engine.Backend}");
        Console.WriteLine("Arrow keys: pan | Mouse wheel: zoom | Space: tone | Escape: quit");
        if (options.Headless)
            Console.WriteLine("Headless CPU validation only: no window, rendering, or audio.");

        // Ordinary C# objects own gameplay. One reusable extraction batch crosses
        // the ABI, not one interop call per entity or per property.
        World world = DemoWorld.Create();
        var batch = new SpriteBatch(DemoWorld.SpriteCount);
        var camera = new Camera { X = -480, Y = -270, Zoom = 1 };
        var clock = Stopwatch.StartNew();
        double previous = clock.Elapsed.TotalSeconds;
        uint previousKeys = 0;
        int frames = 0;
        while (options.Frames == 0 || frames < options.Frames)
        {
            double now = clock.Elapsed.TotalSeconds;
            float dt = options.Headless ? 1f / 60 : (float)Math.Clamp(now - previous, 0, 0.1);
            previous = now;
            var input = engine.Poll();
            if (input.Quit != 0 || (input.Keys & Native.Escape) != 0)
                break;

            MoveCamera(ref camera, input, dt);
            // A held Space key plays once; release/press retriggers it.
            if (Pressed(input.Keys, previousKeys, Native.Space)
                && !engine.TryPlayTone(out string error))
                Console.Error.WriteLine($"Tone unavailable: {error}");
            previousKeys = input.Keys;

            world.Update(dt);
            world.ExtractSprites(batch);
            engine.Draw(camera, batch.Sprites);
            frames++;
            // Presentation usually blocks for vsync; this also prevents a hot
            // event loop on backends/configurations without it.
            if (!options.Headless)
                Thread.Sleep(1);
        }

        Stats stats = engine.GetStats();
        Console.WriteLine($"DONE frames={stats.Frames} sprites={stats.Sprites} draw_calls={stats.DrawCalls} audio_plays={stats.AudioPlays}");
        if (options.Headless && (stats.DrawCalls != 0 || stats.AudioPlays != 0))
            throw new InvalidOperationException("Headless mode reported rendering or audio work.");
        return 0;
    }

    private static int RunRoomDemo(Options options)
    {
        var catalog=new AssetCatalog();RoomGame game=options.LoadPath is {} path?RoomGame.LoadFile(path,catalog):new RoomGame();
        using var engine=new EngineHost(options.Headless,4096);using var bank=new TextureBank(engine,catalog);
        var batch=new SpriteBatch(64){TextureResolver=bank.Resolve};var camera=new Camera{Zoom=1};
        var clock=Stopwatch.StartNew();double previous=clock.Elapsed.TotalSeconds;uint previousKeys=0;int frames=0;
        Console.WriteLine($"TWO ROOM | backend={engine.Backend} | room={game.RoomIndex+1} | WASD/arrows move, E pickup, F drop, T door, F5 save, F9 load");
        while(options.Frames==0||frames<options.Frames)
        {
            double now=clock.Elapsed.TotalSeconds;float dt=options.Headless?RoomGame.FixedDelta:(float)Math.Clamp(now-previous,0,.25);previous=now;
            Input input=engine.Poll();if(input.Quit!=0||(input.Keys&Native.Escape)!=0)break;
            try
            {
                if(Pressed(input.Keys,previousKeys,Native.Save)){game.SaveFile(options.SavePath);Console.WriteLine("Saved "+options.SavePath);}
                if(Pressed(input.Keys,previousKeys,Native.Load)){RoomGame candidate=RoomGame.LoadFile(options.SavePath,catalog);bank.Sync(candidate.World);game=candidate;Console.WriteLine("Loaded "+options.SavePath);}
            }
            catch(Exception exception){Console.Error.WriteLine("Save/load rejected: "+exception.Message);}
            int pickups=game.PickupCount;game.Advance(input.Keys,dt);
            if(game.PickupCount>pickups&&!options.Headless)engine.TryPlayTone(out _);
            if(input.Wheel!=0){var wheel=input;wheel.Keys=0;MoveCamera(ref camera,wheel,0);}
            previousKeys=input.Keys;bank.Sync(game.World);game.World.ExtractSprites(batch);engine.Draw(camera,batch.Draws);frames++;
            if(!options.Headless)Thread.Sleep(1);
        }
        Stats stats=engine.GetStats();bank.Dispose();if(engine.TextureCount!=0)throw new InvalidOperationException("Texture cleanup failed.");
        Console.WriteLine($"ROOM DONE frames={stats.Frames} sprites={stats.Sprites} draw_calls={stats.DrawCalls} room={game.RoomIndex+1} textures_live=0");return 0;
    }

    private static int RunRoomScenario(Options options)
    {
        var catalog=new AssetCatalog();using var engine=new EngineHost(options.Headless,4096);using var bank=new TextureBank(engine,catalog);
        var batch=new SpriteBatch(64){TextureResolver=bank.Resolve};var camera=new Camera{Zoom=1};
        void Render(RoomGame game){bank.Sync(game.World);game.World.ExtractSprites(batch);engine.Draw(camera,batch.Draws);if(!options.Headless&&engine.TextureCount!=bank.LoadedCount)throw new InvalidOperationException("Native texture registry mismatch.");}
        RoomGame restored=RoomGameTests.Exercise(new RoomGame(),catalog,Render);restored.SaveFile(options.SavePath);
        RoomGame fresh=RoomGame.LoadFile(options.SavePath,catalog);Render(fresh);
        Stats stats=engine.GetStats();int loads=bank.Loads,releases=bank.Releases;bank.Dispose();if(engine.TextureCount!=0)throw new InvalidOperationException("Resource cleanup failed.");
        Console.WriteLine($"SCENARIO PASS move/collision/pickup/transition/drop/save/reload frames={stats.Frames} sprites={stats.Sprites} draw_calls={stats.DrawCalls} texture_loads={loads} room_releases={releases} textures_live=0");
        Console.WriteLine($"SAVE {options.SavePath} persistent_player={fresh.Player.PersistentId} persistent_item={fresh.Item.PersistentId}");return 0;
    }

    internal static bool Pressed(uint current, uint previous, uint key) => (current & key) != 0 && (previous & key) == 0;

    internal static void MoveCamera(ref Camera camera, Input input, float dt)
    {
        float speed = 260 * dt / camera.Zoom;
        camera.X += (((input.Keys & Native.Right) != 0 ? 1 : 0) - ((input.Keys & Native.Left) != 0 ? 1 : 0)) * speed;
        camera.Y += (((input.Keys & Native.Down) != 0 ? 1 : 0) - ((input.Keys & Native.Up) != 0 ? 1 : 0)) * speed;
        float zoom = Math.Clamp(camera.Zoom * MathF.Pow(1.12f, Math.Clamp(input.Wheel, -40, 40)), 0.15f, 8);
        // Keep the viewport center fixed while changing scale.
        camera.X += input.Width * 0.5f * (1 / camera.Zoom - 1 / zoom);
        camera.Y += input.Height * 0.5f * (1 / camera.Zoom - 1 / zoom);
        camera.Zoom = zoom;
    }

    internal static void Animate(Span<Sprite> sprites, float time)
    {
        // Three overlapping translucent panels make alpha order easy to inspect.
        sprites[0] = new Sprite { X = -175, Y = -90, Width = 220, Height = 180, R = 0.95f, G = 0.16f, B = 0.28f, A = 0.62f };
        sprites[1] = new Sprite { X = -90, Y = -35, Width = 220, Height = 180, R = 0.12f, G = 0.80f, B = 0.67f, A = 0.62f };
        sprites[2] = new Sprite { X = 0, Y = -90, Width = 220, Height = 180, R = 0.24f, G = 0.39f, B = 1, A = 0.62f };
        for (int i = 3; i < sprites.Length; i++)
        {
            int n = i - 3;
            float phase = time * 1.2f + n * 0.19f;
            sprites[i] = new Sprite
            {
                X = (n % 16 - 7.5f) * 46 + MathF.Sin(phase) * 12,
                Y = (n / 16 - 7.5f) * 25 + MathF.Cos(phase) * 12,
                Width = 12 + 5 * (1 + MathF.Sin(phase)), Height = 16,
                R = 0.3f + 0.7f * ((n % 5) / 4f), G = 0.4f + 0.6f * ((n % 7) / 6f),
                B = 0.9f, A = 0.35f + 0.6f * (0.5f + 0.5f * MathF.Sin(phase))
            };
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: GameAuthoringLab [--room-demo | --scenario] [--headless] [--frames N] [--save-file PATH] [--load-file PATH] | --validate-save PATH | --self-test | --help");
        Console.WriteLine("Resources: --resource-self-test (CPU only) | --resource-graphics-test (real texture upload/release)");
        Console.WriteLine("Playable mission: --game-demo | --game-scenario [--save-file PATH] | --game-self-test (CPU only)");
        Console.WriteLine("Authored scenes: --validate-scene PATH | --authored-demo PATH [--headless] [--frames N]");
        Console.WriteLine("Optional UI: --room-ui-demo | --room-ui-scenario | --ui-demo | --ui-scenario | --validate-ui PATH (requires experimental native UI build for rendering)");
        Console.WriteLine("--frames N must be a positive integer. Headless defaults to 120 frames; a window runs until closed unless bounded.");
    }

    internal readonly record struct Options(bool Headless, int Frames, bool SelfTest, bool Help, bool RoomDemo=false, bool Scenario=false, string SavePath="two-room-save.json", string? LoadPath=null, string? ValidatePath=null,bool UiDemo=false,bool UiScenario=false,string? UiValidatePath=null,bool RoomUi=false,bool RoomUiScenario=false,string? SceneValidatePath=null,string? AuthoredPath=null,bool GameDemo=false,bool GameScenario=false,bool GameSelfTest=false,bool ResourceSelfTest=false,bool ResourceGraphics=false)
    {
        public static bool TryParse(string[] args, out Options options, out string error)
        {
            bool headless = false, selfTest = false, help = false, roomDemo=false, scenario=false,uiDemo=false,uiScenario=false;string? uiValidate=null;bool roomUi=false,roomUiScenario=false;
            string savePath="two-room-save.json";string? loadPath=null;string? validatePath=null;
            int frames = 0;string? sceneValidate=null,authoredPath=null;bool gameDemo=false,gameScenario=false,gameSelfTest=false,resourceSelfTest=false,resourceGraphics=false;
            error = "";
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--validate-scene":if(++i==args.Length||args[i].StartsWith("--",StringComparison.Ordinal))error="--validate-scene requires a path.";else sceneValidate=args[i];break;
                    case "--authored-demo":if(++i==args.Length||args[i].StartsWith("--",StringComparison.Ordinal))error="--authored-demo requires a path.";else authoredPath=args[i];break;
                    case "--room-ui-demo":roomUi=true;break;
                    case "--room-ui-scenario":roomUi=true;roomUiScenario=true;break;
                    case "--ui-demo":uiDemo=true;break;
                    case "--ui-scenario":uiDemo=true;uiScenario=true;break;
                    case "--validate-ui":if(++i==args.Length||args[i].StartsWith("--",StringComparison.Ordinal))error="--validate-ui requires a path.";else uiValidate=args[i];break;
                    case "--resource-self-test": resourceSelfTest=true;break;
                    case "--resource-graphics-test": resourceGraphics=true;break;
                    case "--game-ui-self-test": break;
                    case "--game-demo": gameDemo=true;break;
                    case "--game-scenario": gameDemo=true;gameScenario=true;break;
                    case "--game-self-test": gameSelfTest=true;break;
                    case "--headless": headless = true; break;
                    case "--room-demo": roomDemo=true;break;
                    case "--scenario": scenario=true;roomDemo=true;break;
                    case "--validate-save": if(++i==args.Length||args[i].StartsWith("--",StringComparison.Ordinal))error="--validate-save requires a path.";else validatePath=args[i];break;
                    case "--save-file": if(++i==args.Length||args[i].StartsWith("--",StringComparison.Ordinal))error="--save-file requires a path.";else savePath=args[i];break;
                    case "--load-file": if(++i==args.Length||args[i].StartsWith("--",StringComparison.Ordinal))error="--load-file requires a path.";else {loadPath=args[i];roomDemo=true;}break;
                    case "--self-test": selfTest = true; break;
                    case "--help": case "-h": help = true; break;
                    case "--frames":
                        if (++i == args.Length || !int.TryParse(args[i], NumberStyles.None, CultureInfo.InvariantCulture, out frames) || frames <= 0)
                            error = "--frames requires a positive integer.";
                        break;
                    default: error = $"Unknown argument: {args[i]}"; break;
                }
                if (error.Length != 0)
                {
                    options = default;
                    return false;
                }
            }
            options = new Options(headless, frames != 0 ? frames : headless ? 120 : 0, selfTest, help,roomDemo,scenario,savePath,loadPath,validatePath,uiDemo,uiScenario,uiValidate,roomUi,roomUiScenario,sceneValidate,authoredPath,gameDemo,gameScenario,gameSelfTest,resourceSelfTest,resourceGraphics);
            return true;
        }
    }
}
