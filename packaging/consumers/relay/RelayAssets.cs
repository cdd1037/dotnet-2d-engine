using GameAuthoringLab;

namespace Relay;

// The application chooses every logical key and its path; the package supplies
// only the general asset-root/catalog behavior.
internal static class RelayAssets
{
    public static AssetCatalog Catalog(string root) => new(new AssetRoot(root),
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["room-a"] = "room-a.png",
            ["room-b"] = "room-b.png",
            ["player"] = "player.png",
            ["cell"] = "cell.png",
            ["status-empty"] = "status-empty.png",
            ["status-held"] = "status-held.png",
            ["status-restored"] = "status-restored.png"
        });
}
