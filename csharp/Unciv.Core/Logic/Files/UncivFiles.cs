using System.Text.Json;
using Unciv.Json;
using Unciv.Utils;

namespace Unciv.Logic.Files;

// core/src/com/unciv/logic/files/UncivFiles.kt (engine part: save/load of GameInfo and GameInfoPreview, gzip, autosaves, version checks)
//
// Deviations:
// - Gdx.Files / FileHandle become the explicit data-folder root (IUncivFileHandle). The external-storage preference (Android) is dropped:
//   saves always live in the root's SaveFiles / MultiplayerGames folders.
// - Not ported (client, Won't port or other tickets): GameSettings load/save, getSettingsForPlatformLaunchers, the custom location
//   saver/loader (PlatformSaverLoader), the TextField filter, scenarios (Won't port, T233) and the mod list cache. The autosave limit
//   (GameSettings.maxAutosavesStored) is an explicit argument.
// - Autosaves run synchronously (Kotlin spawned a coroutine job and cloned the game first); the caller clones if it needs to keep playing.
// - The default saveGame callback "rethrow" is an optional Action: null rethrows, like Kotlin's ::rethrowIfNotNull.
// - GameInfo/GameInfoPreview JSON is the System.Text.Json shape (UncivJson); the on-disk container (optional gzip+base64) is Kotlin's.

public class UncivFiles(IUncivFileHandle dataFolder)
{
    public const string SaveFilesFolder = "SaveFiles";
    public const string MultiplayerFilesFolder = "MultiplayerGames";
    public const string AutosaveFileName = "Autosave";

    /// <summary>Kotlin <c>UncivFiles.saveZipped</c>: whether new saves are written gzip+base64.</summary>
    public static bool SaveZipped { get; set; }

    private Autosaves? autosaves;

    public Autosaves Autosaves => autosaves ??= new Autosaves(this);

    // region Helpers

    /// <summary>A path relative to the data folder, '/' separated; the empty path is the data folder itself.</summary>
    public IUncivFileHandle GetLocalFile(string fileName)
    {
        var result = dataFolder;
        foreach (var part in fileName.Split('/'))
        {
            if (part.Length != 0) result = result.Child(part);
        }
        return result;
    }

    public IUncivFileHandle GetDataFolder() => GetLocalFile("");

    public IUncivFileHandle GetSave(string gameName) => GetSave(SaveFilesFolder, gameName);

    public IUncivFileHandle GetMultiplayerSave(string gameName) => GetSave(MultiplayerFilesFolder, gameName);

    private IUncivFileHandle GetSave(string saveFolder, string gameName)
    {
        var localFile = GetLocalFile($"{saveFolder}/{gameName}");
        localFile.Parent.MakeDirs();
        return localFile;
    }

    public IReadOnlyList<IUncivFileHandle> GetMultiplayerSaves() => GetSaves(MultiplayerFilesFolder);

    public IReadOnlyList<IUncivFileHandle> GetSaves(bool autoSaves = true)
    {
        var saves = GetSaves(SaveFilesFolder);
        if (autoSaves) return saves;
        return saves.Where(it => !it.Name.StartsWith(AutosaveFileName, StringComparison.Ordinal)).ToList();
    }

    private IReadOnlyList<IUncivFileHandle> GetSaves(string saveFolder) => GetLocalFile(saveFolder).List();

    public bool DeleteSave(string gameName) => DeleteSave(GetSave(gameName));

    /// <summary>Only use this with a handle obtained by one of the methods of this class!</summary>
    public bool DeleteSave(IUncivFileHandle file)
    {
        Log.Debug("Deleting save %s", file.Name);
        return file.Delete();
    }

    // endregion
    // region Saving

    public IUncivFileHandle SaveGame(GameInfo game, string gameName, Action<Exception?>? saveCompletionCallback = null)
    {
        var file = GetSave(gameName);
        SaveGame(game, file, saveCompletionCallback);
        return file;
    }

    /// <summary>Only use this with a handle obtained by one of the methods of this class! Errors go to the callback, which by default rethrows.</summary>
    public void SaveGame(GameInfo game, IUncivFileHandle file, Action<Exception?>? saveCompletionCallback = null)
    {
        var callback = saveCompletionCallback ?? RethrowIfNotNull;
        try
        {
            Log.Debug("Saving GameInfo %s to %s", game.GameId, file.Name);
            game.Version = CompatibilityVersion.CurrentCompatibilityVersion;
            FileConversions.WriteJson(file, game, SaveZipped);
        }
        catch (Exception ex)
        {
            callback(ex);
            return;
        }
        callback(null);
    }

    /// <summary>Overload of SaveGame to save a GameInfoPreview in the MultiplayerGames folder.</summary>
    public IUncivFileHandle SaveMultiplayerGamePreview(GameInfoPreview game, string gameName, Action<Exception?>? saveCompletionCallback = null)
    {
        var file = GetMultiplayerSave(gameName);
        SaveMultiplayerGamePreview(game, file, saveCompletionCallback);
        return file;
    }

    /// <summary>Only use this with a handle obtained by one of the methods of this class!</summary>
    public void SaveMultiplayerGamePreview(GameInfoPreview game, IUncivFileHandle file, Action<Exception?>? saveCompletionCallback = null)
    {
        var callback = saveCompletionCallback ?? RethrowIfNotNull;
        try
        {
            Log.Debug("Saving GameInfoPreview %s to %s", game.GameId, file.Name);
            using var stream = file.Write(false);
            stream.Write(System.Text.Encoding.UTF8.GetBytes(UncivJson.Serialize(game)));
        }
        catch (Exception ex)
        {
            callback(ex);
            return;
        }
        callback(null);
    }

    private static void RethrowIfNotNull(Exception? ex)
    {
        if (ex != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
    }

    // endregion
    // region Loading

    public GameInfo LoadGameByName(string gameName) => LoadGameFromFile(GetSave(gameName));

    public GameInfo LoadGameFromFile(IUncivFileHandle gameFile)
    {
        if (gameFile.Length == 0) throw EmptyFile(gameFile);

        GameInfo? gameInfo;
        try
        {
            gameInfo = FileConversions.ReadJson<GameInfo>(gameFile);
        }
        catch (OutOfMemoryException outOfMemory)
        {
            throw new UncivShowableException("Not enough memory on phone to load game!", outOfMemory);
        }
        catch (Exception ex)
        {
            Log.Error("Exception while deserializing GameInfo JSON", ex);
            var onlyVersion = FileConversions.ReadJson<GameInfoSerializationVersion>(gameFile).NotNull();
            throw new IncompatibleGameInfoVersionException(onlyVersion.Version, ex);
        }
        if (gameInfo == null) throw new UncivShowableException("The file data seems to be corrupted.");

        CheckVersion(gameInfo);
        gameInfo.SetTransients();
        return gameInfo;
    }

    public GameInfoPreview LoadGamePreviewFromFile(IUncivFileHandle gameFile)
    {
        var preview = FileConversions.ReadJson<GameInfoPreview>(gameFile) ?? throw EmptyFile(gameFile);
        BackwardCompatibility.MigrateCivId(preview);
        return preview;
    }

    /// <summary>The JSON parser does not throw when the file is empty, it just returns null: convert to a nice exception message instead of a NRE.</summary>
    private static JsonException EmptyFile(IUncivFileHandle gameFile) => new($"The file for the game {gameFile.Name} is empty");

    private static void CheckVersion(GameInfo gameInfo)
    {
        // this means there wasn't an immediate error while serializing, but this version will cause other errors later down the line
        if (gameInfo.Version.CompareTo(CompatibilityVersion.CurrentCompatibilityVersion) > 0)
            throw new IncompatibleGameInfoVersionException(gameInfo.Version);
    }

    // endregion

    /// <summary>Kotlin <c>gameInfoFromString</c>.</summary>
    /// <exception cref="IncompatibleGameInfoVersionException">if the data was created by a version of this game that is incompatible with the current one.</exception>
    public static GameInfo GameInfoFromString(string gameData)
    {
        var fixedData = gameData.Trim().Replace("\r", "").Replace("\n", "");
        string unzippedJson;
        try
        {
            unzippedJson = FileConversions.Unzip(fixedData);
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException)
        {
            unzippedJson = fixedData;
        }
        GameInfo? gameInfo;
        try
        {
            gameInfo = UncivJson.Deserialize<GameInfo>(unzippedJson);
        }
        catch (Exception ex)
        {
            Log.Error("Exception while deserializing GameInfo JSON", ex);
            var onlyVersion = UncivJson.Deserialize<GameInfoSerializationVersion>(unzippedJson).NotNull();
            throw new IncompatibleGameInfoVersionException(onlyVersion.Version, ex);
        }
        if (gameInfo == null) throw new UncivShowableException("The file data seems to be corrupted.");

        CheckVersion(gameInfo);
        gameInfo.SetTransients();
        return gameInfo;
    }

    /// <summary>Parses <paramref name="gameData"/> as gzipped serialization of a GameInfoPreview.</summary>
    public static GameInfoPreview GameInfoPreviewFromString(string gameData)
    {
        var preview = UncivJson.Deserialize<GameInfoPreview>(FileConversions.Unzip(gameData)).NotNull();
        BackwardCompatibility.MigrateCivId(preview);
        return preview;
    }

    /// <summary>Serialization of <paramref name="game"/>, optionally gzipped (<paramref name="forceZip"/> overrides <see cref="SaveZipped"/>).</summary>
    public static string GameInfoToString(GameInfo game, bool? forceZip = null, bool updateChecksum = false)
    {
        game.Version = CompatibilityVersion.CurrentCompatibilityVersion;

        if (updateChecksum) game.Checksum = game.CalculateChecksum();
        var plainJson = UncivJson.Serialize(game);

        return forceZip ?? SaveZipped ? FileConversions.Zip(plainJson) : plainJson;
    }

    /// <summary>Gzipped serialization of preview <paramref name="game"/>.</summary>
    public static string GameInfoToString(GameInfoPreview game) => FileConversions.Zip(UncivJson.Serialize(game));

    /// <summary>
    /// Determines whether a filename is acceptable. Forbids trailing and leading blanks; does not deal with forbidden characters
    /// (the client's text field filter does: '\', '/', ':').
    /// </summary>
    public static bool IsValidFileName(string fileName) => fileName.Length != 0 && !fileName.EndsWith(' ') && !fileName.StartsWith(' ');
}

public class Autosaves(UncivFiles files)
{
    /// <summary>Saves <paramref name="gameInfo"/> as "Autosave"; on <paramref name="nextTurn"/> also keeps a numbered copy and purges old ones.</summary>
    /// <param name="gameInfo">The game to save.</param>
    /// <param name="maxAutosavesStored">GameSettings.maxAutosavesStored.</param>
    /// <param name="nextTurn">Also keep a numbered copy.</param>
    public void AutoSave(GameInfo gameInfo, int maxAutosavesStored, bool nextTurn = false)
    {
        try
        {
            files.SaveGame(gameInfo, UncivFiles.AutosaveFileName);
        }
        catch (OutOfMemoryException oom)
        {
            Log.Error("Ran out of memory during autosave", oom);
            return; // not much we can do here
        }

        if (!nextTurn) return;

        // keep auto-saves for the last `maxAutosavesStored` turns
        var newAutosaveFile = files.GetLocalFile(UncivFiles.SaveFilesFolder).Child($"{UncivFiles.AutosaveFileName}-{gameInfo.CurrentPlayer}-{gameInfo.Turns}");
        using (var source = files.GetSave(UncivFiles.AutosaveFileName).Read())
        using (var target = newAutosaveFile.Write(false))
        {
            source.CopyTo(target);
        }

        PurgeOldAutosaves(maxAutosavesStored);
    }

    private void PurgeOldAutosaves(int maxAutosavesStored)
    {
        // Since the latest numbered autosave is a copy of the un-numbered autosave file,
        // we add 1 so `maxAutosavesStored` numbered files AND the un-numbered one are kept (see #12790).
        var oldAutoSaves = files.GetSaves()
            .Where(it => it.Name.StartsWith(UncivFiles.AutosaveFileName, StringComparison.Ordinal))
            .OrderByDescending(it => it.LastModified) // youngest to the top (stable, like sortedByDescending)
            .Skip(Math.Max(maxAutosavesStored + 1, 0)) // dropping those we want to keep
            .Reverse(); // We delete the oldest first - principle of least surprise
        foreach (var file in oldAutoSaves.ToList())
            files.DeleteSave(file);
    }

    public GameInfo LoadLatestAutosave()
    {
        try
        {
            return files.LoadGameByName(UncivFiles.AutosaveFileName);
        }
        catch (Exception)
        {
            // silent fail if we can't read the autosave for any reason - try to load the last autosave by timestamp first
            var autosaves = files.GetSaves().Where(it => it.Name != UncivFiles.AutosaveFileName
                && it.Name.StartsWith(UncivFiles.AutosaveFileName, StringComparison.Ordinal)).ToList();
            // Kotlin maxBy: the first of equal maxima; throws NoSuchElementException on an empty list
            var latest = autosaves.Count == 0 ? throw new InvalidOperationException("Collection is empty.") : autosaves.Aggregate((best, it) => it.LastModified > best.LastModified ? it : best);
            return files.LoadGameFromFile(latest);
        }
    }

    public bool AutosaveExists() => files.GetSave(UncivFiles.AutosaveFileName).Exists;
}

public class IncompatibleGameInfoVersionException(CompatibilityVersion version, Exception? cause = null) : UncivShowableException(
    $"The save was created with an incompatible version of Unciv: [{version.CreatedWith.ToNiceString()}]. " +
    "Please update Unciv to this version or later and try again.",
    cause), IHasGameInfoSerializationVersion
{
    public CompatibilityVersion Version { get; } = version;
}
