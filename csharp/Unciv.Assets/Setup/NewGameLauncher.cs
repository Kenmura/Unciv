using Unciv.Logic;

namespace Unciv.GodotClient.Setup;

/// <summary>The outcome of starting a game from a form: the game and the civ the person plays, or the reasons it did not start.</summary>
public sealed record LaunchResult(GameInfo? Game, string HumanCiv, long Seed, IReadOnlyList<string> Errors)
{
    public bool Ok => Game != null;
}

/// <summary>C015: builds the form's setup and starts it through <see cref="NewGame.Start"/> (validation, then GameStarter). Every failure is a message, not an exception.</summary>
public static class NewGameLauncher
{
    public static LaunchResult Launch(NewGameForm form, long clockMillis)
    {
        var built = form.Build(clockMillis);
        if (built.Setup == null) return Failed(built.Errors);
        try
        {
            var game = NewGame.Start(built.Setup, built.Seed, clockMillis);
            var human = game.Civilizations.FirstOrDefault(it => it.IsHuman());
            return human == null
                ? Failed(["The game started without a human player."])
                : new LaunchResult(game, human.CivName, built.Seed, []);
        }
        catch (UncivShowableException e)
        {
            return Failed([e.Message]);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return Failed(["The game could not be started: " + e.Message]);
        }
    }

    private static LaunchResult Failed(IReadOnlyList<string> errors) => new(null, "", 0, errors);
}
