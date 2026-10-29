using Unciv.Json;
using Unciv.Models;

namespace Unciv.Core.Tests.Models;

// tests/src/com/unciv/logic/ModConstantsTests.kt
public class ModConstantsTests
{
    [Fact]
    public void HashCodeOfDefaultsEqualsFreshInstance()
    {
        Assert.Equal(ModConstants.Defaults.GetHashCode(), new ModConstants().GetHashCode());
    }

    [Fact]
    public void HashCodesMatchTheJvm()
    {
        // Golden from the real class on the JVM (670061036 / 1000388743) until the Brave New World tunables (B001) were added; the hash chain is the JVM's,
        // the values below are this port's own after that deliberate extension
        Assert.Equal(-1594889880, ModConstants.Defaults.GetHashCode());
        var changed = new ModConstants { MaxXPfromBarbarians = 99, CityStrengthBase = 6.0 };
        Assert.Equal(-267524221, changed.GetHashCode());
    }

    [Fact]
    public void Equality()
    {
        var instance1 = new ModConstants { MaxXPfromBarbarians = 99 };
        Assert.True(instance1.Equals(instance1));
        var instance2 = new ModConstants { CityStrengthBase = 6.0 };
        Assert.NotEqual(instance1, instance2);
        instance1.CityStrengthBase = instance2.CityStrengthBase;
        instance2.MaxXPfromBarbarians = instance1.MaxXPfromBarbarians;
        Assert.Equal(instance1, instance2);
    }

    [Fact]
    public void EqualityFollowsJavaBoxedSemantics()
    {
        Assert.Equal(new ModConstants { CityStrengthBase = double.NaN }, new ModConstants { CityStrengthBase = double.NaN });
        Assert.NotEqual(new ModConstants { CityStrengthBase = 0.0 }, new ModConstants { CityStrengthBase = -0.0 });
    }

    [Fact]
    public void ToStringListsOnlyChangedFields()
    {
        var instance = new ModConstants { MaxXPfromBarbarians = 99, CityStrengthBase = 6.0 };
        Assert.Equal("{maxXPfromBarbarians:99,cityStrengthBase:6.0}", instance.ToString());
        Assert.Equal("defaults", ModConstants.Defaults.ToString());
    }

    [Fact]
    public void MergeCopiesOnlyNonDefaultValues()
    {
        var instance1 = new ModConstants { MaxXPfromBarbarians = 99 };
        var instance2 = new ModConstants { CityStrengthBase = 6.0 };
        instance1.Merge(instance2);
        Assert.Equal(6.0, instance1.CityStrengthBase);
        Assert.Equal(99, instance1.MaxXPfromBarbarians);
    }

    [Fact]
    public void UnitUpgradeCostPrintsLikeKotlinDataClass()
    {
        Assert.Equal("UnitUpgradeCost(base=10.0, perProduction=2.0, eraMultiplier=0.0, exponent=1.0, roundTo=5)", new ModConstants().UnitUpgradeCost.ToString());
        Assert.Equal(new ModConstants().UnitUpgradeCost, new ModConstants.UpgradeCostConstants());
    }

    [Fact]
    public void JsonUsesKotlinNames()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new ModConstants());
        Assert.Contains("\"maxXPfromBarbarians\":30", json);
        var back = System.Text.Json.JsonSerializer.Deserialize<ModConstants>("{\"maxXPfromBarbarians\":7,\"unitUpgradeCost\":{\"roundTo\":1}}")!;
        Assert.Equal(7, back.MaxXPfromBarbarians);
        Assert.Equal(1, back.UnitUpgradeCost.RoundTo);
    }
}

public class UncivSoundAndTutorialTests
{
    [Fact]
    public void SilentIsEmptyAndRecordsCompareByName()
    {
        Assert.Equal("", UncivSound.Silent.FileName);
        Assert.Equal(new UncivSound("click"), UncivSound.Click);
    }

    [Fact]
    public void TutorialValuesAndCivilopediaFlag()
    {
        Assert.Equal(41, Enum.GetValues<TutorialTrigger>().Length);
        Assert.All(Enum.GetValues<TutorialTrigger>(), t => Assert.Equal(!t.Value().StartsWith('_'), t.IsCivilopedia()));
        Assert.Equal(Enum.GetValues<TutorialTrigger>().Length, Enum.GetValues<TutorialTrigger>().Select(t => t.Value()).Distinct().Count());
    }

    // region Brave New World tunables (B001)

    private static readonly (string Property, string Json, object Default)[] BnwTunables =
    [
        ("AiTacticalBarbarianPriorityPlunderTradeUnit", "aiTacticalBarbarianPriorityPlunderTradeUnit", (object)0),
        ("BaseCulturePerGreatWork", "baseCulturePerGreatWork", (object)0),
        ("BaseTourismPerGreatWork", "baseTourismPerGreatWork", (object)0),
        ("DiploVictoryAlgorithmThreshold", "diploVictoryAlgorithmThreshold", (object)0.0),
        ("DiploVictoryBeyondAlgorithmMultiplier", "diploVictoryBeyondAlgorithmMultiplier", (object)0.0),
        ("DiploVictoryCivDelegatesCoefficient", "diploVictoryCivDelegatesCoefficient", (object)0.0),
        ("DiploVictoryCivDelegatesConstant", "diploVictoryCivDelegatesConstant", (object)0.0),
        ("DiploVictoryCsDelegatesCoefficient", "diploVictoryCsDelegatesCoefficient", (object)0.0),
        ("DiploVictoryCsDelegatesConstant", "diploVictoryCsDelegatesConstant", (object)0.0),
        ("DiploVictoryDefaultVotePercent", "diploVictoryDefaultVotePercent", (object)0.0),
        ("DiploVictoryTeamMultiplier", "diploVictoryTeamMultiplier", (object)0.0),
        ("EspionageCoupOtherPlayersInfluenceDrop", "espionageCoupOtherPlayersInfluenceDrop", (object)0),
        ("EspionageCoupSpyLevelDeltaThree", "espionageCoupSpyLevelDeltaThree", (object)0f),
        ("ExoticGoodsGoldMax", "exoticGoodsGoldMax", (object)0),
        ("ExoticGoodsGoldMin", "exoticGoodsGoldMin", (object)0),
        ("ExoticGoodsXpMax", "exoticGoodsXpMax", (object)0),
        ("ExoticGoodsXpMin", "exoticGoodsXpMin", (object)0),
        ("FriendshipAlliesOnDeath", "friendshipAlliesOnDeath", (object)0),
        ("FriendshipFriendsOnDeath", "friendshipFriendsOnDeath", (object)0),
        ("FriendshipNeutralOnDeath", "friendshipNeutralOnDeath", (object)0),
        ("FriendshipPerUnitGifted", "friendshipPerUnitGifted", (object)2),
        ("IdeologyPercentClearVictoryPref", "ideologyPercentClearVictoryPref", (object)0),
        ("InfluenceHillCost", "influenceHillCost", (object)2),
        ("InternationalTradeBase", "internationalTradeBase", (object)0),
        ("InternationalTradeCityGptDivisor", "internationalTradeCityGptDivisor", (object)0),
        ("InternationalTradeExclusiveConnection", "internationalTradeExclusiveConnection", (object)0),
        ("LandmarkMinorFriendshipChange", "landmarkMinorFriendshipChange", (object)0),
        ("LeagueMemberProposalsBase", "leagueMemberProposalsBase", (object)0),
        ("LeagueMemberVotesBase", "leagueMemberVotesBase", (object)0),
        ("LeagueMemberVotesForHost", "leagueMemberVotesForHost", (object)0),
        ("LeagueMemberVotesPerCityStateAlly", "leagueMemberVotesPerCityStateAlly", (object)0),
        ("LeagueProjectRewardTier1Threshold", "leagueProjectRewardTier1Threshold", (object)0f),
        ("LeagueProjectRewardTier2Threshold", "leagueProjectRewardTier2Threshold", (object)0),
        ("LeagueSessionIntervalBaseTurns", "leagueSessionIntervalBaseTurns", (object)0),
        ("LeagueSessionSoonWarningTurns", "leagueSessionSoonWarningTurns", (object)0),
        ("MiniumumTourismBlastStrength", "miniumumTourismBlastStrength", (object)0),
        ("MinorFriendshipAnchorDefault", "minorFriendshipAnchorDefault", (object)0),
        ("MinorFriendshipAnchorModProtected", "minorFriendshipAnchorModProtected", (object)0),
        ("MinorFriendshipAnchorModWaryOf", "minorFriendshipAnchorModWaryOf", (object)0),
        ("MinorLiberationFriendship", "minorLiberationFriendship", (object)60),
        ("MinorQuestFriendshipTradeRoute", "minorQuestFriendshipTradeRoute", (object)0),
        ("MinorUnitGiftTravelTurns", "minorUnitGiftTravelTurns", (object)0),
        ("NumAndTechPrereqs", "numAndTechPrereqs", (object)5),
        ("OpinionWeightTradeMax", "opinionWeightTradeMax", (object)0),
        ("PercentHiddenSitesWriting", "percentHiddenSitesWriting", (object)0),
        ("PercentSitesHidden", "percentSitesHidden", (object)0),
        ("QuestDisabledTradeRoute", "questDisabledTradeRoute", (object)0),
        ("ReligionAtheismPressurePerPop", "religionAtheismPressurePerPop", (object)100),
        ("ReligionInitialFoundingCityPressure", "religionInitialFoundingCityPressure", (object)500),
        ("ReligionMissionaryPressureMultiplier", "religionMissionaryPressureMultiplier", (object)1),
        ("ResearchAgreementBoostDivisor", "researchAgreementBoostDivisor", (object)6),
        ("RevoltCounterMin", "revoltCounterMin", (object)0),
        ("SappedCityAttackModifier", "sappedCityAttackModifier", (object)0),
        ("SapperBonusRange", "sapperBonusRange", (object)0),
        ("ScoreBeliefMultiplier", "scoreBeliefMultiplier", (object)0),
        ("ScoreGreatWorkMultiplier", "scoreGreatWorkMultiplier", (object)0),
        ("ScorePolicyMultiplier", "scorePolicyMultiplier", (object)0),
        ("ScoreReligionCitiesMultiplier", "scoreReligionCitiesMultiplier", (object)0),
        ("SettlerGoldMultiplier", "settlerGoldMultiplier", (object)2),
        ("SuperUnhappyThreshold", "superUnhappyThreshold", (object)0),
        ("SwitchPolicyBranchesAnarchyTurns", "switchPolicyBranchesAnarchyTurns", (object)0),
        ("SwitchPolicyBranchesTenetsLost", "switchPolicyBranchesTenetsLost", (object)0),
        ("TemporaryTourismBoostMod", "temporaryTourismBoostMod", (object)0),
        ("TourismModifierDifferentIdeologies", "tourismModifierDifferentIdeologies", (object)0),
        ("TourismModifierDiplomat", "tourismModifierDiplomat", (object)0),
        ("TourismModifierOpenBorders", "tourismModifierOpenBorders", (object)0),
        ("TourismModifierSharedReligion", "tourismModifierSharedReligion", (object)0),
        ("TourismModifierTradeRoute", "tourismModifierTradeRoute", (object)0),
        ("UnresearchedTechBonusFromKillsIntercept", "unresearchedTechBonusFromKillsIntercept", (object)0f),
        ("UnresearchedTechBonusFromKillsSlope", "unresearchedTechBonusFromKillsSlope", (object)0f),
        ("UprisingCounterMin", "uprisingCounterMin", (object)0),
        ("UprisingCounterPossible", "uprisingCounterPossible", (object)0),
        ("UprisingNumBase", "uprisingNumBase", (object)0),
        ("UprisingNumCityCount", "uprisingNumCityCount", (object)0),
        ("VeryUnhappyThreshold", "veryUnhappyThreshold", (object)0),
        ("WarmongerOnCityStateMultiplier", "warmongerOnCityStateMultiplier", (object)0),
    ];

    [Fact]
    public void BnwTunablesAreNamedAfterTheDefineAndDefaultToGnkOrOff()
    {
        foreach (var (property, json, expected) in BnwTunables)
        {
            var info = typeof(ModConstants).GetProperty(property);
            Assert.NotNull(info);
            Assert.Equal(expected, info.GetValue(ModConstants.Defaults));
            Assert.Equal(json, info.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonPropertyNameAttribute), false)
                .Cast<System.Text.Json.Serialization.JsonPropertyNameAttribute>().Single().Name);
        }
        Assert.Equal(76, BnwTunables.Length);
    }

    [Fact]
    public void BnwTunablesMergeFromJsonLikeAnyOtherConstant()
    {
        var bnw = UncivJson.Deserialize<ModConstants>("""{"tourismModifierOpenBorders":25,"diploVictoryCivDelegatesCoefficient":1.443,"settlerGoldMultiplier":3}""")!;
        var merged = new ModConstants();
        merged.Merge(bnw);
        Assert.Equal(25, merged.TourismModifierOpenBorders);
        Assert.Equal(1.443, merged.DiploVictoryCivDelegatesCoefficient);
        Assert.Equal(3, merged.SettlerGoldMultiplier);
        Assert.Equal(2, ModConstants.Defaults.SettlerGoldMultiplier); // G&K keeps its value
        Assert.Equal(0, merged.TourismModifierTradeRoute);
        Assert.Equal("{diploVictoryCivDelegatesCoefficient:1.443,settlerGoldMultiplier:3,tourismModifierOpenBorders:25}", merged.ToString());
    }

    // endregion
}
