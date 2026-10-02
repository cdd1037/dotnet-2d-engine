using System.Runtime.CompilerServices;

namespace GameAuthoringLab;

/// <summary>A C# declaration site, not the origin of runtime data. Column is unavailable from caller info.</summary>
public readonly record struct UiDeclaration(string FilePath, int Line)
{
    internal UiAuthoringException Error(string code, string field, string cause, Exception? inner = null) =>
        new(code, string.IsNullOrEmpty(FilePath) ? "<schema>" : FilePath, Line, 1, field, cause, inner) { Declaration = this };
}

/// <summary>Explicit scalar codec used by typed commands; no CLR type discovery or reflection.</summary>
public sealed class UiArgument<T>
{
    internal UiValueKind Kind { get; }
    internal Func<UiCommandArgument, T> Read { get; }
    internal UiArgument(UiValueKind kind, Func<UiCommandArgument, T> read) { Kind = kind; Read = read; }
}

public static class UiArgs
{
    public static UiArgument<string> Text { get; } = new(UiValueKind.Text, static a => a.Text);
    public static UiArgument<bool> Boolean { get; } = new(UiValueKind.Boolean, static a => a.Boolean);
    public static UiArgument<double> Number { get; } = new(UiValueKind.Number, static a => a.Number);
    public static UiArgument<ulong> Key { get; } = new(UiValueKind.Key, static a => a.Key);
}

/// <summary>Frozen by a session. Keep builders short-lived: like any delegate owner, a retained builder retains its handlers.</summary>
public sealed class UiCommands
{
    internal sealed record Command(string Name, uint Id, UiValueKind[] Arguments,
        Action<UiCommandEvent>? Handler = null, UiDeclaration? Origin = null);
    private readonly List<Command> _commands = [];

    /// <summary>Legacy explicit packet registration; use Poll/IsCurrent and application dispatch.</summary>
    public UiCommands Add(string name, uint id, params UiValueKind[] arguments) => Register(name, id, arguments, null, null);

    private UiCommands Register(string name, uint id, UiValueKind[] arguments, Action<UiCommandEvent>? handler, UiDeclaration? origin)
    {
        try
        {
            UiModelContract.Name(name); ArgumentNullException.ThrowIfNull(arguments);
            if (id == 0 || arguments.Length > 4 || arguments.Any(k => k is < UiValueKind.Text or > UiValueKind.Key))
                throw new ArgumentException("Commands require a nonzero ID and up to four scalar argument types.");
            if (_commands.Count == 32 || _commands.Any(c => c.Name == name || c.Id == id))
                throw new ArgumentException("At most 32 unique command names/IDs are supported.");
            _commands.Add(new(name, id, (UiValueKind[])arguments.Clone(), handler, origin));
            return this;
        }
        catch (ArgumentException e) when (origin is { })
        { throw origin.Value.Error("UI_SCHEMA", "commands." + name, e.Message, e); }
    }

    public UiCommands On(string name, uint id, Action handler,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Register(name, id, [], _ => handler(), new(file, line));
    }
    public UiCommands On<A>(string name, uint id, UiArgument<A> a, Action<A> handler,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        ArgumentNullException.ThrowIfNull(a); ArgumentNullException.ThrowIfNull(handler);
        return Register(name, id, [a.Kind], p => handler(a.Read(p[0])), new(file, line));
    }
    public UiCommands On<A, B>(string name, uint id, UiArgument<A> a, UiArgument<B> b, Action<A, B> handler,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        ArgumentNullException.ThrowIfNull(a); ArgumentNullException.ThrowIfNull(b); ArgumentNullException.ThrowIfNull(handler);
        return Register(name, id, [a.Kind, b.Kind], p => handler(a.Read(p[0]), b.Read(p[1])), new(file, line));
    }
    public UiCommands On<A, B, C>(string name, uint id, UiArgument<A> a, UiArgument<B> b, UiArgument<C> c, Action<A, B, C> handler,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        ArgumentNullException.ThrowIfNull(a); ArgumentNullException.ThrowIfNull(b); ArgumentNullException.ThrowIfNull(c); ArgumentNullException.ThrowIfNull(handler);
        return Register(name, id, [a.Kind, b.Kind, c.Kind], p => handler(a.Read(p[0]), b.Read(p[1]), c.Read(p[2])), new(file, line));
    }
    public UiCommands On<A, B, C, D>(string name, uint id, UiArgument<A> a, UiArgument<B> b, UiArgument<C> c, UiArgument<D> d, Action<A, B, C, D> handler,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        ArgumentNullException.ThrowIfNull(a); ArgumentNullException.ThrowIfNull(b); ArgumentNullException.ThrowIfNull(c); ArgumentNullException.ThrowIfNull(d); ArgumentNullException.ThrowIfNull(handler);
        return Register(name, id, [a.Kind, b.Kind, c.Kind, d.Kind], p => handler(a.Read(p[0]), b.Read(p[1]), c.Read(p[2]), d.Read(p[3])), new(file, line));
    }
    internal Command[] Freeze() => _commands.Select(c => c with { Arguments = (UiValueKind[])c.Arguments.Clone() }).ToArray();
}
