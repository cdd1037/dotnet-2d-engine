using GameAuthoringLab;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace CardRules;
[UiModel]
public sealed record Stat(string Label, string Value);
[UiModel]
public sealed record CardCopy(string Title, string Badge, string Description, [property: UiField(Maximum = 4)] Stat[] Stats);
public sealed record ItemDefinition(ulong Id, CardCopy Card, int InitialCount, int Heal, int Gold);
public sealed record SectionDefinition(ulong Id, string Title, ItemDefinition[] Items);
[UiModel]
public sealed record ItemView(ulong Id, CardCopy Card, int Count, bool Selected, bool CanInspect, bool CanUse, bool CanDiscard);
[UiModel]
public sealed record SectionView(ulong Id, string Title, [property: UiField(Maximum = 8)] ItemView[] Items);
[UiModel]
public sealed record InventoryView(string Hud, string Detail, bool Paused, bool CanLoad, string Message, [property: UiField(Maximum = 4)] SectionView[] Sections);
public sealed record SaveState(int Version, int Health, int Gold, bool Paused, ulong SelectedId, Dictionary<ulong, int> Counts, ulong[] VisibleIds);
public sealed class CardGame
{
    public IReadOnlyList<SectionDefinition> Catalog { get; }
    public int Health { get; private set; }
    public int Gold { get; private set; }
    public bool Paused { get; private set; }
    public ulong SelectedId { get; private set; }
    private Dictionary<ulong, int> _counts = [];
    private List<ulong> _visible = [];
    private IEnumerable<ItemDefinition> Items => Catalog.SelectMany(section => section.Items);
    public string Hud => $"Health {Health}/100 | Gold {Gold}";
    public string Detail => Items.FirstOrDefault(item => item.Id == SelectedId)?.Card.Description ?? "Inspect an item";
    public CardGame(string catalogJson)
    {
        var sections = JsonSerializer.Deserialize(catalogJson, CardJson.Default.SectionDefinitionArray)
            ?? throw new InvalidDataException("Missing catalog");
        var items = sections.SelectMany(section => section.Items).ToArray();
        var keys = sections.Select(section => section.Id).Concat(items.Select(item => item.Id)).ToArray();
        if (sections.Length == 0 || items.Length == 0 || keys.Any(id => id == 0) || keys.Distinct().Count() != keys.Length ||
            sections.Any(section => string.IsNullOrWhiteSpace(section.Title)) ||
            items.Any(item => string.IsNullOrWhiteSpace(item.Card.Title) || item.InitialCount < 0 || item.Heal < 0 || item.Gold < 0))
            throw new InvalidDataException("Invalid catalog");
        Catalog = Array.AsReadOnly(sections);
        Restart();
    }
    public int Count(ulong id) => _counts[id];
    public bool IsVisible(ulong id) => _visible.Contains(id);
    public bool Inspect(ulong id)
    {
        if (Paused || !IsVisible(id)) return false;
        SelectedId = id;
        return true;
    }
    public bool Use(ulong id)
    {
        if (Paused || !IsVisible(id) || !_counts.TryGetValue(id, out int count) || count == 0) return false;
        var item = Items.Single(item => item.Id == id);
        int gold = checked(Gold + item.Gold);
        Health = (int)Math.Min(100L, (long)Health + item.Heal);
        Gold = gold;
        _counts[id]--;
        return true;
    }
    public bool Discard(ulong id)
    {
        if (Paused || !IsVisible(id) || !_counts.TryGetValue(id, out int count) || count <= 0) return false;
        _counts[id] = count - 1;
        return true;
    }
    public void TogglePause() => Paused = !Paused;
    public void Reverse() => _visible.Reverse();
    public void RemoveEmpty()
    {
        _visible.RemoveAll(id => Count(id) == 0);
        if (!IsVisible(SelectedId)) SelectedId = 0;
    }
    public void Restart()
    {
        Health = 50;
        Gold = 0;
        Paused = false;
        SelectedId = 0;
        _counts = Items.ToDictionary(item => item.Id, item => item.InitialCount);
        _visible = Items.Select(item => item.Id).ToList();
    }
    public InventoryView View(bool canLoad, string message) => new(Hud, Detail, Paused, canLoad, message,
        Catalog.Select(section => new SectionView(section.Id, section.Title,
            _visible.Where(id => section.Items.Any(item => item.Id == id)).Select(id =>
            {
                var item = section.Items.Single(item => item.Id == id);
                return new ItemView(id, item.Card, Count(id), id == SelectedId, !Paused, !Paused && Count(id) > 0, !Paused && Count(id) > 0);
            }).ToArray())).ToArray());
    public string Save() => JsonSerializer.Serialize(new SaveState(2, Health, Gold, Paused, SelectedId,
        new(_counts), _visible.ToArray()), CardJson.Default.SaveState);
    public void Load(string json)
    {
        var state = JsonSerializer.Deserialize(json, CardJson.Default.SaveState) ?? throw new InvalidDataException("Missing save");
        if (state.Version != 2 || state.Health is < 0 or > 100 || state.Gold < 0 || state.Counts is null || state.VisibleIds is null ||
            state.Counts.Any(pair => pair.Value < 0 || !_counts.ContainsKey(pair.Key)) ||
            state.VisibleIds.Any(id => !_counts.ContainsKey(id)) || state.VisibleIds.Distinct().Count() != state.VisibleIds.Length ||
            (state.SelectedId != 0 && !state.VisibleIds.Contains(state.SelectedId))) throw new InvalidDataException("Invalid save");
        var counts = Items.ToDictionary(item => item.Id, item => state.Counts.TryGetValue(item.Id, out int value) ? value : item.InitialCount);
        var visible = state.VisibleIds.Concat(Items.Where(item => !state.Counts.ContainsKey(item.Id)).Select(item => item.Id)).ToList();
        Health = state.Health;
        Gold = state.Gold;
        Paused = state.Paused;
        SelectedId = state.SelectedId;
        _counts = counts;
        _visible = visible;
    }
}
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SectionDefinition[]))]
[JsonSerializable(typeof(SaveState))]
internal partial class CardJson : JsonSerializerContext;
