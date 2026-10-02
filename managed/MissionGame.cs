using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameAuthoringLab;

internal enum MissionScreen { Title, Playing, Paused, Won, Lost }

// A deliberately small authored mission, not a second scene/prefab framework.
internal sealed record MissionDefinition
{
    public required string Kind { get; init; }
    public required int Version { get; init; }
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Objective { get; init; }
    public required int Seconds { get; init; }
    public required int DeliveryRoom { get; init; }
    public required float DeliveryX { get; init; }
    public required float DeliveryY { get; init; }
    public required float DeliveryRadius { get; init; }

    public static MissionDefinition Load(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length is <= 0 or > 16384) throw new SceneFormatException("$ mission must contain 1..16384 bytes.");
            using var document = JsonDocument.Parse(stream);
            MissionJson.CheckDuplicates(document.RootElement, "$");
            var mission = document.RootElement.Deserialize(MissionJsonContext.Default.MissionDefinition)
                ?? throw new SceneFormatException("$ mission cannot be null.");
            mission.Validate();
            return mission;
        }
        catch (Exception e) when (e is JsonException or SceneFormatException or UiAuthoringException or IOException or UnauthorizedAccessException)
        { throw new SceneFormatException($"{path}: {e.Message}", e); }
    }

    public void Validate()
    {
        if (Kind != "gal-relay-mission" || Version != 1) throw new SceneFormatException("$.kind/$.version: expected gal-relay-mission v1.");
        if (Id is null || Id.Length is < 1 or > 64 || Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) throw new SceneFormatException("$.id: expected 1..64 ASCII letters, digits or hyphens.");
        UiSettingsContract.ValidateText(Title, 96, 64, "$.title");
        UiSettingsContract.ValidateText(Objective, 240, 160, "$.objective");
        if (string.IsNullOrWhiteSpace(Title) || string.IsNullOrWhiteSpace(Objective)) throw new SceneFormatException("$.title/$.objective: text must not be empty.");
        if (Seconds is < 10 or > 600) throw new SceneFormatException("$.seconds: expected 10..600.");
        if (DeliveryRoom != 1) throw new SceneFormatException("$.deliveryRoom: this two-room mission delivers in room 1 (Field Archive).");
        // This rectangle is clear of the sample's fixed collision fixtures.
        if (!float.IsFinite(DeliveryX) || DeliveryX is < 740 or > 840) throw new SceneFormatException("$.deliveryX: expected 740..840 in the clear archive delivery zone.");
        if (!float.IsFinite(DeliveryY) || DeliveryY is < 150 or > 220) throw new SceneFormatException("$.deliveryY: expected 150..220 in the clear archive delivery zone.");
        if (!float.IsFinite(DeliveryRadius) || DeliveryRadius is < 24 or > 64) throw new SceneFormatException("$.deliveryRadius: expected 24..64.");
    }
}

internal sealed record MissionSave
{
    public required string Kind { get; init; }
    public required int Version { get; init; }
    public required MissionDefinition Mission { get; init; }
    public required int RemainingTicks { get; init; }
    public required string World { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, WriteIndented = true)]
[JsonSerializable(typeof(MissionDefinition))]
[JsonSerializable(typeof(MissionSave))]
internal partial class MissionJsonContext : JsonSerializerContext { }

internal static class MissionJson
{
    internal static void CheckDuplicates(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw new SceneFormatException(path + "." + property.Name + ": duplicate field.");
                CheckDuplicates(property.Value, path + "." + property.Name);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (var child in element.EnumerateArray()) CheckDuplicates(child, path + "[" + index++ + "]");
        }
    }
}

// Owns one entire playthrough. Replacement is prepared before touching the live run.
// Screen transitions are host policy; no native resources or UI callbacks live in World.
internal sealed class MissionGame : IDisposable
{
    private readonly AssetCatalog _catalog;
    private readonly string _missionPath;
    private bool _waitForNeutral = true, _disposed, _retiring, _preparing;
    private RoomGame? _pendingRetirement;
    private uint _previousKeys;
    private bool _pendingDelivery;
    public MissionDefinition Definition { get; private set; }
    public RoomGame Room { get; private set; }
    public MissionScreen Screen { get; private set; } = MissionScreen.Title;
    public int RemainingTicks { get; private set; }
    public int SecondsLeft => (RemainingTicks + 59) / 60;
    public bool WaitingForNeutral => _waitForNeutral;
    public int Revision { get; private set; }
    public string Notice { get; private set; } = "";

    public MissionGame(string missionPath, AssetCatalog catalog)
    {
        _missionPath = missionPath; _catalog = catalog;
        Definition = MissionDefinition.Load(missionPath);
        Room = new RoomGame(); RemainingTicks = Definition.Seconds * 60;
        try { ValidateResources(Room); } catch { Retire(Room); throw; }
    }

    private void ValidateResources(RoomGame room)
    {
        // Both destinations are preflighted before beginning/replacing a playthrough.
        foreach (string key in new[] { "room-a", "room-b", "player", "cell", "status-empty", "status-held", "status-restored" })
            CheckResource(key);
        foreach (var entity in room.World.Entities)
            if (entity.Sprite?.AssetKey is { } key) CheckResource(key);
        void CheckResource(string key)
        {
            try { _catalog.PathFor(key); }
            catch (Exception e) when (e is IOException or InvalidDataException)
            { throw new SceneFormatException($"{Path.Combine(_catalog.Root, key + ".bmp")}: resource '{key}' required by {_missionPath}: {e.Message}", e); }
        }
    }

    public void Start(Action<RoomGame>? prepare = null)
    {
        RequireReplacementAllowed();
        RetirePending();
        var definition = MissionDefinition.Load(_missionPath);
        var candidate = new RoomGame();
        PrepareCandidate(candidate, prepare);
        Replace(candidate, definition, definition.Seconds * 60, MissionScreen.Playing);
        Notice = "Find the cell. Carry it to the archive relay.";
    }

    private void PrepareCandidate(RoomGame candidate, Action<RoomGame>? prepare)
    {
        try
        {
            _preparing = true;
            try { ValidateResources(candidate); prepare?.Invoke(candidate); }
            finally { _preparing = false; }
            // Dispose remains allowed during preparation, but cannot be undone by
            // committing the candidate after the callback returns.
            RequireReplacementAllowed();
        }
        catch { _pendingRetirement = candidate; RetirePending(); throw; }
    }

    private void Replace(RoomGame candidate, MissionDefinition definition, int ticks, MissionScreen screen)
    {
        var old = Room;
        Room = candidate; Definition = definition; RemainingTicks = ticks;
        Boundary(screen);
        // Replacement is committed even if old cleanup fails. Keep ownership of
        // preflight-blocked objects until a later operation or Dispose can retry.
        _pendingRetirement = old;
        RetirePending();
    }

    public void Pause() { ObjectDisposedException.ThrowIf(_disposed, this); if (Screen == MissionScreen.Playing) Boundary(MissionScreen.Paused); }
    public void Resume() { ObjectDisposedException.ThrowIf(_disposed, this); if (Screen == MissionScreen.Paused) Boundary(MissionScreen.Playing); }
    public void Title() { ObjectDisposedException.ThrowIf(_disposed, this); Boundary(MissionScreen.Title); Notice = ""; }
    private void Boundary(MissionScreen screen)
    {
        Screen = screen; Revision++; _waitForNeutral = true; _previousKeys = 0; _pendingDelivery = false; Room.ResetInputBoundary();
    }
    public void SetNotice(string notice) { ObjectDisposedException.ThrowIf(_disposed, this); Notice = notice; }

    public void Advance(uint keys, float elapsed) => Advance(keys, keys & ~_previousKeys, elapsed);
    public void Advance(uint keys, uint pressed, float elapsed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!float.IsFinite(elapsed) || elapsed < 0) throw new ArgumentOutOfRangeException(nameof(elapsed));
        if (Screen != MissionScreen.Playing) return;
        if (_waitForNeutral) { if ((keys | pressed) == 0) _waitForNeutral = false; return; }
        _previousKeys = keys;
        _pendingDelivery |= (pressed & Native.Interact) != 0;
        Room.Advance(keys, pressed, Math.Min(elapsed, RemainingTicks * RoomGame.FixedDelta));
        RemainingTicks = Math.Max(0, RemainingTicks - Room.LastSteps);
        var player = Room.Player.LocalTransform;
        // Delivery wins a final tick tie. E is an explicit edge, never automatic arrival.
        if (Room.LastSteps > 0 && _pendingDelivery && Room.Held is not null && Room.RoomIndex == Definition.DeliveryRoom &&
            MathF.Abs(player.X - Definition.DeliveryX) <= Definition.DeliveryRadius &&
            MathF.Abs(player.Y - Definition.DeliveryY) <= Definition.DeliveryRadius)
        { Boundary(MissionScreen.Won); Notice = "Relay restored. The archive is back online."; }
        else if (RemainingTicks == 0) { Boundary(MissionScreen.Lost); Notice = "The relay ran out of power. Try again."; }
        if (Room.LastSteps > 0) _pendingDelivery = false;
    }

    public string Save()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Screen is not (MissionScreen.Playing or MissionScreen.Paused)) throw new SceneFormatException("Save is available during play or pause. Start a new run first.");
        return JsonSerializer.Serialize(new MissionSave { Kind = "gal-relay-save", Version = 1, Mission = Definition,
            RemainingTicks = RemainingTicks, World = Room.Save() }, MissionJsonContext.Default.MissionSave);
    }

    public void SaveFile(string path)
    {
        string json = Save();
        string full = Path.GetFullPath(path), temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, json); File.Move(temp, full, true); Notice = "Saved. F9 restores this checkpoint."; }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public void Load(string json, Action<RoomGame>? prepare = null)
    {
        RequireReplacementAllowed();
        RetirePending();
        if (json.Length > 1024 * 1024) throw new SceneFormatException("$ relay save exceeds 1 Mi-character limit.");
        MissionSave save;
        try
        {
            using var document = JsonDocument.Parse(json);
            MissionJson.CheckDuplicates(document.RootElement, "$");
            save = document.RootElement.Deserialize(MissionJsonContext.Default.MissionSave) ?? throw new SceneFormatException("$ save cannot be null.");
        }
        catch (JsonException e) { throw new SceneFormatException(e.Message, e); }
        if (save.Kind != "gal-relay-save" || save.Version != 1) throw new SceneFormatException("$.kind/$.version: expected gal-relay-save v1.");
        if (save.Mission is null) throw new SceneFormatException("$.mission: missing mission.");
        save.Mission.Validate();
        var definition = MissionDefinition.Load(_missionPath);
        if (save.Mission != definition) throw new SceneFormatException("$.mission: authored mission changed; saved rules do not match. Start a new run.");
        if (save.RemainingTicks < 1 || save.RemainingTicks > definition.Seconds * 60) throw new SceneFormatException("$.remainingTicks: outside mission time limit.");
        if (save.World is null) throw new SceneFormatException("$.world: missing runtime snapshot.");
        var candidate = RoomGame.Load(save.World, _catalog);
        PrepareCandidate(candidate, prepare);
        Replace(candidate, definition, save.RemainingTicks, MissionScreen.Paused);
        Notice = "Checkpoint restored. Resume when ready.";
    }

    public void LoadFile(string path, Action<RoomGame>? prepare = null)
    {
        RequireReplacementAllowed();
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length > 1024 * 1024) throw new SceneFormatException("$ relay save exceeds 1 MiB.");
            using var reader = new StreamReader(stream);
            Load(reader.ReadToEnd(), prepare);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SceneFormatException or UiAuthoringException)
        { throw new SceneFormatException($"{path}: {e.Message}", e); }
    }

    private void RequireReplacementAllowed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_preparing || _retiring) throw new InvalidOperationException("Mission replacement is forbidden during preparation or retirement callbacks.");
    }

    private void RetirePending()
    {
        if (_pendingRetirement is not { } room) return;
        bool wasRetiring = _retiring;
        _retiring = true;
        try { Retire(room); }
        finally
        {
            _retiring = wasRetiring;
            if (room.World.EntityCount == 0 && room.World.LoadedScenes.All(scene => scene == room.World.PersistentScene))
                _pendingRetirement = null;
        }
    }

    internal static void Retire(RoomGame room)
    {
        List<Exception>? failures = null;
        foreach (var scene in room.World.LoadedScenes.ToArray())
        {
            if (scene == room.World.PersistentScene) continue;
            try { room.World.UnloadScene(scene); }
            catch (Exception error) { (failures ??= []).Add(error); }
        }
        // Snapshot once: a preflight failure can leave an entity alive, while a
        // cleanup failure means destruction already committed. Never spin on it.
        foreach (var entity in room.World.Entities.ToArray())
        {
            if (!entity.IsAlive) continue;
            try { room.World.Destroy(entity); }
            catch (Exception error) { (failures ??= []).Add(error); }
        }
        if (failures is not null)
            throw new AggregateException("Mission retirement encountered errors; remaining cleanup was attempted.", failures);
    }
    public void Dispose()
    {
        if (_retiring) return;
        // Close before callbacks. Later Dispose calls may retry anything whose
        // teardown preflight failed, but no operation can reopen this mission.
        _disposed = _retiring = true;
        try
        {
            List<Exception>? failures = null;
            try { RetirePending(); }
            catch (Exception error) { (failures ??= []).Add(error); }
            try { Retire(Room); }
            catch (Exception error) { (failures ??= []).Add(error); }
            if (failures is not null) throw new AggregateException("Mission disposal encountered retirement errors.", failures);
        }
        finally { _retiring = false; }
    }
}
