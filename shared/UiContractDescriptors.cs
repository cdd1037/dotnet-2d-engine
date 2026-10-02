using System;

namespace GameAuthoringLab;

// These descriptors are source-shared by the runtime and the build-time generator. They do
// not depend on the engine, native ABI, application delegates, or runtime model values.
internal sealed class UiContractDeclaration
{
    internal UiContractDeclaration(string filePath, int line) { FilePath = filePath; Line = line; }
    internal string FilePath { get; }
    internal int Line { get; }
}

internal sealed class UiContractNode
{
    // Kinds: 1 Text, 2 Boolean, 3 Number, 4 Key, 5 Record, 6 Array. Roots have parent -1.
    internal UiContractNode(string name, uint kind, int parent, uint arrayMaximum = 0,
        UiContractDeclaration? declaration = null)
    {
        Name = name; Kind = kind; Parent = parent; ArrayMaximum = arrayMaximum; Declaration = declaration;
    }
    internal string Name { get; }
    internal uint Kind { get; }
    internal int Parent { get; }
    internal uint ArrayMaximum { get; }
    internal UiContractDeclaration? Declaration { get; }
}

internal sealed class UiContractCommand
{
    internal UiContractCommand(string name, uint[] arguments, bool typedProvenance,
        UiContractDeclaration? declaration = null)
    {
        Name = name; Arguments = arguments; TypedProvenance = typedProvenance; Declaration = declaration;
    }
    internal string Name { get; }
    internal uint[] Arguments { get; }
    internal bool TypedProvenance { get; }
    internal UiContractDeclaration? Declaration { get; }
}

/// <summary>A use-site diagnostic independent of the runtime and of Roslyn.</summary>
internal sealed class UiContractException : Exception
{
    internal UiContractException(string code, string filePath, int line, int column, string field, string cause,
        UiContractDeclaration? declaration = null, Exception? inner = null)
        : base($"{filePath}:{Math.Max(1, line)}:{Math.Max(1, column)} [{code}] {field}: {cause}", inner)
    {
        Code = code; FilePath = filePath; Line = Math.Max(1, line); Column = Math.Max(1, column);
        Field = field; Cause = cause; Declaration = declaration;
    }
    internal string Code { get; }
    internal string FilePath { get; }
    internal int Line { get; }
    internal int Column { get; }
    internal string Field { get; }
    internal string Cause { get; }
    internal UiContractDeclaration? Declaration { get; }
}
