namespace GameAuthoringLab;
internal static class SampleAssets
{
    private static readonly string[] Keys=["room-a", "room-b", "player", "cell", "status-empty", "status-held", "status-restored"];
    public static AssetCatalog Catalog(string? root=null)=>new(new AssetRoot(root),Keys.ToDictionary(key=>key,key=>key+".bmp",StringComparer.Ordinal));
}
