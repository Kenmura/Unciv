using Unciv.Logic.Tourism;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Logic.Civilization.Managers;

// Brave New World happiness consequences (ticket B019, spec id B070, bnw-spec/SPEC.md section 4.8). Not part of the Kotlin source. Everything here is
// gated by the ruleset's "BNW happiness rules" unique, so Gods & Kings behaves as before (its fixed -50% production / -33% strength global uniques and
// its single "Rebel units may spawn" counter stay in TurnManager).
// Deliberate deviations and unresolved spec items (no decompiler output is available, so [K]/[?] items keep the spec value or a documented choice):
// - Graded penalty: max(-40, 2 x excess) percent for production, gold and unit strength while the excess happiness is negative. The -2 per point and the -40
//   cap are the spec's hard-coded VERY_UNHAPPY_*_PER_UNHAPPY / MAX values (not ModConstants). Gold is the gross gold of each city (before building
//   maintenance); the BNW ruleset no longer carries the fixed G&K uniques.
// - Uprising counter: arming (uprisingCounterMin + rand(uprisingCounterPossible)) is scaled by the speed percent above 100%, halved when the civ
//   has just become very unhappy (the first arming, not the re-arm after rebels spawned) and at least 1. Speed percent is round(Speed.Modifier x 100).
// - Rebels: the number is the spec's formula. City weight @100cc2f0 [?] is population + rand(10). The plot is rated rand(10), +4 forest/jungle, +4 route; the
//   +7 specific feature subtype [?] is not applied. The unit is the strongest buildable land melee unit that is not unique to a nation (first in
//   ruleset order on ties). No barbarian civ in the game: nothing spawns (the counter still re-arms).
// - City revolt (@1016a790 [?]): the receiving civ is the living major civ that follows the preferred ideology and pressures us most (ties: civ order);
//   the city is the non-capital city closest (aerial distance) to one of its cities (ties: city order). A civ with only its capital, or no such
//   civ, loses nothing. The counter is scaled like the uprising counter, without the halving.
// - The RebelsSpawned / CityRevolted events of spec 2.4 have no hook yet: both only notify the civs concerned.
public static class BnwHappinessRules
{
    private const int PenaltyPerPoint = -2, MaxPenalty = -40, DefaultVeryUnhappy = -10, DefaultSuperUnhappy = -20;

    public static bool IsEnabled(Ruleset ruleset) => ((IHasUniques)ruleset.ModOptions).HasUnique(UniqueType.BnwHappinessRules);

    /// <summary>The percent added to production, gold and unit strength: 0 when content or without the BNW rules, else <c>max(-40, 2 x excess)</c> (@1003ea70, @1020fb90).</summary>
    public static int PenaltyPercent(CivilizationObject civ)
    {
        if (!IsEnabled(civ.GameInfo.Ruleset) || !civ.IsMajorCiv()) return 0;
        int excess = civ.GetHappiness();
        
    }

    private static int VeryUnhappyThreshold(Ruleset ruleset) =>
        ruleset.ModOptions.Constants.VeryUnhappyThreshold is var v && v != 0 ? v : DefaultVeryUnhappy;

    private static int SuperUnhappyThreshold(Ruleset ruleset) =>
        ruleset.ModOptions.Constants.SuperUnhappyThreshold is var v && v != 0 ? v : DefaultSuperUnhappy;

    /// <summary>Excess happiness at or below the very unhappy threshold (@10150e00): uprisings.</summary>
    public static bool IsVeryUnhappy(CivilizationObject civ) =>
        IsEnabled(civ.GameInfo.Ruleset) && civ.GetHappiness() <= VeryUnhappyThreshold(civ.GameInfo.Ruleset);

    /// <summary>Excess happiness at or below the super unhappy threshold with opinion unhappiness (@1016c270): city revolt.</summary>
    public static bool IsRevoltingUnhappy(CivilizationObject civ) =>
        IsEnabled(civ.GameInfo.Ruleset) && civ.GetHappiness() <= SuperUnhappyThreshold(civ.GameInfo.Ruleset)
        && PublicOpinionRules.Unhappiness(civ) > 0;

    private static int SpeedPercent(CivilizationObject civ) => (int)(civ.GameInfo.Speed.Modifier * 100f + 0.5f);

    private static int Scaled(CivilizationObject civ, int turns)
    {
        int percent = SpeedPercent(civ);
        return percent > 100 ? unchecked(turns * percent / 100) : turns;
    }

    /// <summary>The turns of a freshly armed uprising counter (@10154110).</summary>
    public static int UprisingCounter(CivilizationObject civ, bool first)
    {
        var constants = civ.GameInfo.Ruleset.ModOptions.Constants;
        int possible = Math.Max(1, constants.UprisingCounterPossible);
        int turns = Scaled(civ, unchecked(constants.UprisingCounterMin + civ.State.StateBasedRandom("BnwHappinessRules.uprisingCounter").NextInt(possible)));
        return Math.Max(1, first ? turns / 2 : turns);
    }

    /// <summary>The turns of a freshly armed city revolt counter (@101545a0).</summary>
    public static int RevoltCounter(CivilizationObject civ) =>
        Math.Max(1, Scaled(civ, civ.GameInfo.Ruleset.ModOptions.Constants.RevoltCounterMin));

    /// <summary>Start of the civ's turn, after the flags counted down: arm the counters that are not running, drop those whose condition ended (spec 2.3 step 3).</summary>
    public static void UpdateCounters(CivilizationObject civ)
    {
        string uprising = CivFlags.RevoltSpawning.ToString(), revolt = CivFlags.CityRevolt.ToString();
        if (!civ.IsMajorCiv() || !civ.IsAlive())
        {
            civ.RemoveFlag(uprising);
            civ.RemoveFlag(revolt);
            return;
        }
        if (!IsVeryUnhappy(civ)) civ.RemoveFlag(uprising);
        else if (!civ.HasFlag(uprising)) civ.AddFlag(uprising, UprisingCounter(civ, first: true));

        if (!IsRevoltingUnhappy(civ)) civ.RemoveFlag(revolt);
        else if (!civ.HasFlag(revolt)) civ.AddFlag(revolt, RevoltCounter(civ));
    }

    // ---- rebels (spec 4.8, @101541b0)

    /// <summary>The uprising counter reached 0: spawn the rebels, then re-arm while still very unhappy.</summary>
    public static void SpawnRebels(CivilizationObject civ)
    {
        string flag = CivFlags.RevoltSpawning.ToString();
        civ.RemoveFlag(flag);
        if (IsVeryUnhappy(civ))
        {
            DoSpawnRebels(civ);
            civ.AddFlag(flag, UprisingCounter(civ, first: false));
        }
    }

    private static void DoSpawnRebels(CivilizationObject civ)
    {
        var game = civ.GameInfo;
        if (civ.Cities.Count == 0 || !game.Civilizations.Any(it => it.IsBarbarian)) return;
        var barbarians = game.GetBarbarianCivilization();
        var constants = game.Ruleset.ModOptions.Constants;
        var random = civ.State.StateBasedRandom("BnwHappinessRules.rebels");

        int count = unchecked(random.NextInt(Math.Max(1, unchecked(civ.Cities.Count * constants.UprisingNumCityCount + 100))) + constants.UprisingNumBase) / 100;
        if (count <= 0) return;

        CityObject? city = null;
        int bestWeight = 0;
        foreach (var candidate in civ.Cities)
        {
            int weight = unchecked(candidate.Population.Population + random.NextInt(10));
            if (city == null || weight > bestWeight) (city, bestWeight) = (candidate, weight);
        }

        TileClass? plot = null;
        int bestRating = 0;
        foreach (var tile in city.NotNull().GetTiles())
        {
            if (tile.IsWater || tile.IsCityCenter() || tile.MilitaryUnit != null || tile.CivilianUnit != null || tile.IsImpassible()) continue;
            int rating = random.NextInt(10);
            if (tile.TerrainFeatures.Contains(Constants.Forest) || tile.TerrainFeatures.Contains(Constants.Jungle)) rating += 4;
            if (tile.RoadStatus != Map.Tiles.RoadStatus.None) rating += 4;
            if (plot == null || rating > bestRating) (plot, bestRating) = (tile, rating);
        }
        if (plot == null) return;

        Models.Ruleset.Unit.BaseUnit? unit = null;
        foreach (var candidate in game.Ruleset.Units.Values)
        {
            if (candidate.UniqueTo != null || !candidate.IsMelee() || !candidate.IsLandUnit
                || ((IHasUniques)candidate).HasUnique(UniqueType.CannotAttack) || !candidate.IsBuildable(civ)) continue;
            if (unit == null || candidate.Strength > unit.Strength) unit = candidate;
        }
        if (unit == null) return;

        for (int i = 0; i < count; i++) game.TileMap.PlaceUnitNearTile(plot.Position, unit, barbarians);
        civ.AddNotification("Your citizens are revolting due to very high unhappiness!", plot.Position, NotificationCategory.General, unit.Name, "StatIcons/Malcontent");
    }

    // ---- city revolt (spec 4.8, @1016c270)

    /// <summary>The revolt counter reached 0: a city leaves for the civ the people prefer, then re-arm while the condition holds.</summary>
    public static void RevoltCity(CivilizationObject civ)
    {
        string flag = CivFlags.CityRevolt.ToString();
        civ.RemoveFlag(flag);
        if (!IsRevoltingUnhappy(civ)) return;
        DoRevoltCity(civ);
        civ.AddFlag(flag, RevoltCounter(civ));
    }

    private static void DoRevoltCity(CivilizationObject civ)
    {
        var opinion = PublicOpinionRules.Evaluate(civ);
        if (opinion.Preferred == null) return;
        CivilizationObject? target = null;
        int best = 0;
        foreach (var pressure in opinion.Pressures)
        {
            if (pressure.Ideology != opinion.Preferred) continue;
            var other = civ.GameInfo.GetCivilization(pressure.Civ);
            if (!other.IsAlive() || other.Cities.Count == 0) continue;
            if (target == null || pressure.Amount > best) (target, best) = (other, pressure.Amount);
        }
        if (target == null) return;

        CityObject? city = null;
        int bestDistance = 0;
        foreach (var candidate in civ.Cities)
        {
            if (candidate.IsCapital()) continue;
            int distance = target.Cities.Min(it => it.GetCenterTile().AerialDistanceTo(candidate.GetCenterTile()));
            if (city == null || distance < bestDistance) (city, bestDistance) = (candidate, distance);
        }
        if (city == null) return;

        string name = city.Name;
        var position = city.Location;
        city.MoveToCiv(target);
        civ.AddNotification($"[{name}] has revolted and joined [{target.CivName}]!", position, NotificationCategory.Cities, "StatIcons/Malcontent");
        target.AddNotification($"[{name}] has revolted against [{civ.CivName}] and joined us!", position, NotificationCategory.Cities, "StatIcons/Malcontent");
    }
}
