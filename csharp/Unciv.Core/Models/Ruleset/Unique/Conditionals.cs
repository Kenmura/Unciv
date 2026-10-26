using Unciv.Logic.Battle;
using Unciv.Logic.Civilization.Managers;
using Unciv.Models.Ruleset.Validation;
using Unciv.Models.StatTypes;
using Unciv.Utils;

namespace Unciv.Models.Ruleset.Unique;

/// <summary>What <c>Conditionals</c> reads from <c>UncivGame.Current.settings</c> (a client global).</summary>
public interface IConditionalsSettings
{
    bool ShowTutorials { get; }
    bool IsTutorialTaskCompleted(string task);
}

/// <summary>
/// Ported from core/src/com/unciv/models/ruleset/unique/Conditionals.kt.
/// </summary>
/// <remarks>
/// Deviations: <c>timeThis</c> profiling is dropped. <c>ConditionalChance</c> hashes the Unique by its text and source instead of the JVM
/// identity hash (<c>Unique</c> has no <c>hashCode</c> in Kotlin, so the original result varies per run): see <see cref="UniqueHash"/>.
/// <c>UncivGame.Current.settings</c> is <see cref="Settings"/>; with none set, tutorials count as disabled.
/// The game classes are the seam interfaces of <c>GameContextConditionals.cs</c>.
/// </remarks>
public static class Conditionals
{
    private sealed class NoSettings : IConditionalsSettings
    {
        public bool ShowTutorials => false;
        public bool IsTutorialTaskCompleted(string task) => false;
    }

    private static volatile IConditionalsSettings CurrentSettings = new NoSettings();
    private static readonly AsyncLocal<IConditionalsSettings?> ScopedSettings = new();

    /// <summary>Stands in for <c>UncivGame.Current.settings</c>. A <see cref="UseSettings"/> scope of the running call chain wins over the global value.</summary>
    public static IConditionalsSettings Settings
    {
        get => ScopedSettings.Value ?? CurrentSettings;
        set => CurrentSettings = value;
    }

    private sealed class SettingsScope(IConditionalsSettings? previous) : IDisposable
    {
        public void Dispose() => ScopedSettings.Value = previous;
    }

    /// <summary>
    /// C054: makes <paramref name="settings"/> the settings of the calling flow only (a query evaluating the viewer's tutorial conditionals must not touch the
    /// process-wide value other hosts in the same process read). Dispose restores the previous scope.
    /// </summary>
    public static IDisposable UseSettings(IConditionalsSettings settings)
    {
        var previous = ScopedSettings.Value;
        ScopedSettings.Value = settings;
        return new SettingsScope(previous);
    }

    // Deterministic stand-in for Unique.hashCode() (JVM identity hash in Kotlin).
    private static int UniqueHash(Unique unique) =>
        Randomness.HashOf(JavaHash.HashCode(unique.Text), unique.SourceObjectType == null ? 0 : JavaHash.HashCode(unique.SourceObjectType.Value.ToString()),
            unique.SourceObjectName == null ? 0 : JavaHash.HashCode(unique.SourceObjectName));

    private static float GetStateBasedRandom(GameContext state, Unique? unique)
    {
        int seed = Randomness.HashOf(state.GameInfo?.Turns ?? 0, unique == null ? 0 : UniqueHash(unique), state.GetHashCode());
        return Randomness.Salted(seed, state.GameInfo?.RandomSalt ?? 0).NextFloat();
    }

    private static float ToFloat(string text) =>
        UniqueParameterTypeExtensions.ToFloatOrNull(text) ?? throw new FormatException($"Invalid float: \"{text}\"");

    private static int ToInt(string text) =>
        UniqueParameterTypeExtensions.ToIntOrNull(text) ?? throw new FormatException($"Invalid int: \"{text}\"");

    public static bool ConditionalApplies(Unique? unique, Unique conditional, GameContext state)
    {
        if (conditional.IsOtherModifierType)
            return true; // not a filtering condition, includes e.g. ModifierHiddenFromUsers

        // Helper to simplify conditional tests requiring gameInfo
        bool CheckOnGameInfo(Func<IGameContextGameInfo, bool> predicate)
        {
            if (state.GameInfo == null) return false;
            return predicate(state.GameInfo);
        }

        // Helper to simplify conditional tests requiring a Civilization
        bool CheckOnCiv(Func<IGameContextCivilization, bool> predicate)
        {
            if (state.RelevantCiv == null) return false;
            return predicate(state.RelevantCiv);
        }

        // Helper to simplify conditional tests requiring a City
        bool CheckOnCity(Func<IGameContextCity, bool> predicate)
        {
            if (state.RelevantCity == null) return false;
            return predicate(state.RelevantCity);
        }

        // Helper to simplify the "compare civ's current era with named era" conditions
        bool CompareEra(string eraParam, Func<int, int, bool> compare)
        {
            if (state.GameInfo == null) return false;
            int? era = state.GameInfo.Ruleset.GetEraNumber(eraParam);
            if (era == null) return false;
            return compare(state.RelevantCiv.NotNull().GetEraNumber(), era.Value);
        }

        // Helper for ConditionalWhenAboveAmountStatResource and its below counterpart
        bool CheckResourceOrStatAmount(string resourceOrStatName, float lowerLimit, float upperLimit, bool modifyByGameSpeed,
            Func<int, float, float, bool> compare)
        {
            if (state.GameInfo == null) return false;
            float gameSpeedModifier = modifyByGameSpeed ? state.GameInfo.Speed.Modifier : 1f;

            if (state.GameInfo.Ruleset.TileResources.ContainsKey(resourceOrStatName))
                return compare(state.GetResourceAmount(resourceOrStatName), lowerLimit * gameSpeedModifier, upperLimit * gameSpeedModifier);
            var stat = StatExtensions.SafeValueOf(resourceOrStatName);
            if (stat == null) return false;
            int statReserve = state.GetStatAmount(stat.Value);

            gameSpeedModifier = modifyByGameSpeed ? state.GameInfo.Speed.GetStatCostModifier(stat.Value) : 1f;
            return compare(statReserve, lowerLimit * gameSpeedModifier, upperLimit * gameSpeedModifier);
        }

        bool CompareCountables2(string first, string second, Func<int, int, bool> compare)
        {
            int? firstNumber = Countables.GetCountableAmount(first, state);
            int? secondNumber = Countables.GetCountableAmount(second, state);
            return firstNumber != null && secondNumber != null && compare(firstNumber.Value, secondNumber.Value);
        }

        bool CompareCountables3(string first, string second, string third, Func<int, int, int, bool> compare)
        {
            int? firstNumber = Countables.GetCountableAmount(first, state);
            int? secondNumber = Countables.GetCountableAmount(second, state);
            int? thirdNumber = Countables.GetCountableAmount(third, state);
            return firstNumber != null && secondNumber != null && thirdNumber != null
                && compare(firstNumber.Value, secondNumber.Value, thirdNumber.Value);
        }

        var parameters = conditional.Params;
        // Kotlin evaluates params[n] lazily inside each branch, so an absent/invalid param only throws in its own branch.
        string P(int index) => parameters[index];

        bool BuildingBuiltBy(IGameContextCivilization civ, string name) => civ.Cities.Any(it => it.ContainsBuildingOrEquivalent(name));

        bool DifficultyCompare(IGameContextGameInfo gameInfo, Func<int, int, bool> compare)
        {
            string difficulty = P(0);
            if (gameInfo.Ruleset.DifficultyNames.Contains(difficulty))
            {
                var difficulties = gameInfo.Ruleset.DifficultyNames;
                return compare(IndexOf(difficulties, gameInfo.GetDifficultyName()), IndexOf(difficulties, difficulty));
            }
            return false;
        }

        switch (conditional.Type)
        {
            case UniqueType.ConditionalChance: return GetStateBasedRandom(state, unique) < ToFloat(P(0)) / 100f;
            case UniqueType.ConditionalEveryTurns: return CheckOnGameInfo(g => g.Turns % ToInt(P(0)) == 0);
            case UniqueType.ConditionalBeforeTurns: return CheckOnGameInfo(g => g.Turns < ToInt(P(0)));
            case UniqueType.ConditionalAfterTurns: return CheckOnGameInfo(g => g.Turns >= ToInt(P(0)));
            case UniqueType.ConditionalTutorialsEnabled: return Settings.ShowTutorials;
            case UniqueType.ConditionalTutorialCompleted: return Settings.IsTutorialTaskCompleted(P(0));

            case UniqueType.ConditionalCivFilter: return CheckOnCiv(c => c.MatchesFilter(P(0), state));
            case UniqueType.ConditionalWar: return CheckOnCiv(c => c.IsAtWar());
            case UniqueType.ConditionalNotWar: return CheckOnCiv(c => !c.IsAtWar());
            case UniqueType.ConditionalWithResource: return state.GetResourceAmount(P(0)) > 0;
            case UniqueType.ConditionalWithoutResource: return state.GetResourceAmount(P(0)) <= 0;

            case UniqueType.ConditionalWhenAboveAmountStatResource:
                return CheckResourceOrStatAmount(P(1), ToFloat(P(0)), float.MaxValue, unique?.IsModifiedByGameSpeed() == true,
                    (current, lowerLimit, _) => current > lowerLimit);
            case UniqueType.ConditionalWhenBelowAmountStatResource:
                // Kotlin Float.MIN_VALUE is the smallest positive float, i.e. float.Epsilon (not float.MinValue)
                return CheckResourceOrStatAmount(P(1), float.Epsilon, ToFloat(P(0)), unique?.IsModifiedByGameSpeed() == true,
                    (current, _, upperLimit) => current < upperLimit);
            case UniqueType.ConditionalWhenBetweenStatResource:
                return CheckResourceOrStatAmount(P(2), ToFloat(P(0)), ToFloat(P(1)), unique?.IsModifiedByGameSpeed() == true,
                    (current, lowerLimit, upperLimit) => current >= lowerLimit && current <= upperLimit);

            case UniqueType.ConditionalHappy: return CheckOnCiv(c => c.Happiness >= 0);
            case UniqueType.ConditionalGoldenAge: return CheckOnCiv(c => c.IsGoldenAge());
            case UniqueType.ConditionalNotGoldenAge: return CheckOnCiv(c => !c.IsGoldenAge());

            case UniqueType.ConditionalBeforeEra: return CompareEra(P(0), (current, param) => current < param);
            case UniqueType.ConditionalStartingFromEra: return CompareEra(P(0), (current, param) => current >= param);
            case UniqueType.ConditionalDuringEra: return CompareEra(P(0), (current, param) => current == param);
            case UniqueType.ConditionalIfStartingInEra: return CheckOnGameInfo(g => g.GameParameters.StartingEra == P(0));
            case UniqueType.ConditionalSpeed: return CheckOnGameInfo(g => g.GameParameters.Speed == P(0));
            case UniqueType.ConditionalDifficulty: return CheckOnGameInfo(g => g.GameParameters.Difficulty == P(0));
            case UniqueType.ConditionalDifficultyOrHigher: return CheckOnGameInfo(g => DifficultyCompare(g, (a, b) => a >= b));
            case UniqueType.ConditionalDifficultyOrLower: return CheckOnGameInfo(g => DifficultyCompare(g, (a, b) => a <= b));
            case UniqueType.ConditionalVictoryEnabled: return CheckOnGameInfo(g => g.GameParameters.VictoryTypes.Contains(P(0)));
            case UniqueType.ConditionalVictoryDisabled: return CheckOnGameInfo(g => !g.GameParameters.VictoryTypes.Contains(P(0)));
            case UniqueType.ConditionalReligionEnabled: return CheckOnGameInfo(g => g.IsReligionEnabled());
            case UniqueType.ConditionalReligionDisabled: return CheckOnGameInfo(g => !g.IsReligionEnabled());
            case UniqueType.ConditionalEspionageEnabled: return CheckOnGameInfo(g => g.IsEspionageEnabled());
            case UniqueType.ConditionalEspionageDisabled: return CheckOnGameInfo(g => !g.IsEspionageEnabled());
            case UniqueType.ConditionalNuclearWeaponsEnabled: return CheckOnGameInfo(g => g.GameParameters.NuclearWeaponsEnabled);
            case UniqueType.ConditionalNuclearWeaponsDisabled: return CheckOnGameInfo(g => !g.GameParameters.NuclearWeaponsEnabled);
            case UniqueType.ConditionalTech:
                return CheckOnCiv(c =>
                {
                    string filter = P(0);
                    if (c.GameInfo.NotNull().Ruleset.Technologies.ContainsKey(filter)) return c.IsTechResearched(P(0)); // fast common case
                    return c.AnyResearchedTechnologyMatchesFilter(filter);
                });
            case UniqueType.ConditionalNoTech:
                return CheckOnCiv(c =>
                {
                    string filter = P(0);
                    if (c.GameInfo.NotNull().Ruleset.Technologies.ContainsKey(filter)) return !c.IsTechResearched(P(0)); // fast common case
                    return !c.AnyResearchedTechnologyMatchesFilter(filter);
                });
            case UniqueType.ConditionalWhileResearching: return CheckOnCiv(c => c.CurrentTechnologyMatchesFilter(P(0)));
            case UniqueType.ConditionalNoCivAdopted:
                return CheckOnGameInfo(g => !g.Civilizations.Any(it =>
                    it.IsMajorCiv() && it.IsAlive() && (it.IsPolicyAdopted(P(0)) || it.Religion?.HasBelief(P(0)) == true)));
            case UniqueType.ConditionalAfterPolicyOrBelief:
                return CheckOnCiv(c => c.IsPolicyAdopted(P(0)) || c.Religion?.HasBelief(P(0)) == true);
            case UniqueType.ConditionalBeforePolicyOrBelief:
                return CheckOnCiv(c => !c.IsPolicyAdopted(P(0)) && c.Religion?.HasBelief(P(0)) != true);
            case UniqueType.ConditionalBeforePantheon: return CheckOnCiv(c => c.ReligionState == ReligionState.None);
            case UniqueType.ConditionalAfterPantheon: return CheckOnCiv(c => c.ReligionState != ReligionState.None);
            case UniqueType.ConditionalBeforeReligion: return CheckOnCiv(c => c.ReligionState < ReligionState.Religion);
            case UniqueType.ConditionalAfterReligion: return CheckOnCiv(c => c.ReligionState >= ReligionState.Religion);
            case UniqueType.ConditionalBeforeEnhancingReligion: return CheckOnCiv(c => c.ReligionState < ReligionState.EnhancedReligion);
            case UniqueType.ConditionalAfterEnhancingReligion: return CheckOnCiv(c => c.ReligionState >= ReligionState.EnhancedReligion);
            case UniqueType.ConditionalAfterGeneratingGreatProphet: return CheckOnCiv(c => c.GreatProphetsEarned() > 0);

            case UniqueType.ConditionalBuildingBuilt: return CheckOnCiv(c => BuildingBuiltBy(c, P(0)));
            case UniqueType.ConditionalBuildingNotBuilt: return CheckOnCiv(c => !BuildingBuiltBy(c, P(0)));
            case UniqueType.ConditionalBuildingBuiltAll:
                return CheckOnCiv(c => c.Cities.Where(it => it.MatchesFilter(P(1), null)).All(it => it.ContainsBuildingOrEquivalent(P(0))));
            case UniqueType.ConditionalBuildingBuiltAmount:
                return CheckOnCiv(c => c.Cities.Count(it => it.ContainsBuildingOrEquivalent(P(0)) && it.MatchesFilter(P(2), null)) >= ToInt(P(1)));
            case UniqueType.ConditionalBuildingBuiltByAnybody:
                return CheckOnGameInfo(g => g.GetCities().Any(it => it.ContainsBuildingOrEquivalent(P(0))));
            case UniqueType.ConditionalBuildingNotBuiltByAnybody:
                return !CheckOnGameInfo(g => g.GetCities().Any(it => it.ContainsBuildingOrEquivalent(P(0))));

            // Filtered via city.getMatchingUniques
            case UniqueType.ConditionalInThisCity: return state.RelevantCity != null;
            case UniqueType.ConditionalCityFilter: return CheckOnCity(c => c.MatchesFilter(P(0), state.RelevantCiv));
            case UniqueType.ConditionalCityConnected: return CheckOnCity(c => c.IsConnectedToCapital());
            case UniqueType.ConditionalCityReligion:
                return CheckOnCity(c => c.GetMajorityReligion()?.MatchesFilter(P(0), state, state.RelevantCiv) == true);
            case UniqueType.ConditionalCityNotReligion:
                return CheckOnCity(c => c.GetMajorityReligion()?.MatchesFilter(P(0), state, state.RelevantCiv) != true);
            case UniqueType.ConditionalCityMajorReligion: return CheckOnCity(c => c.GetMajorityReligion()?.IsMajorReligion() == true);
            case UniqueType.ConditionalCityEnhancedReligion: return CheckOnCity(c => c.GetMajorityReligion()?.IsEnhancedReligion() == true);
            case UniqueType.ConditionalCityThisReligion:
                return CheckOnCity(c => Equals(c.GetMajorityReligion(), state.RelevantCiv?.Religion));
            case UniqueType.ConditionalWLTKD: return CheckOnCity(c => c.IsWeLoveTheKingDayActive());
            case UniqueType.ConditionalCityWithBuilding: return CheckOnCity(c => c.ContainsBuildingOrEquivalent(P(0)));
            case UniqueType.ConditionalCityWithoutBuilding: return CheckOnCity(c => !c.ContainsBuildingOrEquivalent(P(0)));
            case UniqueType.ConditionalPopulationFilter: return CheckOnCity(c => c.GetPopulationFilterAmount(P(1)) >= ToInt(P(0)));
            case UniqueType.ConditionalExactPopulationFilter: return CheckOnCity(c => c.GetPopulationFilterAmount(P(1)) == ToInt(P(0)));
            case UniqueType.ConditionalBetweenPopulationFilter:
                return CheckOnCity(c =>
                {
                    int amount = c.GetPopulationFilterAmount(P(2));
                    return amount >= ToInt(P(0)) && amount <= ToInt(P(1));
                });
            case UniqueType.ConditionalBelowPopulationFilter: return CheckOnCity(c => c.GetPopulationFilterAmount(P(1)) < ToInt(P(0)));
            case UniqueType.ConditionalWhenGarrisoned: return CheckOnCity(c => c.CenterTileMilitaryUnitCanGarrison());

            case UniqueType.ConditionalVsCity: return state.TheirCombatant?.MatchesFilter("City", false) == true;
            case UniqueType.ConditionalVsUnits:
            case UniqueType.ConditionalVsCombatant:
                return state.TheirCombatant?.MatchesFilter(P(0), true) == true;
            case UniqueType.ConditionalOurUnit:
            case UniqueType.ConditionalOurUnitOnUnit:
                return state.RelevantUnit?.MatchesFilter(P(0)) == true;
            case UniqueType.ConditionalUnitWithPromotion:
                return state.RelevantUnit != null && state.RelevantUnit.HasPromotionOrStatus(P(0));
            case UniqueType.ConditionalUnitWithoutPromotion:
                return state.RelevantUnit != null && !state.RelevantUnit.HasPromotionOrStatus(P(0));
            case UniqueType.ConditionalAttacking: return state.CombatAction == CombatAction.Attack;
            case UniqueType.ConditionalDefending: return state.CombatAction == CombatAction.Defend;
            case UniqueType.ConditionalAboveHP:
                return (state.RelevantUnit != null && state.RelevantUnit.Health > ToInt(P(0)))
                    || (state.OurCombatant != null && state.OurCombatant.GetHealth() > ToInt(P(0)));
            case UniqueType.ConditionalBelowHP:
                return (state.RelevantUnit != null && state.RelevantUnit.Health < ToInt(P(0)))
                    || (state.OurCombatant != null && state.OurCombatant.GetHealth() < ToInt(P(0)));
            case UniqueType.ConditionalAboveMovement:
                return state.RelevantUnit != null && state.RelevantUnit.CurrentMovement > ToInt(P(0));
            case UniqueType.ConditionalBelowMovement:
                return state.RelevantUnit != null && state.RelevantUnit.CurrentMovement < ToInt(P(0));
            case UniqueType.ConditionalHasNotUsedOtherActions:
                return state.Unit == null // So we get the action as a valid action in BaseUnit.hasUnique()
                    || state.Unit.AbilityToTimesUsedIsEmpty;
            case UniqueType.ConditionalStackedWithUnit:
                return state.RelevantUnit != null
                    && state.RelevantUnit.GetTile().GetUnits().Any(it => !Equals(it, state.RelevantUnit) && it.MatchesFilter(P(0)));
            case UniqueType.ConditionalNotStackedWithUnit:
                return state.RelevantUnit == null
                    || !state.RelevantUnit.GetTile().GetUnits().Any(it => !Equals(it, state.RelevantUnit) && it.MatchesFilter(P(0)));

            case UniqueType.ConditionalInTiles: return state.RelevantTile?.MatchesFilter(P(0), state.RelevantCiv) == true;
            case UniqueType.ConditionalInTilesNot: return state.RelevantTile?.MatchesFilter(P(0), state.RelevantCiv) == false;
            case UniqueType.ConditionalAdjacentTo: return state.RelevantTile?.IsAdjacentTo(P(0), state.RelevantCiv) == true;
            case UniqueType.ConditionalNotAdjacentTo: return state.RelevantTile?.IsAdjacentTo(P(0), state.RelevantCiv) == false;
            case UniqueType.ConditionalFightingInTiles: return state.AttackedTile?.MatchesFilter(P(0), state.RelevantCiv) == true;
            case UniqueType.ConditionalNearTiles:
                return state.RelevantTile != null
                    && state.RelevantTile.GetTilesInDistance(ToInt(P(0))).Any(it => it.MatchesFilter(P(1), state.RelevantCiv));

            case UniqueType.ConditionalVsLargerCiv:
                {
                    int yourCities = state.RelevantCiv?.Cities.Count ?? 1;
                    int theirCities = state.TheirCombatant?.GetCivInfo().Cities.Count ?? 0;
                    return yourCities < theirCities;
                }
            case UniqueType.ConditionalForeignContinent:
                return CheckOnCiv(c =>
                    state.RelevantTile != null && (
                        c.Cities.Count == 0 || c.GetCapital() == null
                        || c.GetCapital().NotNull().GetCenterTileOrNull().NotNull().GetContinent() != state.RelevantTile.GetContinent()));
            case UniqueType.ConditionalAdjacentUnit:
                return state.RelevantCiv != null
                    && state.RelevantUnit != null
                    && state.RelevantTile.NotNull().Neighbors.Any(tile => tile.GetUnits().Any(it =>
                        !Equals(it, state.RelevantUnit) && Equals(it.Civ, state.RelevantCiv) && it.MatchesFilter(P(0))));

            case UniqueType.ConditionalNeighborTiles:
                if (state.RelevantTile == null) return false;
                {
                    int count = state.RelevantTile.Neighbors.Count(it => it.MatchesFilter(P(2), state.RelevantCiv));
                    return count >= ToInt(P(0)) && count <= ToInt(P(1));
                }

            case UniqueType.ConditionalOnWaterMaps: return state.Region?.ContinentId == -1;
            case UniqueType.ConditionalInRegionOfType: return state.Region?.Type == P(0);
            case UniqueType.ConditionalInRegionExceptOfType: return state.Region?.Type != P(0);

            case UniqueType.ConditionalFirstCivToResearch:
                return unique != null
                    && unique.SourceObjectType == UniqueTarget.Tech
                    && CheckOnGameInfo(g => !g.Civilizations.Any(it =>
                        !Equals(it, state.RelevantCiv) && it.IsMajorCiv()
                        && it.IsTechResearched(unique.SourceObjectName.NotNull()))); // guarded by the sourceObjectType check
            case UniqueType.ConditionalFirstCivToAdopt:
                return unique != null
                    && unique.SourceObjectType == UniqueTarget.Policy
                    && CheckOnGameInfo(g => !g.Civilizations.Any(it =>
                        !Equals(it, state.RelevantCiv) && it.IsMajorCiv()
                        && it.IsPolicyAdopted(unique.SourceObjectName.NotNull()))); // guarded by the sourceObjectType check

            case UniqueType.ConditionalCountableEqualTo: return CompareCountables2(P(0), P(1), (a, b) => a == b);
            case UniqueType.ConditionalCountableDifferentThan: return CompareCountables2(P(0), P(1), (a, b) => a != b);
            case UniqueType.ConditionalCountableMoreThan: return CompareCountables2(P(0), P(1), (a, b) => a > b);
            case UniqueType.ConditionalCountableLessThan: return CompareCountables2(P(0), P(1), (a, b) => a < b);
            case UniqueType.ConditionalCountableBetween:
                return CompareCountables3(P(0), P(1), P(2), (first, second, third) => first >= second && first <= third);

            case UniqueType.ConditionalWhenCarriedBy:
                {
                    // Check if the unit is currently transported and being carried by matching filter
                    if (state.RelevantUnit == null || !state.RelevantUnit.IsTransported) return false;
                    var carrier = state.RelevantUnit.GetTile().MilitaryUnit;
                    // Only true if: 1) carrier exists, 2) carrier is NOT the unit itself, 3) carrier matches filter
                    return carrier != null && !Equals(carrier, state.RelevantUnit) && carrier.MatchesFilter(P(0));
                }

            case UniqueType.ConditionalModEnabled:
                return CheckOnGameInfo(g => ModsAndBase(g).Any(it => ModCompatibility.ModNameFilter(it, P(0))));
            case UniqueType.ConditionalModNotEnabled:
                return CheckOnGameInfo(g => !ModsAndBase(g).Any(it => ModCompatibility.ModNameFilter(it, P(0))));

            default: return false;
        }
    }

    private static IEnumerable<string> ModsAndBase(IGameContextGameInfo gameInfo) =>
        gameInfo.GameParameters.Mods.Concat([gameInfo.GameParameters.BaseRuleset]);

    // Kotlin List.indexOf: -1 when absent
    private static int IndexOf(IReadOnlyList<string> list, string item)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i] == item) return i;
        return -1;
    }
}
