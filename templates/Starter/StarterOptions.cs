namespace Dotnet2DStarter;

internal sealed record StarterOptions(bool Headless, bool Physics, bool SelfTest, bool Help, int Frames, string? Assets)
{
    public const string Usage = "dotnet run -- [--headless] [--physics] [--frames N] [--assets PATH] [--self-test] [--help]\nHeadless defaults to 120 deterministic frames; desktop runs until close. No font is needed.";
    public static StarterOptions Parse(string[] arguments)
    {
        bool headless = false, physics = false, selfTest = false, help = false;
        int? frames = null;
        string? assets = null;
        for (int i = 0; i < arguments.Length; i++)
        {
            switch (arguments[i])
            {
                case "--headless": headless = true; break;
                case "--physics": physics = true; break;
                case "--self-test": selfTest = true; break;
                case "--help": help = true; break;
                case "--frames":
                    if (++i == arguments.Length || !int.TryParse(arguments[i], out int count) || count < 1)
                        throw new ArgumentException("--frames requires a positive integer.");
                    frames = count; break;
                case "--assets":
                    if (++i == arguments.Length || string.IsNullOrWhiteSpace(arguments[i]))
                        throw new ArgumentException("--assets requires a directory.");
                    assets = arguments[i]; break;
                default: throw new ArgumentException($"Unknown argument '{arguments[i]}'. {Usage}");
            }
        }
        return new(headless, physics, selfTest, help, frames ?? (headless ? 120 : 0), assets);
    }
}
