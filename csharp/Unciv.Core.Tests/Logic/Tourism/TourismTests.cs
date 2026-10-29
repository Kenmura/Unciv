using Unciv.Core.Tests.Logic.GreatWorks;
using Unciv.Json;
using Unciv.Logic;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Tourism;
using Unciv.Models.Ruleset;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Logic.Tourism;

// B008 (bnw-spec/SPEC.md 4.3.1 and 4.3.2): tourism of a city and of a civ, and the modifier toward another civ. All expected values are
// computed by hand from the formulas of the spec.
[Collection("RulesetCache")]
public sealed class TourismTests
{
    private static GreatWorkWorld World(params string[] aUniques)
    {
        var w = new GreatWorkWorld(aUniques);
        var c = w.Game.Ruleset.ModOptions.Constants;
        c.BaseTourismPerGreatWork = 2;
        c.TemporaryTourismBoostMod = 100;
        c.TourismModifierOpenBorders = 25;
        c.TourismModifierTradeRoute = 25;
        c.TourismModifierSharedReligion = 25;
        c.TourismModifierDifferentIdeologies = -34;
        c.TourismModifierDiplomat = 25;
        return w;
    }

    /// <summary>Fills a building of <paramref name="city"/> with <paramref name="works"/> art works (no theming rows).</summary>
    private static Building Fill(GreatWorkWorld w, CityObject city, int works, params ThemingBonus[] rows)
    {
        var building = w.Build(city, works, GreatWorkSlotType.ArtArtifact, rows);
        var names = new[] { "ArtAncient", "ArtClassical", "ArtMedieval", "ArtAnyEra" };
        for (int i = 0; i < works; i++) w.Place(city, building, i, names[i], w.A.CivId);
        return building;
    }

    private static CivilizationObject AddThirdCiv(GreatWorkWorld w)
    {
        var c = w.Game.AddCiv(isPlayer: true);
        w.Game.AddCity(c, w.Game.GetTile(4, -6));
        w.A.DiplomacyFunctions.MakeCivilizationsMeet(c);
        w.B.DiplomacyFunctions.MakeCivilizationsMeet(c);
        return c;
    }

    private static void War(CivilizationObject x, CivilizationObject y)
    {
        x.GetDiplomacyManager(y)!.DiplomaticStatus = DiplomaticStatus.War;
        y.GetDiplomacyManager(x)!.DiplomaticStatus = DiplomaticStatus.War;
    }

    // --- city tourism before modifiers

    [Fact]
    public void ACityWithoutWorksHasNoTourism()
    {
        var w = World();
        Assert.Equal(0, TourismRules.CityTourism(w.CapA));
        Assert.Equal(0, TourismRules.PlayerTourism(w.A));
    }

    [Fact]
    public void EachWorkGivesTheBaseTourismAndTheCivSumsItsCities()
    {
        var w = World();
        Fill(w, w.CapA, 3);
        Assert.Equal(6, TourismRules.CityTourism(w.CapA));
        var second = w.Build(w.CityA2, 1, GreatWorkSlotType.Music);
        w.Place(w.CityA2, second, 0, "MusicAncient", w.A.CivId);
        Assert.Equal(2, TourismRules.CityTourism(w.CityA2));
        Assert.Equal(8, TourismRules.PlayerTourism(w.A));
        Assert.Equal(0, TourismRules.PlayerTourism(w.B));
    }

    [Fact]
    public void TourismIsOffWhenTheRulesetDoesNotEnableIt()
    {
        var w = new GreatWorkWorld(enabled: false);
        w.Game.Ruleset.ModOptions.Constants.BaseTourismPerGreatWork = 2;
        Fill(w, w.CapA, 2);
        Assert.Equal(0, TourismRules.CityTourism(w.CapA));
    }

    [Fact]
    public void ThemingBonusesCountAsTourism()
    {
        var w = World();
        Fill(w, w.CapA, 2, new ThemingBonus { Bonus = 4, SameEra = false, MustBeArt = true });
        Assert.Equal(2 * 2 + 4, TourismRules.CityTourismBeforeModifiers(w.CapA));
    }

    [Fact]
    public void TheGreatWorkPercentAddsToTheWorksOnlyAndTruncates()
    {
        var w = World("[+50]% Tourism from Great Works [in all cities]");
        Fill(w, w.CapA, 3); // 6 + 6 * 50 / 100
        Assert.Equal(9, TourismRules.CityTourismBeforeModifiers(w.CapA));
        var oneWork = World("[+25]% Tourism from Great Works [in all cities]");
        Fill(oneWork, oneWork.CapA, 1); // 2 + 2 * 25 / 100 = 2 (truncated)
        Assert.Equal(2, TourismRules.CityTourismBeforeModifiers(oneWork.CapA));
    }

    [Fact]
    public void TheGreatWorkPercentRespectsItsCityFilter()
    {
        var w = World("[+100]% Tourism from Great Works [in foreign cities]", "[+50]% Tourism from Great Works [in this city]");
        Fill(w, w.CapA, 2);
        Assert.Equal(4 + 2, TourismRules.CityTourismBeforeModifiers(w.CapA)); // only the 50% applies to an own city
    }

    [Fact]
    public void ARazingCityOrACityInResistanceHasNoTourism()
    {
        var w = World();
        Fill(w, w.CapA, 2);
        w.CapA.IsBeingRazed = true;
        Assert.Equal(0, TourismRules.CityTourism(w.CapA));
        w.CapA.IsBeingRazed = false;
        Assert.Equal(4, TourismRules.CityTourism(w.CapA));
        w.CapA.SetFlag(Unciv.Logic.City.CityFlags.Resistance, 3);
        Assert.Equal(0, TourismRules.CityTourism(w.CapA));
    }

    [Fact]
    public void FlatTourismFromBuildingsCountsEachMatchingBuildingAndHonoursTheCityFilter()
    {
        var w = World("[+2] Tourism from [Buildings] buildings [in all cities]", "[+7] Tourism from [World Wonder] buildings [in foreign cities]");
        w.Game.CreateBuilding();
        w.CapA.CityConstructions.AddBuilding(w.Game.CreateBuilding());
        w.CapA.CityConstructions.AddBuilding(w.Game.CreateBuilding());
        w.CapA.CityConstructions.AddBuilding(w.Game.CreateWonder());
        // two buildings match [Buildings] (the palace is a wonder); the second unique's city filter excludes an own city
        Assert.Equal(4, TourismRules.CityTourismBeforeModifiers(w.CapA));
        Assert.Equal(0, TourismRules.CityTourismBeforeModifiers(w.CityB));
    }

    [Fact]
    public void TechEnhancedTourismOfABuildingCountsOnlyAfterTheTech()
    {
        var w = World();
        var tech = w.Game.Ruleset.Technologies.Keys.First(it => !w.A.Tech.IsResearched(it));
        w.CapA.CityConstructions.AddBuilding(w.Game.CreateBuilding("[+3] Tourism <after discovering [" + tech + "]>"));
        Assert.Equal(0, TourismRules.CityTourismBeforeModifiers(w.CapA));
        w.A.Tech.AddTechnology(tech);
        Assert.Equal(3, TourismRules.CityTourismBeforeModifiers(w.CapA));
    }

    [Fact]
    public void WonderCultureCountsAsTourismByTheBuildingsPercent()
    {
        var w = World();
        var wonder = w.Game.CreateWonder("[50]% of Culture from Wonders, Landmarks and Natural Wonders added as Tourism");
        wonder.Culture = 8f;
        w.CapA.CityConstructions.AddBuilding(wonder);
        Assert.Equal(4, TourismRules.CityTourismBeforeModifiers(w.CapA)); // 8 * 50 / 100
        var plain = World();
        var noPercent = plain.Game.CreateWonder();
        noPercent.Culture = 8f;
        plain.CapA.CityConstructions.AddBuilding(noPercent);
        Assert.Equal(0, TourismRules.CityTourismBeforeModifiers(plain.CapA));
    }

    [Fact]
    public void LandmarkTilesCountForTheLandmarkPercent()
    {
        var w = World();
        var landmark = w.Game.CreateTileImprovement("Is a Landmark");
        landmark.Culture = 6f;
        var tile = w.CapA.GetTiles().First(it => it.Position != w.CapA.GetCenterTile().Position);
        tile.Improvement = landmark.Name;
        var wonder = w.Game.CreateWonder("[100]% of Culture from Wonders, Landmarks and Natural Wonders added as Tourism");
        w.CapA.CityConstructions.AddBuilding(wonder);
        Assert.Equal(6, TourismRules.CityTourismBeforeModifiers(w.CapA));
        tile.Improvement = null;
        Assert.Equal(0, TourismRules.CityTourismBeforeModifiers(w.CapA));
    }

    // --- city modifier

    [Fact]
    public void ThePercentModifierScalesAndTruncates()
    {
        var w = World("[+25]% Tourism");
        Fill(w, w.CapA, 3);
        Assert.Equal(25, TourismRules.CityTourismModifier(w.CapA));
        Assert.Equal(7, TourismRules.CityTourism(w.CapA)); // 6 * 125 / 100 = 7
    }

    [Fact]
    public void ANegativeModifierIsIgnored()
    {
        var w = World("[-30]% Tourism");
        Fill(w, w.CapA, 3);
        Assert.Equal(0, TourismRules.CityTourismModifier(w.CapA));
        Assert.Equal(6, TourismRules.CityTourism(w.CapA));
    }

    [Fact]
    public void TheTemporaryBoostAddsItsConstantWhileTurnsRemainAndStacksAdditively()
    {
        var w = World("[+25]% Tourism");
        Fill(w, w.CapA, 3);
        w.A.Culture = new CivCultureManager();
        Assert.Equal(7, TourismRules.CityTourism(w.CapA));
        w.A.Culture.AddTourismBonusTurns(2);
        Assert.Equal(125, TourismRules.CityTourismModifier(w.CapA));
        Assert.Equal(6 * 225 / 100, TourismRules.CityTourism(w.CapA));
        w.A.Culture.AddTourismBonusTurns(-5);
        Assert.Equal(0, w.A.Culture.TourismBonusTurns);
        Assert.Equal(7, TourismRules.CityTourism(w.CapA));
    }

    [Fact]
    public void ABuildingPercentCountsOnlyWhileAMatchingBuildingStands()
    {
        var w = World("[+50]% Tourism from [World Wonder] buildings");
        Fill(w, w.CapA, 2);
        Assert.Equal(4, TourismRules.CityTourism(w.CapA));
        w.CapA.CityConstructions.AddBuilding(w.Game.CreateWonder());
        Assert.Equal(50, TourismRules.CityTourismModifier(w.CapA));
        Assert.Equal(6, TourismRules.CityTourism(w.CapA)); // 4 * 150 / 100
    }

    [Fact]
    public void AGoldenAgeModifierOnlyCountsDuringTheGoldenAge()
    {
        var w = World("[+100]% Tourism <during a Golden Age>");
        Fill(w, w.CapA, 3);
        Assert.Equal(6, TourismRules.CityTourism(w.CapA));
        w.A.GoldenAges.TurnsLeftForCurrentGoldenAge = 5;
        Assert.Equal(12, TourismRules.CityTourism(w.CapA));
    }

    // --- modifier toward a civ

    [Fact]
    public void WithoutAnyRelationTheModifierIsZero()
    {
        var w = World();
        Assert.Equal(0, TourismRules.ModifierWith(w.A, w.B));
    }

    [Fact]
    public void OpenBordersGiveTheConstantPlusThePolicyWhenTheTargetAllowsEntry()
    {
        var w = World("[+15]% Tourism toward civs with open borders with you");
        Assert.Equal(0, TourismRules.ModifierWith(w.A, w.B));
        w.B.GetDiplomacyManager(w.A)!.HasOpenBorders = true; // A allows B in: that does not help A's tourism
        Assert.Equal(0, TourismRules.ModifierWith(w.A, w.B));
        w.A.GetDiplomacyManager(w.B)!.HasOpenBorders = true; // B allows A in
        Assert.Equal(25 + 15, TourismRules.ModifierWith(w.A, w.B));
        Assert.Equal(25, TourismRules.ModifierWith(w.B, w.A)); // B -> A: A allows B in (flag B->A set above)
    }

    [Fact]
    public void ATradeRouteInEitherDirectionGivesItsConstantAndPolicy()
    {
        var w = World("[+15]% Tourism toward civs you have a trade route with");
        Assert.Equal(0, TourismRules.ModifierWith(w.A, w.B));
        w.Game.GameInfo.TradeRoutes = [new TradeRoute { Id = 1, OriginOwner = w.B.CivId, DestOwner = w.A.CivId }];
        Assert.Equal(25 + 15, TourismRules.ModifierWith(w.A, w.B));
        w.Game.GameInfo.TradeRoutes = [new TradeRoute { Id = 1, OriginOwner = w.A.CivId, DestOwner = w.B.CivId }];
        Assert.Equal(25 + 15, TourismRules.ModifierWith(w.A, w.B));
        w.Game.GameInfo.TradeRoutes = [new TradeRoute { Id = 1, OriginOwner = w.A.CivId, DestOwner = "Nobody" }];
        Assert.Equal(0, TourismRules.ModifierWith(w.A, w.B));
    }

    [Fact]
    public void DifferentIdeologiesCostAndOnlyThenTheDiplomatHelps()
    {
        var w = World("[+10]% Tourism toward civs following your ideology");
        w.A.Policies.Ideology = "Freedom";
        Assert.Equal(0, TourismRules.ModifierWith(w.A, w.B, diplomatInTargetCapital: true)); // B has none: no ideology rule at all
        w.B.Policies.Ideology = "Order";
        Assert.Equal(-34, TourismRules.ModifierWith(w.A, w.B));
        Assert.Equal(-34 + 25, TourismRules.ModifierWith(w.A, w.B, diplomatInTargetCapital: true));
        w.B.Policies.Ideology = "Freedom";
        Assert.Equal(10, TourismRules.ModifierWith(w.A, w.B, diplomatInTargetCapital: true)); // shared ideology: policy, no diplomat bonus
    }

    [Fact]
    public void LessHappyCountsOnlyWhenIAmStrictlyHappier()
    {
        var w = World("[+7]% Tourism toward civs less happy than you");
        int a = w.A.GetHappiness();
        int b = w.B.GetHappiness();
        Assert.Equal(a > b ? 7 : 0, TourismRules.ModifierWith(w.A, w.B));
        Assert.Equal(0, TourismRules.ModifierWith(w.B, w.A)); // B has no such policy
    }

    [Fact]
    public void CommonFoesCountOncePerLivingMajorCivAtWarWithBoth()
    {
        var w = World("[+10]% Tourism toward civs with a common enemy");
        var c = AddThirdCiv(w);
        Assert.Equal(0, TourismRules.ModifierWith(w.A, w.B));
        War(w.A, c);
        Assert.Equal(0, TourismRules.ModifierWith(w.A, w.B)); // B is not at war with C
        War(w.B, c);
        Assert.Equal(10, TourismRules.ModifierWith(w.A, w.B));
        Assert.Equal(0, TourismRules.ModifierWith(w.A, c)); // the only candidate third civ, B, is at war with C but not with A
    }

    [Fact]
    public void AGoldenAgeOfTheTouristCountsTheUniquesThatNameIt()
    {
        var w = World("[+100]% Tourism <during a Golden Age>", "[+30]% Tourism");
        Assert.Equal(0, TourismRules.ModifierWith(w.A, w.B));
        w.A.GoldenAges.TurnsLeftForCurrentGoldenAge = 3;
        Assert.Equal(100, TourismRules.ModifierWith(w.A, w.B));
    }

    [Fact]
    public void SharedReligionNeedsMostOfTheTargetsCitiesToFollowMyFoundedReligion()
    {
        var w = World("[+15]% Tourism toward civs following your religion");
        var second = w.Game.AddCity(w.B, w.Game.GetTile(4, 6));
        var mine = w.Game.AddReligion(w.A);
        var other = w.Game.AddReligion(w.B);
        Assert.Equal(0, TourismRules.ModifierWith(w.A, w.B)); // nobody follows anything
        w.CityB.Religion.AddPressure(mine.Name, 200);
        Assert.Equal(25 + 15, TourismRules.ModifierWith(w.A, w.B)); // one city, mine
        second.Religion.AddPressure(other.Name, 200);
        Assert.Equal(25 + 15, TourismRules.ModifierWith(w.A, w.B)); // 1:1, the religion that reached the count first (first city) wins
        second.Religion.AddPressure(other.Name, 200);
        Assert.Equal(25 + 15, TourismRules.ModifierWith(w.A, w.B));
        w.CityB.Religion.AddPressure(other.Name, 1000);
        Assert.Equal(0, TourismRules.ModifierWith(w.A, w.B)); // both cities now follow B's religion
    }

    [Fact]
    public void WithoutAFoundedReligionThereIsNoSharedReligion()
    {
        var w = World();
        var other = w.Game.AddReligion(w.B);
        w.CityB.Religion.AddPressure(other.Name, 200);
        Assert.Equal(0, TourismRules.ModifierWith(w.A, w.B));
        Assert.Equal(0, TourismRules.ModifierWith(w.B, w.A)); // A's cities follow nothing
    }

    // --- serialisation

    [Fact]
    public void AGameWithoutTourismStateSerialisesWithoutIt()
    {
        var w = World();
        string json = UncivJson.Serialize(w.Game.GameInfo);
        Assert.DoesNotContain("\"culture\":{", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ideology\"", json, StringComparison.Ordinal);
        Assert.Null(w.A.Culture);
    }
}
