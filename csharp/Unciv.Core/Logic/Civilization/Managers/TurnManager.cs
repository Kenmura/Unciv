using System.Globalization;
using Unciv.Logic.City.Managers;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Map.MapUnits;
using Unciv.Logic.Map.Tiles;
using Unciv.Logic.Trade.Routes;
using Unciv.Models.Ruleset.Unique;
using Unciv.UI.Components;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;
using StatsObject = Unciv.Models.StatTypes.Stats;

namespace Unciv.Logic.Civilization.Managers;

// Ported from core/src/com/unciv/logic/civilization/managers/TurnManager.kt (T070).
//
// Stripped: the timeThis profiling wrappers and the purity annotations.
// Deviations:
// - NextTurnProgress (a UI progress bar) is an optional `Action` that is called where Kotlin calls `progressBar?.increment()`.
// - UncivGame.Current.settings are the per-civ PlayerSettings (T183), VERSION is GameClientState (via IGameInfoState.ClientState) and Version.Current.
// - NextTurnAutomation (T094) is the INextTurnAutomation hook on the ClientState (the real automation by default); with no hook the calls are skipped
//   (automateGoldToSciencePercentage, automateCityBombardment, automateCivMoves). They get the civilization where
//   Kotlin builds `GameView(gameInfo, civ).civView`.
// - TradeEvaluation().isTradeValid (T089) is TradeEvaluation.Default unless the ClientState installs an ITradeValidity.
// - GameInfo.processDiplomaticVictory (T071) is IGameInfoState.ProcessDiplomaticVictory.

public class TurnManager(Civilization civInfo)
{
    private readonly Civilization civInfo = civInfo;

    public void StartTurn(Action? progressBar = null)
    {
        if (civInfo.IsSpectator()) return;
        GameEventHooks.TurnStarted(civInfo);

        foreach (var city in civInfo.Cities) city.HasSoldBuildingThisTurn = false;

        TradeRouteTurns.StartTurn(civInfo); // B020: BNW trade routes advance before the stats are updated; a no-op without routes

        if (civInfo.Cities.Count != 0 && civInfo.GameInfo.Ruleset.Technologies.Count != 0)
            civInfo.Tech.UpdateResearchProgress();

        civInfo.Cache.UpdateCivResources(); // If you offered a trade last turn, this turn it will have been accepted/declined
        foreach (var stockpiledResource in civInfo.GetCivResourceSupply().Where(it => it.Resource.IsStockpiled).ToList())
            civInfo.GainStockpiledResource(stockpiledResource.Resource, stockpiledResource.Amount);

        civInfo.CivConstructions.StartTurn();
        civInfo.AttacksSinceTurnStart.Clear();
        civInfo.UpdateStatsForNextTurn(); // for things that change when turn passes e.g. golden age, city state influence

        // Do this after updateStatsForNextTurn but before cities.startTurn
        if (civInfo.PlayerType == PlayerType.AI && ((IHasUniques)civInfo.GameInfo.Ruleset.ModOptions).HasUnique(UniqueType.ConvertGoldToScience))
            civInfo.GameInfo.ClientState.NextTurnAutomation?.AutomateGoldToSciencePercentage(civInfo);

        // Generate great people at the start of the turn,
        // so they won't be generated out in the open and vulnerable to enemy attacks before you can control them
        if (civInfo.Cities.Count != 0) // if no city available, addGreatPerson will throw exception
        {
            var greatPerson = civInfo.GreatPeople.GetNewGreatPerson();
            while (greatPerson != null)
            {
                if (civInfo.GameInfo.Ruleset.Units.ContainsKey(greatPerson))
                    civInfo.Units.AddUnit(greatPerson);
                greatPerson = civInfo.GreatPeople.GetNewGreatPerson();
            }
            civInfo.ReligionManager.StartTurn();
            if (civInfo.IsLongCountActive())
                MayaCalendar.StartTurnForMaya(civInfo);
        }

        civInfo.Cache.UpdateViewableTiles(); // adds explored tiles so that the units will be able to perform automated actions better
        civInfo.Cache.UpdateCitiesConnectedToCapital();
        StartTurnFlags();
        UpdateRevolts();

        civInfo.ForEachTriggeredUnique(UniqueType.TriggerUponTurnStart, civInfo.State, _ => true, true, unique =>
        {
            UniqueTriggerActivation.TriggerUnique(unique, civInfo);
        });

        foreach (var city in civInfo.Cities)
        {
            progressBar?.Invoke();
            new CityTurnManager(city).StartTurn(); // Most expensive part of startTurn
        }

        foreach (var unit in civInfo.Units.GetCivUnits().ToList()) new UnitTurnManager(unit).StartTurn();

        if (civInfo.PlayerType == PlayerType.Human && civInfo.GameInfo.GetPlayerSettings(civInfo.CivId).AutomatedUnitsMoveOnTurnStart)
        {
            civInfo.HasMovedAutomatedUnits = true;
            foreach (var unit in civInfo.Units.GetCivUnits().ToList())
                unit.DoAction();
        }
        else civInfo.HasMovedAutomatedUnits = false;

        foreach (var tradeRequest in civInfo.TradeRequests.ToList()) // remove trade requests where one of the sides can no longer supply
        {
            var offeringCiv = civInfo.GameInfo.GetCivilization(tradeRequest.RequestingCiv);
            if (offeringCiv.IsDefeated() || !IsTradeValid(tradeRequest.Trade, offeringCiv))
            {
                civInfo.TradeRequests.Remove(tradeRequest);
                // Yes, this is the right direction. I checked.
                offeringCiv.AddNotification("Our proposed trade is no longer relevant!", NotificationCategory.Trade, NotificationIcon.Trade);
                // If it's a counteroffer, remove notification
                civInfo.Notifications.RemoveAll(it => it.Text == $"[{offeringCiv.CivName}] has made a counteroffer to your trade request");
            }
        }

        var promotableUnits = civInfo.Units.GetCivUnits()
            .Where(it => it.Promotions.CanBePromoted())
            .ToList();
        if (promotableUnits.Count <= 3)
        {
            foreach (var unit in promotableUnits)
            {
                civInfo.AddNotification(
                    $"[{unit.DisplayName()}] can be promoted!",
                    [MapUnitActionFor(unit), new PromoteUnitAction(unit.Name, unit.GetTile().Position, unit.Id)],
                    NotificationCategory.Units,
                    unit.Name);
            }
        }
        else
        {
            civInfo.AddNotification(
                $"[{promotableUnits.Count.ToString(CultureInfo.InvariantCulture)}] units can be promoted!",
                promotableUnits.Select(it => (NotificationAction)MapUnitActionFor(it)).ToList(),
                NotificationCategory.Units,
                "UnitActionIcons/Promote");
        }

        UpdateWinningCiv();
    }

    private static MapUnitAction MapUnitActionFor(MapUnit unit) => new(unit.CurrentTile.Position, unit.Id);

    private bool IsTradeValid(Trade.Trade trade, Civilization offeringCiv) =>
        (civInfo.GameInfo.ClientState.TradeEvaluation ?? Trade.TradeEvaluation.Default)
        .IsTradeValid(trade, civInfo, offeringCiv);

    private void StartTurnFlags()
    {
        foreach (var flag in civInfo.FlagsCountdown.Keys.ToList())
        {
            // In case we remove flags while iterating
            if (!civInfo.FlagsCountdown.ContainsKey(flag)) continue;

            if (flag == CivFlags.CityStateGreatPersonGift.ToString())
            {
                var rng = civInfo.State.StateBasedRandom("TurnManager.startTurnFlags");
                List<Civilization> cityStateAllies =
                    civInfo.GetKnownCivs().Where(it => it.IsCityState && it.AllyCiv == civInfo).ToList();
                var givingCityState = cityStateAllies.Where(it => it.Cities.Count != 0).ToList().RandomOrNull(rng);

                if (cityStateAllies.Count != 0) civInfo.FlagsCountdown[flag] = unchecked(civInfo.FlagsCountdown[flag] - 1);

                if (civInfo.FlagsCountdown[flag] < Math.Min(cityStateAllies.Count, 10) && civInfo.Cities.Count != 0
                        && givingCityState != null)
                {
                    givingCityState.CityStateFunctions.GiveGreatPersonToPatron(civInfo);
                    civInfo.FlagsCountdown[flag] = civInfo.CityStateFunctions.TurnsForGreatPersonFromCityState();
                }

                continue;
            }

            if (civInfo.FlagsCountdown[flag] > 0)
                civInfo.FlagsCountdown[flag] = unchecked(civInfo.FlagsCountdown[flag] - 1);

            if (civInfo.FlagsCountdown[flag] != 0) continue;

            if (flag == CivFlags.RevoltSpawning.ToString()) DoRevoltSpawn();
            else if (flag == CivFlags.TurnsTillCityStateElection.ToString()) civInfo.CityStateFunctions.HoldElections();
        }
        HandleDiplomaticVictoryFlags();
    }

    private bool FlagIsZero(CivFlags flag) =>
        civInfo.FlagsCountdown.TryGetValue(flag.ToString(), out var value) && value == 0;

    private void HandleDiplomaticVictoryFlags()
    {
        if (FlagIsZero(CivFlags.ShouldResetDiplomaticVotes))
        {
            civInfo.GameInfo.DiplomaticVictoryVotesCast.Clear();
            civInfo.RemoveFlag(CivFlags.ShowDiplomaticVotingResults.ToString());
            civInfo.RemoveFlag(CivFlags.ShouldResetDiplomaticVotes.ToString());
        }

        if (FlagIsZero(CivFlags.ShowDiplomaticVotingResults))
        {
            civInfo.GameInfo.ProcessDiplomaticVictory();
            if (civInfo.GameInfo.Civilizations.Any(it => it.VictoryManager.HasWon()))
            {
                civInfo.RemoveFlag(CivFlags.TurnsTillNextDiplomaticVote.ToString());
            }
            else
            {
                civInfo.AddFlag(CivFlags.ShouldResetDiplomaticVotes.ToString(), 1);
                civInfo.AddFlag(CivFlags.TurnsTillNextDiplomaticVote.ToString(), civInfo.GetTurnsBetweenDiplomaticVotes());
            }
        }

        if (FlagIsZero(CivFlags.TurnsTillNextDiplomaticVote))
        {
            civInfo.AddFlag(CivFlags.ShowDiplomaticVotingResults.ToString(), 1);
        }
    }

    private void UpdateRevolts()
    {
        if (!civInfo.GameInfo.Civilizations.Any(it => it.IsBarbarian))
        {
            // Can't spawn revolts without barbarians ¯\_(ツ)_/¯
            return;
        }

        if (!civInfo.HasUnique(UniqueType.SpawnRebels))
        {
            civInfo.RemoveFlag(CivFlags.RevoltSpawning.ToString());
            return;
        }

        if (!civInfo.HasFlag(CivFlags.RevoltSpawning.ToString()))
        {
            civInfo.AddFlag(CivFlags.RevoltSpawning.ToString(), Math.Max(GetTurnsBeforeRevolt(), 1));
        }
    }

    private void DoRevoltSpawn()
    {
        Civilization barbarians;
        try
        {
            // The first test in `updateRevolts` should prevent getting here in a no-barbarians game, but it has been shown to still occur
            barbarians = civInfo.GameInfo.GetBarbarianCivilization();
        }
        catch (InvalidOperationException ex)
        {
            Log.Error("Barbarian civilization not found", ex);
            civInfo.RemoveFlag(CivFlags.RevoltSpawning.ToString());
            return;
        }

        var random = civInfo.State.StateBasedRandom("TurnManager.doRevoltSpawn");
        int rebelCount = 1 + (random.NextInt(100 + (20 * (civInfo.Cities.Count - 1))) / 100);
        // Kotlin maxByOrNull: the first maximum wins; a single element is returned without calling the selector (no random draw)
        CityObject? spawnCity = null;
        int bestCityValue = 0;
        bool onlyOneCity = civInfo.Cities.Count == 1;
        foreach (var city in civInfo.Cities)
        {
            if (onlyOneCity) { spawnCity = city; break; }
            int value = random.NextInt(city.Population.Population + 10);
            if (spawnCity == null || value > bestCityValue) { spawnCity = city; bestCityValue = value; }
        }
        if (spawnCity == null) return;

        Tile? spawnTile = null;
        int bestTileValue = 0;
        foreach (var tile in spawnCity.GetTiles())
        {
            int value = RateTileForRevoltSpawn(tile);
            if (spawnTile == null || value > bestTileValue) { spawnTile = tile; bestTileValue = value; }
        }
        if (spawnTile == null) return;

        Models.Ruleset.Unit.BaseUnit? unitToSpawn = null;
        int bestUnitValue = 0;
        foreach (var unit in civInfo.GameInfo.Ruleset.Units.Values.Where(it =>
                     it.UniqueTo == null && it.IsMelee() && it.IsLandUnit
                     && !((IHasUniques)it).HasUnique(UniqueType.CannotAttack) && it.IsBuildable(civInfo)))
        {
            int value = random.NextInt(1000);
            if (unitToSpawn == null || value > bestUnitValue) { unitToSpawn = unit; bestUnitValue = value; }
        }
        if (unitToSpawn == null) return;

        for (int i = 0; i < rebelCount; i++)
        {
            civInfo.GameInfo.TileMap.PlaceUnitNearTile(spawnTile.Position, unitToSpawn, barbarians);
        }

        // Will be automatically added again as long as unhappiness is still low enough
        civInfo.RemoveFlag(CivFlags.RevoltSpawning.ToString());

        civInfo.AddNotification("Your citizens are revolting due to very high unhappiness!", spawnTile.Position, NotificationCategory.General, unitToSpawn.Name, "StatIcons/Malcontent");
    }

    // Higher is better
    private static int RateTileForRevoltSpawn(Tile tile)
    {
        if (tile.IsWater || tile.MilitaryUnit != null || tile.CivilianUnit != null || tile.IsCityCenter() || tile.IsImpassible())
            return -1;
        int score = 10;
        if (tile.Improvement == null)
        {
            score += 4;
            if (tile.TileResource != null)
            {
                score += 3;
            }
        }
        if (tile.GetDefensiveBonus() > 0)
            score += 4;
        return score;
    }

    private int GetTurnsBeforeRevolt() =>
        KotlinMath.FloatToInt(
            unchecked(civInfo.GameInfo.Ruleset.ModOptions.Constants.BaseTurnsUntilRevolt + civInfo.State.StateBasedRandom("TurnManager.getTurnsBeforeRevolt").NextInt(3))
            * Math.Max(civInfo.GameInfo.Speed.Modifier, 1f));

    public void EndTurn(Action? progressBar = null)
    {
        EndTurnCore(progressBar);
        if (!civInfo.IsSpectator()) GameEventHooks.TurnEnded(civInfo);
    }

    // T186 deviation: the body of Kotlin's endTurn, split off so that the TurnEnded event is reported once after every early return.
    private void EndTurnCore(Action? progressBar)
    {
        var clientState = civInfo.GameInfo.ClientState;
        var settings = civInfo.GameInfo.GetPlayerSettings(civInfo.CivId);
        if (settings.CitiesAutoBombardAtEndOfTurn)
            clientState.NextTurnAutomation?.AutomateCityBombardment(civInfo); // Bombard with all cities that haven't, maybe you missed one.

        civInfo.ForEachTriggeredUnique(UniqueType.TriggerUponTurnEnd, civInfo.State, _ => true, true, unique =>
        {
            UniqueTriggerActivation.TriggerUnique(unique, civInfo);
        });

        var notificationsLog = civInfo.NotificationsLogs;
        var notificationsThisTurn = new Civilization.NotificationsLog(civInfo.GameInfo.Turns);
        notificationsThisTurn.Notifications.AddRange(civInfo.Notifications);

        while (notificationsLog.Count >= settings.NotificationsLogMaxTurns)
        {
            notificationsLog.RemoveAt(0);
        }

        if (notificationsThisTurn.Notifications.Count != 0)
            notificationsLog.Add(notificationsThisTurn);

        civInfo.Notifications.Clear();
        civInfo.NotificationCountAtStartTurn = null;

        if (civInfo.IsDefeated() || civInfo.IsSpectator()) return; // yes they do call this, best not update any further stuff

        StatsObject nextTurnStats;
        if (civInfo.IsBarbarian)
            nextTurnStats = new StatsObject();
        else
        {
            civInfo.UpdateStatsForNextTurn();
            nextTurnStats = civInfo.Stats.StatsForNextTurn;
        }

        civInfo.Policies.EndTurn(KotlinMath.FloatToInt(nextTurnStats.Culture));
        civInfo.TotalCultureForContests = unchecked(civInfo.TotalCultureForContests + KotlinMath.FloatToInt(nextTurnStats.Culture));

        if (civInfo.IsCityState)
        {
            civInfo.QuestManager.EndTurn();

            // Set turns to elections to a random number so not every city-state has the same election date
            // May be called at game start or when migrating a game from an older version
            if (civInfo.GameInfo.IsEspionageEnabled() && !civInfo.HasFlag(CivFlags.TurnsTillCityStateElection.ToString()))
            {
                var rng = civInfo.State.StateBasedRandom("TurnManager.endTurn(Espianage)");
                civInfo.AddFlag(CivFlags.TurnsTillCityStateElection.ToString(), rng.NextInt(unchecked(civInfo.GameInfo.Ruleset.ModOptions.Constants.CityStateElectionTurns + 1)));
            }
        }

        if (!civInfo.IsBarbarian)
        {
            // disband military units until there are none left OR the gold values are normal
            while (civInfo.Gold <= -200 && KotlinMath.FloatToInt(nextTurnStats.Gold) < 0)
            {
                // prioritize units inside our territory, because disbanding them yields gold, and inexperienced units
                var unitToDisband = civInfo.Units.GetCivUnits()
                    .Where(it => it.IsMilitary())
                    .OrderByDescending(it => it.CurrentTile.GetOwner() == civInfo)
                    .ThenBy(it => it.Promotions.ValueOfPromotionsAndXp())
                    .FirstOrDefault();
                if (unitToDisband == null) break;
                unitToDisband.Disband();
                var unitName = unitToDisband.ShortDisplayName();
                civInfo.AddNotification($"Cannot provide unit upkeep for {unitName} - unit has been disbanded!", NotificationCategory.Units, unitName, NotificationIcon.Death);
                // No need to recalculate unit upkeep, disband did that in UnitManager.removeUnit
                nextTurnStats = civInfo.Stats.StatsForNextTurn;
            }
        }

        civInfo.AddGold(KotlinMath.FloatToInt(nextTurnStats.Gold));

        if (civInfo.Cities.Count != 0 && civInfo.GameInfo.Ruleset.Technologies.Count != 0)
            civInfo.Tech.EndTurn(KotlinMath.FloatToInt(nextTurnStats.Science));

        civInfo.ReligionManager.EndTurn(KotlinMath.FloatToInt(nextTurnStats.Faith));
        civInfo.TotalFaithForContests = unchecked(civInfo.TotalFaithForContests + KotlinMath.FloatToInt(nextTurnStats.Faith));

        civInfo.EspionageManager.EndTurn();

        if (civInfo.IsMajorCiv()) // City-states don't get great people!
            civInfo.GreatPeople.AddGreatPersonPoints();

        // To handle tile's owner issue (#8246), we need to run cities being razed first.
        // a city can be removed while iterating (if it's being razed) so we need to iterate over a copy - sorting does one
        foreach (var city in civInfo.Cities.OrderByDescending(it => it.IsBeingRazed).ToList())
        {
            progressBar?.Invoke();
            new CityTurnManager(city).EndTurn();
        }

        civInfo.TemporaryUniques.EndTurn();

        civInfo.GoldenAges.EndTurn(civInfo.GetHappiness());
        foreach (var unit in civInfo.Units.GetCivUnits().ToList()) new UnitTurnManager(unit).EndTurn(); // This is the most expensive part of endTurn
        foreach (var diplomacy in civInfo.Diplomacy.Values.ToList()) diplomacy.NextTurn(); // we copy the diplomacy values so if it changes in-loop we won't crash
        civInfo.Cache.UpdateHasActiveEnemyMovementPenalty();

        civInfo.ResetMilitaryMightCache();

        UpdateWinningCiv(); // Maybe we did something this turn to win

        civInfo.LastTurnProcessedWithVersion = Version.Current;
    }

    public void UpdateWinningCiv()
    {
        if (civInfo.GameInfo.VictoryData != null) return; // Game already won

        var victoryType = civInfo.VictoryManager.GetVictoryTypeAchieved();
        if (victoryType != null)
        {
            civInfo.GameInfo.VictoryData = new VictoryData(civInfo, victoryType, civInfo.GameInfo.Turns);

            // Notify other human players about this civInfo's victory
            foreach (var otherCiv in civInfo.GameInfo.Civilizations)
            {
                // Skip winner, displaying VictoryScreen is handled separately in WorldScreen.update
                // by checking `viewingCiv.isDefeated() || gameInfo.checkForVictory()`
                if (otherCiv.PlayerType != PlayerType.Human || otherCiv == civInfo) continue;
                otherCiv.AddPopupAlert(new PopupAlert(AlertType.GameHasBeenWon, ""));
            }
        }
    }

    public void AutomateTurn()
    {
        // Defeated civs do nothing
        if (civInfo.IsDefeated())
            return;
        // Do stuff
        civInfo.GameInfo.ClientState.NextTurnAutomation?.AutomateCivMoves(civInfo);

        // Update barbarian camps
        if (civInfo.IsBarbarian && !civInfo.GameInfo.GameParameters.NoBarbarians)
            civInfo.GameInfo.Barbarians.UpdateEncampments();
        GameEventHooks.AutoPlayerTurnProcessed(civInfo);
    }
}
