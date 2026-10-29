using Unciv.Logic;
using Unciv.Logic.Tourism;
using Unciv.Logic.Trade.Routes;
using Unciv.Logic.WorldCongress;
using Unciv.Models.Ruleset;
using Unciv.Logic.Map.MapUnit.Actions;
using Unciv.Models;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Metadata;
using Unciv.Utils;
using Unciv.Core.Tests.Support;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Logic.WorldCongress;

// B016 (bnw-spec/SPEC.md 4.7.5): the effects of the enacted resolutions, each proven on the real generated BNW ruleset. A, B are humans, C is an AI, each with
// a city; S is a city-state. A league exists (founded by A), the resolutions are put in force the way a decided session does it (enact + OnChanged) and
// removed again to prove that a repeal restores the state.
[Collection("RulesetCache")]
public sealed class LeagueEffectsTests
{
    private readonly TestGame game = new(BaseRuleset.Civ_V_BNW);
    private readonly CivilizationObject a, b, c, s;
    private readonly League league;
    private GameInfo G => game.GameInfo;
    private int nextId = 100;

    public LeagueEffectsTests()
    {
        game.MakeHexagonalMap(9);
        a = game.AddCiv(isPlayer: true);
        b = game.AddCiv(isPlayer: true);
        c = game.AddCiv();
        s = game.AddCiv(cityStateType: "Cultured");
        game.AddCity(a, game.GetTile(-5, 0));
        game.AddCity(b, game.GetTile(0, 0));
        game.AddCity(c, game.GetTile(5, 0));
        game.AddCity(s, game.GetTile(0, 6));
        foreach (var civ in new[] { a, b, c, s })
            foreach (var other in new[] { a, b, c, s })
                if (civ != other) civ.DiplomacyFunctions.MakeCivilizationsMeet(other);
        a.Tech.AddTechnology("Printing Press");
        a.Tech.Era = game.Ruleset.Eras["Renaissance era"];
        LeagueRules.ProcessTurn(G);
        league = G.League.NotNull();
        league.Proposals.Clear();
        league.InSession = false;
    }

    private ActiveResolution Enact(string resolution, string choice = "")
    {
        var active = new ActiveResolution { Id = nextId++, Resolution = resolution, Choice = choice, Proposer = a.CivId, Turn = G.Turns };
        league.Active.Add(active);
        LeagueEffects.OnChanged(G, league);
        return active;
    }

    private void Repeal(ActiveResolution active)
    {
        league.Active.Remove(active);
        LeagueEffects.OnChanged(G, league);
    }

    private static float Culture(CityObject city)
    {
        city.CityStats.Update();
        return city.CityStats.CurrentCityStats.Culture;
    }

    // ---- the table

    [Fact]
    public void AllEighteenResolutionsAreTheSpecRows()
    {
        var effects = LeagueData.Resolutions.ToDictionary(it => it.Name);
        Assert.Equal(18, effects.Count);
        Assert.True(effects["All City-States Embargo"].EmbargoCityStates);
        Assert.True(effects["Player Embargo"].EmbargoPlayer);
        Assert.True(effects["Ban Luxury"].NoResourceHappiness);
        Assert.Equal(25, effects["Standing Army Tax"].UnitMaintenanceGoldPercent);
        Assert.Equal(20, effects["Scholars in Residence"].MemberDiscoveredTechMod);
        Assert.Equal(3, effects["Cultural Heritage Sites"].CulturePerWonder);
        Assert.Equal(5, effects["Natural Heritage Sites"].CulturePerNaturalWonder);
        Assert.True(effects["Nuclear Non-Proliferation"].NoTrainingNuclearWeapons);
        Assert.Equal(25, effects[LeagueData.WorldReligion].ReligionSpreadStrengthMod);
        Assert.Equal(2, effects[LeagueData.WorldIdeology].OtherIdeologyRebellionMod);
        Assert.Equal((33, -33), (effects["Arts Funding"].ArtsyGreatPersonRateMod, effects["Arts Funding"].ScienceyGreatPersonRateMod));
        Assert.Equal((-33, 33), (effects["Sciences Funding"].ArtsyGreatPersonRateMod, effects["Sciences Funding"].ScienceyGreatPersonRateMod));
        Assert.Equal((2, 4), (effects["Historical Landmarks"].GreatPersonTileImprovementCulture, effects["Historical Landmarks"].LandmarkCulture));
        Assert.Equal(["World's Fair", "International Games", "International Space Station"], effects.Values.Where(it => it.Project.Length > 0).Select(it => it.Project));
    }

    [Fact]
    public void WithoutALeagueOrForANonMemberThereAreNoEffects()
    {
        Enact("Standing Army Tax");
        Assert.Equal(25, LeagueEffects.For(a).UnitMaintenancePercent);
        Assert.Equal(LeagueEffects.None, LeagueEffects.For(s)); // a city-state is no member
        var lone = new TestGame(BaseRuleset.Civ_V_BNW);
        lone.MakeHexagonalMap(3);
        Assert.Equal(LeagueEffects.None, LeagueEffects.For(lone.AddCiv()));
    }

    // ---- Change League Host / projects

    [Fact]
    public void ChangeLeagueHostMakesTheWinnerHostAndTheProjectResolutionsEnableTheirProjects()
    {
        league.InSession = true;
        league.Proposals.Add(new LeagueProposal { Id = 1, Resolution = LeagueData.ChangeLeagueHost, Votes = [new() { Civ = a.CivId, Choice = b.CivId, Count = 2 }] });
        LeagueRules.ProcessTurn(G);
        Assert.Equal(b.CivId, league.Host);

        Assert.Empty(LeagueEffects.EnabledProjects(G));
        var fair = Enact("World's Fair");
        Enact("International Games");
        Assert.Equal(["World's Fair", "International Games"], LeagueEffects.EnabledProjects(G));
        Repeal(fair);
        Assert.Equal(["International Games"], LeagueEffects.EnabledProjects(G));
    }

    // ---- embargoes

    private (RouteRefusal? Refusal, RoutePlan? Plan) Check(CivilizationObject from, CivilizationObject to)
    {
        var origin = from.Cities[0];
        var dest = to.Cities[0];
        dest.GetCenterTile().SetExplored(from, true);
        var refusal = TradeRouteRules.Check(from, origin, dest, TradeDomain.Land, TradeRouteKind.International, out var plan);
        return (refusal, plan);
    }

    [Fact]
    public void PlayerEmbargoRefusesAndEndsRoutesWithTheEmbargoedCivOnly()
    {
        a.Tech.AddTechnology("Animal Husbandry");
        var (refusal, plan) = Check(a, b);
        Assert.Null(refusal);
        var route = TradeRouteRules.Establish(game.AddUnit("Warrior", a, a.Cities[0].GetCenterTile()), plan.NotNull());
        Assert.Contains(route, G.TradeRoutes!);

        var embargo = Enact("Player Embargo", c.CivId); // somebody else: nothing happens
        Assert.Contains(route, G.TradeRoutes!);
        Assert.False(LeagueEffects.IsEmbargoed(G, a, b));
        Repeal(embargo);

        embargo = Enact("Player Embargo", b.CivId);
        Assert.DoesNotContain(route, G.TradeRoutes!);
        Assert.Equal(Unciv.Commands.CommandErrors.NotAllowed, Check(a, b).Refusal!.Value.Code);
        Assert.Equal(Unciv.Commands.CommandErrors.NotAllowed, Check(b, a).Refusal!.Value.Code); // from either side
        Assert.True(LeagueEffects.IsEmbargoed(G, a, b));
        Assert.False(LeagueEffects.IsEmbargoed(G, a, c));
        Assert.False(LeagueEffects.IsEmbargoed(G, a, a));

        Repeal(embargo);
        Assert.Null(Check(a, b).Refusal);
    }

    [Fact]
    public void CityStateEmbargoForbidsRoutesToCityStates()
    {
        a.Tech.AddTechnology("Animal Husbandry");
        Assert.Null(Check(a, s).Refusal);
        Assert.False(LeagueEffects.IsEmbargoed(G, a, s));
        var embargo = Enact("All City-States Embargo");
        Assert.True(LeagueEffects.IsEmbargoed(G, a, s));
        Assert.True(LeagueEffects.IsEmbargoed(G, s, c));
        Assert.False(LeagueEffects.IsEmbargoed(G, a, b));
        Assert.Equal(Unciv.Commands.CommandErrors.NotAllowed, Check(a, s).Refusal!.Value.Code);
        Repeal(embargo);
        Assert.Null(Check(a, s).Refusal);
    }

    [Fact]
    public void AnEmbargoEnactedByASessionEndsTheRoutesAtOnce()
    {
        a.Tech.AddTechnology("Animal Husbandry");
        var route = TradeRouteRules.Establish(game.AddUnit("Warrior", a, a.Cities[0].GetCenterTile()), Check(a, b).Plan.NotNull());
        league.InSession = true;
        league.Proposals.Add(new LeagueProposal { Id = 7, Resolution = "Player Embargo", Choice = b.CivId, Proposer = a.CivId, Votes = [new() { Civ = a.CivId, Choice = LeagueRules.Yes, Count = 2 }] });
        LeagueRules.ProcessTurn(G);
        Assert.Single(league.Active);
        Assert.DoesNotContain(route, G.TradeRoutes!);
    }

    // ---- Ban Luxury

    [Fact]
    public void BanLuxuryRemovesThatLuxuryFromTheHappinessOfEveryMember()
    {
        var luxuries = game.Ruleset.TileResources.Values.Where(it => it.ResourceType == ResourceType.Luxury && it.Improvement != null).Take(2).ToList();
        var second = game.AddCity(a, game.GetTile(-5, 4));
        var centers = new[] { a.Cities[0], second };
        for (int i = 0; i < luxuries.Count; i++)
        {
            a.Tech.AddTechnology(game.Ruleset.TileImprovements[luxuries[i].Improvement!].TechRequired ?? "Mining");
            centers[i].GetCenterTile().SetTileResource(luxuries[i].Name);
            centers[i].GetCenterTile().ResourceAmount = 1;
        }
        a.Cache.UpdateCivResources();
        float Happiness() => a.Stats.GetHappinessBreakdown()["Luxury resources"];
        float both = Happiness();
        Assert.True(both > 0);
        var ban = Enact("Ban Luxury", luxuries[0].Name);
        Assert.Equal(both / 2, Happiness());
        Assert.True(LeagueEffects.For(a).IsLuxuryBanned(luxuries[0].Name));
        Assert.False(LeagueEffects.For(a).IsLuxuryBanned(luxuries[1].Name));
        Repeal(ban);
        Assert.Equal(both, Happiness());
    }

    // ---- Standing Army Tax

    [Fact]
    public void StandingArmyTaxRaisesUnitMaintenanceByAQuarter()
    {
        foreach (var tile in G.TileMap.Values.Where(it => it.IsLand && it.MilitaryUnit == null && it.GetCity() == null).Take(40)) game.AddUnit("Warrior", a, tile);
        float Upkeep() => -a.Stats.GetStatMapForNextTurn()["Unit upkeep"].Gold;
        float before = Upkeep();
        Assert.True(before >= 8);
        var tax = Enact("Standing Army Tax");
        float after = Upkeep();
        Assert.True(after > before);
        Assert.InRange(after, before * 1.2f, before * 1.3f);
        Repeal(tax);
        Assert.Equal(before, Upkeep());
    }

    // ---- Scholars in Residence

    [Fact]
    public void ScholarsInResidenceCutsTheCostOfTechsAnotherMemberKnowsByAFifth()
    {
        const string tech = "Currency";
        b.Tech.AddTechnology(tech);
        int plain = a.Tech.CostOfTech(tech);
        int plainB = b.Tech.CostOfTech(tech);
        int plainS = s.Tech.CostOfTech(tech);
        int plainSailing = a.Tech.CostOfTech("Sailing");
        var scholars = Enact("Scholars in Residence");
        int expected = (int)(plain * 0.8f);
        Assert.InRange(a.Tech.CostOfTech(tech), expected - 1, expected + 1);
        Assert.Equal(plainSailing, a.Tech.CostOfTech("Sailing")); // nobody else knows it
        Assert.Equal(plainB, b.Tech.CostOfTech(tech)); // B's own discount needs another member (A) to know it
        Assert.Equal(plainS, s.Tech.CostOfTech(tech)); // a city-state is no member
        a.Tech.AddTechnology("Sailing");
        Assert.Equal(plainB, b.Tech.CostOfTech(tech));
        Assert.True(b.Tech.CostOfTech("Sailing") < plainSailing);
        Repeal(scholars);
        Assert.Equal(plain, a.Tech.CostOfTech(tech));
    }

    // ---- heritage and landmarks

    [Fact]
    public void CulturalHeritageSitesGiveThreeCulturePerWorldWonder()
    {
        var city = a.Cities[0];
        var wonders = game.Ruleset.Buildings.Values.Where(it => it.IsWonder && it.Name != "Palace").Take(2).ToList();
        foreach (var wonder in wonders) city.CityConstructions.AddBuilding(wonder.Name);
        float before = Culture(city);
        float without = Culture(b.Cities[0]);
        var sites = Enact("Cultural Heritage Sites");
        Assert.Equal(before + 6, Culture(city));
        Assert.Equal(without, Culture(b.Cities[0])); // B has no wonders: nothing to add
        Repeal(sites);
        Assert.Equal(before, Culture(city));
    }

    [Fact]
    public void NaturalHeritageSitesGiveFiveCulturePerNaturalWonderInTheCityTerritory()
    {
        var city = a.Cities[0];
        var natural = game.Ruleset.Terrains.Values.First(it => it.Type == TerrainType.NaturalWonder);
        foreach (var dx in new[] { 1, 2 })
        {
            var tile = game.GetTile(-5 + dx, -1);
            tile.NaturalWonder = natural.Name;
            game.AddTileToCity(city, tile);
        }
        float before = Culture(city);
        var sites = Enact("Natural Heritage Sites");
        Assert.Equal(before + 10, Culture(city));
        Repeal(sites);
        Assert.Equal(before, Culture(city));
    }

    [Fact]
    public void HistoricalLandmarksGiveCultureOnWorkedGreatPersonImprovementsAndLandmarks()
    {
        var city = a.Cities[0];
        var great = game.Ruleset.TileImprovements.Values.First(it => it.IsGreatImprovement());
        var landmark = game.Ruleset.TileImprovements.Values.First(it => ((IHasUniques)it).HasUnique(UniqueType.IsLandmark));
        var plain = game.Ruleset.TileImprovements.Values.First(it => !it.IsGreatImprovement() && it.Name == "Farm");
        var tiles = new[] { (great, 1), (landmark, 2), (plain, 3) };
        foreach (var (improvement, dx) in tiles)
        {
            var tile = game.GetTile(-5 + dx, 1);
            tile.Improvement = improvement.Name;
            game.AddTileToCity(city, tile);
            city.WorkedTiles.Add(tile.Position);
        }
        float before = Culture(city);
        var landmarks = Enact("Historical Landmarks");
        Assert.Equal(before + 2 + 4, Culture(city));
        var greatTile = game.GetTile(-5 + 1, 1);
        city.WorkedTiles.Remove(greatTile.Position); // an unworked tile gives nothing
        Assert.Equal(4, LeagueEffects.CityStats(city).Culture);
        greatTile.ImprovementIsPillaged = true;
        city.WorkedTiles.Add(greatTile.Position); // and neither does a pillaged one
        Assert.Equal(4, LeagueEffects.CityStats(city).Culture);
        Repeal(landmarks);
        Assert.Equal(0, LeagueEffects.CityStats(city).Culture);
    }

    // ---- Nuclear Non-Proliferation

    [Fact]
    public void NuclearNonProliferationForbidsBuildingNuclearWeapons()
    {
        var nuke = game.Ruleset.Units.Values.First(it => ((IHasUniques)it).HasUnique(UniqueType.NuclearWeapon));
        var city = a.Cities[0];
        bool Rejected() => nuke.GetRejectionReasons(city.CityConstructions).Any(it => it.ErrorMessage.Contains("Nuclear Non-Proliferation"));
        Assert.False(Rejected());
        var ban = Enact("Nuclear Non-Proliferation");
        Assert.True(Rejected());
        Assert.DoesNotContain(game.Ruleset.Units.Values.First(it => it.IsMilitary && !((IHasUniques)it).HasUnique(UniqueType.NuclearWeapon)).GetRejectionReasons(city.CityConstructions), it => it.ErrorMessage.Contains("Nuclear"));
        Repeal(ban);
        Assert.False(Rejected());
    }

    // ---- World Religion and World Ideology

    [Fact]
    public void WorldReligionRaisesTheSpreadStrengthOfTheChosenReligionByAQuarter()
    {
        var religion = game.AddReligion(a);
        religion.AddBelief(game.Ruleset.Beliefs.Values.First(it => it.Type == BeliefType.Founder).Name);
        var other = game.AddReligion(b);
        Assert.Equal(0, G.LeagueReligionSpreadPercent(religion.Name));
        var world = Enact(LeagueData.WorldReligion, religion.Name);
        Assert.Equal(25, G.LeagueReligionSpreadPercent(religion.Name));
        Assert.Equal(0, G.LeagueReligionSpreadPercent(other.Name));
        Assert.Equal(0, G.LeagueReligionSpreadPercent(null));

        // a city that spreads the religion presses its neighbour a quarter harder
        var home = a.Cities[0];
        var neighbour = game.AddCity(a, game.GetTile(-5, 4));
        home.Religion.AddPressure(religion.Name, 1000);
        Assert.Equal(religion.Name, home.Religion.GetMajorityReligionName());
        Repeal(world);
        int plain = neighbour.Religion.GetPressuresFromSurroundingCities()[religion.Name];
        Assert.True(plain > 0);
        Enact(LeagueData.WorldReligion, religion.Name);
        Assert.Equal((int)(plain * 1.25f), neighbour.Religion.GetPressuresFromSurroundingCities()[religion.Name]);
    }

    [Fact]
    public void WorldReligionAlsoStrengthensTheMissionariesOfTheChosenReligion()
    {
        var religion = game.AddReligion(a);
        religion.AddBelief(game.Ruleset.Beliefs.Values.First(it => it.Type == BeliefType.Founder).Name);
        int Spread(CityObject target)
        {
            var missionary = game.AddUnit("Missionary", a, target.GetCenterTile());
            missionary.Religion = religion.Name;
            var action = UnitActions.GetUnitActions(missionary, UnitActionType.SpreadReligion).First();
            Assert.NotNull(action.Action);
            action.Action();
            return target.Religion.GetPressures()[religion.Name];
        }
        int plain = Spread(game.AddCity(a, game.GetTile(-5, 4)));
        Assert.True(plain > 0);
        Enact(LeagueData.WorldReligion, religion.Name);
        Assert.Equal((int)(plain * 1.25f), Spread(game.AddCity(a, game.GetTile(-5, -4))));
    }

    [Fact]
    public void WorldIdeologyAddsPublicOpinionPressureTowardTheChosenIdeology()
    {
        Assert.Equal(0, LeagueEffects.IdeologyPressure(a, "Freedom"));
        var world = Enact(LeagueData.WorldIdeology, "Freedom");
        Assert.Equal(2, LeagueEffects.IdeologyPressure(a, "Freedom"));
        Assert.Equal(0, LeagueEffects.IdeologyPressure(a, "Order"));
        Assert.Equal(0, LeagueEffects.IdeologyPressure(s, "Freedom"));

        a.Policies.Ideology = "Order"; // 2 against the chosen Freedom: dissidents who would rather follow it
        var opinion = PublicOpinionRules.Evaluate(a);
        Assert.Equal((PublicOpinionLevel.Dissidents, 2, "Freedom"), (opinion.Level, opinion.Dissatisfaction, opinion.Preferred));
        a.Policies.Ideology = "Freedom"; // and for a follower of the chosen one nothing
        Assert.Equal(PublicOpinionLevel.Content, PublicOpinionRules.Evaluate(a).Level);
        Repeal(world);
        Assert.Equal(0, LeagueEffects.IdeologyPressure(a, "Freedom"));
        a.Policies.Ideology = "Order";
        Assert.Equal(PublicOpinionLevel.Content, PublicOpinionRules.Evaluate(a).Level);
    }

    // ---- Arts and Sciences Funding

    private static int WithPercent(int basePoints, int percent) => (basePoints * 1000 + basePoints * 1000 * percent / 100 + 500) / 1000;

    [Fact]
    public void ArtsAndSciencesFundingMoveTheGreatPersonRatesByAThirdInOppositeDirections()
    {
        var city = a.Cities[0];
        var artsy = game.Ruleset.Buildings.Values.First(it => it.GreatPersonPoints.ContainsKey("Great Writer"));
        var sciencey = game.Ruleset.Buildings.Values.First(it => it.GreatPersonPoints.ContainsKey("Great Scientist"));
        city.CityConstructions.AddBuilding(artsy.Name);
        city.CityConstructions.AddBuilding(sciencey.Name);
        int writer = city.GetGreatPersonPoints()["Great Writer"];
        int scientist = city.GetGreatPersonPoints()["Great Scientist"];
        int baseWriter = artsy.GreatPersonPoints["Great Writer"];
        int baseScientist = sciencey.GreatPersonPoints["Great Scientist"];

        var arts = Enact("Arts Funding");
        Assert.Equal(writer + WithPercent(baseWriter, 33) - baseWriter, city.GetGreatPersonPoints()["Great Writer"]);
        Assert.Equal(scientist + WithPercent(baseScientist, -33) - baseScientist, city.GetGreatPersonPoints()["Great Scientist"]);
        Repeal(arts);
        Assert.Equal(writer, city.GetGreatPersonPoints()["Great Writer"]);

        var sciences = Enact("Sciences Funding");
        Assert.Equal(writer + WithPercent(baseWriter, -33) - baseWriter, city.GetGreatPersonPoints()["Great Writer"]);
        Assert.Equal(scientist + WithPercent(baseScientist, 33) - baseScientist, city.GetGreatPersonPoints()["Great Scientist"]);
        Enact("Arts Funding"); // both: the two percents cancel
        Assert.Equal(writer, city.GetGreatPersonPoints()["Great Writer"]);
        Assert.Equal(scientist, city.GetGreatPersonPoints()["Great Scientist"]);
        Repeal(sciences);
    }
}
