using Unciv.Core.Tests.Logic.GreatWorks;
using Unciv.Json;
using Unciv.Logic;
using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Tourism;
using Unciv.Logic.Trade.Routes;
using Unciv.Models.Ruleset;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Logic.Tourism;

// B008 (bnw-spec/SPEC.md 4.3.3, 4.3.5, 4.3.6): influence accumulation, levels, trend, turns to Influential, level benefits, tourism blast.
[Collection("RulesetCache")]
public sealed class InfluenceTests
{
    private static GreatWorkWorld World(params string[] aUniques)
    {
        var w = new GreatWorkWorld(aUniques);
        var c = w.Game.Ruleset.ModOptions.Constants;
        c.BaseTourismPerGreatWork = 2;
        c.CultureLevelExotic = 10;
        c.CultureLevelFamiliar = 30;
        c.CultureLevelPopular = 60;
        c.CultureLevelInfluential = 100;
        c.CultureLevelDominant = 200;
        c.MiniumumTourismBlastStrength = 100;
        c.TemporaryTourismBoostMod = 100;
        c.TourismModifierOpenBorders = 25;
        return w;
    }

    private static void GiveWorks(GreatWorkWorld w, CityObject city, int works)
    {
        var building = w.Build(city, works, GreatWorkSlotType.ArtArtifact);
        var names = new[] { "ArtAncient", "ArtClassical", "ArtMedieval", "ArtAnyEra" };
        for (int i = 0; i < works; i++) w.Place(city, building, i, names[i], w.A.CivId);
    }

    private static void Set(CivilizationObject me, CivilizationObject target, int influence, int last = 0, int lastLifetime = 0)
    {
        var entry = (me.Culture ??= new CivCultureManager()).GetOrAdd(target.CivId);
        entry.Influence = influence;
        entry.LastTurnInfluence = last;
        entry.LastTurnLifetimeCulture = lastLifetime;
    }

    // Founding a city near another civ makes them meet by sight: take that back where a test needs a stranger.
    private static void Unmeet(GreatWorkWorld w, CivilizationObject civ)
    {
        foreach (var other in w.Game.GameInfo.Civilizations.Where(it => it != civ))
        {
            other.Diplomacy.Remove(civ.CivId);
            civ.Diplomacy.Remove(other.CivId);
        }
    }

    // --- levels

    [Theory]
    [InlineData(0, 0)]
    [InlineData(99, 0)]
    [InlineData(100, 1)] // 10%
    [InlineData(299, 1)]
    [InlineData(300, 2)] // 30%
    [InlineData(599, 2)]
    [InlineData(600, 3)] // 60%
    [InlineData(999, 3)]
    [InlineData(1000, 4)] // 100%
    [InlineData(1999, 4)]
    [InlineData(2000, 5)] // 200%
    [InlineData(50000, 5)]
    public void LevelChangesExactlyAtTheThresholds(int influence, int level)
    {
        var w = World();
        w.B.TotalCultureForContests = 1000;
        Set(w.A, w.B, influence);
        Assert.Equal((InfluenceLevel)level, InfluenceRules.Level(w.A, w.B));
    }

    [Fact]
    public void ZeroLifetimeCultureMeansNoPercentAndUnknown()
    {
        var w = World();
        Set(w.A, w.B, 500);
        Assert.Equal(0, InfluenceRules.Percent(w.A, w.B));
        Assert.Equal(InfluenceLevel.Unknown, InfluenceRules.Level(w.A, w.B));
    }

    [Fact]
    public void PercentTruncatesAndSurvivesHugeValues()
    {
        var w = World();
        w.B.TotalCultureForContests = 3;
        Set(w.A, w.B, 1);
        Assert.Equal(33, InfluenceRules.Percent(w.A, w.B));
        Set(w.A, w.B, int.MaxValue);
        Assert.Equal(unchecked((int)(int.MaxValue * 100L / 3)), InfluenceRules.Percent(w.A, w.B)); // 64-bit product, the result wraps to Int like any Civ V counter
    }

    [Fact]
    public void LevelIsUnknownWhenTourismIsOff()
    {
        var w = new GreatWorkWorld(enabled: false);
        w.B.TotalCultureForContests = 10;
        Set(w.A, w.B, 1000);
        Assert.Equal(InfluenceLevel.Unknown, InfluenceRules.Level(w.A, w.B));
    }

    // --- trend

    [Fact]
    public void TrendComparesTheSharesByCrossMultiplication()
    {
        var w = World();
        w.B.TotalCultureForContests = 1000;
        Set(w.A, w.B, 500, last: 400, lastLifetime: 1000);
        Assert.Equal(InfluenceTrend.Rising, InfluenceRules.Trend(w.A, w.B));
        Set(w.A, w.B, 400, last: 500, lastLifetime: 1000);
        Assert.Equal(InfluenceTrend.Falling, InfluenceRules.Trend(w.A, w.B));
        Set(w.A, w.B, 400, last: 400, lastLifetime: 1000);
        Assert.Equal(InfluenceTrend.Static, InfluenceRules.Trend(w.A, w.B));
        // the target's culture grew faster than our influence: the share fell although influence rose
        Set(w.A, w.B, 410, last: 400, lastLifetime: 900);
        Assert.Equal(InfluenceTrend.Falling, InfluenceRules.Trend(w.A, w.B));
    }

    [Fact]
    public void TrendIsStaticWithoutPositiveLifetimes()
    {
        var w = World();
        Assert.Equal(InfluenceTrend.Static, InfluenceRules.Trend(w.A, w.B)); // no entry
        Set(w.A, w.B, 500, last: 100, lastLifetime: 0);
        w.B.TotalCultureForContests = 1000;
        Assert.Equal(InfluenceTrend.Static, InfluenceRules.Trend(w.A, w.B));
        Set(w.A, w.B, 500, last: 100, lastLifetime: 1000);
        w.B.TotalCultureForContests = 0;
        Assert.Equal(InfluenceTrend.Static, InfluenceRules.Trend(w.A, w.B));
    }

    // --- turn

    [Fact]
    public void InfluenceGrowsByTourismTimesTheModifierEachTurn()
    {
        var w = World("[+50]% Tourism toward civs with open borders with you");
        GiveWorks(w, w.CapA, 3); // 6 tourism
        w.A.GetDiplomacyManager(w.B)!.HasOpenBorders = true; // modifier 25 + 50 = 75
        w.B.TotalCultureForContests = 700;
        InfluenceRules.StartTurn(w.A);
        Assert.Equal(6 * 175 / 100, InfluenceRules.InfluenceOn(w.A, w.B)); // 10
        InfluenceRules.StartTurn(w.A);
        Assert.Equal(20, InfluenceRules.InfluenceOn(w.A, w.B));
        var entry = w.A.Culture!.Find(w.B.CivId)!;
        Assert.Equal((10, 700), (entry.LastTurnInfluence, entry.LastTurnLifetimeCulture));
        Assert.Equal(InfluenceTrend.Rising, InfluenceRules.Trend(w.A, w.B));
        Assert.Equal(0, InfluenceRules.InfluenceOn(w.B, w.A)); // B has no tourism but is tracked
    }

    [Fact]
    public void OnlyMetLivingMajorCivsAreInfluenced()
    {
        var w = World();
        GiveWorks(w, w.CapA, 2);
        var stranger = w.Game.AddCiv(isPlayer: true);
        w.Game.AddCity(stranger, w.Game.GetTile(4, -6));
        Unmeet(w, stranger);
        var cityState = w.Game.AddCiv(cityStateType: "Cultured");
        w.Game.AddCity(cityState, w.Game.GetTile(0, -8));
        w.A.DiplomacyFunctions.MakeCivilizationsMeet(cityState);
        InfluenceRules.StartTurn(w.A);
        Assert.Equal(4, InfluenceRules.InfluenceOn(w.A, w.B));
        Assert.Equal([w.B.CivId], w.A.Culture!.Entries.Select(it => it.Civ));
        Assert.False(InfluenceRules.CanInfluence(w.A, stranger));
        Assert.False(InfluenceRules.CanInfluence(w.A, cityState));
        Assert.False(InfluenceRules.CanInfluence(w.A, w.A));
    }

    [Fact]
    public void ATurnInAGameWithoutTourismChangesNothing()
    {
        var w = new GreatWorkWorld(enabled: false);
        InfluenceRules.StartTurn(w.A);
        Assert.Null(w.A.Culture);
    }

    [Fact]
    public void TheTourismBoostCountsDownAfterItWasUsed()
    {
        var w = World();
        GiveWorks(w, w.CapA, 3);
        w.A.Culture = new CivCultureManager();
        w.A.Culture.AddTourismBonusTurns(1);
        InfluenceRules.StartTurn(w.A);
        Assert.Equal(12, InfluenceRules.InfluenceOn(w.A, w.B)); // 6 * 200 / 100
        Assert.Equal(0, w.A.Culture.TourismBonusTurns);
        InfluenceRules.StartTurn(w.A);
        Assert.Equal(18, InfluenceRules.InfluenceOn(w.A, w.B));
    }

    [Fact]
    public void ANegativeModifierNeverTurnsInfluenceNegative()
    {
        Assert.Equal(0, InfluenceRules.InfluencePerTurn(10, -150));
        Assert.Equal(0, InfluenceRules.InfluencePerTurn(10, -100));
        Assert.Equal(10 * 66 / 100, InfluenceRules.InfluencePerTurn(10, -34));
    }

    [Fact]
    public void InfluenceStateRoundTripsCloneAndSerialisesSorted()
    {
        var w = World();
        Set(w.A, w.B, 5, 3, 9);
        w.A.Culture!.GetOrAdd("Aaa").Influence = 1;
        w.A.Culture.AddTourismBonusTurns(4);
        Assert.Equal(["Aaa", w.B.CivId], w.A.Culture.Entries.Select(it => it.Civ));
        string json = UncivJson.Serialize(w.Game.GameInfo);
        var back = UncivJson.Deserialize<GameInfo>(json)!;
        var culture = back.Civilizations.Single(it => it.CivId == w.A.CivId).Culture!;
        Assert.Equal((5, 3, 9, 4), (culture.InfluenceOn(w.B.CivId), culture.LastTurnInfluenceOn(w.B.CivId), culture.LastTurnLifetimeCultureOf(w.B.CivId), culture.TourismBonusTurns));
        Assert.Equal(json, UncivJson.Serialize(back));
        var clone = w.A.Clone();
        clone.Culture!.AddInfluence(w.B.CivId, 100);
        Assert.Equal(5, w.A.Culture.InfluenceOn(w.B.CivId));
        Assert.Equal(0, w.A.Culture.InfluenceOn("missing"));
    }

    // --- turns to influential, least influenced

    [Fact]
    public void TurnsToInfluentialRoundsUpAndIsNeverUnlessRising()
    {
        var w = World();
        GiveWorks(w, w.CapA, 3); // 6 per turn
        w.B.TotalCultureForContests = 100;
        Set(w.A, w.B, 50, last: 40, lastLifetime: 100);
        w.B.Stats.StatsForNextTurn.Culture = 0f;
        // needs 100 - 50 = 50, rate 6 - 0 -> ceil(8.33) = 9
        Assert.Equal(9, InfluenceRules.TurnsToInfluential(w.A, w.B));
        Set(w.A, w.B, 50, last: 50, lastLifetime: 100); // static
        Assert.Equal(InfluenceRules.NeverInfluential, InfluenceRules.TurnsToInfluential(w.A, w.B));
        Set(w.A, w.B, 100, last: 40, lastLifetime: 100); // already Influential
        Assert.Equal(InfluenceRules.NeverInfluential, InfluenceRules.TurnsToInfluential(w.A, w.B));
    }

    [Fact]
    public void TurnsToInfluentialIsNeverWhenTheTargetOutgrowsOurRate()
    {
        var w = World();
        GiveWorks(w, w.CapA, 3); // 6 per turn
        w.B.TotalCultureForContests = 100;
        Set(w.A, w.B, 50, last: 40, lastLifetime: 100);
        w.B.Stats.StatsForNextTurn.Culture = 6f;
        Assert.Equal(InfluenceRules.NeverInfluential, InfluenceRules.TurnsToInfluential(w.A, w.B));
        w.B.Stats.StatsForNextTurn.Culture = 5f; // rate 6 - 5 = 1: 50 turns
        Assert.Equal(50, InfluenceRules.TurnsToInfluential(w.A, w.B));
    }

    [Fact]
    public void LeastInfluencedIsTheSmallestShareAmongThoseNotYetInfluential()
    {
        var w = World();
        var c = w.Game.AddCiv(isPlayer: true);
        w.Game.AddCity(c, w.Game.GetTile(4, -6));
        w.B.TotalCultureForContests = 1000;
        c.TotalCultureForContests = 1000;
        Unmeet(w, c);
        Set(w.A, w.B, 500);
        Set(w.A, c, 300);
        Assert.Same(w.B, InfluenceRules.LeastInfluenced(w.A, onlyMet: true)); // c is not met
        Assert.Same(c, InfluenceRules.LeastInfluenced(w.A, onlyMet: false));
        w.A.DiplomacyFunctions.MakeCivilizationsMeet(c);
        Assert.Same(c, InfluenceRules.LeastInfluenced(w.A, onlyMet: true));
        Set(w.A, c, 1000); // Influential now: skipped
        Assert.Same(w.B, InfluenceRules.LeastInfluenced(w.A, onlyMet: true));
        Set(w.A, w.B, 1000);
        Assert.Null(InfluenceRules.LeastInfluenced(w.A, onlyMet: true));
        Set(w.A, w.B, 300);
        Set(w.A, c, 300);
        Assert.Same(w.B, InfluenceRules.LeastInfluenced(w.A, onlyMet: true)); // tie: first in civ order
    }

    // --- level benefits

    [Fact]
    public void LevelBenefitsStartAtTheirLevel()
    {
        var w = World();
        w.B.TotalCultureForContests = 1000;
        Set(w.A, w.B, 99);
        Assert.Equal((0, 0), (InfluenceRules.TradeRouteScienceBonusTimes100(w.A, w.B), InfluenceRules.MajorCivSpyRankBonus(w.A, w.B)));
        Set(w.A, w.B, 100); // Exotic
        Assert.Equal((0, 1), (InfluenceRules.TradeRouteScienceBonusTimes100(w.A, w.B), InfluenceRules.MajorCivSpyRankBonus(w.A, w.B)));
        Set(w.A, w.B, 300); // Familiar
        Assert.Equal((100, 1), (InfluenceRules.TradeRouteScienceBonusTimes100(w.A, w.B), InfluenceRules.MajorCivSpyRankBonus(w.A, w.B)));
    }

    [Fact]
    public void TheFamiliarScienceBonusReachesTheOriginYieldOfARoute()
    {
        var w = World();
        w.B.TotalCultureForContests = 1000;
        var none = TradeRouteYields.Compute(w.CapA, w.CityB, TradeDomain.Land, TradeRouteKind.International);
        Set(w.A, w.B, 300);
        var familiar = TradeRouteYields.Compute(w.CapA, w.CityB, TradeDomain.Land, TradeRouteKind.International);
        Assert.Equal(none.Origin[TradeYield.Science] + 100, familiar.Origin[TradeYield.Science]);
        Assert.Equal(none.Dest[TradeYield.Science], familiar.Dest[TradeYield.Science]);
    }

    // --- tourism blast

    [Fact]
    public void BlastStrengthIsTourismTimesTheUnitTimesSpeedWithAMinimum()
    {
        var w = World();
        GiveWorks(w, w.CapA, 4); // 8 tourism
        int speed = KotlinMath.RoundToInt(w.Game.GameInfo.Speed.Modifier * 100f);
        Assert.Equal(100, InfluenceRules.BlastStrength(w.A, 10)); // 8 * 10 = 80, below the minimum
        Assert.Equal(System.Math.Max(100, 8 * 50 * speed / 100), InfluenceRules.BlastStrength(w.A, 50));
        Assert.Equal(100, InfluenceRules.BlastStrength(w.B, 10)); // no tourism at all
    }

    [Fact]
    public void ABlastGivesTheTargetAllAndEveryOtherMetCivAPercent()
    {
        var w = World();
        var c = w.Game.AddCiv(isPlayer: true);
        w.Game.AddCity(c, w.Game.GetTile(4, -6));
        w.A.DiplomacyFunctions.MakeCivilizationsMeet(c);
        var d = w.Game.AddCiv(isPlayer: true); // never met
        w.Game.AddCity(d, w.Game.GetTile(0, -8));
        Unmeet(w, d);
        Assert.Equal(100, InfluenceRules.ApplyBlast(w.A, w.B, 10, 20));
        Assert.Equal(100, InfluenceRules.InfluenceOn(w.A, w.B));
        Assert.Equal(20, InfluenceRules.InfluenceOn(w.A, c));
        Assert.Equal(0, InfluenceRules.InfluenceOn(w.A, d));
        Assert.Equal(0, InfluenceRules.ApplyBlast(w.A, d, 10, 20)); // not met: nothing happens
        Assert.Equal(100, InfluenceRules.InfluenceOn(w.A, w.B));
    }
}
