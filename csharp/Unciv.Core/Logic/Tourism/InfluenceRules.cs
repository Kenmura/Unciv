using Unciv.Utils;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Logic.Tourism;

// Brave New World (ticket B008, spec id B031, bnw-spec/SPEC.md section 4.3.3, 4.3.5 and 4.3.6). Not part of the Kotlin source.
//
// Influence of one civ over another: accumulation, level, trend, turns to Influential, the benefits of each level and the tourism blast.
// Deliberate deviations and unresolved spec items (no decompiler output or SDK source is available, so [K]/[?] items keep the spec value):
// - The percentage and the trend multiply in 64 bits (influence * 100 and the cross products overflow an Int long before a game ends).
// - The level benefits of 4.3.5 are [K]: Familiar and above give +1 science per trade route, Exotic and above +1 spy rank in the civ. The
//   spy rank of city-states, the shorter resistance after conquest (Popular) and no revolt of conquered cities (Influential) are not
//   decoded and not implemented (B020 reads the spy rank bonus, the happiness ticket B019 the others).
// - InfluenceSpreadModifier (Internet) and NullifiesForeignInfluence (Great Firewall) have unresolved maths (4.3.4 [?]) and are not applied.
// - The blast applies the game speed as Speed.Modifier * 100 (the spec says "speed%" without naming the field).
// - The one-shot tourism of Policy_TourismOnUnitCreation (a Great Person's birth) is not applied (B011 owns the Great Person actions).

/// <summary>How well a civ knows another (spec 4.3.3; the value is the engine's level number).</summary>
public enum InfluenceLevel
{
    Unknown = 0,
    Exotic = 1,
    Familiar = 2,
    Popular = 3,
    Influential = 4,
    Dominant = 5,
}

/// <summary>Whether the share of the target's culture we hold is growing (spec 4.3.3).</summary>
public enum InfluenceTrend
{
    Falling = -1,
    Static = 0,
    Rising = 1,
}

public static class InfluenceRules
{
    /// <summary>The "turns to Influential" answer when it never happens (@10073ec0).</summary>
    public const int NeverInfluential = 999;

    /// <summary>[K] Science added to each trade route to a civ we are at least Familiar with.</summary>
    public const int FamiliarRouteScience = 1;

    /// <summary>[K] Spy rank added in a civ we are at least Exotic with.</summary>
    public const int ExoticSpyRank = 1;

    /// <summary>Can <paramref name="me"/> have influence over <paramref name="target"/>: tourism is on, both are living major civs, they have met.</summary>
    public static bool CanInfluence(CivilizationObject me, CivilizationObject target) =>
        TourismRules.IsEnabled(me) && me != target && me.IsMajorCiv() && me.IsAlive() && target.IsMajorCiv() && target.IsAlive() && me.Knows(target);

    public static int InfluenceOn(CivilizationObject me, CivilizationObject target) => me.Culture?.InfluenceOn(target.CivId) ?? 0;

    /// <summary>The influence per turn over a civ: all tourism, scaled by the modifier toward it (spec 4.3.3), at least 0.</summary>
    public static int InfluencePerTurn(CivilizationObject me, CivilizationObject target) =>
        InfluencePerTurn(TourismRules.PlayerTourism(me), TourismRules.ModifierWith(me, target));

    internal static int InfluencePerTurn(int tourism, int modifier) => unchecked(tourism * Math.Max(0, 100 + modifier) / 100);

    /// <summary>Influence as a percentage of the target's lifetime culture, 0 when that is 0 (@1006e410).</summary>
    public static int Percent(CivilizationObject me, CivilizationObject target)
    {
        int lifetime = target.TotalCultureForContests;
        return lifetime <= 0 ? 0 : (int)(InfluenceOn(me, target) * 100L / lifetime);
    }

    public static InfluenceLevel Level(CivilizationObject me, CivilizationObject target)
    {
        if (!TourismRules.IsEnabled(me)) return InfluenceLevel.Unknown;
        var constants = me.GameInfo.Ruleset.ModOptions.Constants;
        int percent = Percent(me, target);
        if (percent >= constants.CultureLevelDominant) return InfluenceLevel.Dominant;
        if (percent >= constants.CultureLevelInfluential) return InfluenceLevel.Influential;
        if (percent >= constants.CultureLevelPopular) return InfluenceLevel.Popular;
        if (percent >= constants.CultureLevelFamiliar) return InfluenceLevel.Familiar;
        if (percent >= constants.CultureLevelExotic) return InfluenceLevel.Exotic;
        return InfluenceLevel.Unknown;
    }

    /// <summary>Compares the share now with the share at the start of last turn by cross multiplication (@1006c4d0); Static unless both lifetimes are positive.</summary>
    public static InfluenceTrend Trend(CivilizationObject me, CivilizationObject target)
    {
        var entry = me.Culture?.Find(target.CivId);
        int lifetimeNow = target.TotalCultureForContests;
        if (entry == null || lifetimeNow <= 0 || entry.LastTurnLifetimeCulture <= 0) return InfluenceTrend.Static;
        long now = (long)entry.Influence * entry.LastTurnLifetimeCulture;
        long before = (long)entry.LastTurnInfluence * lifetimeNow;
        return now > before ? InfluenceTrend.Rising : now < before ? InfluenceTrend.Falling : InfluenceTrend.Static;
    }

    /// <summary>Turns until the civ is Influential over the target at the current rates (@10073ec0), <see cref="NeverInfluential"/> when it is not rising or never gets there.</summary>
    public static int TurnsToInfluential(CivilizationObject me, CivilizationObject target)
    {
        if (Level(me, target) >= InfluenceLevel.Influential || Trend(me, target) != InfluenceTrend.Rising) return NeverInfluential;
        int influential = me.GameInfo.Ruleset.ModOptions.Constants.CultureLevelInfluential;
        long needed = influential * (long)target.TotalCultureForContests / 100 - InfluenceOn(me, target);
        long rate = InfluencePerTurn(me, target) - influential * (long)KotlinMath.FloatToInt(target.Stats.StatsForNextTurn.Culture) / 100;
        if (rate <= 0) return NeverInfluential;
        return needed <= 0 ? 0 : (int)Math.Min(int.MaxValue, (needed + rate - 1) / rate);
    }

    /// <summary>The living major civ with the smallest share (<see cref="Percent"/>) among those we are not yet Influential over, first in civ order on a tie (@1006e5d0).</summary>
    public static CivilizationObject? LeastInfluenced(CivilizationObject me, bool onlyMet)
    {
        CivilizationObject? best = null;
        int bestPercent = int.MaxValue;
        foreach (var other in me.GameInfo.Civilizations)
        {
            if (other == me || !other.IsMajorCiv() || !other.IsAlive() || (onlyMet && !me.Knows(other))) continue;
            if (Level(me, other) >= InfluenceLevel.Influential) continue;
            int percent = Percent(me, other);
            if (percent < bestPercent) { best = other; bestPercent = percent; }
        }
        return best;
    }

    // ---- turn (spec 2.3 step 2)

    /// <summary>Start of <paramref name="me"/>'s turn: remember last turn's numbers, add this turn's influence over every met living major civ, count the tourism boost down.</summary>
    public static void StartTurn(CivilizationObject me)
    {
        if (!TourismRules.IsEnabled(me) || !me.IsMajorCiv() || !me.IsAlive()) return;
        var culture = me.Culture ??= new();
        int tourism = TourismRules.PlayerTourism(me);
        foreach (var target in me.GameInfo.Civilizations)
        {
            if (!CanInfluence(me, target)) continue;
            var entry = culture.GetOrAdd(target.CivId);
            entry.LastTurnInfluence = entry.Influence;
            entry.LastTurnLifetimeCulture = target.TotalCultureForContests;
            entry.Influence = unchecked(entry.Influence + InfluencePerTurn(tourism, TourismRules.ModifierWith(me, target)));
        }
        if (culture.TourismBonusTurns > 0) culture.TourismBonusTurns--;
    }

    // ---- benefits of the levels (4.3.5)

    /// <summary>Science a trade route of <paramref name="me"/> to <paramref name="target"/> gains, in hundredths (the engine's route yields are Times100).</summary>
    public static int TradeRouteScienceBonusTimes100(CivilizationObject me, CivilizationObject target) =>
        CanInfluence(me, target) && Level(me, target) >= InfluenceLevel.Familiar ? FamiliarRouteScience * 100 : 0;

    /// <summary>Spy rank added to a spy of <paramref name="me"/> in <paramref name="target"/>.</summary>
    public static int MajorCivSpyRankBonus(CivilizationObject me, CivilizationObject target) =>
        CanInfluence(me, target) && Level(me, target) >= InfluenceLevel.Exotic ? ExoticSpyRank : 0;

    // ---- tourism blast (4.3.6)

    /// <summary>Strength of a blast by a unit with <paramref name="oneShotTourism"/> (unique <c>Gives [x] Tourism with the target civ ...</c>): the civ's tourism per turn times it times the speed, at least the minimum.</summary>
    public static int BlastStrength(CivilizationObject me, int oneShotTourism)
    {
        int speedPercent = KotlinMath.RoundToInt(me.GameInfo.Speed.Modifier * 100f);
        long strength = TourismRules.PlayerTourism(me) * (long)oneShotTourism * speedPercent / 100;
        int minimum = me.GameInfo.Ruleset.ModOptions.Constants.MiniumumTourismBlastStrength;
        return (int)Math.Clamp(Math.Max(strength, minimum), int.MinValue, int.MaxValue);
    }

    /// <summary>
    /// Applies a blast: <paramref name="target"/> receives the whole strength as influence, every other met living major civ
    /// <paramref name="percentOthers"/> percent of it ([K], spec 4.3.6). Returns the strength, 0 when the blast reaches nobody.
    /// </summary>
    public static int ApplyBlast(CivilizationObject me, CivilizationObject target, int oneShotTourism, int percentOthers)
    {
        int strength = BlastStrength(me, oneShotTourism);
        if (!CanInfluence(me, target)) return 0;
        var culture = me.Culture ??= new();
        culture.AddInfluence(target.CivId, strength);
        foreach (var other in me.GameInfo.Civilizations)
            if (other != target && CanInfluence(me, other)) culture.AddInfluence(other.CivId, (int)(strength * (long)percentOthers / 100));
        return strength;
    }
}
