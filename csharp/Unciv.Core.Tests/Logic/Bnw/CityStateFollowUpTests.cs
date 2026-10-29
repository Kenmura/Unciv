using Unciv.Core.Tests.Support;
using Unciv.Json;
using Unciv.Logic;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Map.MapUnit.Actions;
using Unciv.Models;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Logic.Bnw;

// B034 (bnw-spec 4.10, 3.2): the city-state follow-ups of Brave New World on the REAL generated ruleset: the Feitoria luxury siphon, Charitable
// Missions and Scholasticism. World: Portugal (human) has a city at (-4,0), the Mercantile city-state a city at (4,0) whose capital provides Jewelry (the Mercantile bonus).
[Collection("RulesetCache")]
public sealed class CityStateFollowUpTests
{
    private readonly TestGame game;
    private readonly CivilizationObject portugal, cityState;
    private readonly CityObject csCity;
    private GameInfo G => game.GameInfo;

    public CityStateFollowUpTests() : this(BaseRuleset.Civ_V_BNW)
    {
    }

    private CityStateFollowUpTests(BaseRuleset baseRuleset)
    {
        game = new TestGame(baseRuleset);
        game.MakeHexagonalMap(12);
        game.SetSpeed("Standard");
        G.GameParameters.BaseRuleset = baseRuleset.FullName();
        portugal = game.AddCiv(game.Ruleset.Nations[baseRuleset == BaseRuleset.Civ_V_BNW ? "Portugal" : "Rome"], isPlayer: true);
        cityState = game.AddCiv(cityStateType: "Mercantile");
        game.AddCity(portugal, game.GetTile(-4, 0));
        csCity = game.AddCity(cityState, game.GetTile(4, 0));
        portugal.DiplomacyFunctions.MakeCivilizationsMeet(cityState);
        foreach (var civ in new[] { portugal, cityState }) foreach (var other in new[] { portugal, cityState }) civ.GetDiplomacyManager(other)?.SetInfluenceWithoutSideEffects(0f);
        G.CurrentPlayerCiv = portugal;
        G.CurrentPlayer = portugal.CivId;
    }

    private int Amount(string resource) => portugal.GetResourceAmount(resource);

    private DiplomacyManager Influence => cityState.GetDiplomacyManager(portugal)!;

    // ---- Feitoria

    [Fact]
    public void TheFeitoriaIsGeneratedWithItsSiphonUniqueAndOnlyPortugalOwnsIt()
    {
        var feitoria = game.Ruleset.TileImprovements["Feitoria"];
        Assert.Equal("Portugal", feitoria.UniqueTo);
        Assert.True(((IHasUniques)feitoria).HasUnique(UniqueType.LuxuryCopiesFromCityState));
    }

    [Fact]
    public void AFeitoriaInCityStateTerritoryGivesOneCopyOfTheCityStatesLuxuriesAndItsRemovalTakesThemBack()
    {
        Assert.Equal(0, Amount("Jewelry"));
        var tile = game.GetTile(3, 0);
        Assert.Equal(cityState, tile.GetOwner());

        tile.SetImprovement("Feitoria", portugal);
        Assert.Equal(1, Amount("Jewelry"));
        Assert.Contains(portugal.DetailedCivResources, it => it.Resource.Name == "Jewelry" && it.Origin == "Feitoria" && it.Amount == 1);
        Assert.Equal(1, cityState.GetResourceAmount("Jewelry")); // the city-state keeps its own

        tile.ImprovementIsPillaged = true;
        tile.SetImprovement((Unciv.Models.Ruleset.Tile.TileImprovement?)null, portugal);
        Assert.Equal(0, Amount("Jewelry"));
    }

    [Fact]
    public void ThePillagedFeitoriaGivesNothingAndRepairingItRestoresTheCopy()
    {
        var tile = game.GetTile(3, 0);
        tile.SetImprovement("Feitoria", portugal);
        Assert.Equal(1, Amount("Jewelry"));
        tile.SetImprovement("Feitoria", portugal);
        Assert.Equal(1, Amount("Jewelry")); // rebuilding is no second copy

        tile.ImprovementIsPillaged = true;
        portugal.Cache.UpdateCivResources();
        Assert.Equal(0, Amount("Jewelry"));
        tile.ImprovementIsPillaged = false;
        portugal.Cache.UpdateCivResources();
        Assert.Equal(1, Amount("Jewelry"));
    }

    [Fact]
    public void AnEnemyPillagingTheFeitoriaStopsTheSiphon()
    {
        var tile = game.GetTile(3, 0);
        tile.SetImprovement("Feitoria", portugal);
        Assert.Equal(1, Amount("Jewelry"));
        var raider = game.AddCiv(game.Ruleset.Nations["Venice"]);
        raider.GetDiplomacyManagerOrMeet(cityState).DiplomaticStatus = DiplomaticStatus.War;
        var unit = game.AddUnit("Warrior", raider, tile);
        unit.CurrentMovement = unit.GetMaxMovement();
        var pillage = UnitActions.GetUnitActions(unit, UnitActionType.Pillage).Single();
        pillage.Action!.Invoke();
        Assert.True(tile.IsPillaged());
        Assert.Equal(0, Amount("Jewelry"));
    }

    [Fact]
    public void OnlyTheCityStatesLuxuryIsSiphonedAndOnlyForPortugalAndOnlyInsideACityStateTerritory()
    {
        var rival = game.AddCiv(game.Ruleset.Nations["Venice"], isPlayer: true);
        game.GetTile(3, 1).SetImprovement("Feitoria", rival); // Venice cannot build it: nothing is siphoned for Venice
        game.GetTile(3, 0).SetImprovement("Feitoria", portugal);

        Assert.Equal(1, Amount("Jewelry"));
        Assert.Equal(["Jewelry"], portugal.DetailedCivResources.Where(it => it.Origin == "Feitoria").Select(it => it.Resource.Name));
        Assert.Equal(0, rival.GetResourceAmount("Jewelry"));

        var own = game.GetTile(-3, 0);
        Assert.Equal(portugal, own.GetOwner());
        own.SetImprovement("Feitoria", portugal); // not in city-state territory: the siphon does not count it
        Assert.Equal(1, Amount("Jewelry"));
    }

    [Fact]
    public void TheFeitoriaAddsToTheCopiesAnAllianceAlreadyGives()
    {
        game.GetTile(3, 0).SetImprovement("Feitoria", portugal);
        Influence.SetInfluence(100f);
        cityState.CityStateFunctions.UpdateAllyCivForCityState();
        Assert.Equal(cityState.AllyCiv, portugal);
        portugal.Cache.UpdateCivResources();
        Assert.Equal(2, Amount("Jewelry"));
    }

    [Fact]
    public void GnKHasNoSiphonImprovementAndASiphonlessCivCountsNothing()
    {
        var gk = new CityStateFollowUpTests(BaseRuleset.Civ_V_GnK);
        Assert.DoesNotContain("Feitoria", gk.game.Ruleset.TileImprovements.Keys);
        gk.portugal.Cache.UpdateCivResources();
        Assert.DoesNotContain(gk.portugal.DetailedCivResources, it => it.Origin == "Feitoria");
    }

    // ---- Charitable Missions

    private void AdoptCharitableMissions()
    {
        var religion = game.AddReligion(portugal);
        religion.AddBelief("Charitable Missions");
        portugal.Cache.UpdateState();
    }

    [Fact]
    public void CharitableMissionsRaisesTheInfluenceOfGoldGiftsByThirtyPercent()
    {
        var functions = (CityStateFunctions)cityState.CityStateFunctions;
        Assert.Equal(30, functions.InfluenceGainedByGift(portugal, 300)); // 31.8 rounded down to five
        AdoptCharitableMissions();
        Assert.Equal(40, functions.InfluenceGainedByGift(portugal, 300)); // 41.3
        Assert.Equal(5, functions.InfluenceGainedByGift(portugal, 1));
        var other = game.AddCiv(game.Ruleset.Nations["Venice"]);
        Assert.Equal(30, functions.InfluenceGainedByGift(other, 300));

        portugal.AddGold(1000);
        functions.ReceiveGoldGift(portugal, 300);
        Assert.Equal(40f, Influence.GetInfluence());
    }

    [Fact]
    public void CharitableMissionsScalesAUnitGiftAndOnlyPositiveGains()
    {
        Assert.Equal(5f, ScaleInfluenceGain(5f));
        AdoptCharitableMissions();
        Assert.Equal(6.5f, ScaleInfluenceGain(5f));
        Assert.Equal(-20f, ScaleInfluenceGain(-20f));
        Assert.Equal(0f, ScaleInfluenceGain(0f));

        var tile = game.GetTile(3, 0);
        var unit = game.AddUnit("Warrior", portugal, null);
        unit.CurrentTile = tile;
        unit.CurrentMovement = unit.GetMaxMovement();
        tile.MilitaryUnit = unit;
        var gift = UnitActions.GetUnitActions(unit, UnitActionType.GiftUnit).Single();
        gift.Action!.Invoke();
        Assert.Equal(cityState, unit.Civ);
        Assert.Equal(6.5f, Influence.GetInfluence());
    }

    private float ScaleInfluenceGain(float amount) => CityStateFunctions.ScaleInfluenceGain(portugal, amount);

    [Fact]
    public void CharitableMissionsScalesTheInfluenceOfAQuestReward()
    {
        AdoptCharitableMissions();
        cityState.QuestManager = UncivJson.Deserialize<QuestManager>(
            "{\"assignedQuests\":[{\"questName\":\"Acquire Great Person\",\"assigner\":\"" + cityState.CivName + "\",\"assignee\":\"" + portugal.CivName
            + "\",\"assignedOnTurn\":0,\"data1\":\"Great Artist\"}],\"globalQuestCountdown\":33,\"individualQuestCountdown\":{}}")!;
        cityState.QuestManager.SetTransients(cityState);
        float reward = cityState.QuestManager.GetAssignedQuestsFor(portugal).Single().Quest.Influence;
        Assert.True(reward > 0f);
        game.AddUnit("Great Artist", portugal, game.GetTile(-4, 0));

        cityState.QuestManager.EndTurn();
        Assert.Empty(cityState.QuestManager.GetAssignedQuestsFor(portugal));
        Assert.Equal(reward * 1.3f, Influence.GetInfluence(), 0.01f);
    }

    // ---- Scholasticism

    [Fact]
    public void ScholasticismGivesAQuarterOfAnAlliedCityStatesScience()
    {
        csCity.CityConstructions.AddBuilding("Library");
        cityState.UpdateStatsForNextTurn();
        float csScience = cityState.Stats.StatsForNextTurn[Stat.Science];
        Assert.True(csScience > 0f);

        Influence.SetInfluence(100f);
        cityState.CityStateFunctions.UpdateAllyCivForCityState();
        Assert.Equal(cityState.AllyCiv, portugal);
        float Bonus() => portugal.Stats.GetStatMapForNextTurn().TryGetValue(Constants.CityStates, out var stats) ? stats.Science : 0f;
        Assert.Equal(0f, Bonus());

        portugal.Policies.Adopt(game.Ruleset.Policies["Scholasticism"], true);
        Assert.Equal(csScience * 0.25f, Bonus());

        Influence.SetInfluence(0f);
        cityState.CityStateFunctions.UpdateAllyCivForCityState();
        Assert.Equal(0f, Bonus()); // no longer allied
    }
}
