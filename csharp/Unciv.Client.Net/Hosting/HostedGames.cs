using System.Text.Json;
using Unciv.Net.Persistence;

namespace Unciv.Client.Net.Hosting;

/// <summary>One saved hosted game: a game directory (<c>game.json</c>, log, snapshots) under the hosted-games folder.</summary>
/// <param name="Directory">The game directory.</param>
/// <param name="GameId">The id in <c>game.json</c>.</param>
/// <param name="Players">Display names of the players bound to a civ, in civ order.</param>
/// <param name="HostBound">Whether the asking player's key owns a civ in this game, so they can host it again.</param>
/// <param name="LastWrittenUtc">When the directory's game file last changed (for sorting and showing).</param>
public sealed record HostedGameInfo(string Directory, string GameId, IReadOnlyList<string> Players, bool HostBound, DateTime LastWrittenUtc);

/// <summary>C034: lists the game directories the in-process host (T205) wrote, newest first, so the host screen can offer them for resuming.</summary>
public static class HostedGames
{
    private const string MetaName = "game.json";

    /// <summary>The saved games under <paramref name="root"/>. A directory without a readable <c>game.json</c> is skipped (it is not a game); a missing root is no games.</summary>
    public static IReadOnlyList<HostedGameInfo> List(string root, string hostPlayerKey)
    {
        if (!Directory.Exists(root)) return [];
        var games = new List<HostedGameInfo>();
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var path = Path.Combine(directory, MetaName);
            if (!File.Exists(path)) continue;
            GameMeta? meta;
            try
            {
                meta = JsonSerializer.Deserialize<GameMeta>(File.ReadAllText(path));
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
            {
                continue;
            }
            if (meta == null) continue;
            var players = meta.Slots.Values.Select(it => it.DisplayName).ToList();
            bool hostBound = meta.Slots.Values.Any(it => string.Equals(it.PlayerKey, hostPlayerKey, StringComparison.Ordinal));
            games.Add(new HostedGameInfo(directory, meta.GameId, players, hostBound, File.GetLastWriteTimeUtc(path)));
        }
        return games.OrderByDescending(it => it.LastWrittenUtc).ThenBy(it => it.Directory, StringComparer.Ordinal).ToList();
    }
}
