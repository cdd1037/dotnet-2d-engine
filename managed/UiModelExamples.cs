namespace GameAuthoringLab;

/// <summary>
/// Three unrelated C# models rendered by ordinary RML templates. The schemas only project
/// application data; neither the bridge nor these models prescribe a widget hierarchy.
/// </summary>
public static partial class UiModelExamples
{
    public const string InventoryAsset = "ui/model-inventory.rml";
    public const string DialogueAsset = "ui/model-dialogue.rml";
    public const string SettingsAsset = "ui/model-settings.rml";
    public static IReadOnlyList<string> InventoryImages { get; } =
        Array.AsReadOnly(new[] { "model-blade.bmp", "model-lantern.bmp" });

    public sealed class InventoryModel
    {
        public string Title { get; set; } = "Ready for the road";
        public string Status { get; set; } = "Select equipment or lighten your pack";
        public List<InventoryItem> Items { get; set; } = [];
    }

    public sealed class InventoryItem
    {
        public ulong Id { get; set; }
        public ItemCard Card { get; set; } = new();
        public bool Equipped { get; set; }
        public double Quantity { get; set; } = 1;
    }

    public sealed class ItemCard
    {
        public string Image { get; set; } = "model-blade.bmp";
        public string Title { get; set; } = "";
        public string Badge { get; set; } = "";
        public string Description { get; set; } = "";
    }

    public sealed class DialogueModel
    {
        public string Scene { get; set; } = "The last light before the pass";
        public Conversation Conversation { get; set; } = new();
        public List<DialogueChoice> Choices { get; set; } = [];
        public string Status { get; set; } = "A choice carries its exact key and plain-text response";
    }

    public sealed class Conversation
    {
        public Speaker Speaker { get; set; } = new();
        public DialogueMessage Message { get; set; } = new();
    }

    public sealed class Speaker
    {
        public string Name { get; set; } = "Mira";
        public string Role { get; set; } = "Keeper of the waystation";
    }

    public sealed class DialogueMessage
    {
        public string Body { get; set; } = "The mountain path is open again. Take a lantern, and follow the blue markers when the mist rolls in.";
        public string Aside { get; set; } = "Outside, the bells of the evening caravan begin to ring.";
        public bool ShowAside { get; set; } = true;
    }

    public sealed class DialogueChoice
    {
        public ulong Id { get; set; }
        public string Text { get; set; } = "";
        public string Note { get; set; } = "";
        public bool Available { get; set; } = true;
    }

    public sealed class SettingsModel
    {
        public ExplorerProfile Profile { get; set; } = new();
        public List<SettingsGroup> Groups { get; set; } = [];
        public string Status { get; set; } = "Preferences are kept in the C# model";
    }

    public sealed class ExplorerProfile
    {
        public string Name { get; set; } = "Rowan / 旅人";
        public double Volume { get; set; } = 65;
        public bool Hints { get; set; } = true;
    }

    public sealed class SettingsGroup
    {
        public ulong Id { get; set; }
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public List<SettingsOption> Options { get; set; } = [];
    }

    public sealed class SettingsOption
    {
        public ulong Id { get; set; }
        public string Label { get; set; } = "";
        public bool Enabled { get; set; }
        public double Weight { get; set; }
    }

    public static InventoryModel Inventory() => new()
    {
        Items =
        [
            new() { Id = 9_007_199_254_740_993UL, Equipped = true, Card = new()
                { Title = "Sunsteel blade", Badge = "RARE", Description = "A light, balanced companion for the mountain trail." } },
            new() { Id = ulong.MaxValue - 1, Card = new()
                { Image = "model-lantern.bmp", Title = "Wayfinder lantern", Badge = "TOOL", Description = "Warm light that stays steady through wind and rain." } },
            new() { Id = 10_003, Quantity = 3, Card = new()
                { Image = "model-lantern.bmp", Title = "Ember flask", Badge = "SUPPLY", Description = "A little warmth, saved for the coldest part of the climb." } }
        ]
    };

    public static DialogueModel Dialogue() => new()
    {
        Choices =
        [
            new() { Id = 9_007_199_254_741_101UL, Text = "Tell me about the blue markers.", Note = "Ask for guidance" },
            new() { Id = 21_002, Text = "I'll travel with the evening caravan.", Note = "Take the safe route" },
            new() { Id = 21_003, Text = "I can open the northern gate.", Note = "Requires a gate key", Available = false }
        ]
    };

    public static SettingsModel Settings() => new()
    {
        Groups =
        [
            new() { Id = 31_001, Title = "Along the trail", Description = "Choose what appears while exploring", Options =
                [ new() { Id = 9_007_199_254_742_001UL, Label = "Show trail markers", Enabled = true, Weight = 1 },
                  new() { Id = 31_011, Label = "Highlight useful supplies", Enabled = false, Weight = 2 } ] },
            new() { Id = 31_002, Title = "At the waystation", Description = "Keep the quiet moments comfortable", Options =
                [ new() { Id = 31_021, Label = "Dialogue subtitles", Enabled = true, Weight = 1 },
                  new() { Id = ulong.MaxValue - 2, Label = "Ambient lantern sounds", Enabled = true, Weight = 3 } ] }
        ]
    };

    public static UiRecord<InventoryModel> InventorySchema()
    {
        var card = new UiRecord<ItemCard>()
            .Text("image", static x => x.Image).Text("title", static x => x.Title)
            .Text("badge", static x => x.Badge).Text("description", static x => x.Description);
        var item = new UiRecord<InventoryItem>()
            .Key("id", static x => x.Id).Record("card", static x => x.Card, card)
            .Boolean("equipped", static x => x.Equipped).Number("quantity", static x => x.Quantity);
        return new UiRecord<InventoryModel>()
            .Text("title", static x => x.Title).Text("status", static x => x.Status)
            .Array("items", static x => x.Items, item, 32);
    }

    public static UiRecord<DialogueModel> DialogueSchema()
    {
        var speaker = new UiRecord<Speaker>()
            .Text("name", static x => x.Name).Text("role", static x => x.Role);
        var message = new UiRecord<DialogueMessage>()
            .Text("body", static x => x.Body).Text("aside", static x => x.Aside)
            .Boolean("show_aside", static x => x.ShowAside);
        var conversation = new UiRecord<Conversation>()
            .Record("speaker", static x => x.Speaker, speaker).Record("message", static x => x.Message, message);
        var choice = new UiRecord<DialogueChoice>()
            .Key("id", static x => x.Id).Text("text", static x => x.Text)
            .Text("note", static x => x.Note).Boolean("available", static x => x.Available);
        return new UiRecord<DialogueModel>()
            .Text("scene", static x => x.Scene).Record("conversation", static x => x.Conversation, conversation)
            .Array("choices", static x => x.Choices, choice, 16).Text("status", static x => x.Status);
    }

    public static UiRecord<SettingsModel> SettingsSchema()
    {
        var profile = new UiRecord<ExplorerProfile>()
            .Text("name", static x => x.Name).Number("volume", static x => x.Volume)
            .Boolean("hints", static x => x.Hints);
        var option = new UiRecord<SettingsOption>()
            .Key("id", static x => x.Id).Text("label", static x => x.Label)
            .Boolean("enabled", static x => x.Enabled).Number("weight", static x => x.Weight);
        var group = new UiRecord<SettingsGroup>()
            .Key("id", static x => x.Id).Text("title", static x => x.Title)
            .Text("description", static x => x.Description).Array("options", static x => x.Options, option, 8);
        return new UiRecord<SettingsModel>()
            .Record("profile", static x => x.Profile, profile)
            .Array("groups", static x => x.Groups, group, 8).Text("status", static x => x.Status);
    }

    public static UiCommands InventoryCommands(InventoryModel model, Action? next = null) => new UiCommands()
        .On("equip", UiArgs.Key, (ulong id) => EquipItem(model, id))
        .On("drop", UiArgs.Key, (ulong id) => DropItem(model, id))
        .On("next", () => next?.Invoke());

    public static UiCommands DialogueCommands(DialogueModel model, Action? next = null) => new UiCommands()
        .On("choose", UiArgs.Key, UiArgs.Text, (ulong id, string text) => ChooseDialogue(model, id, text))
        .On("next", () => next?.Invoke());

    public static UiCommands SettingsCommands(SettingsModel model, Action? next = null) => new UiCommands()
        .On("rename", UiArgs.Text, (string name) => { model.Profile.Name = name; model.Status = "Explorer name updated"; })
        .On("volume", UiArgs.Number, (double value) => { model.Profile.Volume = Math.Clamp(value, 0, 100); model.Status = "Master volume updated"; })
        .On("hints", UiArgs.Boolean, (bool value) => { model.Profile.Hints = value; model.Status = value ? "Journey hints enabled" : "Journey hints hidden"; })
        .On("option", UiArgs.Key, UiArgs.Boolean, (ulong id, bool value) => SetOptionValue(model, id, value))
        .On("next", () => next?.Invoke());

    public static void EquipItem(InventoryModel model, ulong id)
    {
        var item = model.Items.Find(x => x.Id == id); if (item is null) return;
        item.Equipped = !item.Equipped;
        model.Status = item.Card.Title + (item.Equipped ? " is now in your kit" : " returned to your pack");
    }
    public static void DropItem(InventoryModel model, ulong id)
    {
        var item = model.Items.Find(x => x.Id == id); if (item is null) return;
        model.Items.Remove(item); model.Status = "Left behind: " + item.Card.Title;
    }
    public static void ChooseDialogue(DialogueModel model, ulong id, string text)
    {
        var choice = model.Choices.Find(x => x.Id == id); if (choice is null || !choice.Available) return;
        model.Status = "You chose: " + text;
        model.Conversation.Message.Body = choice.Id == 9_007_199_254_741_101UL
            ? "The markers were painted by the first trail keepers. When the path forks, look for the small silver star."
            : "Then you are in good company. The caravan leaves when the last bell rings. Safe travels, explorer.";
        model.Conversation.Message.ShowAside = false;
    }
    public static void SetOptionValue(SettingsModel model, ulong id, bool value)
    {
        foreach (var group in model.Groups)
        {
            var option = group.Options.Find(x => x.Id == id); if (option is null) continue;
            option.Enabled = value; model.Status = option.Label + (value ? " enabled" : " disabled"); return;
        }
    }

    /// <summary>Interactive examples. F5 or the footer button advances; Escape exits.</summary>
    public static int Run(int frames)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frames);
        using var engine = EngineHost.Create(false, 32);
        var assets = new AssetRoot();
        var camera = new Camera { Zoom = 1 };
        var inventory = Inventory();
        var dialogue = Dialogue();
        var settings = Settings();
        string? captures = Environment.GetEnvironmentVariable("GAL_MODEL_UI_CAPTURE_DIR");
        if (captures is not null) Directory.CreateDirectory(captures);
        int index = 0, rendered = 0, exampleFrames = 0;
        bool advanceRequested = false;
        int cycleFrames = frames > 0 ? Math.Max(1, (int)(((long)frames + 2) / 3)) : int.MaxValue;

        IExample Open(int selected) => selected switch
        {
            0 => new Example<InventoryModel>(engine, assets, camera, InventoryAsset, inventory,
                InventorySchema(), InventoryCommands(inventory, () => advanceRequested = true), InventoryImages),
            1 => new Example<DialogueModel>(engine, assets, camera, DialogueAsset, dialogue,
                DialogueSchema(), DialogueCommands(dialogue, () => advanceRequested = true)),
            _ => new Example<SettingsModel>(engine, assets, camera, SettingsAsset, settings,
                SettingsSchema(), SettingsCommands(settings, () => advanceRequested = true))
        };

        IExample example = Open(index);
        Console.WriteLine("MODEL UI | three normal RML templates | F5 / footer: next | Escape: exit");
        try
        {
            while (frames == 0 || rendered < frames)
            {
                var input = engine.PollInputFrame();
                if (input.Quit || input.Game.KeyPressed(PhysicalKey.Escape)) break;
                example.Update();
                bool advance = advanceRequested || input.Game.KeyPressed(PhysicalKey.F5) || exampleFrames >= cycleFrames;
                advanceRequested = false;
                if (advance)
                {
                    example.Dispose();
                    index = (index + 1) % 3;
                    example = Open(index);
                    exampleFrames = 0;
                }
                if (!input.Drawable) { Thread.Sleep(1); continue; }
                if (captures is not null && exampleFrames == 0)
                    example.Capture(Path.Combine(captures, new[] { "inventory.bmp", "dialogue.bmp", "settings.bmp" }[index]));
                engine.Draw(camera, ReadOnlySpan<SpriteCommand>.Empty);
                rendered++;
                exampleFrames++;
                Thread.Sleep(1);
            }
        }
        finally { example.Dispose(); }
        Console.WriteLine($"MODEL UI DONE frames={rendered}");
        return 0;
    }

    private interface IExample : IDisposable
    {
        bool Update();
        void Capture(string path);
    }

    private sealed class Example<T> : IExample
    {
        private readonly UiModelSession<T> _session;
        private readonly T _model;

        public Example(EngineHost engine, AssetRoot assets, Camera camera, string path, T model,
            UiRecord<T> schema, UiCommands commands,
            IReadOnlyList<string>? images = null)
        {
            _model = model;
            _session = new(engine, schema, commands);
            try
            {
                _session.StageAsset(assets, path, model, images);
                engine.Draw(camera, ReadOnlySpan<SpriteCommand>.Empty);
            }
            catch { _session.Dispose(); throw; }
        }

        public bool Update()
        {
            bool changed = false;
            for (var command = _session.Poll(); !command.IsEmpty; command = _session.Poll())
            {
                changed |= _session.Dispatch(command);
            }
            // Drain the revision's commands first. Applying after each character
            // would retire later text packets delivered in the same input frame.
            if (changed) _session.Apply(_model);
            return false;
        }

        public void Capture(string path) => _session.Capture(path);
        public void Dispose() => _session.Dispose();
    }
}
