using GameAuthoringLab;
namespace Relay;

// Application projections and command names: the runtime knows no mission screens or rules.
internal sealed record RelayView(bool Playing, bool TitleScreen, bool Paused, bool Result,
    string Title, string Objective, string Status, int Seconds, bool CanSave, bool CanLoad)
{
    public static UiRecord<RelayView> Schema() => new UiRecord<RelayView>()
        .Boolean("playing", v => v.Playing).Boolean("title_screen", v => v.TitleScreen)
        .Boolean("paused", v => v.Paused).Boolean("result", v => v.Result)
        .Text("title", v => v.Title).Text("objective", v => v.Objective)
        .Text("status", v => v.Status).Number("seconds", v => v.Seconds)
        .Boolean("can_save", v => v.CanSave).Boolean("can_load", v => v.CanLoad);

    public static RelayView From(MissionGame game, bool canLoad)
    {
        string status = game.Notice;
        if (game.Screen == MissionScreen.Playing)
            status = game.Room.Held is not null
                ? game.Room.RoomIndex == 0 ? "CELL LINKED - east door: T" : "CELL LINKED - upper-right glowing relay: E to deliver"
                : game.Room.ItemRoomIndex == game.Room.RoomIndex ? "Find the amber power cell. E picks up; F drops." : "The cell is in the other room. Use T at the door.";
        if (game.Screen == MissionScreen.Playing && (game.Notice.StartsWith("Could not", StringComparison.Ordinal) || game.Notice.StartsWith("Saved", StringComparison.Ordinal))) status += " | " + game.Notice;
        string title = game.Screen switch { MissionScreen.Paused => "PAUSED / 暂停", MissionScreen.Won => "RELAY RESTORED / 任务完成", MissionScreen.Lost => "TIME IS UP / 时间耗尽", _ => game.Definition.Title };
        return new(game.Screen == MissionScreen.Playing, game.Screen == MissionScreen.Title,
            game.Screen == MissionScreen.Paused, game.Screen is MissionScreen.Won or MissionScreen.Lost,
            title, game.Definition.Objective, status, game.SecondsLeft,
            game.Screen is MissionScreen.Playing or MissionScreen.Paused, canLoad);
    }
}
