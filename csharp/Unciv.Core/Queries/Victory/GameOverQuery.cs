using Unciv.View;
using VictoryRuleset = Unciv.Models.Ruleset.Victory;

namespace Unciv.Queries.Victory;

// Status: Playing (nothing to show), Won (the game is over and the viewer is the winner), Lost (the game is over and someone else won) or
//   Defeated (the viewer's civ is destroyed but the game goes on: the viewer may keep watching)
// Winner, VictoryType, VictoryTurn: who won how and when; null / 0 while nobody has won (the result is public once the game is over, as in Kotlin)
// Text: the victory type's own victory or defeat paragraph (a translation key), the default defeat text for Defeated, empty while Playing
public sealed record GameOverResult(string Status, string? Winner, string? VictoryType, int VictoryTurn, string Text);

// C024: what the victory/defeat screen needs to know, and nothing else. It looks only at the shared outcome (VictoryData, OneMoreTurnMode) and at whether
// the viewer's own civ is destroyed. After "One more turn" the game is Playing again (the victory stays recorded, the commands work).
[QueryType("victory.GameOver")]
public sealed record GameOverQuery : IQuery<GameOverResult>
{
    public const string Playing = nameof(Playing);
    public const string Won = nameof(Won);
    public const string Lost = nameof(Lost);
    public const string Defeated = nameof(Defeated);

    public string Type => "victory.GameOver";

    public GameOverResult Run(GameView view)
    {
        var viewer = view.CivView.Unwrap();
        var game = (Unciv.Logic.GameInfo)viewer.GameInfo;
        if (game.VictoryData is { } data && !game.OneMoreTurnMode)
        {
            var victory = game.Ruleset.Victories.GetValueOrDefault(data.VictoryType) ?? new VictoryRuleset(); // the defaults for a victory the ruleset does not define, as Kotlin
            bool won = data.WinningCiv == viewer.CivName;
            return new GameOverResult(won ? Won : Lost, data.WinningCiv, data.VictoryType, data.VictoryTurn, won ? victory.VictoryString : victory.DefeatString);
        }
        if (viewer.IsDefeated())
            return new GameOverResult(Defeated, null, null, 0, new VictoryRuleset().DefeatString);
        return new GameOverResult(Playing, null, null, 0, "");
    }
}
