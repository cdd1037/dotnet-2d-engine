namespace GameAuthoringLab;

// Internal protocol regression fixtures. Maintained examples use the typed handlers in UiModelExamples.
public static partial class UiModelExamples
{
    public const uint Equip = 1, Drop = 2, Choose = 3, Rename = 4;
    public const uint SetVolume = 5, SetHints = 6, SetOption = 7, Next = 90;
    public static UiCommands InventoryCommands() => new UiCommands()
        .Add("equip", Equip, UiValueKind.Key).Add("drop", Drop, UiValueKind.Key).Add("next", Next);

    public static UiCommands DialogueCommands() => new UiCommands()
        .Add("choose", Choose, UiValueKind.Key, UiValueKind.Text).Add("next", Next);

    public static UiCommands SettingsCommands() => new UiCommands()
        .Add("rename", Rename, UiValueKind.Text).Add("volume", SetVolume, UiValueKind.Number)
        .Add("hints", SetHints, UiValueKind.Boolean).Add("option", SetOption, UiValueKind.Key, UiValueKind.Boolean)
        .Add("next", Next);

    // Call these only after the owning session's IsCurrent check, as the demo below does.
    public static bool Handle(InventoryModel model, UiCommandEvent command)
    {
        if (command.CommandId is not (Equip or Drop)) return false;
        var item = model.Items.Find(x => x.Id == command[0].Key);
        if (item is null) return false;
        if (command.CommandId == Equip)
        {
            item.Equipped = !item.Equipped;
            model.Status = item.Card.Title + (item.Equipped ? " is now in your kit" : " returned to your pack");
        }
        else
        {
            model.Items.Remove(item);
            model.Status = "Left behind: " + item.Card.Title;
        }
        return true;
    }

    public static bool Handle(DialogueModel model, UiCommandEvent command)
    {
        if (command.CommandId != Choose) return false;
        var choice = model.Choices.Find(x => x.Id == command[0].Key);
        if (choice is null || !choice.Available) return false;
        // The command's second argument demonstrates copied text; application logic still
        // resolves the current keyed choice instead of treating displayed text as identity.
        model.Status = "You chose: " + command[1].Text;
        model.Conversation.Message.Body = choice.Id == 9_007_199_254_741_101UL
            ? "The markers were painted by the first trail keepers. When the path forks, look for the small silver star."
            : "Then you are in good company. The caravan leaves when the last bell rings. Safe travels, explorer.";
        model.Conversation.Message.ShowAside = false;
        return true;
    }

    public static bool Handle(SettingsModel model, UiCommandEvent command)
    {
        switch (command.CommandId)
        {
            case Rename:
                model.Profile.Name = command[0].Text;
                model.Status = "Explorer name updated";
                return true;
            case SetVolume:
                model.Profile.Volume = Math.Clamp(command[0].Number, 0, 100);
                model.Status = "Master volume updated";
                return true;
            case SetHints:
                model.Profile.Hints = command[0].Boolean;
                model.Status = model.Profile.Hints ? "Journey hints enabled" : "Journey hints hidden";
                return true;
            case SetOption:
                foreach (var group in model.Groups)
                {
                    var option = group.Options.Find(x => x.Id == command[0].Key);
                    if (option is null) continue;
                    option.Enabled = command[1].Boolean;
                    model.Status = option.Label + (option.Enabled ? " enabled" : " disabled");
                    return true;
                }
                return false;
            default: return false;
        }
    }

}
