using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Dotnet2D.Ui.Generator;

internal sealed class ContractBuilder
{
    internal const string ContractAttribute = "GameAuthoringLab.UiContractAttribute";
    private const string ModelAttribute = "GameAuthoringLab.UiModelAttribute";
    private const string CommandAttribute = "GameAuthoringLab.UiCommandAttribute";
    private const string FieldAttribute = "GameAuthoringLab.UiFieldAttribute";
    private const string Runtime = "global::GameAuthoringLab.";
    private readonly List<Issue> issues = new();
    private bool schemaLimitReported;
    private readonly List<NodeDescriptor> nodes = new();
    private readonly List<CommandDescriptor> commands = new();
    private readonly HashSet<ITypeSymbol> visiting = new(SymbolEqualityComparer.Default);
    private readonly CancellationToken cancellation;
    private readonly Compilation compilation;
    private readonly INamedTypeSymbol controller;

    private ContractBuilder(GeneratorAttributeSyntaxContext context, CancellationToken cancellation)
    { this.cancellation = cancellation; compilation = context.SemanticModel.Compilation; controller = (INamedTypeSymbol)context.TargetSymbol; }

    internal static ContractDescriptor Create(GeneratorAttributeSyntaxContext context, CancellationToken cancellation) => new ContractBuilder(context, cancellation).Build(context.Attributes[0]);

    private ContractDescriptor Build(AttributeData attribute)
    {
        SourceSite site = Site(controller);
        string identity = controller.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string document = attribute.ConstructorArguments.Length == 2 ? attribute.ConstructorArguments[1].Value as string ?? "" : "";
        var model = attribute.ConstructorArguments.Length > 0 ? attribute.ConstructorArguments[0].Value as INamedTypeSymbol : null;
        var nesting = new Stack<INamedTypeSymbol>();
        for (var type = controller; type is not null; type = type.ContainingType)
        {
            nesting.Push(type);
            if (type.TypeKind != TypeKind.Class || type.IsRecord || type.Arity != 0 || type.IsFileLocal || !IsPartial(type))
                Error("DUI001", type, "UI controllers and their containing types must be non-generic, non-file-local partial classes. Move the contract to such a class or make every declaration partial.");
        }
        if (controller.IsStatic) Error("DUI001", controller, "A UI controller must be an instance class so generated handlers can call its command methods.");
        if (controller.GetMembers("CreateUiCommands").Length != 0 || controller.GetMembers("CreateUiSchema").Length != 0)
            Error("DUI001", controller, "The UI contract generates CreateUiCommands() and CreateUiSchema(); rename the existing members with those names.");
        if (model is null) Error("DUI003", controller, "UiContract must specify a concrete [UiModel] class or struct using typeof(Model).");
        if (string.IsNullOrWhiteSpace(document)) Error("DUI004", controller, "UiContract must name its RML document using a nonempty project-relative path, and that file must be an AdditionalFiles item.");

        var registrations = new List<string>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<uint>();
        foreach (IMethodSymbol method in controller.GetMembers().OfType<IMethodSymbol>().OrderBy(m => m.Name, StringComparer.Ordinal).ThenBy(m => m.ToDisplayString(), StringComparer.Ordinal))
        {
            cancellation.ThrowIfCancellationRequested();
            AttributeData? command = Attribute(method, CommandAttribute);
            if (command is null) continue;
            int before = issues.Count;
            string name = NamedString(command, "Name") ?? method.Name;
            uint id = NamedUInt(command, "Id");
            if (!ValidName(name)) Error("DUI002", method, "UI command name '" + name + "' must match [A-Za-z][A-Za-z0-9_]{0,46}. Use [UiCommand(Name = \"...\")] to provide an explicit migration alias.");
            if (ReservedCommandName(name)) Error("DUI002", method, "UI command name '" + name + "' is reserved by RmlUi (case-insensitive). Rename the method or supply a nonreserved UiCommand.Name alias.");
            if (!names.Add(name)) Error("DUI002", method, "UI command name '" + name + "' is duplicated; command overloads need distinct explicit names.");
            if (id != 0 && !ids.Add(id)) Error("DUI002", method, "UI command ID " + id + " is duplicated. Omit Id for automatic IDs or choose a unique explicit ID.");
            if (method.MethodKind != MethodKind.Ordinary || method.IsStatic || method.IsAbstract || method.IsExtern || method.IsAsync || !method.ReturnsVoid || method.IsGenericMethod || method.Parameters.Length > 4 || method.ExplicitInterfaceImplementations.Length != 0 || method.PartialDefinitionPart is not null || (method.IsPartialDefinition && method.PartialImplementationPart is null))
                Error("DUI002", method, "A [UiCommand] must be an implemented, ordinary instance synchronous void method with zero to four string, bool, double, or ulong parameters; generic, async, static, abstract, extern, and explicit-interface methods are unsupported.");
            var codecs = new List<string>(); var kinds = new List<uint>();
            foreach (var parameter in method.Parameters)
            {
                uint kind = CommandKind(parameter.Type);
                if (parameter.RefKind != RefKind.None || parameter.IsParams || kind == 0)
                    Error("DUI002", parameter, "UI command parameter '" + parameter.Name + "' must be a by-value string, bool, double, or ulong. Use an ordinary scalar parameter and perform conversions inside the method.");
                codecs.Add(Runtime + "UiArgs." + KindName(kind)); kinds.Add(kind);
            }
            if (issues.Count != before) continue;
            SourceSite origin = Site(method);
            commands.Add(new CommandDescriptor(name, kinds.ToArray(), origin));
            string arguments = Literal(name) + (id == 0 ? "" : ", " + id + "u");
            if (codecs.Count != 0) arguments += ", " + string.Join(", ", codecs);
            // A direct method group preserves ordinary overload/type checking and instance
            // ownership. There is no reflection, delegate-body inspection, or runtime lookup.
            arguments += ", this.@" + method.Name + ", file: " + Literal(origin.File) + ", line: " + origin.Line;
            registrations.Add("            .On(" + arguments + ")");
        }
        if (commands.Count > 32) Error("DUI002", controller, "A UI contract supports at most 32 commands. Split this controller into smaller contracts.");

        string schema = model is null ? "" : BuildRecord(model, "state", -1, 1, site);
        string source = "";
        if (issues.Count == 0)
        {
            var code = new StringBuilder("// <auto-generated/>\n#nullable enable\n");
            if (!controller.ContainingNamespace.IsGlobalNamespace) code.Append("namespace ").Append(controller.ContainingNamespace.ToDisplayString()).Append("\n{\n");
            foreach (var type in nesting) code.Append(type.IsStatic ? "static partial class @" : "partial class @").Append(type.Name).Append("\n{\n");
            code.Append("    private ").Append(Runtime).Append("UiCommands CreateUiCommands() =>\n        new ").Append(Runtime).Append("UiCommands()\n");
            foreach (string registration in registrations) code.Append(registration).Append('\n');
            code.Append("        ;\n\n    private static ").Append(Runtime).Append("UiRecord<").Append(TypeName(model!)).Append("> CreateUiSchema() =>\n        ").Append(schema).Append(";\n");
            foreach (var _ in nesting) code.Append("}\n");
            if (!controller.ContainingNamespace.IsGlobalNamespace) code.Append("}\n");
            source = code.ToString();
        }
        return new ContractDescriptor(identity, source, document, site, nodes.ToArray(), commands.ToArray(), issues.ToArray());
    }

    private string BuildRecord(INamedTypeSymbol model, string name, int parent, int depth, SourceSite origin)
    {
        cancellation.ThrowIfCancellationRequested();
        if (nodes.Count >= 128) { SchemaLimit(origin); return ""; }
        if (depth > 16) { Error("DUI003", model, "The UI model exceeds depth 16 (including array elements). Reduce the nesting.", origin); return ""; }
        if (model.TypeKind != TypeKind.Class && model.TypeKind != TypeKind.Struct || model.IsRefLikeType || model.IsUnboundGenericType || model.ContainsTypeParameters())
        { Error("DUI003", model, "UI records must be concrete, non-ref-like [UiModel] classes or structs.", origin); return ""; }
        if (Attribute(model, ModelAttribute) is null)
        { Error("DUI003", model, "Model type '" + model.ToDisplayString() + "' is not opted in. Add [UiModel] or expose a supported scalar type.", origin); return ""; }
        if (!compilation.IsSymbolAccessibleWithin(model, controller))
        { Error("DUI003", model, "Model type '" + model.ToDisplayString() + "' is inaccessible to the UI controller.", origin); return ""; }
        if (!visiting.Add(model))
        { Error("DUI003", model, "UI model cycle reaches '" + model.ToDisplayString() + "'. Project a finite acyclic view model instead of recursive object references.", origin); return ""; }
        int node = AddNode(name, 5, parent, 0, origin);
        var expression = new StringBuilder("new " + Runtime + "UiRecord<" + TypeName(model) + ">()");
        var exposed = new Dictionary<string, ISymbol>(StringComparer.Ordinal);
        var fields = new List<ISymbol>();
        // Flatten simple inherited public members. Reject hiding instead of guessing whether
        // a getter intends the base or derived contract name.
        var ancestry = new Stack<INamedTypeSymbol>();
        for (var type = model; type is not null && type.SpecialType != SpecialType.System_Object && type.SpecialType != SpecialType.System_ValueType; type = type.BaseType) ancestry.Push(type);
        foreach (var type in ancestry)
        foreach (var member in type.GetMembers().OrderBy(m => m.Name, StringComparer.Ordinal))
        {
            if (member.IsStatic || member.IsImplicitlyDeclared || member.DeclaredAccessibility != Accessibility.Public) continue;
            if (member is IPropertySymbol property && property.GetMethod?.DeclaredAccessibility == Accessibility.Public)
            {
                if (property.IsIndexer || property.ReturnsByRef || property.ReturnsByRefReadonly)
                { Error("DUI003", property, "UI models cannot expose indexers or ref-returning properties. Expose a named, ordinary readable property."); continue; }
                if (property.OverriddenProperty is not null)
                {
                    int index = fields.FindIndex(f => f.Name == property.Name);
                    if (index >= 0) { fields[index] = member; exposed[member.Name] = member; continue; }
                }
            }
            else if (member is IFieldSymbol field && !field.IsConst)
            {
                if (field.IsFixedSizeBuffer) { Error("DUI003", field, "UI models cannot expose fixed buffers. Use a bounded supported collection."); continue; }
            }
            else continue;
            if (exposed.ContainsKey(member.Name)) { Error("DUI003", member, "UI member '" + member.Name + "' hides an inherited public member. Use an unambiguous view model."); continue; }
            exposed.Add(member.Name, member); fields.Add(member);
        }
        if (fields.Count == 0) Error("DUI003", model, "A [UiModel] must expose at least one public readable instance property or field.", origin);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in fields)
        {
            if (nodes.Count >= 128) { SchemaLimit(Site(member)); break; }
            AttributeData? attribute = Attribute(member, FieldAttribute);
            string fieldName = attribute is null ? member.Name : NamedString(attribute, "Name") ?? member.Name;
            int maximum = attribute is null ? 64 : NamedInt(attribute, "Maximum", 64);
            SourceSite site = Site(member);
            if (string.IsNullOrEmpty(site.File)) site = origin;
            if (!ValidName(fieldName)) { Error("DUI003", member, "UI field name '" + fieldName + "' must match [A-Za-z][A-Za-z0-9_]{0,46}. Set UiField.Name for an explicit migration alias."); continue; }
            if (!names.Add(fieldName)) { Error("DUI003", member, "UI field name '" + fieldName + "' is duplicated after explicit aliases. Choose unique names."); continue; }
            ITypeSymbol type = member is IPropertySymbol p ? p.Type : ((IFieldSymbol)member).Type;
            string getter = "static value => value.@" + member.Name;
            string ending = ", file: " + Literal(site.File) + ", line: " + site.Line + ")";
            uint kind = ModelKind(type);
            if (kind != 0)
            {
                if (attribute is not null && attribute.NamedArguments.Any(a => a.Key == "Maximum"))
                    Error("DUI003", member, "UiField.Maximum applies only to collection members; remove it from this scalar field.");
                if (depth + 1 > 16) Error("DUI003", member, "The UI model exceeds depth 16 (including array elements). Reduce the nesting.");
                AddNode(fieldName, kind, node, 0, site);
                if (kind == 3) getter = "static value => (double)value.@" + member.Name;
                else if (kind == 1) getter += "!";
                expression.Append("\n            .").Append(KindName(kind)).Append('(').Append(Literal(fieldName)).Append(", ").Append(getter).Append(ending);
                continue;
            }
            ITypeSymbol? element = CollectionElement(type);
            if (element is not null)
            {
                if (element.IsReferenceType && element.NullableAnnotation == NullableAnnotation.Annotated)
                { Error("DUI003", member, "Nullable UI collection element types are unsupported because generated UiData<T> projections require nonnullable elements. Use a collection of nonnullable elements or a handwritten projection that handles null explicitly."); continue; }
                if (maximum < 1 || maximum > 64) { Error("DUI003", member, "UiField.Maximum must be between 1 and 64, inclusive."); continue; }
                if (depth + 2 > 16) { Error("DUI003", member, "The UI model exceeds depth 16 (including array elements). Reduce the nesting."); continue; }
                int arrayNode = AddNode(fieldName, 6, node, (uint)maximum, site);
                string data;
                uint elementKind = CommandKind(element);
                if (elementKind != 0)
                { AddNode("item", elementKind, arrayNode, 0, site); data = Runtime + "UiData." + KindName(elementKind); }
                else if (ModelKind(element) == 3)
                { Error("DUI003", member, "Numeric UI collections currently require double elements. Use a double collection or a handwritten projection to convert '" + element.ToDisplayString() + "' safely."); continue; }
                else if (element is INamedTypeSymbol record && CollectionElement(element) is null)
                    data = BuildRecord(record, "item", arrayNode, depth + 2, site);
                else
                { Error("DUI003", member, "UI collections require string, bool, double, ulong, or [UiModel] elements. Use a finite opted-in record to represent nested collections."); continue; }
                expression.Append("\n            .Array<").Append(TypeName(element)).Append(">(").Append(Literal(fieldName)).Append(", ").Append(getter).Append("!, ").Append(data).Append(", maximum: ").Append(maximum).Append(ending);
            }
            else if (type is INamedTypeSymbol record && Attribute(record, ModelAttribute) is not null)
            {
                if (attribute is not null && attribute.NamedArguments.Any(a => a.Key == "Maximum")) Error("DUI003", member, "UiField.Maximum applies only to collection members; remove it from this record field.");
                string data = BuildRecord(record, fieldName, node, depth + 1, site);
                expression.Append("\n            .Record(").Append(Literal(fieldName)).Append(", ").Append(getter).Append("!, ").Append(data).Append(ending);
            }
            else Error("DUI003", member, "Unsupported UI member type '" + type.ToDisplayString() + "'. Use string, bool, a safe numeric scalar, ulong for exact keys, an opted-in [UiModel] record, or an array/List/IReadOnlyList of supported elements. Nullable value types and arbitrary getters/delegates are not inferred.");
        }
        visiting.Remove(model);
        return expression.ToString();
    }

    private int AddNode(string name, uint kind, int parent, uint maximum, SourceSite site)
    {
        if (nodes.Count >= 128) { SchemaLimit(site); return -1; }
        int index = nodes.Count;
        nodes.Add(new NodeDescriptor(name, kind, parent, maximum, site));
        return index;
    }
    private void SchemaLimit(SourceSite site)
    {
        if (schemaLimitReported) return;
        schemaLimitReported = true;
        if (string.IsNullOrEmpty(site.File)) site = Site(controller);
        issues.Add(new Issue("DUI003", "The UI model exceeds 128 schema nodes (including nested records and array elements). Reduce the exposed model shape.", site));
    }
    private static bool ReservedCommandName(string name) =>
        new[] { "it", "it_index", "ev", "true", "false", "size", "literal" }.Contains(name, StringComparer.OrdinalIgnoreCase);

    private static bool IsPartial(INamedTypeSymbol type) => type.DeclaringSyntaxReferences.Length != 0 && type.DeclaringSyntaxReferences.All(r => r.GetSyntax() is TypeDeclarationSyntax declaration && declaration.Modifiers.Any(SyntaxKind.PartialKeyword));
    private static ITypeSymbol? CollectionElement(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array) return array.Rank == 1 ? array.ElementType : null;
        if (type is INamedTypeSymbol named && named.TypeArguments.Length == 1)
        {
            string definition = named.OriginalDefinition.ToDisplayString();
            if (definition == "System.Collections.Generic.List<T>" || definition == "System.Collections.Generic.IReadOnlyList<T>") return named.TypeArguments[0];
        }
        return null;
    }
    private static uint CommandKind(ITypeSymbol type) => type.SpecialType switch
    { SpecialType.System_String => 1, SpecialType.System_Boolean => 2, SpecialType.System_Double => 3, SpecialType.System_UInt64 => 4, _ => 0 };
    private static uint ModelKind(ITypeSymbol type) => type.SpecialType switch
    {
        SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Single => 3,
        _ => CommandKind(type)
    };
    private static string KindName(uint kind) => kind switch { 1 => "Text", 2 => "Boolean", 3 => "Number", 4 => "Key", _ => "Unsupported" };
    private static AttributeData? Attribute(ISymbol symbol, string name) => symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == name);
    private static string? NamedString(AttributeData data, string name) => data.NamedArguments.FirstOrDefault(a => a.Key == name).Value.Value as string;
    private static uint NamedUInt(AttributeData data, string name) => data.NamedArguments.FirstOrDefault(a => a.Key == name).Value.Value is uint value ? value : 0;
    private static int NamedInt(AttributeData data, string name, int fallback) => data.NamedArguments.FirstOrDefault(a => a.Key == name).Value.Value is int value ? value : fallback;
    private static SourceSite Site(ISymbol symbol) => SourceSite.From(symbol.Locations.FirstOrDefault(l => l.IsInSource));
    private void Error(string id, ISymbol symbol, string message, SourceSite? fallback = null)
    {
        SourceSite site = Site(symbol);
        if (string.IsNullOrEmpty(site.File)) site = fallback ?? Site(controller);
        issues.Add(new Issue(id, message, site));
    }
    private static string TypeName(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    internal static string Literal(string value) => SymbolDisplay.FormatLiteral(value, true);
    private static bool ValidName(string name) => name.Length > 0 && name.Length <= 47 && IsLetter(name[0]) && name.All(c => IsLetter(c) || c >= '0' && c <= '9' || c == '_');
    private static bool IsLetter(char c) => c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z';
}

internal static class TypeSymbolExtensions
{
    internal static bool ContainsTypeParameters(this ITypeSymbol type)
    {
        if (type is ITypeParameterSymbol) return true;
        if (type is IArrayTypeSymbol array) return array.ElementType.ContainsTypeParameters();
        return type is INamedTypeSymbol named && (named.TypeArguments.Any(ContainsTypeParameters) || named.ContainingType is not null && named.ContainingType.ContainsTypeParameters());
    }
}
