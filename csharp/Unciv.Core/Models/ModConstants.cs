using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using Unciv.Utils;

namespace Unciv.Models;

// core/src/com/unciv/models/ModConstants.kt

/// <summary>
/// Used as a member of ModOptions for moddable "constants" - factors in formulae and such.
/// When combining mods, this is <see cref="Merge"/>d _per constant/field_, not as entire object like other RulesetObjects.
/// Merging happens on a very simple basis: If a Mod comes with a non-default value, it is copied, otherwise the parent value is left intact.
/// If several mods change the same field, the last one wins.
/// </summary>
/// <remarks>
/// <see cref="Merge"/>, <see cref="Equals(object?)"/>, <see cref="GetHashCode"/> and <see cref="ToString"/> go through reflection over the properties in
/// declaration order, like upstream does over the Java fields, so adding a property needs no change there. The semantics of Java's boxed
/// <c>equals</c>/<c>hashCode</c>/<c>toString</c> are kept: <c>Float</c>/<c>Double</c> compare by bits (<c>NaN</c> equals <c>NaN</c>, <c>0.0</c> differs from
/// <c>-0.0</c>), hash codes are the JVM's, and values print the way <c>Double.toString</c> does. All members must conform to the equality contract.
/// </remarks>
public sealed class ModConstants
{
    private static readonly ImmutableArray<PropertyInfo> Fields = [.. typeof(ModConstants)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .OrderBy(property => property.MetadataToken)];

    /// <summary>As merge will need a default instance repeatedly, store it as static.</summary>
    public static ModConstants Defaults { get; } = new();

    // Max amount of experience that can be gained from combat with barbarians
    [JsonPropertyName("maxXPfromBarbarians")]
    public int MaxXPfromBarbarians { get; set; } = 30;

    // Formula for city Strength:
    // Strength = baseStrength + strengthPerPop + strengthFromTiles +
    //            ((%techs * multiplier) ^ exponent) * fullMultiplier +
    //            (garrisonBonus * garrisonUnitStrength * garrisonUnitHealth/100) +
    //            defensiveBuildingStrength
    // where %techs is the percentage of techs in the tech tree that are complete
    // If no techs exist in this ruleset, %techs = 0.5 (=50%)
    [JsonPropertyName("cityStrengthBase")]
    public double CityStrengthBase { get; set; } = 8.0;

    [JsonPropertyName("cityStrengthPerPop")]
    public double CityStrengthPerPop { get; set; } = 0.4;

    [JsonPropertyName("cityStrengthFromTechsMultiplier")]
    public double CityStrengthFromTechsMultiplier { get; set; } = 5.5;

    [JsonPropertyName("cityStrengthFromTechsExponent")]
    public double CityStrengthFromTechsExponent { get; set; } = 2.8;

    [JsonPropertyName("cityStrengthFromTechsFullMultiplier")]
    public double CityStrengthFromTechsFullMultiplier { get; set; } = 1.0;

    [JsonPropertyName("cityStrengthFromGarrison")]
    public double CityStrengthFromGarrison { get; set; } = 0.2;

    // Formula for Unit Supply:
    // Supply = unitSupplyBase (difficulties.json)
    //          unitSupplyPerCity * amountOfCities + (difficulties.json)
    //          unitSupplyPerPopulation * amountOfPopulationInAllCities
    // unitSupplyBase and unitSupplyPerCity can be found in difficulties.json
    // unitSupplyBase, unitSupplyPerCity and unitSupplyPerPopulation can also be increased through uniques
    [JsonPropertyName("unitSupplyPerPopulation")]
    public double UnitSupplyPerPopulation { get; set; } = 0.5;

    // The minimal distance that must be between any two cities, not counting the tiles cities are on
    // The number is the amount of tiles between two cities, not counting the tiles the cities are on.
    // e.g. "C__C", where "C" is a tile with a city and "_" is a tile without a city, has a distance of 2.
    // First constant is for cities on the same landmass, the second is for cities on different continents.
    [JsonPropertyName("minimalCityDistance")]
    public int MinimalCityDistance { get; set; } = 3;

    [JsonPropertyName("minimalCityDistanceOnDifferentContinents")]
    public int MinimalCityDistanceOnDifferentContinents { get; set; } = 2;

    [JsonPropertyName("baseCityBombardRange")]
    public int BaseCityBombardRange { get; set; } = 2;

    [JsonPropertyName("cityWorkRange")]
    public int CityWorkRange { get; set; } = 3;

    [JsonPropertyName("cityExpandRange")]
    public int CityExpandRange { get; set; } = 5;

    // Number of air units that can be stationed in a city
    [JsonPropertyName("cityAirUnitCapacity")]
    public int CityAirUnitCapacity { get; set; } = 6;

    // Modifies how much the gold value of a one-sided trade is applied to the gifts diplomatic modifier.
    // Eg: One side offers a city, resource or gold for nothing in return.
    [JsonPropertyName("goldGiftMultiplier")]
    public float GoldGiftMultiplier { get; set; } = 1f;

    // Modifies how much the gold value of a regular trade is applied to the gifts diplomatic modifier.
    [JsonPropertyName("goldGiftTradeMultiplier")]
    public float GoldGiftTradeMultiplier { get; set; } = 0.8f;

    // Modifies how quickly the GaveUsGifts dimplomacy modifier runs out. A higher value makes it run out quicker.
    // Normally the gifts reduced by ~2.5% per turn depending on the diplomatic relations with the default value.
    [JsonPropertyName("goldGiftDegradationMultiplier")]
    public float GoldGiftDegradationMultiplier { get; set; } = 1f;

    // Constants used to calculate Unit Upgrade gold Cost (can only be modded all-or-nothing)
    // This is a data class for one reason only: The equality implementation enables Gdx Json to omit it when default (otherwise only the individual fields are omitted)
    [JsonPropertyName("unitUpgradeCost")]
    public UpgradeCostConstants UnitUpgradeCost { get; set; } = new();

    // NaturalWonderGenerator uses these to determine the number of Natural Wonders to spawn for a given map size.
    // With these values, radius * mul + add gives a 1-2-3-4-5 progression for Unciv predefined map sizes and a 2-3-4-5-6-7 progression for the original Civ5 map sizes.
    // 0.124 = (Civ5.Huge.getHexagonalRadiusForArea(w*h) - Civ5.Duel.getHexagonalRadiusForArea(w*h)) / 5 (if you do not round in the radius function)
    // The other constant is empiric to avoid an ugly jump in the progression.
    [JsonPropertyName("naturalWonderCountMultiplier")]
    public float NaturalWonderCountMultiplier { get; set; } = 0.124f;

    [JsonPropertyName("naturalWonderCountAddedConstant")]
    public float NaturalWonderCountAddedConstant { get; set; } = 0.1f;

    // MapGenerator.spreadAncientRuins: number of ruins = suitable tile count * this
    [JsonPropertyName("ancientRuinCountMultiplier")]
    public float AncientRuinCountMultiplier { get; set; } = 0.025f;

    // MapGenerator.spawnLakesAndCoasts: Water bodies up to this tile count become Lakes
    [JsonPropertyName("maxLakeSize")]
    public int MaxLakeSize { get; set; } = 10;

    // RiverGenerator: river frequency and length bounds
    [JsonPropertyName("riverCountMultiplier")]
    public float RiverCountMultiplier { get; set; } = 0.01f;

    [JsonPropertyName("minRiverLength")]
    public int MinRiverLength { get; set; } = 5;

    [JsonPropertyName("maxRiverLength")]
    public int MaxRiverLength { get; set; } = 666; // Do not set to less than the maximal map radius. Set to 0 to disable river generation.

    // Factors in formula for Maximum Number of foundable Religions
    [JsonPropertyName("religionLimitBase")]
    public int ReligionLimitBase { get; set; } = 1;

    [JsonPropertyName("religionLimitMultiplier")]
    public float ReligionLimitMultiplier { get; set; } = 0.5f;

    // Factors in formula for pantheon cost
    [JsonPropertyName("pantheonBase")]
    public int PantheonBase { get; set; } = 10;

    [JsonPropertyName("pantheonGrowth")]
    public int PantheonGrowth { get; set; } = 5;

    // AI behaviour
    [JsonPropertyName("workboatAutomationSearchMaxTiles")]
    public int WorkboatAutomationSearchMaxTiles { get; set; } = 37;

    [JsonPropertyName("minimumCityLocationTileValue")]
    public float MinimumCityLocationTileValue { get; set; } = 53f;

    // Civilization
    [JsonPropertyName("minimumWarDuration")]
    public int MinimumWarDuration { get; set; } = 10;

    [JsonPropertyName("baseTurnsUntilRevolt")]
    public int BaseTurnsUntilRevolt { get; set; } = 4;

    [JsonPropertyName("cityStateElectionTurns")]
    public int CityStateElectionTurns { get; set; } = 15;

    // Economy
    [JsonPropertyName("maxGoldTradeOffer")]
    public int MaxGoldTradeOffer { get; set; } = 10000;

    // City State Tribute: maximum points from Force ranking towards reaching Tribute willingness threshold
    [JsonPropertyName("tributeGlobalModifier")]
    public int TributeGlobalModifier { get; set; } = 100; // 75 in BNW

    [JsonPropertyName("tributeLocalModifier")]
    public int TributeLocalModifier { get; set; } = 100; // 125 in BNW

    // Espionage
    [JsonPropertyName("maxSpyRank")]
    public int MaxSpyRank { get; set; } = 3;

    // How much of a skill bonus each rank gives.
    // Rank 0 is 100, rank 1 is 130, and so on.
    // Half as much for a coup.
    [JsonPropertyName("spyRankSkillPercentBonus")]
    public int SpyRankSkillPercentBonus { get; set; } = 30;

    // Rank 2 is +25% tech steal rate, rank 3 is +50%, and so on
    [JsonPropertyName("spyRankStealPercentBonus")]
    public int SpyRankStealPercentBonus { get; set; } = 25;

    // Steal cost equal to 125% of the most expensive stealable tech
    [JsonPropertyName("spyTechStealCostModifier")]
    public float SpyTechStealCostModifier { get; set; } = 1.25f;

    // Score value of things
    // SCORE_POPULATION_MULTIPLIER: BNW 4, G&K 3
    [JsonPropertyName("scoreFromPopulation")]
    public int ScoreFromPopulation { get; set; } = 3;

    // SCORE_WONDER_MULTIPLIER: BNW 25, G&K 40
    [JsonPropertyName("scoreFromWonders")]
    public int ScoreFromWonders { get; set; } = 40;

    // UI: If set >= 0, ImprovementPicker will silently skip improvements whose tech requirement is more advanced than your current Era + this
    [JsonPropertyName("maxImprovementTechErasForward")]
    public int MaxImprovementTechErasForward { get; set; } = -1;

    // Brave New World tunables (bnw-spec/SPEC.md section 2.1, ticket B001). Defaults are the G&K behaviour, or 0 (off) for
    // values G&K has no use for; the BNW ModOptions.json sets the BNW values (the define value is in each comment).
    // Nothing reads them yet: each feature ticket reads its own and stays gated by the ModOptions uniques.

    // AI_TACTICAL_BARBARIAN_PRIORITY_PLUNDER_TRADE_UNIT: BNW 20
    [JsonPropertyName("aiTacticalBarbarianPriorityPlunderTradeUnit")]
    public int AiTacticalBarbarianPriorityPlunderTradeUnit { get; set; } = 0;

    // BASE_CULTURE_PER_GREAT_WORK: BNW 2
    [JsonPropertyName("baseCulturePerGreatWork")]
    public int BaseCulturePerGreatWork { get; set; } = 0;

    // BASE_TOURISM_PER_GREAT_WORK: BNW 2
    [JsonPropertyName("baseTourismPerGreatWork")]
    public int BaseTourismPerGreatWork { get; set; } = 0;

    // CULTURE_LEVEL_EXOTIC: BNW 10
    [JsonPropertyName("cultureLevelExotic")]
    public int CultureLevelExotic { get; set; } = 0;

    // CULTURE_LEVEL_FAMILIAR: BNW 30
    [JsonPropertyName("cultureLevelFamiliar")]
    public int CultureLevelFamiliar { get; set; } = 0;

    // CULTURE_LEVEL_POPULAR: BNW 60
    [JsonPropertyName("cultureLevelPopular")]
    public int CultureLevelPopular { get; set; } = 0;

    // CULTURE_LEVEL_INFLUENTIAL: BNW 100
    [JsonPropertyName("cultureLevelInfluential")]
    public int CultureLevelInfluential { get; set; } = 0;

    // CULTURE_LEVEL_DOMINANT: BNW 200
    [JsonPropertyName("cultureLevelDominant")]
    public int CultureLevelDominant { get; set; } = 0;

    // DIPLO_VICTORY_ALGORITHM_THRESHOLD: BNW 28
    [JsonPropertyName("diploVictoryAlgorithmThreshold")]
    public double DiploVictoryAlgorithmThreshold { get; set; } = 0.0;

    // DIPLO_VICTORY_BEYOND_ALGORITHM_MULTIPLIER: BNW 35
    [JsonPropertyName("diploVictoryBeyondAlgorithmMultiplier")]
    public double DiploVictoryBeyondAlgorithmMultiplier { get; set; } = 0.0;

    // DIPLO_VICTORY_CIV_DELEGATES_COEFFICIENT: BNW 1.443
    [JsonPropertyName("diploVictoryCivDelegatesCoefficient")]
    public double DiploVictoryCivDelegatesCoefficient { get; set; } = 0.0;

    // DIPLO_VICTORY_CIV_DELEGATES_CONSTANT: BNW 7
    [JsonPropertyName("diploVictoryCivDelegatesConstant")]
    public double DiploVictoryCivDelegatesConstant { get; set; } = 0.0;

    // DIPLO_VICTORY_CS_DELEGATES_COEFFICIENT: BNW 16.023
    [JsonPropertyName("diploVictoryCsDelegatesCoefficient")]
    public double DiploVictoryCsDelegatesCoefficient { get; set; } = 0.0;

    // DIPLO_VICTORY_CS_DELEGATES_CONSTANT: BNW -13.758
    [JsonPropertyName("diploVictoryCsDelegatesConstant")]
    public double DiploVictoryCsDelegatesConstant { get; set; } = 0.0;

    // DIPLO_VICTORY_DEFAULT_VOTE_PERCENT: BNW 67
    [JsonPropertyName("diploVictoryDefaultVotePercent")]
    public double DiploVictoryDefaultVotePercent { get; set; } = 0.0;

    // DIPLO_VICTORY_TEAM_MULTIPLIER: BNW 1.1
    [JsonPropertyName("diploVictoryTeamMultiplier")]
    public double DiploVictoryTeamMultiplier { get; set; } = 0.0;

    // ESPIONAGE_COUP_OTHER_PLAYERS_INFLUENCE_DROP: BNW 20
    [JsonPropertyName("espionageCoupOtherPlayersInfluenceDrop")]
    public int EspionageCoupOtherPlayersInfluenceDrop { get; set; } = 0;

    // ESPIONAGE_COUP_SPY_LEVEL_DELTA_ONE: BNW 1.5
    [JsonPropertyName("espionageCoupSpyLevelDeltaOne")]
    public float EspionageCoupSpyLevelDeltaOne { get; set; } = 0f;

    // ESPIONAGE_COUP_SPY_LEVEL_DELTA_TWO: BNW 2.25
    [JsonPropertyName("espionageCoupSpyLevelDeltaTwo")]
    public float EspionageCoupSpyLevelDeltaTwo { get; set; } = 0f;

    // ESPIONAGE_COUP_SPY_LEVEL_DELTA_THREE: BNW 2.6
    [JsonPropertyName("espionageCoupSpyLevelDeltaThree")]
    public float EspionageCoupSpyLevelDeltaThree { get; set; } = 0f;

    // ESPIONAGE_COUP_SPY_LEVEL_DELTA_FOUR: BNW 2.8
    [JsonPropertyName("espionageCoupSpyLevelDeltaFour")]
    public float EspionageCoupSpyLevelDeltaFour { get; set; } = 0f;

    // EXOTIC_GOODS_GOLD_MAX: BNW 400
    [JsonPropertyName("exoticGoodsGoldMax")]
    public int ExoticGoodsGoldMax { get; set; } = 0;

    // EXOTIC_GOODS_GOLD_MIN: BNW 100
    [JsonPropertyName("exoticGoodsGoldMin")]
    public int ExoticGoodsGoldMin { get; set; } = 0;

    // EXOTIC_GOODS_XP_MAX: BNW 30
    [JsonPropertyName("exoticGoodsXpMax")]
    public int ExoticGoodsXpMax { get; set; } = 0;

    // EXOTIC_GOODS_XP_MIN: BNW 10
    [JsonPropertyName("exoticGoodsXpMin")]
    public int ExoticGoodsXpMin { get; set; } = 0;

    // FRIENDSHIP_ALLIES_ON_DEATH: BNW 45
    [JsonPropertyName("friendshipAlliesOnDeath")]
    public int FriendshipAlliesOnDeath { get; set; } = 0;

    // FRIENDSHIP_FRIENDS_ON_DEATH: BNW 15
    [JsonPropertyName("friendshipFriendsOnDeath")]
    public int FriendshipFriendsOnDeath { get; set; } = 0;

    // FRIENDSHIP_NEUTRAL_ON_DEATH: BNW 0
    [JsonPropertyName("friendshipNeutralOnDeath")]
    public int FriendshipNeutralOnDeath { get; set; } = 0;

    // FRIENDSHIP_PER_UNIT_GIFTED: BNW 5, G&K 2
    [JsonPropertyName("friendshipPerUnitGifted")]
    public int FriendshipPerUnitGifted { get; set; } = 2;

    // IDEOLOGY_PERCENT_CLEAR_VICTORY_PREF: BNW 25
    [JsonPropertyName("ideologyPercentClearVictoryPref")]
    public int IdeologyPercentClearVictoryPref { get; set; } = 0;

    // INFLUENCE_HILL_COST: BNW 1, G&K 2
    [JsonPropertyName("influenceHillCost")]
    public int InfluenceHillCost { get; set; } = 2;

    // INTERNATIONAL_TRADE_BASE: BNW 100
    [JsonPropertyName("internationalTradeBase")]
    public int InternationalTradeBase { get; set; } = 0;

    // INTERNATIONAL_TRADE_CITY_GPT_DIVISOR: BNW 20
    [JsonPropertyName("internationalTradeCityGptDivisor")]
    public int InternationalTradeCityGptDivisor { get; set; } = 0;

    // INTERNATIONAL_TRADE_EXCLUSIVE_CONNECTION: BNW 0
    [JsonPropertyName("internationalTradeExclusiveConnection")]
    public int InternationalTradeExclusiveConnection { get; set; } = 0;

    // LANDMARK_MINOR_FRIENDSHIP_CHANGE: BNW 50
    [JsonPropertyName("landmarkMinorFriendshipChange")]
    public int LandmarkMinorFriendshipChange { get; set; } = 0;

    // LEAGUE_MEMBER_PROPOSALS_BASE: BNW 1
    [JsonPropertyName("leagueMemberProposalsBase")]
    public int LeagueMemberProposalsBase { get; set; } = 0;

    // LEAGUE_MEMBER_VOTES_BASE: BNW 1
    [JsonPropertyName("leagueMemberVotesBase")]
    public int LeagueMemberVotesBase { get; set; } = 0;

    // LEAGUE_MEMBER_VOTES_FOR_HOST: BNW 1
    [JsonPropertyName("leagueMemberVotesForHost")]
    public int LeagueMemberVotesForHost { get; set; } = 0;

    // LEAGUE_MEMBER_VOTES_PER_CITY_STATE_ALLY: BNW 1
    [JsonPropertyName("leagueMemberVotesPerCityStateAlly")]
    public int LeagueMemberVotesPerCityStateAlly { get; set; } = 0;

    // LEAGUE_PROJECT_REWARD_TIER_1_THRESHOLD: BNW 0.5
    [JsonPropertyName("leagueProjectRewardTier1Threshold")]
    public float LeagueProjectRewardTier1Threshold { get; set; } = 0f;

    // LEAGUE_PROJECT_REWARD_TIER_2_THRESHOLD: BNW 1
    [JsonPropertyName("leagueProjectRewardTier2Threshold")]
    public int LeagueProjectRewardTier2Threshold { get; set; } = 0;

    // LEAGUE_SESSION_INTERVAL_BASE_TURNS: BNW 12
    [JsonPropertyName("leagueSessionIntervalBaseTurns")]
    public int LeagueSessionIntervalBaseTurns { get; set; } = 0;

    // LEAGUE_SESSION_SOON_WARNING_TURNS: BNW 5
    [JsonPropertyName("leagueSessionSoonWarningTurns")]
    public int LeagueSessionSoonWarningTurns { get; set; } = 0;

    // MINIUMUM_TOURISM_BLAST_STRENGTH: BNW 100
    [JsonPropertyName("miniumumTourismBlastStrength")]
    public int MiniumumTourismBlastStrength { get; set; } = 0;

    // MINOR_FRIENDSHIP_ANCHOR_DEFAULT: BNW 0
    [JsonPropertyName("minorFriendshipAnchorDefault")]
    public int MinorFriendshipAnchorDefault { get; set; } = 0;

    // MINOR_FRIENDSHIP_ANCHOR_MOD_PROTECTED: BNW 5, G&K 10
    [JsonPropertyName("minorFriendshipAnchorModProtected")]
    public int MinorFriendshipAnchorModProtected { get; set; } = 10;

    // MINOR_FRIENDSHIP_ANCHOR_MOD_WARY_OF: BNW -20, G&K -20
    [JsonPropertyName("minorFriendshipAnchorModWaryOf")]
    public int MinorFriendshipAnchorModWaryOf { get; set; } = -20;

    // MINOR_LIBERATION_FRIENDSHIP: BNW 105; the engine already uses 105 (CityConquestFunctions), so that is the default (spec lists G&K 60)
    [JsonPropertyName("minorLiberationFriendship")]
    public int MinorLiberationFriendship { get; set; } = 105;

    // MINOR_QUEST_FRIENDSHIP_TRADE_ROUTE: BNW 40
    [JsonPropertyName("minorQuestFriendshipTradeRoute")]
    public int MinorQuestFriendshipTradeRoute { get; set; } = 0;

    // MINOR_UNIT_GIFT_TRAVEL_TURNS: BNW 3
    [JsonPropertyName("minorUnitGiftTravelTurns")]
    public int MinorUnitGiftTravelTurns { get; set; } = 0;

    // NUM_AND_TECH_PREREQS: BNW 6, G&K 5
    [JsonPropertyName("numAndTechPrereqs")]
    public int NumAndTechPrereqs { get; set; } = 5;

    // OPINION_WEIGHT_TRADE_MAX: BNW -30
    [JsonPropertyName("opinionWeightTradeMax")]
    public int OpinionWeightTradeMax { get; set; } = 0;

    // PERCENT_HIDDEN_SITES_WRITING: BNW 30
    [JsonPropertyName("percentHiddenSitesWriting")]
    public int PercentHiddenSitesWriting { get; set; } = 0;

    // PERCENT_SITES_HIDDEN: BNW 30
    [JsonPropertyName("percentSitesHidden")]
    public int PercentSitesHidden { get; set; } = 0;

    // QUEST_DISABLED_TRADE_ROUTE: BNW 0
    [JsonPropertyName("questDisabledTradeRoute")]
    public int QuestDisabledTradeRoute { get; set; } = 0;

    // RELIGION_ATHEISM_PRESSURE_PER_POP: BNW 1000, G&K 100
    [JsonPropertyName("religionAtheismPressurePerPop")]
    public int ReligionAtheismPressurePerPop { get; set; } = 100;

    // RELIGION_INITIAL_FOUNDING_CITY_PRESSURE: BNW 5000, G&K 500
    [JsonPropertyName("religionInitialFoundingCityPressure")]
    public int ReligionInitialFoundingCityPressure { get; set; } = 500;

    // RELIGION_MISSIONARY_PRESSURE_MULTIPLIER: BNW 10, G&K 1
    [JsonPropertyName("religionMissionaryPressureMultiplier")]
    public int ReligionMissionaryPressureMultiplier { get; set; } = 1;

    // RESEARCH_AGREEMENT_BOOST_DIVISOR: BNW 3, G&K 6
    [JsonPropertyName("researchAgreementBoostDivisor")]
    public int ResearchAgreementBoostDivisor { get; set; } = 6;

    // REVOLT_COUNTER_MIN: BNW 5
    [JsonPropertyName("revoltCounterMin")]
    public int RevoltCounterMin { get; set; } = 0;

    // SAPPED_CITY_ATTACK_MODIFIER: BNW 50
    [JsonPropertyName("sappedCityAttackModifier")]
    public int SappedCityAttackModifier { get; set; } = 0;

    // SAPPER_BONUS_RANGE: BNW 2
    [JsonPropertyName("sapperBonusRange")]
    public int SapperBonusRange { get; set; } = 0;

    // SCORE_BELIEF_MULTIPLIER: BNW 20
    [JsonPropertyName("scoreBeliefMultiplier")]
    public int ScoreBeliefMultiplier { get; set; } = 0;

    // SCORE_GREAT_WORK_MULTIPLIER: BNW 4
    [JsonPropertyName("scoreGreatWorkMultiplier")]
    public int ScoreGreatWorkMultiplier { get; set; } = 0;

    // SCORE_POLICY_MULTIPLIER: BNW 4
    [JsonPropertyName("scorePolicyMultiplier")]
    public int ScorePolicyMultiplier { get; set; } = 0;

    // SCORE_RELIGION_CITIES_MULTIPLIER: BNW 1
    [JsonPropertyName("scoreReligionCitiesMultiplier")]
    public int ScoreReligionCitiesMultiplier { get; set; } = 0;

    // SETTLER_GOLD_MULTIPLIER: BNW 3, G&K 2
    [JsonPropertyName("settlerGoldMultiplier")]
    public int SettlerGoldMultiplier { get; set; } = 2;

    // SUPER_UNHAPPY_THRESHOLD: BNW -20
    [JsonPropertyName("superUnhappyThreshold")]
    public int SuperUnhappyThreshold { get; set; } = 0;

    // SWITCH_POLICY_BRANCHES_ANARCHY_TURNS: BNW 2
    [JsonPropertyName("switchPolicyBranchesAnarchyTurns")]
    public int SwitchPolicyBranchesAnarchyTurns { get; set; } = 0;

    // SWITCH_POLICY_BRANCHES_TENETS_LOST: BNW 2
    [JsonPropertyName("switchPolicyBranchesTenetsLost")]
    public int SwitchPolicyBranchesTenetsLost { get; set; } = 0;

    // TEMPORARY_TOURISM_BOOST_MOD: BNW 100
    [JsonPropertyName("temporaryTourismBoostMod")]
    public int TemporaryTourismBoostMod { get; set; } = 0;

    // TOURISM_MODIFIER_DIFFERENT_IDEOLOGIES: BNW -34
    [JsonPropertyName("tourismModifierDifferentIdeologies")]
    public int TourismModifierDifferentIdeologies { get; set; } = 0;

    // TOURISM_MODIFIER_DIPLOMAT: BNW 25
    [JsonPropertyName("tourismModifierDiplomat")]
    public int TourismModifierDiplomat { get; set; } = 0;

    // TOURISM_MODIFIER_OPEN_BORDERS: BNW 25
    [JsonPropertyName("tourismModifierOpenBorders")]
    public int TourismModifierOpenBorders { get; set; } = 0;

    // TOURISM_MODIFIER_SHARED_RELIGION: BNW 25
    [JsonPropertyName("tourismModifierSharedReligion")]
    public int TourismModifierSharedReligion { get; set; } = 0;

    // TOURISM_MODIFIER_TRADE_ROUTE: BNW 25
    [JsonPropertyName("tourismModifierTradeRoute")]
    public int TourismModifierTradeRoute { get; set; } = 0;

    // UNRESEARCHED_TECH_BONUS_FROM_KILLS_INTERCEPT: BNW -114.07
    [JsonPropertyName("unresearchedTechBonusFromKillsIntercept")]
    public float UnresearchedTechBonusFromKillsIntercept { get; set; } = 0f;

    // UNRESEARCHED_TECH_BONUS_FROM_KILLS_SLOPE: BNW 9.68
    [JsonPropertyName("unresearchedTechBonusFromKillsSlope")]
    public float UnresearchedTechBonusFromKillsSlope { get; set; } = 0f;

    // UPRISING_COUNTER_MIN: BNW 4
    [JsonPropertyName("uprisingCounterMin")]
    public int UprisingCounterMin { get; set; } = 0;

    // UPRISING_COUNTER_POSSIBLE: BNW 3
    [JsonPropertyName("uprisingCounterPossible")]
    public int UprisingCounterPossible { get; set; } = 0;

    // UPRISING_NUM_BASE: BNW 100
    [JsonPropertyName("uprisingNumBase")]
    public int UprisingNumBase { get; set; } = 0;

    // UPRISING_NUM_CITY_COUNT: BNW 20
    [JsonPropertyName("uprisingNumCityCount")]
    public int UprisingNumCityCount { get; set; } = 0;

    // VERY_UNHAPPY_THRESHOLD: BNW -10
    [JsonPropertyName("veryUnhappyThreshold")]
    public int VeryUnhappyThreshold { get; set; } = 0;

    // WARMONGER_ON_CITY_STATE_MULTIPLIER: BNW 50
    [JsonPropertyName("warmongerOnCityStateMultiplier")]
    public int WarmongerOnCityStateMultiplier { get; set; } = 0;

    public void Merge(ModConstants other)
    {
        foreach (var field in Fields)
        {
            object? value = field.GetValue(other);
            if (JavaEquals(field.GetValue(Defaults), value))
            {
                continue;
            }

            field.SetValue(this, value);
        }
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        return obj is ModConstants other && Fields.All(field => JavaEquals(field.GetValue(this), field.GetValue(other)));
    }

    // Note: This is of course heavily dependent on iteration order, which is declaration order here as upstream
    public override int GetHashCode()
    {
        int result = 0;
        foreach (var field in Fields)
        {
            result = result.WithHash(JavaHashCode(field.GetValue(this)));
        }

        return result;
    }

    /// <summary>Debug only so far.</summary>
    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append('{');
        foreach (var field in Fields)
        {
            object? value = field.GetValue(this);
            if (JavaEquals(value, field.GetValue(Defaults)))
            {
                continue;
            }

            sb.Append(field.GetCustomAttribute<JsonPropertyNameAttribute>().NotNull().Name);
            sb.Append(':');
            sb.Append(JavaFormat.ToJavaString(value));
            sb.Append(',');
        }

        sb.Length--; // remove extra ',' (or the opening brace if nothing differs)
        sb.Append('}');
        string text = sb.ToString();
        return text == "}" ? "defaults" : text;
    }

    private static bool JavaEquals(object? a, object? b) => (a, b) switch
    {
        (float x, float y) => JavaHash.HashCode(x) == JavaHash.HashCode(y),
        (double x, double y) => JavaHash.HashCode(x) == JavaHash.HashCode(y),
        _ => Equals(a, b),
    };

    private static int JavaHashCode(object? value) => value switch
    {
        int i => JavaHash.HashCode(i),
        float f => JavaHash.HashCode(f),
        double d => JavaHash.HashCode(d),
        null => 0,
        _ => value.GetHashCode(),
    };

    /// <summary>
    /// Constants used to calculate Unit Upgrade gold Cost (can only be modded all-or-nothing).
    /// A data class upstream: equality, hash code (31-chained) and string form follow Kotlin's.
    /// </summary>
    public sealed class UpgradeCostConstants : IEquatable<UpgradeCostConstants>
    {
        [JsonPropertyName("base")]
        public float Base { get; init; } = 10f;

        [JsonPropertyName("perProduction")]
        public float PerProduction { get; init; } = 2f;

        [JsonPropertyName("eraMultiplier")]
        public float EraMultiplier { get; init; } // 0.3 in Civ5 cpp sources but 0 in xml

        [JsonPropertyName("exponent")]
        public float Exponent { get; init; } = 1f;

        [JsonPropertyName("roundTo")]
        public int RoundTo { get; init; } = 5;

        public bool Equals(UpgradeCostConstants? other) =>
            other != null
            && JavaHash.HashCode(Base) == JavaHash.HashCode(other.Base)
            && JavaHash.HashCode(PerProduction) == JavaHash.HashCode(other.PerProduction)
            && JavaHash.HashCode(EraMultiplier) == JavaHash.HashCode(other.EraMultiplier)
            && JavaHash.HashCode(Exponent) == JavaHash.HashCode(other.Exponent)
            && RoundTo == other.RoundTo;

        public override bool Equals(object? obj) => obj is UpgradeCostConstants other && Equals(other);

        public override int GetHashCode() =>
            JavaHash.HashCode(Base)
                .WithHash(JavaHash.HashCode(PerProduction))
                .WithHash(JavaHash.HashCode(EraMultiplier))
                .WithHash(JavaHash.HashCode(Exponent))
                .WithHash(RoundTo);

        public override string ToString() =>
            $"UnitUpgradeCost(base={JavaFormat.ToJavaString(Base)}, perProduction={JavaFormat.ToJavaString(PerProduction)}, eraMultiplier={JavaFormat.ToJavaString(EraMultiplier)}, exponent={JavaFormat.ToJavaString(Exponent)}, roundTo={RoundTo.ToString(CultureInfo.InvariantCulture)})";
    }
}
