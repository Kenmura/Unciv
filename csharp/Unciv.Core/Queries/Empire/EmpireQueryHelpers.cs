using Unciv.Logic.Civilization;
using Unciv.View;

namespace Unciv.Queries.Empire;

internal static class EmpireQueryHelpers
{
    /// <summary>A civ other than the viewer that the viewer has met. Unmet civs are answered like civs that do not exist.</summary>
    public static Civilization KnownCiv(GameView view, string civName)
    {
        var civ = view.CivView.Unwrap();
        var other = civ.GameInfo.GetCivilizationOrNull(civName);
        if (other == null || other == civ || other.IsBarbarian || other.IsSpectator() || !civ.Knows(other) || civ.GetDiplomacyManager(other) == null)
            throw new QueryException(EmpireQueryCodes.UnknownCiv, $"You do not know a civilization \"{civName}\"");
        return other;
    }
}
