using Unciv.Logic.City;
using Unciv.Logic.City.Managers;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Map;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Nation;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using TradeObject = Unciv.Logic.Trade.Trade;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Core.Tests.Logic.CityTests;

// T064: CityTurnManager and CityConquestFunctions over the same real Civilization / TileMap / City world as CityManagerTests (the
// TestGame-based Kotlin fixture CityConquestFunctionsTest is ported with T072). Battle (T083) and TradeLogic (T086) are stand-ins
// installed on City.ConquestExternals.
public sealed class CityTurnTests
{
    private sealed class StandInConquestExternals : ICityConquestExternals
    {
        public List<(string Defeated, string Attacker, HexCoord? Location)> DestroyIfDefeatedCalls { get; } = [];
        public List<(string Our, string Their, TradeObject Trade)> AcceptedTrades { get; } = [];
        public bool AutoAssignSpecialistsInNewCities => true;
        public string? LocalUserId => null;
        public void DestroyIfDefeated(Civilization attackedCiv, Civilization attacker, HexCoord? notificationLocation) =>
            DestroyIfDefeatedCalls.Add((attackedCiv.CivId, attacker.CivId, notificationLocation));
        public void AcceptTrade(Civilization ourCiv, Civilization theirCiv, TradeObject trade) =>
            AcceptedTrades.Add((ourCiv.CivId, theirCiv.CivId, trade));
    }

    private static CityManagerTests.World NewWorld(bool human = true, List<string>? nationUniques = null)
    {
        var w = CityManagerTests.NewWorld(human, nationUniques: nationUniques);
        w.Constructions.ChooseNextConstructionIsNoOp = true; // an empty queue would ask the (unported, T091) construction automation
        w.Stats.ResetCounters();
        return w;
    }

    // ---- CityTurnManager

    [Fact]
    public void StartTurn_SeedsTheResourceDemandCountdownOfANewCity_AndUpdatesTheStats()
    {
        var w = NewWorld();
        Assert.False(w.City.HasFlag(CityFlags.ResourceDemand));
        w.City.AttackedThisTurn = true;

        new CityTurnManager(w.City).StartTurn();

        Assert.False(w.City.AttackedThisTurn);
        Assert.True(w.City.HasFlag(CityFlags.ResourceDemand));
        // the capital check is false here (no palace): 15 + nextInt(10) turns at standard speed, one of them already counted down in nextTurnFlags? No: the flag is set after it
        Assert.InRange(w.City.GetFlag(CityFlags.ResourceDemand), 15, 24);
        Assert.Equal(1, w.Stats.UpdateCalls);
    }

    [Fact]
    public void StartTurn_PuppetsFocusOnGold_AndReassignAllPopulation()
    {
        var w = NewWorld();
        w.City.IsPuppet = true;

        new CityTurnManager(w.City).StartTurn();

        Assert.Equal(CityFocus.GoldFocus.ToString(), w.City.GetCityFocus().ToString());
        Assert.False(w.City.ShouldReassignPopulation);
    }

    [Fact]
    public void StartTurn_CountsDownFlags_AndAnnouncesTheEndOfResistance()
    {
        var w = NewWorld();
        w.City.SetFlag(CityFlags.Resistance, 2);

        var turn = new CityTurnManager(w.City);
        turn.StartTurn();
        Assert.Equal(1, w.City.GetFlag(CityFlags.Resistance));
        Assert.Empty(w.Civ.Notifications);

        turn.StartTurn();
        Assert.False(w.City.HasFlag(CityFlags.Resistance));
        var notification = Assert.Single(w.Civ.Notifications);
        Assert.Equal("The resistance in [Roma] has ended!", notification.Text);
    }

    [Fact]
    public void StartTurn_WhenTheResourceDemandExpires_DemandsAMissingLuxuryNotInTheCityRadius()
    {
        var w = NewWorld();
        w.TileAt(0, 0).TileResource = w.Info.RulesetData.TileResources["Wine"]; // far outside the work range of the city at (4,4)
        w.City.SetFlag(CityFlags.ResourceDemand, 1);

        new CityTurnManager(w.City).StartTurn();

        Assert.Equal("Wine", w.City.DemandedResource);
        Assert.True(w.City.HasFlag(CityFlags.ResourceDemand)); // the cooldown for the next demand
        Assert.Contains(w.Civ.Notifications, it => it.Text == "[Roma] demands [Wine]!");
    }

    [Fact]
    public void StartTurn_DoesNotDemandAResourceThatLiesInTheCityRadius()
    {
        var w = NewWorld();
        w.TileAt(5, 4).TileResource = w.Info.RulesetData.TileResources["Wine"]; // next to the city
        w.City.SetFlag(CityFlags.ResourceDemand, 1);

        new CityTurnManager(w.City).StartTurn();

        Assert.Equal("", w.City.DemandedResource);
    }

    [Fact]
    public void EndTurn_HealsTwentyPointsUpToTheMaximum()
    {
        var w = NewWorld();
        w.City.Health = 150;
        new CityTurnManager(w.City).EndTurn();
        Assert.Equal(170, w.City.Health);
        w.City.Health = 195;
        new CityTurnManager(w.City).EndTurn();
        Assert.Equal(200, w.City.Health);
    }

    [Fact]
    public void EndTurn_ARazedCityLosesOnePopulationPerTurn_AndIsDestroyedWhenItRunsOut()
    {
        var w = NewWorld();
        w.City.IsBeingRazed = true;
        w.Population.Population = 3;
        w.Population.FoodStored = 500;

        new CityTurnManager(w.City).EndTurn();
        Assert.Equal(2, w.Population.Population);
        Assert.Equal(w.Population.GetFoodToNextPopulation() - 1, w.Population.FoodStored);
        Assert.Equal([w.City], w.Civ.Cities);

        w.Population.Population = 1;
        new CityTurnManager(w.City).EndTurn();
        Assert.Empty(w.Civ.Cities);
        Assert.Contains(w.Civ.Notifications, it => it.Text == "[Roma] has been razed to the ground!");
    }

    [Theory]
    [InlineData(4, false, "Wonder", true)]
    [InlineData(4, false, "Spaceship", true)]
    [InlineData(4, false, "Plain", false)]
    [InlineData(3, false, "Wonder", false)]
    [InlineData(4, true, "Wonder", false)] // human players keep their focus: only the AI chooses
    public void StartTurn_AiCitiesChooseTheirFocusFromThePopulationAndTheConstruction(int population, bool human, string construction, bool expectsProduction)
    {
        var w = NewWorld(human: human);
        w.Population.Population = population;
        w.Constructions.CurrentConstruction = construction switch
        {
            "Wonder" => new Building { Name = "Pyramids", IsWonder = true },
            "Spaceship" => new Building { Name = "Apollo Program", Uniques = [UniqueType.SpaceshipPart.Text()] },
            _ => new Building { Name = "Library" },
        };

        new CityTurnManager(w.City).StartTurn();

        Assert.Equal((expectsProduction ? CityFocus.ProductionFocus : CityFocus.NoFocus).ToString(), w.City.GetCityFocus().ToString());
    }

    [Fact]
    public void StartTurn_CelebratesWeLoveTheKingDay_WhenTheCivHasTheDemandedResource()
    {
        var w = NewWorld();
        var wine = w.Info.RulesetData.TileResources["Wine"];
        w.Civ.SummarizedCivResourceSupply = new ResourceSupplyList { new ResourceSupplyList.ResourceSupply(wine, "Tiles", 1) };
        w.City.DemandedResource = "Wine";
        w.City.SetFlag(CityFlags.ResourceDemand, 5);
        w.Info.SpeedData.Modifier = 1.5f;

        new CityTurnManager(w.City).StartTurn();

        Assert.False(w.City.HasFlag(CityFlags.ResourceDemand));
        Assert.Equal(KotlinMath.RoundToInt(20 * 1.5f) + 1 - 1, w.City.GetFlag(CityFlags.WeLoveTheKing)); // +1 is counted down in the same startTurn
        Assert.Contains(w.Civ.Notifications, it => it.Text == "Because they have [Wine], the citizens of [Roma] are celebrating We Love The King Day!");
    }

    [Fact]
    public void StartTurn_TheEndOfWeLoveTheKingDayNotifiesAndDemandsTheNextResource()
    {
        var w = NewWorld();
        w.City.DemandedResource = "Wine";
        w.City.SetFlag(CityFlags.WeLoveTheKing, 1);

        new CityTurnManager(w.City).StartTurn();

        Assert.False(w.City.HasFlag(CityFlags.WeLoveTheKing));
        Assert.Contains(w.Civ.Notifications, it => it.Text == "We Love The King Day in [Roma] has ended.");
        Assert.Equal("", w.City.DemandedResource); // no luxury exists on the map to demand
    }

    [Fact]
    public void StartTurn_DemandsAnyCandidateWhenTheCivAlreadyHasAllOfThem()
    {
        var w = NewWorld();
        var wine = w.Info.RulesetData.TileResources["Wine"];
        w.TileAt(0, 0).TileResource = wine;
        w.Civ.SummarizedCivResourceSupply = new ResourceSupplyList { new ResourceSupplyList.ResourceSupply(wine, "Tiles", 1) };
        w.City.SetFlag(CityFlags.ResourceDemand, 1);

        new CityTurnManager(w.City).StartTurn();

        Assert.Equal("Wine", w.City.DemandedResource);
        Assert.False(w.City.HasFlag(CityFlags.ResourceDemand)); // no cooldown and no notification: it triggers WLTKD next turn
        Assert.DoesNotContain(w.Civ.Notifications, it => it.Text.Contains("demands"));
    }

    [Fact]
    public void EndTurn_CitiesRazedFasterLoseMorePopulation()
    {
        var w = NewWorld(nationUniques: ["Cities are razed [3] times as fast"]);
        w.City.IsBeingRazed = true;
        w.Population.Population = 4;

        new CityTurnManager(w.City).EndTurn();

        Assert.Equal(1, w.Population.Population); // 4 - (1 + (3 - 1))
        Assert.Equal([w.City], w.Civ.Cities);
    }

    // ---- CityConquestFunctions

    [Fact]
    public void AnnexCity_EndsThePuppetState_AndSchedulesAPopulationReassignment()
    {
        var w = NewWorld();
        w.City.IsPuppet = true;
        w.City.AvoidGrowth = true;

        w.City.AnnexCity();

        Assert.False(w.City.IsPuppet);
        Assert.False(w.City.AvoidGrowth);
        Assert.True(w.City.ShouldReassignPopulation);
        Assert.Equal(1, w.Stats.UpdateCalls);
    }

    [Fact]
    public void AnnexCity_InResistance_DoesNotScheduleAReassignment()
    {
        var w = NewWorld();
        w.City.SetFlag(CityFlags.Resistance, 3);
        w.City.AnnexCity();
        Assert.False(w.City.ShouldReassignPopulation);
    }

    private static Civilization AddCiv(CityManagerTests.World w, string name, bool human = true) =>
        CityManagerTests.MakeCiv(w.Info, name, human);

    [Fact]
    public void MoveToCiv_TransfersTheCityAndRecordsThePreviousOwner()
    {
        var w = NewWorld();
        var greece = AddCiv(w, "Greece");
        w.Info.Turns = 42;
        w.City.IsBeingRazed = true;
        w.City.HasJustBeenConquered = true;
        w.City.UnitShouldUseSavedPromotion["Warrior"] = true;
        w.City.SetFlag(CityFlags.WeLoveTheKing, 5);
        w.City.DemandedResource = "Wine";

        w.City.MoveToCiv(greece);

        Assert.Empty(w.Civ.Cities);
        Assert.Equal([w.City], greece.Cities);
        Assert.Same(greece, w.City.Civ);
        Assert.Equal("c1", w.City.Id);
        Assert.Equal("Rome", w.City.PreviousOwner);
        Assert.Equal(42, w.City.TurnAcquired);
        Assert.False(w.City.HasJustBeenConquered);
        Assert.False(w.City.IsBeingRazed);
        Assert.Empty(w.City.UnitShouldUseSavedPromotion);
        Assert.False(w.City.HasFlag(CityFlags.WeLoveTheKing));
        Assert.Equal("", w.City.DemandedResource);
        Assert.Same(greece, w.City.State.CivInfo);
    }

    [Fact]
    public void PuppetCity_PlundersGold_PuppetsTheCity_AndAddsResistance()
    {
        var w = NewWorld();
        var greece = AddCiv(w, "Greece");
        var externals = new StandInConquestExternals();
        w.City.ConquestExternals = externals;
        w.Info.Turns = 100; // turnAcquired 0: the turn modifier is capped at 1
        w.Population.Population = 5;
        w.City.Health = 200;
        var expectedGold = 20 + 10 * 5 + new GameRandom(w.City.GetCenterTile().Position.GetHashCode()).NextInt(40);

        w.City.PuppetCity(greece);

        Assert.Equal(expectedGold, greece.Gold);
        Assert.Contains(greece.Notifications, it => it.Text == $"Received [{expectedGold}] Gold for capturing [Roma]");
        Assert.Same(greece, w.City.Civ);
        Assert.True(w.City.IsPuppet);
        Assert.Equal(100, w.City.Health);
        Assert.Equal(5 - 1 - 5 / 4, w.Population.Population);
        Assert.True(w.City.HasFlag(CityFlags.Resistance));
        Assert.Equal(w.Population.Population, w.City.GetFlag(CityFlags.Resistance));
        Assert.Equal([("Rome", "Greece", (HexCoord?)w.City.Location)], externals.DestroyIfDefeatedCalls);
        Assert.True(greece.Knows(w.Civ));
    }

    [Fact]
    public void PuppetCity_ScalesTheGoldWithTheTurnsSinceTheCityWasAcquired()
    {
        var w = NewWorld();
        var greece = AddCiv(w, "Greece");
        w.City.ConquestExternals = new StandInConquestExternals();
        w.Info.Turns = 10;
        w.City.TurnAcquired = 0; // 10 / 50 of the plunder
        var baseGold = 20 + 10 * 1 + new GameRandom(w.City.GetCenterTile().Position.GetHashCode()).NextInt(40);

        w.City.PuppetCity(greece);

        Assert.Equal((int)(baseGold * (10 / 50f)), greece.Gold);
    }

    [Fact]
    public void LiberateCity_OfACityWithoutFoundingCiv_PuppetsAndAnnexesIt()
    {
        var w = NewWorld();
        var greece = AddCiv(w, "Greece");
        w.City.ConquestExternals = new StandInConquestExternals();
        Assert.Null(w.City.FoundingCivObject);

        w.City.LiberateCity(greece);

        Assert.Same(greece, w.City.Civ);
        Assert.False(w.City.IsPuppet);
    }

    [Fact]
    public void LiberateCity_ReturnsTheCityToItsFounder_AndOffersOpenBorders()
    {
        var w = NewWorld();
        var greece = AddCiv(w, "Greece");
        var externals = new StandInConquestExternals();
        w.City.ConquestExternals = externals;
        w.City.FoundingCivObject = w.Civ;
        w.City.MoveToCiv(greece); // Greece holds a city that Rome founded
        Assert.Empty(w.Civ.Cities);
        w.Population.Population = 1;

        w.City.LiberateCity(greece);

        Assert.Same(w.Civ, w.City.Civ);
        Assert.Equal([w.City], w.Civ.Cities);
        Assert.False(w.City.IsPuppet);
        Assert.False(w.City.HasFlag(CityFlags.Resistance)); // the founder receives it: no resistance
        Assert.Equal(10f + 100f, w.Civ.GetDiplomacyManager(greece)!.DiplomaticModifiers["CapturedOurCities"]);
        var (our, their, trade) = Assert.Single(externals.AcceptedTrades);
        Assert.Equal(("Rome", "Greece"), (our, their));
        Assert.Equal(["Open Borders"], trade.OurOffers.Select(it => it.Name));
        Assert.Empty(trade.TheirOffers);
    }

    private static UniqueObject BuildingUnique(string text) => new(text, UniqueTarget.Building, "Test");

    [Fact]
    public void PuppetCity_AppliesTheGoldModifiersOfTheCityAndTheConqueror()
    {
        var w = NewWorld();
        var greece = AddCiv(w, "Greece");
        greece.Nation.Uniques = ["Receive [100]% Gold from Barbarian encampments and pillaging Cities"];
        w.Constructions.BuiltUniques.AddUnique(BuildingUnique("[-50]% Gold given to enemy if city is captured"));
        w.City.ConquestExternals = new StandInConquestExternals();
        w.Info.Turns = 100;
        var baseGold = 20 + 10 * 1 + new GameRandom(w.City.GetCenterTile().Position.GetHashCode()).NextInt(40);

        w.City.PuppetCity(greece);

        Assert.Equal((int)(baseGold * 1f * (1 + -50 / 100f) * (1 + 100 / 100f)), greece.Gold);
    }

    [Fact]
    public void PuppetCity_DestroysBuildingsOnCaptureByTheirUniquesAndTheSeededChance()
    {
        var w = NewWorld();
        var greece = AddCiv(w, "Greece");
        w.City.ConquestExternals = new StandInConquestExternals();
        var wonder = new Building { Name = "Pyramids", IsWonder = true };
        var kept = new Building { Name = "Monument", Uniques = ["Never destroyed when the city is captured"] };
        var palace = new Building { Name = "Palace", Uniques = ["Indicates the capital city"] };
        var doomed = new Building { Name = "Walls", Uniques = ["Destroyed when the city is captured"] };
        var regular = Enumerable.Range(0, 8).Select(it => new Building { Name = $"Regular{it}" }).ToList();
        w.Constructions.BuiltBuildingObjects.AddRange([wonder, kept, palace, doomed, .. regular]);

        // the Kotlin tileBasedRandom: the gold roll comes first, then one nextInt(100) per regular building (in order)
        var rng = new GameRandom(w.City.GetCenterTile().Position.GetHashCode());
        rng.NextInt(40);
        var expectedSurvivors = regular.Where(_ => rng.NextInt(100) >= 34).ToList();

        w.City.PuppetCity(greece);

        Assert.Equal([wonder, kept, palace, .. expectedSurvivors], w.Constructions.BuiltBuildingObjects);
    }

    [Fact]
    public void MoveToCiv_RemovesNationalWondersAndBuildingsOverTheLimitOfTheNewCiv()
    {
        var w = NewWorld();
        var greece = AddCiv(w, "Greece");
        var national = new Building { Name = "Heroic Epic", IsNationalWonder = true };
        var keptNational = new Building { Name = "Statue", IsNationalWonder = true, Uniques = ["Never destroyed when the city is captured"] };
        var limited = new Building { Name = "Manhattan Project", Uniques = ["Limited to [1] per Civilization"] };
        w.Constructions.BuiltBuildingObjects.AddRange([national, keptNational, limited]);
        var existing = new CityObject { Id = "g1", Name = "Athens", Location = HexCoord.Of(0, 0), CityConstructions = new FakeCityConstructions() };
        greece.Cities = [existing];
        existing.SetTransients(greece);
        ((FakeCityConstructions)existing.CityConstructions).BuiltNames.Add("Manhattan Project");

        w.City.MoveToCiv(greece);

        Assert.Equal([keptNational], w.Constructions.BuiltBuildingObjects);
        Assert.Equal([existing, w.City], greece.Cities.ToList());
    }

    [Fact]
    public void MoveToCiv_PuppetsTheCityOfACivThatMayNotAnnex()
    {
        var w = NewWorld();
        var greece = AddCiv(w, "Greece");
        greece.Nation.Uniques = ["May not annex cities"];
        w.Constructions.CurrentConstructionNameData = "";

        w.City.MoveToCiv(greece);

        Assert.True(w.City.IsPuppet);
    }

    [Fact]
    public void PuppetCity_RaisesTheAttitudeOfOthersAgainstTheConqueror()
    {
        var w = NewWorld();
        var greece = AddCiv(w, "Greece");
        var egypt = AddCiv(w, "Egypt");
        var persia = AddCiv(w, "Persia");
        greece.DiplomacyFunctions.MakeCivilizationsMeet(egypt);
        greece.DiplomacyFunctions.MakeCivilizationsMeet(persia);
        persia.DiplomacyFunctions.MakeCivilizationsMeet(w.Civ);
        persia.GetDiplomacyManager(w.Civ)!.DiplomaticStatus = DiplomaticStatus.War; // Persia shares an enemy with Greece
        w.City.ConquestExternals = new StandInConquestExternals();

        w.City.PuppetCity(greece);

        // 100% of Rome's population is in the city: aggro = 10 + 100
        Assert.Equal(-110f, w.Civ.GetDiplomacyManager(greece)!.DiplomaticModifiers["CapturedOurCities"]);
        Assert.Equal(-11f, egypt.GetDiplomacyManager(greece)!.DiplomaticModifiers["WarMongerer"]);
        Assert.Equal(11f, persia.GetDiplomacyManager(greece)!.DiplomaticModifiers["SharedEnemy"]);
    }

    [Fact]
    public void LiberateCity_OfACityStateGivesInfluenceAndPeace()
    {
        var w = NewWorld();
        var greece = AddCiv(w, "Greece");
        var egypt = AddCiv(w, "Egypt");
        w.Info.RulesetData.CityStateTypes.Put("Cultured", new CityStateType { Name = "Cultured" });
        var state = CityManagerTests.MakeCiv(w.Info, "Geneva", cityStateType: "Cultured");
        state.QuestManager = new FakeQuestManager();
        var externals = new StandInConquestExternals();
        w.City.ConquestExternals = externals;
        w.City.FoundingCivObject = state;
        egypt.DiplomacyFunctions.MakeCivilizationsMeet(state);
        state.GetDiplomacyManager(egypt)!.SetInfluence(40f);
        state.DiplomacyFunctions.MakeCivilizationsMeet(w.Civ);
        state.GetDiplomacyManager(w.Civ)!.DiplomaticStatus = DiplomaticStatus.War;
        w.City.MoveToCiv(greece);

        w.City.LiberateCity(greece);

        Assert.Same(state, w.City.Civ);
        // max(influence of the other majors, -60) = 40; max(40, own 0) + 105
        Assert.Equal(145f, state.GetDiplomacyManager(greece)!.GetInfluence());
        Assert.Contains(externals.AcceptedTrades, it => it.Our == "Geneva" && it.Their == "Rome"
            && it.Trade.OurOffers.Single().Name == "Peace Treaty" && it.Trade.TheirOffers.Single().Name == "Peace Treaty");
    }

    [Fact]
    public void ConquestFunctions_WithoutBattleAndTradeLogic_ThrowNotSupported()
    {
        var externals = new CityConquestFunctionsSeamProbe();
        Assert.Throws<NotSupportedException>(() => externals.DestroyIfDefeated());
        Assert.Throws<NotSupportedException>(() => externals.AcceptTrade());
    }

    private sealed class CityConquestFunctionsSeamProbe
    {
        private readonly ICityConquestExternals unported = CityManagerSeams.UnportedConquest;
        public void DestroyIfDefeated() => unported.DestroyIfDefeated(null!, null!, null);
        public void AcceptTrade() => unported.AcceptTrade(null!, null!, new TradeObject());
    }
}
