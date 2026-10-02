namespace GameAuthoringLab;

/// <summary>Converts frozen engine registrations to the shared schema-only authoring proof.</summary>
internal static class UiModelPreflight
{
    internal static unsafe void Validate(UiXmlElement root, string file, ModelSchema[] schema, UiCommands.Command[] commands)
    {
        var nodes = new UiContractNode[schema.Length];
        for (int i = 0; i < schema.Length; i++)
        {
            ModelSchema node = schema[i];
            nodes[i] = new UiContractNode(UiNative.Text(node.Name, 48), node.Kind,
                node.Parent == uint.MaxValue ? -1 : checked((int)node.Parent), node.Limit);
        }
        var registered = commands.Select(command => new UiContractCommand(command.Name,
            command.Arguments.Select(kind => (uint)kind).ToArray(), command.Handler is not null,
            command.Origin is { } origin ? new UiContractDeclaration(origin.FilePath, origin.Line) : null)).ToArray();
        try
        {
            // The runtime authoring gate already validates names, including malformed invocations.
            UiContractValidator.Validate(root, file, nodes, registered, diagnoseUnknownCommands: false);
        }
        catch (UiContractException error) { throw ToAuthoringException(error); }
    }

    internal static UiAuthoringException ToAuthoringException(UiContractException error) => UiAuthoring.ToAuthoringException(error);

}
