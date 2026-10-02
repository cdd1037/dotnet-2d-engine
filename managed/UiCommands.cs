using System.Runtime.CompilerServices;

namespace GameAuthoringLab;

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
    internal UiCommands Add(string name, uint id, params UiValueKind[] arguments) => Register(name, id, arguments, null, null);

    private UiCommands Register(string name, uint id, UiValueKind[] arguments, Action<UiCommandEvent>? handler, UiDeclaration? origin, bool automatic = false)
    {
        try
        {
            UiModelContract.Name(name); ArgumentNullException.ThrowIfNull(arguments);
            if (id == 0 && !automatic || arguments.Length > 4 || arguments.Any(k => k is < UiValueKind.Text or > UiValueKind.Key))
                throw new ArgumentException("Commands require a nonzero ID and up to four scalar argument types.");
            if (_commands.Count == 32 || _commands.Any(c => c.Name == name || id != 0 && c.Id == id))
                throw new ArgumentException("At most 32 unique command names/IDs are supported.");
            _commands.Add(new(name, id, (UiValueKind[])arguments.Clone(), handler, origin));
            return this;
        }
        catch (ArgumentException e) when (origin is { })
        { throw origin.Value.Error("UI_SCHEMA", "commands." + name, e.Message, e); }
    }

    /// <summary>Register a typed handler without allocating a wire ID. IDs are assigned when a session freezes this builder.
    /// Automatic IDs are local to that frozen contract; do not persist them or use them as application action identifiers.</summary>
    public UiCommands On(string name, Action handler,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Register(name, 0, [], _ => handler(), new(file, line), automatic: true);
    }
    public UiCommands On<A>(string name, UiArgument<A> a, Action<A> handler,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        ArgumentNullException.ThrowIfNull(a); ArgumentNullException.ThrowIfNull(handler);
        return Register(name, 0, [a.Kind], p => handler(a.Read(p[0])), new(file, line), automatic: true);
    }
    public UiCommands On<A, B>(string name, UiArgument<A> a, UiArgument<B> b, Action<A, B> handler,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        ArgumentNullException.ThrowIfNull(a); ArgumentNullException.ThrowIfNull(b); ArgumentNullException.ThrowIfNull(handler);
        return Register(name, 0, [a.Kind, b.Kind], p => handler(a.Read(p[0]), b.Read(p[1])), new(file, line), automatic: true);
    }
    public UiCommands On<A, B, C>(string name, UiArgument<A> a, UiArgument<B> b, UiArgument<C> c, Action<A, B, C> handler,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        ArgumentNullException.ThrowIfNull(a); ArgumentNullException.ThrowIfNull(b); ArgumentNullException.ThrowIfNull(c); ArgumentNullException.ThrowIfNull(handler);
        return Register(name, 0, [a.Kind, b.Kind, c.Kind], p => handler(a.Read(p[0]), b.Read(p[1]), c.Read(p[2])), new(file, line), automatic: true);
    }
    public UiCommands On<A, B, C, D>(string name, UiArgument<A> a, UiArgument<B> b, UiArgument<C> c, UiArgument<D> d, Action<A, B, C, D> handler,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        ArgumentNullException.ThrowIfNull(a); ArgumentNullException.ThrowIfNull(b); ArgumentNullException.ThrowIfNull(c); ArgumentNullException.ThrowIfNull(d); ArgumentNullException.ThrowIfNull(handler);
        return Register(name, 0, [a.Kind, b.Kind, c.Kind, d.Kind], p => handler(a.Read(p[0]), b.Read(p[1]), c.Read(p[2]), d.Read(p[3])), new(file, line), automatic: true);
    }

    internal UiCommands On(string name, uint id, Action handler,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Register(name, id, [], _ => handler(), new(file, line));
    }
    internal UiCommands On<A>(string name, uint id, UiArgument<A> a, Action<A> handler,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        ArgumentNullException.ThrowIfNull(a); ArgumentNullException.ThrowIfNull(handler);
        return Register(name, id, [a.Kind], p => handler(a.Read(p[0])), new(file, line));
    }
    internal UiCommands On<A, B>(string name, uint id, UiArgument<A> a, UiArgument<B> b, Action<A, B> handler,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        ArgumentNullException.ThrowIfNull(a); ArgumentNullException.ThrowIfNull(b); ArgumentNullException.ThrowIfNull(handler);
        return Register(name, id, [a.Kind, b.Kind], p => handler(a.Read(p[0]), b.Read(p[1])), new(file, line));
    }
    internal UiCommands On<A, B, C>(string name, uint id, UiArgument<A> a, UiArgument<B> b, UiArgument<C> c, Action<A, B, C> handler,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        ArgumentNullException.ThrowIfNull(a); ArgumentNullException.ThrowIfNull(b); ArgumentNullException.ThrowIfNull(c); ArgumentNullException.ThrowIfNull(handler);
        return Register(name, id, [a.Kind, b.Kind, c.Kind], p => handler(a.Read(p[0]), b.Read(p[1]), c.Read(p[2])), new(file, line));
    }
    internal UiCommands On<A, B, C, D>(string name, uint id, UiArgument<A> a, UiArgument<B> b, UiArgument<C> c, UiArgument<D> d, Action<A, B, C, D> handler,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        ArgumentNullException.ThrowIfNull(a); ArgumentNullException.ThrowIfNull(b); ArgumentNullException.ThrowIfNull(c); ArgumentNullException.ThrowIfNull(d); ArgumentNullException.ThrowIfNull(handler);
        return Register(name, id, [a.Kind, b.Kind, c.Kind, d.Kind], p => handler(a.Read(p[0]), b.Read(p[1]), c.Read(p[2]), d.Read(p[3])), new(file, line));
    }
    internal Command[] Freeze()
    {
        // Reserve all explicit IDs first, including declarations added after automatic ones.
        // Sorting names makes allocation independent of registration order, without promising
        // protocol stability when the contract itself changes. Never mutate the retained builder.
        var assigned = new Dictionary<string, uint>(StringComparer.Ordinal);
        var used = _commands.Where(c => c.Id != 0).Select(c => c.Id).ToHashSet();
        uint next = 1;
        foreach (var command in _commands.Where(c => c.Id == 0).OrderBy(c => c.Name, StringComparer.Ordinal))
        {
            while (used.Contains(next)) next++;
            assigned.Add(command.Name, next); used.Add(next++);
        }
        return _commands.Select(c => c with
        {
            Id = c.Id == 0 ? assigned[c.Name] : c.Id,
            Arguments = (UiValueKind[])c.Arguments.Clone()
        }).ToArray();
    }
}
