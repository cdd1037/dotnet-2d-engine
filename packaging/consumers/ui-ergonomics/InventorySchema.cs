using CardRules;
using GameAuthoringLab;
namespace CardUiOurs;
public static class InventorySchema
{
    public static UiRecord<InventoryView> Create()
    {
        var stat = new UiRecord<Stat>()
            .Text("label", x => x.Label)
            .Text("value", x => x.Value);
        var card = new UiRecord<CardCopy>()
            .Text("title", x => x.Title)
            .Text("badge", x => x.Badge)
            .Text("description", x => x.Description)
            .Array("stats", x => x.Stats, stat, 4);
        var item = new UiRecord<ItemView>()
            .Key("id", x => x.Id)
            .Record("card", x => x.Card, card)
            .Number("count", x => x.Count)
            .Boolean("selected", x => x.Selected)
            .Boolean("can_inspect", x => x.CanInspect)
            .Boolean("can_use", x => x.CanUse)
            .Boolean("can_discard", x => x.CanDiscard);
        var section = new UiRecord<SectionView>()
            .Key("id", x => x.Id)
            .Text("title", x => x.Title)
            .Array("items", x => x.Items, item, 8);
        return new UiRecord<InventoryView>()
            .Text("hud", x => x.Hud)
            .Text("detail", x => x.Detail)
            .Boolean("paused", x => x.Paused)
            .Boolean("can_load", x => x.CanLoad)
            .Text("message", x => x.Message)
            .Array("sections", x => x.Sections, section, 4);
    }
}
