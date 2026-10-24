using Unciv.Core.Tests.Support;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Trade;
using Unciv.Models.Ruleset.Unique;
using Unciv.UI.Components;
using TradeObject = Unciv.Logic.Trade.Trade;

namespace Unciv.Core.Tests.Logic;

// T070: TurnManager.kt. The Kotlin NextTurnLatencyTest is a benchmark and is not ported. Automation (T094) and TradeEvaluation (T089)
// are the hooks on GameClientState; the fakes below record the calls.
[Collection("RulesetCache")]
public sealed class CivTurnTests
{
    private sealed class RecordingAutomation : INextTurnAutomation
    {
        public List<string> Calls { get; } = [];
        public void AutomateGoldToSciencePercentage(Civilization civInfo) => Calls.Add("gold:" + civInfo.CivName);
        public void AutomateCityBombardment(Civilization civInfo) => Calls.Add("bombard:" + civInfo.CivName);
        public void ChooseGreatPerson(Civilization civInfo) => Calls.Add("great:" + civInfo.CivName);
        public void AutomateCivMoves(Civilization civInfo) => Calls.Add("moves:" + civInfo.CivName);
        public int ValueCityStateAlliance(Civilization civInfo, Civilization cityState, bool includeQuests = false) => 0;
    }

    private sealed class FixedTradeValidity(bool valid) : ITradeValidity
    {
        public bool IsTradeValid(TradeObject trade, Civilization offerer, Civilization tradePartner) => valid;
    }

    private readonly TestGame game = new();

    private Civilization Major(bool human = false)
    {
        var civ = game.AddCiv(isPlayer: human);
        return civ;
    }

    [Fact]
    public void FlagCountdownDecrementsAndStopsAtZero()
    {
        var civ = Major();
        civ.AddFlag(nameof(CivFlags.RecentlyBullied), 2);
        new TurnManager(civ).StartTurn();
        Assert.Equal(1, civ.FlagsCountdown[nameof(CivFlags.RecentlyBullied)]);
        new TurnManager(civ).StartTurn();
        new TurnManager(civ).StartTurn();
        Assert.Equal(0, civ.FlagsCountdown[nameof(CivFlags.RecentlyBullied)]);
    }

    [Fact]
    public void DiplomaticVoteFlagsChainShowResultsThenReset()
    {
        var civ = Major();
        civ.AddFlag(nameof(CivFlags.TurnsTillNextDiplomaticVote), 1);
        new TurnManager(civ).StartTurn();
        // countdown reached 0 -> ShowDiplomaticVotingResults is added with 1
        Assert.Equal(1, civ.FlagsCountdown[nameof(CivFlags.ShowDiplomaticVotingResults)]);

        civ.RemoveFlag(nameof(CivFlags.TurnsTillNextDiplomaticVote));
        civ.GameInfo.DiplomaticVictoryVotesCast["x"] = "y";
        civ.AddFlag(nameof(CivFlags.ShouldResetDiplomaticVotes), 0);
        civ.AddFlag(nameof(CivFlags.ShowDiplomaticVotingResults), 5);
        new TurnManager(civ).StartTurn();
        Assert.Empty(civ.GameInfo.DiplomaticVictoryVotesCast);
        Assert.False(civ.HasFlag(nameof(CivFlags.ShouldResetDiplomaticVotes)));
        Assert.False(civ.HasFlag(nameof(CivFlags.ShowDiplomaticVotingResults)));
    }

    [Fact]
    public void ShowingDiplomaticVotingResultsProcessesTheDiplomaticVictory()
    {
        var civ = Major();
        civ.AddFlag(nameof(CivFlags.ShowDiplomaticVotingResults), 0);
        var gameInfo = (GameInfo)civ.GameInfo;
        Assert.False(gameInfo.DiplomaticVictoryVotesProcessed); // GameInfo.processDiplomaticVictory (T071)
        new TurnManager(civ).StartTurn();
        Assert.True(gameInfo.DiplomaticVictoryVotesProcessed);
    }

    [Fact]
    public void RevoltsNeedBarbariansAndTheSpawnRebelsUnique()
    {
        var civ = Major();
        var manager = new TurnManager(civ);
        manager.StartTurn();
        Assert.False(civ.HasFlag(nameof(CivFlags.RevoltSpawning)));

        game.AddBarbarianCiv();
        civ.AddFlag(nameof(CivFlags.RevoltSpawning), 7);
        manager.StartTurn();
        Assert.False(civ.HasFlag(nameof(CivFlags.RevoltSpawning))); // no SpawnRebels unique: the flag is removed
    }

    [Fact]
    public void RevoltFlagIsAddedWithAtLeastOneTurn()
    {
        var civ = game.AddCiv(["Rebel units may spawn"]);
        game.AddBarbarianCiv();
        new TurnManager(civ).StartTurn();
        Assert.True(civ.HasFlag(nameof(CivFlags.RevoltSpawning)));
        Assert.True(civ.FlagsCountdown[nameof(CivFlags.RevoltSpawning)] >= 1);
    }

    [Fact]
    public void StaleTradeRequestIsRemovedWithANotificationForTheOfferer()
    {
        var us = Major(human: true);
        var them = Major(human: true);
        game.AddCity(them, game.GetTile(0, 0));
        us.GameInfo.ClientState.TradeEvaluation = new FixedTradeValidity(false);
        us.TradeRequests.Add(new TradeRequest { RequestingCiv = them.CivName, Trade = new TradeObject() });
        us.AddNotification($"[{them.CivName}] has made a counteroffer to your trade request", Notification.NotificationCategory.Trade);
        new TurnManager(us).StartTurn();
        Assert.Empty(us.TradeRequests);
        Assert.DoesNotContain(us.Notifications, it => it.Text.Contains("counteroffer", StringComparison.Ordinal));
        Assert.Contains(them.Notifications, it => it.Text == "Our proposed trade is no longer relevant!");
    }

    [Fact]
    public void ValidTradeRequestStaysWithTheRealAndTheInstalledTradeEvaluation()
    {
        var us = Major();
        var them = Major();
        game.AddCity(them, game.GetTile(0, 0));
        us.DiplomacyFunctions.MakeCivilizationsMeet(them); // the real TradeEvaluation reads both diplomacy managers
        us.TradeRequests.Add(new TradeRequest { RequestingCiv = them.CivName, Trade = new TradeObject() });
        new TurnManager(us).StartTurn(); // no hook installed: the real TradeEvaluation judges the (empty, hence valid) trade
        Assert.Single(us.TradeRequests);
        us.GameInfo.ClientState.TradeEvaluation = new FixedTradeValidity(true);
        new TurnManager(us).StartTurn();
        Assert.Single(us.TradeRequests);
    }

    [Fact]
    public void SpectatorsSkipStartTurn()
    {
        var spectator = game.AddCiv(game.Ruleset.Nations.Values.First(it => it.IsSpectator));
        spectator.AddFlag(nameof(CivFlags.RecentlyBullied), 3);
        new TurnManager(spectator).StartTurn();
        Assert.Equal(3, spectator.FlagsCountdown[nameof(CivFlags.RecentlyBullied)]);
    }

    [Fact]
    public void EndTurnArchivesNotificationsAndTrimsTheLog()
    {
        var civ = Major(human: true);
        game.GameInfo.ClientState.NotificationsLogMaxTurns = 2;
        for (int turn = 0; turn < 3; turn++)
        {
            game.GameInfo.Turns = turn;
            civ.AddNotification("note " + turn, Notification.NotificationCategory.General);
            new TurnManager(civ).EndTurn();
        }
        Assert.Empty(civ.Notifications);
        Assert.Equal([1, 2], civ.NotificationsLogs.Select(it => it.Turn));
        Assert.Null(civ.NotificationCountAtStartTurn);
    }

    [Fact]
    public void EndTurnOfAnEmptyTurnLeavesNoLogEntry()
    {
        var civ = Major();
        new TurnManager(civ).EndTurn();
        Assert.Empty(civ.NotificationsLogs);
    }

    [Fact]
    public void EndTurnAddsTheTurnsGold()
    {
        var civ = Major();
        game.AddCity(civ, game.GetTile(0, 0));
        civ.AddGold(10);
        new TurnManager(civ).EndTurn();
        Assert.Equal(10 + (int)civ.Stats.StatsForNextTurn.Gold, civ.Gold);
        Assert.Equal(Unciv.Logic.Version.Current, civ.LastTurnProcessedWithVersion);
    }

    [Fact]
    public void BarbariansGetNoStatsInEndTurn()
    {
        var barbarians = game.AddBarbarianCiv();
        barbarians.SetTransients();
        barbarians.AddGold(5);
        new TurnManager(barbarians).EndTurn();
        Assert.Equal(5, barbarians.Gold);
    }

    [Fact]
    public void AutomationHooksAreCalledWhereKotlinCallsNextTurnAutomation()
    {
        var civ = Major();
        game.AddCity(civ, game.GetTile(0, 0));
        var automation = new RecordingAutomation();
        game.GameInfo.ClientState.NextTurnAutomation = automation;
        game.GameInfo.ClientState.CitiesAutoBombardAtEndOfTurn = true;
        new TurnManager(civ).EndTurn();
        new TurnManager(civ).AutomateTurn();
        Assert.Equal(["bombard:" + civ.CivName, "moves:" + civ.CivName], automation.Calls);
    }

    [Fact]
    public void AutomateTurnUpdatesBarbarianCampsOnlyWhenBarbariansAreEnabled()
    {
        var barbarians = game.AddBarbarianCiv();
        game.GameInfo.GameParameters.NoBarbarians = true;
        new TurnManager(barbarians).AutomateTurn(); // no hook, no encampment update: must not throw
    }

    [Fact]
    public void UpdateWinningCivKeepsAnExistingVictory()
    {
        var civ = Major();
        var existing = new VictoryData(civ, "Cultural", 5);
        game.GameInfo.VictoryData = existing;
        new TurnManager(civ).UpdateWinningCiv();
        Assert.Same(existing, game.GameInfo.VictoryData);
    }

    [Fact]
    public void MayaCalendarGivesAGreatPersonOnlyOnANewBaktun()
    {
        var civ = Major(); // AI
        var automation = new RecordingAutomation();
        game.GameInfo.ClientState.NextTurnAutomation = automation;
        var turns = Enumerable.Range(1, 3000).ToList();
        int newCycleTurn = turns.First(t => MayaCalendar.YearToBaktuns(game.GameInfo.GetYear(t)) != MayaCalendar.YearToBaktuns(game.GameInfo.GetYear(t - 1)));
        game.GameInfo.Turns = newCycleTurn - 1;
        MayaCalendar.StartTurnForMaya(civ);
        Assert.Empty(automation.Calls);
        Assert.Equal(0, civ.GreatPeople.FreeGreatPeople);

        game.GameInfo.Turns = newCycleTurn;
        MayaCalendar.StartTurnForMaya(civ);
        Assert.Equal(1, civ.GreatPeople.FreeGreatPeople);
        Assert.Equal(["great:" + civ.CivName], automation.Calls);
    }

    [Fact]
    public void MayaCalendarLeavesTheChoiceToHumans()
    {
        var civ = Major(human: true);
        var automation = new RecordingAutomation();
        game.GameInfo.ClientState.NextTurnAutomation = automation;
        int newCycleTurn = Enumerable.Range(1, 3000).First(t => MayaCalendar.YearToBaktuns(game.GameInfo.GetYear(t)) != MayaCalendar.YearToBaktuns(game.GameInfo.GetYear(t - 1)));
        game.GameInfo.Turns = newCycleTurn;
        MayaCalendar.StartTurnForMaya(civ);
        Assert.Equal(1, civ.GreatPeople.FreeGreatPeople);
        Assert.Empty(automation.Calls);
        game.GameInfo.ClientState.IsFullAutoPlayAI = true;
        MayaCalendar.StartTurnForMaya(civ);
        Assert.Equal(["great:" + civ.CivName], automation.Calls);
    }

    private void RoomyMap() => game.MakeHexagonalMap(3, "Grassland");

    [Fact]
    public void ConvertGoldToScienceModOptionCallsTheAutomationForAIOnly()
    {
        game.Ruleset.ModOptions.Uniques.Add("Can convert gold to science with sliders");
        var ai = Major();
        var human = Major(human: true);
        var automation = new RecordingAutomation();
        game.GameInfo.ClientState.NextTurnAutomation = automation;
        new TurnManager(human).StartTurn();
        Assert.Empty(automation.Calls);
        new TurnManager(ai).StartTurn();
        Assert.Equal(["gold:" + ai.CivName], automation.Calls);
    }

    [Fact]
    public void StartAndEndTurnWithACityAndPromotableUnits()
    {
        RoomyMap();
        var civ = Major(human: true);
        var city = game.AddCity(civ, game.GetTile(0, 0));
        city.HasSoldBuildingThisTurn = true;
        var first = game.AddUnit("Warrior", civ, game.GetTile(1, 0));
        first.Promotions.XP = 1000;
        civ.GameInfo.ClientState.AutomatedUnitsMoveOnTurnStart = true;

        new TurnManager(civ).StartTurn();
        Assert.False(city.HasSoldBuildingThisTurn);
        Assert.True(civ.HasMovedAutomatedUnits);
        Assert.Contains(civ.Notifications, it => it.Text == "[[Warrior]] can be promoted!");

        civ.GameInfo.ClientState.AutomatedUnitsMoveOnTurnStart = false;
        foreach (var (x, y) in new[] { (0, 1), (1, 1), (-1, 0) })
            game.AddUnit("Warrior", civ, game.GetTile(x, y)).Promotions.XP = 1000;
        civ.Notifications.Clear();
        new TurnManager(civ).StartTurn();
        Assert.False(civ.HasMovedAutomatedUnits);
        Assert.Contains(civ.Notifications, it => it.Text == "[4] units can be promoted!");

        int goldBefore = civ.Gold;
        new TurnManager(civ).EndTurn();
        Assert.Equal(Unciv.Logic.Version.Current, civ.LastTurnProcessedWithVersion);
        Assert.True(civ.Gold >= goldBefore - 100);
    }

    [Fact]
    public void RevoltSpawnPlacesRebelsForTheBarbariansAndClearsTheFlag()
    {
        RoomyMap();
        var civ = game.AddCiv(["Rebel units may spawn"], isPlayer: true);
        var barbarians = game.AddBarbarianCiv();
        barbarians.SetTransients();
        game.AddCity(civ, game.GetTile(0, 0));
        civ.AddFlag(nameof(CivFlags.RevoltSpawning), 1);
        new TurnManager(civ).StartTurn();
        Assert.True(barbarians.Units.GetCivUnits().Any());
        // single city: Kotlin's maxByOrNull draws nothing for it, so the first draw is the rebel count, then the unit choice
        var replay = civ.State.StateBasedRandom("TurnManager.doRevoltSpawn");
        replay.NextInt(100);
        var expected = game.Ruleset.Units.Values.Where(it => it.UniqueTo == null && it.IsMelee() && it.IsLandUnit
            && !((IHasUniques)it).HasUnique(UniqueType.CannotAttack) && it.IsBuildable(civ))
            .Select(it => (Unit: it, Roll: replay.NextInt(1000))).MaxBy(it => it.Roll).Unit;
        Assert.Contains(barbarians.Units.GetCivUnits(), it => it.Name == expected.Name);
        Assert.Contains(civ.Notifications, it => it.Text == "Your citizens are revolting due to very high unhappiness!");
        // StartTurn runs updateRevolts after the flags: the flag is rescheduled for a later turn
        Assert.True(civ.FlagsCountdown[nameof(CivFlags.RevoltSpawning)] >= 1);
    }

    [Fact]
    public void RevoltSpawnWithoutBarbariansJustDropsTheFlag()
    {
        var civ = game.AddCiv(["Rebel units may spawn"]);
        civ.AddFlag(nameof(CivFlags.RevoltSpawning), 1);
        new TurnManager(civ).StartTurn();
        Assert.False(civ.HasFlag(nameof(CivFlags.RevoltSpawning)));
    }

    [Fact]
    public void CityStateGiftsAGreatPersonWhenTheCountdownRunsLowerThanTheNumberOfAllies()
    {
        RoomyMap();
        var civ = Major();
        var cityState = game.AddCiv(cityStateType: "Cultured");
        game.AddCity(civ, game.GetTile(0, 0));
        game.AddCity(cityState, game.GetTile(3, 0));
        civ.DiplomacyFunctions.MakeCivilizationsMeet(cityState);
        cityState.AllyCiv = civ;
        var civFake = new FakeCityStateFunctions { GreatPersonTurns = 17 };
        var stateFake = new FakeCityStateFunctions();
        civ.CityStateFunctions = civFake;
        cityState.CityStateFunctions = stateFake;

        civ.AddFlag(nameof(CivFlags.CityStateGreatPersonGift), 3);
        new TurnManager(civ).StartTurn();
        Assert.Equal(2, civ.FlagsCountdown[nameof(CivFlags.CityStateGreatPersonGift)]);
        Assert.Empty(stateFake.PatronGifts);

        civ.AddFlag(nameof(CivFlags.CityStateGreatPersonGift), 1);
        new TurnManager(civ).StartTurn();
        Assert.Equal([civ], stateFake.PatronGifts);
        Assert.Equal(17, civ.FlagsCountdown[nameof(CivFlags.CityStateGreatPersonGift)]);
    }

    [Fact]
    public void CityStateGiftFlagDoesNotCountDownWithoutAllies()
    {
        var civ = Major();
        civ.AddFlag(nameof(CivFlags.CityStateGreatPersonGift), 3);
        new TurnManager(civ).StartTurn();
        Assert.Equal(3, civ.FlagsCountdown[nameof(CivFlags.CityStateGreatPersonGift)]);
    }

    [Fact]
    public void ElectionFlagHoldsElectionsWhenItReachesZero()
    {
        var civ = Major();
        var fake = new FakeCityStateFunctions();
        civ.CityStateFunctions = fake;
        civ.AddFlag(nameof(CivFlags.TurnsTillCityStateElection), 1);
        new TurnManager(civ).StartTurn();
        Assert.Equal(1, fake.ElectionsHeld);
    }

    [Fact]
    public void CityStatesGetAnElectionFlagInEndTurnWhenEspionageIsOn()
    {
        RoomyMap();
        game.GameInfo.GameParameters.EspionageEnabled = true;
        var cityState = game.AddCiv(cityStateType: "Cultured");
        game.AddCity(cityState, game.GetTile(0, 0));
        new TurnManager(cityState).EndTurn();
        Assert.True(cityState.HasFlag(nameof(CivFlags.TurnsTillCityStateElection)));
    }

    [Fact]
    public void BankruptCivDisbandsMilitaryUnitsAndNotifies()
    {
        var g = new TestGame("[-300 Gold] [in all cities]");
        g.MakeHexagonalMap(3, "Grassland");
        var civ = g.AddCiv(isPlayer: true);
        g.AddCity(civ, g.GetTile(0, 0));
        g.AddUnit("Warrior", civ, g.GetTile(1, 0));
        g.AddUnit("Warrior", civ, g.GetTile(0, 1));
        civ.AddGold(-1000);
        new TurnManager(civ).EndTurn();
        Assert.DoesNotContain(civ.Units.GetCivUnits(), it => it.IsMilitary());
        Assert.Contains(civ.Notifications, it => it.Text.StartsWith("Cannot provide unit upkeep for", StringComparison.Ordinal));
    }
}
