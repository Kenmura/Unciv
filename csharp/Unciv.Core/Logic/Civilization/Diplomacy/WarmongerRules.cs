using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Logic.Civilization.Diplomacy;

/// <summary>
/// BNW warmongering scale (B023, bnw-spec 4.12): the penalty other civs hold against an aggressor is multiplied by the aggressor's
/// <c>Era.WarmongerPercent</c> (50% Ancient to 100% Modern) and, when the victim is a city-state, by
/// <c>ModConstants.WarmongerOnCityStateMultiplier</c> (50). G&amp;K states neither (100% / 0 = off), so its penalties are unchanged.
/// </summary>
public static class WarmongerRules
{
    public static float Scale(float amount, CivilizationObject aggressor, CivilizationObject victim)
    {
        float scaled = amount * aggressor.GetEra().WarmongerPercent / 100f;
        int csMultiplier = aggressor.GameInfo.Ruleset.ModOptions.Constants.WarmongerOnCityStateMultiplier;
        if (victim.IsCityState && csMultiplier > 0) scaled = scaled * csMultiplier / 100f;
        return scaled;
    }
}
