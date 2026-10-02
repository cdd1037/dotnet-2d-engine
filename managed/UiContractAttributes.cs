namespace GameAuthoringLab;

/// <summary>Generate a UI schema and typed command registrations from named C# declarations.
/// DocumentPath is a project-relative RML path also included as an MSBuild AdditionalFile.
/// Generated contracts do not load assets, publish snapshots or own the application loop.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class UiContractAttribute(Type modelType, string documentPath) : Attribute
{
    public Type ModelType { get; } = modelType;
    public string DocumentPath { get; } = documentPath;
}

/// <summary>Opt a DTO into generated projection of its public readable instance properties/fields.
/// Member names remain exact and case-sensitive. Handwritten UiRecord projections remain available.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class UiModelAttribute : Attribute;

/// <summary>Expose a synchronous void method as a typed UI command. Supported parameters are
/// string/Text, bool/Boolean, double/Number and ulong/Key. No reflection or runtime discovery occurs.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class UiCommandAttribute : Attribute
{
    /// <summary>Optional exact alias for an existing RML contract; defaults to the method's name.</summary>
    public string? Name { get; set; }
    /// <summary>Advanced explicit packet ID. Zero (default) selects automatic session-local assignment.</summary>
    public uint Id { get; set; }
}

/// <summary>Optional generated DTO field settings. Scalar kinds come from the declared CLR type.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = false)]
public sealed class UiFieldAttribute : Attribute
{
    /// <summary>Optional exact alias; defaults to the declared member name without case conversion.</summary>
    public string? Name { get; set; }
    /// <summary>Maximum array/list length, between 1 and 64. Only set this on collection members.</summary>
    public int Maximum { get; set; } = 64;
}
