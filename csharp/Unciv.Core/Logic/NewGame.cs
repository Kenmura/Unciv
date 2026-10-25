using Unciv.Logic.Civilization;
using Unciv.Logic.Files;
using Unciv.Models.Metadata;
using Unciv.Queries.Setup;

namespace Unciv.Logic;

// PlayerId: the player key id of the human taking the slot (host lobby); Nation: a nation name or "Random"
public sealed record LobbySlot(string Nation, string PlayerId = "");

// T199: the entry point for starting a game from setup data. Thin: validates, then GameStarter.StartNewGame. The seed (from the host)
// and the clock are explicit inputs; the engine never reads either itself. GameStarter draws 'Random' nations and the random number of
// players from its seeded RNG, so the same (setup, seed, clock) always gives the same game.
public static class NewGame
{
    // Returns the started game; throws UncivShowableException with the first validation errors when the setup is not valid.
    public static GameInfo Start(GameSetupInfo setup, long seed, long clockMillis, bool isHostedGame = false,
        Func<string, IUncivFileHandle>? openMapFile = null)
    {
        var validation = new ValidateSetupQuery(setup, isHostedGame).Run();
        if (!validation.Ok)
            throw new UncivShowableException(string.Join("\n", validation.Errors.Select(it => it.Message)));
        return GameStarter.StartNewGame(setup, seed, clockMillis, openMapFile);
    }

    // Hosted lobby: one human player per slot with its player key id (gameParameters.players is replaced). Returns a clone, the input is untouched.
    public static GameSetupInfo ApplyLobby(GameSetupInfo setup, IEnumerable<LobbySlot> slots)
    {
        var result = setup.Clone();
        result.GameParameters.IsOnlineMultiplayer = true;
        result.GameParameters.Players = slots
            .Select(it => new Player(it.Nation, PlayerType.Human, it.PlayerId))
            .ToList();
        return result;
    }

    // Inverse of ApplyLobby: the human slots of a setup.
    public static IReadOnlyList<LobbySlot> ToLobby(GameSetupInfo setup) =>
        setup.GameParameters.Players
            .Where(it => it.PlayerType == PlayerType.Human)
            .Select(it => new LobbySlot(it.ChosenCiv, it.PlayerId))
            .ToList();
}
