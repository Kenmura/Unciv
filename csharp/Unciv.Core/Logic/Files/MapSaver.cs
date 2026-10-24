using Unciv.Json;
using Unciv.Logic.Map;

namespace Unciv.Logic.Files;

// core/src/com/unciv/logic/files/MapSaver.kt
//
// Deviations:
// - UncivGame.Current.files.getLocalFile becomes the explicit `localFile` resolver argument (a function from a path relative to the
//   local data folder to its handle) of SaveMap and GetMaps.
// - TileMap and TileMap.Preview JSON is wired by their own converters (T107).
//   The file/zip/trim/fallback logic here is complete.

public static class MapSaver
{
    public const string MapsFolder = "maps";

    public static bool SaveZipped { get; set; } = true;

    public static TileMap MapFromSavedString(string mapString)
    {
        string unzippedJson;
        try
        {
            unzippedJson = FileConversions.Unzip(mapString.Trim());
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException)
        {
            unzippedJson = mapString;
        }
        return MapFromJson(unzippedJson);
    }

    public static string MapToSavedString(TileMap tileMap)
    {
        tileMap.AssignContinents(TileMap.AssignContinentsMode.Reassign);
        var mapJson = UncivJson.Serialize(tileMap);
        return SaveZipped ? FileConversions.Zip(mapJson) : mapJson;
    }

    public static void SaveMap(string mapName, TileMap tileMap, Func<string, IUncivFileHandle> localFile)
    {
        tileMap.AssignContinents(TileMap.AssignContinentsMode.Reassign);
        FileConversions.WriteJson(localFile($"{MapsFolder}/{mapName}"), tileMap, SaveZipped);
    }

    public static TileMap LoadMap(IUncivFileHandle mapFile) => FileConversions.ReadJson<TileMap>(mapFile)!;

    public static IReadOnlyList<IUncivFileHandle> GetMaps(Func<string, IUncivFileHandle> localFile) => localFile(MapsFolder).List();

    private static TileMap MapFromJson(string json) => UncivJson.Deserialize<TileMap>(json)!;

    public static MapParameters LoadMapParameters(IUncivFileHandle mapFile) => LoadMapPreview(mapFile).MapParameters;

    public static TileMap.Preview LoadMapPreview(IUncivFileHandle mapFile) => FileConversions.ReadJson<TileMap.Preview>(mapFile)!;
}
