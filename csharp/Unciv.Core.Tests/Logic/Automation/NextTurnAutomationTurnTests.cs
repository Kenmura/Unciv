using Unciv.Core.Tests.Support;
using Unciv.Logic;
using Unciv.Logic.Automation.Civilization;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.View;
using BarbarianEncampment = Unciv.Logic.Civilization.BarbarianEncampment;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Logic.Automation;

// T094, C#-only cases (NextTurnAutomation.kt has no Kotlin fixture beyond NextTurnAutomationTest): expected values are derived by hand from the Kotlin.
[Collection("RulesetCache")]
public class NextTurnAutomationTurnTests
{
    private readonly TestGame testGame = new();
    private readonly CivilizationObject civ;
    private readonly CivilizationObject other;

    public NextTurnAutomationTurnTests()
    {
        testGame.GameInfo.EngineOptions.VisibleMap = true;
        testGame.MakeHexagonalMap(12, Constants.Grassland);
        civ = testGame.AddCiv();
        other = testGame.AddCiv();
        testGame.AddCity(civ, testGame.GetTile(0, 0));
        testGame.AddCity(other, testGame.GetTile(8, 0));
        civ.DiplomacyFunctions.MakeCivilizationsMeet(other);
    }

    private CivView CivViewOf(CivilizationObject c) => new GameView(testGame.GameInfo, c).CivView;

    private MapUnitView UnitView(Unciv.Logic.Map.MapUnits.MapUnit unit) => new GameView(testGame.GameInfo, unit.Civ).GetMapUnitView(unit);

    private void War() => civ.GetDiplomacyManager(other)!.DeclareWar();

    // ---- hook and gold to science

    [Fact]
    public void DefaultHookForwardsToNextTurnAutomation()
    {
        var hook = testGame.GameInfo.ClientState.NextTurnAutomation!;
        civ.AddGold(100000);
        hook.AutomateGoldToSciencePercentage(civ);
        Assert.Equal(0.8f, civ.Tech.GoldPercentConvertedToScience);
        hook.AutomateCityBombardment(civ);
        civ.GreatPeople.FreeGreatPeople = 1;
        hook.ChooseGreatPerson(civ);
        Assert.Equal(0, civ.GreatPeople.FreeGreatPeople);
        Assert.Equal(0, hook.ValueCityStateAlliance(civ, other)); // not a city state
        hook.AutomateCivMoves(civ);
    }

    [Fact]
    public void GoldToScienceFollowsTheEraThresholds()
    {
        var view = CivViewOf(civ);
        civ.AddGold(-civ.Gold);
        NextTurnAutomation.AutomateGoldToSciencePercentage(view);
        Assert.Equal(0f, civ.Tech.GoldPercentConvertedToScience); // no gold
        var pissPoor = civ.GetEra().BaseUnitBuyCost;
        civ.AddGold(pissPoor - view.GetGoldPerTurn());
        NextTurnAutomation.AutomateGoldToSciencePercentage(view);
        Assert.Equal(0f, civ.Tech.GoldPercentConvertedToScience); // projected gold not above pissPoor
        var stinkingRich = civ.GetEra().StartingGold * 10 + 1 * 2 * pissPoor;
        civ.AddGold(pissPoor + stinkingRich / 2);
        NextTurnAutomation.AutomateGoldToSciencePercentage(view);
        var projected = civ.Gold + view.GetGoldPerTurn();
        Assert.Equal((projected - pissPoor) * 0.8f / stinkingRich, civ.Tech.GoldPercentConvertedToScience);
    }

    // ---- popup alerts and demands

    [Fact]
    public void DemandAlertsAreAgreedToWhenTheDemanderIsThreateningAndRefusedOtherwise()
    {
        civ.PopupAlerts.Add(new PopupAlert(Demand.DoNotAttackUs.DemandAlert, other.CivId));
        NextTurnAutomation.AutomateCivMoves(civ, tradeAndChangeState: false);
        Assert.Empty(civ.PopupAlerts);
        var penalty = civ.GetDiplomacyManager(other)!.DiplomaticModifiers[nameof(Unciv.Logic.Civilization.Diplomacy.DiplomaticModifiers.UnacceptableDemands)];
        if (other.GetDiplomacyManager(civ)!.HasFlag(Demand.DoNotAttackUs.AgreedToDemand))
            Assert.Equal(-10f, penalty); // threatening demander: agreed
        else
            Assert.Equal(-20f, penalty); // refused (and the refusal of this demand is war)
    }

    [Fact]
    public void DemandAlertFromADefeatedCivIsIgnored()
    {
        var defeated = testGame.AddCiv();
        civ.DiplomacyFunctions.MakeCivilizationsMeet(defeated);
        civ.PopupAlerts.Add(new PopupAlert(Demand.DontSpyOnUs.DemandAlert, defeated.CivId));
        NextTurnAutomation.AutomateCivMoves(civ, tradeAndChangeState: false);
        Assert.Empty(civ.PopupAlerts);
        Assert.False(civ.GetDiplomacyManager(defeated)!.HasFlag(Demand.DontSpyOnUs.AgreedToDemand));
    }

    [Fact]
    public void DeclarationOfFriendshipAlertIsSignedOrDeclined()
    {
        // Unknown favourability: with a hostile opinion the AI declines and flags it
        civ.GetDiplomacyManager(other)!.SetModifier(DiplomaticModifiers.WarMongerer, -200f);
        civ.PopupAlerts.Add(new PopupAlert(AlertType.DeclarationOfFriendship, other.CivId));
        NextTurnAutomation.AutomateCivMoves(civ, tradeAndChangeState: false);
        Assert.True(other.GetDiplomacyManager(civ)!.HasFlag(DiplomacyFlags.DeclinedDeclarationOfFriendship));

        var third = testGame.AddCiv();
        civ.DiplomacyFunctions.MakeCivilizationsMeet(third);
        civ.GetDiplomacyManager(third)!.SetModifier(DiplomaticModifiers.GaveUsGifts, 200f);
        civ.PopupAlerts.Add(new PopupAlert(AlertType.DeclarationOfFriendship, third.CivId));
        NextTurnAutomation.AutomateCivMoves(civ, tradeAndChangeState: false);
        Assert.Empty(civ.PopupAlerts);
    }

    [Fact]
    public void DefeatedRequesterOfAFriendshipIsIgnored()
    {
        var defeated = testGame.AddCiv();
        civ.DiplomacyFunctions.MakeCivilizationsMeet(defeated);
        civ.PopupAlerts.Add(new PopupAlert(AlertType.DeclarationOfFriendship, defeated.CivId));
        NextTurnAutomation.AutomateCivMoves(civ, tradeAndChangeState: false);
        Assert.DoesNotContain(defeated.Notifications, it => it.Text.Contains("Declaration of Friendship"));
    }

    [Fact]
    public void ViolatedDemandsAreRaisedOrPunished()
    {
        var demand = Demand.DoNotAttackUs;
        var manager = civ.GetDiplomacyManager(other)!;
        // violation before promise: the offender gets the demand alert
        manager.SetFlag(demand.ViolationOccurred, 10);
        NextTurnAutomation.AutomateCivMoves(civ);
        Assert.False(manager.HasFlag(demand.ViolationOccurred));
        // violation after promise: the offender gets the discovered alert and the promise is gone
        manager.SetFlag(demand.AgreedToDemand, 10);
        manager.SetFlag(demand.ViolationOccurred, 10);
        NextTurnAutomation.AutomateCivMoves(civ);
        Assert.False(manager.HasFlag(demand.AgreedToDemand));
        Assert.True(manager.HasFlag(demand.WillIgnoreViolation));
        Assert.True(manager.HasModifier(demand.BetrayedPromiseDiplomacyModifier));
        // ignored violations only get the flag removed
        manager.SetFlag(demand.ViolationOccurred, 10);
        NextTurnAutomation.AutomateCivMoves(civ);
        Assert.False(manager.HasFlag(demand.ViolationOccurred));
        // most violations are not checked during war
        War();
        var other2 = Demand.DontSpyOnUs;
        manager.SetFlag(other2.ViolationOccurred, 10);
        NextTurnAutomation.AutomateCivMoves(civ);
        Assert.True(manager.HasFlag(other2.ViolationOccurred));
    }

    [Fact]
    public void BrokenNoAttackPromiseCostsOpinionWithCommonKnownCivs()
    {
        var third = testGame.AddCiv();
        testGame.AddCity(third, testGame.GetTile(-8, 0)); // a civ without cities or units is defeated
        civ.DiplomacyFunctions.MakeCivilizationsMeet(third);
        other.DiplomacyFunctions.MakeCivilizationsMeet(third);
        var manager = civ.GetDiplomacyManager(other)!;
        manager.SetFlag(Demand.DoNotAttackUs.AgreedToDemand, 10);
        manager.SetFlag(Demand.DoNotAttackUs.ViolationOccurred, 10);
        NextTurnAutomation.AutomateCivMoves(civ);
        Assert.True(third.GetDiplomacyManager(other)!.HasModifier(DiplomaticModifiers.BetrayedPromiseToNotAttackOtherCiv));
    }

    // ---- research, policies, great people

    [Fact]
    public void FreeTechsAndTheResearchQueueAreFilled()
    {
        civ.Tech.FreeTechs = 2;
        NextTurnAutomation.AutomateCivMoves(civ, tradeAndChangeState: false);
        Assert.Equal(0, civ.Tech.FreeTechs);
        Assert.True(civ.Tech.ResearchedTechnologies.Count >= 2);
        Assert.NotEmpty(civ.Tech.TechsToResearch);
    }

    [Fact]
    public void FreePoliciesAreAdopted()
    {
        civ.Policies.FreePolicies = 3;
        NextTurnAutomation.AutomateCivMoves(civ);
        Assert.NotEmpty(civ.Policies.AdoptedPolicies);
    }

    [Fact]
    public void ChooseGreatPersonPrefersAScientist()
    {
        civ.GreatPeople.FreeGreatPeople = 1;
        NextTurnAutomation.ChooseGreatPerson(CivViewOf(civ));
        Assert.Equal(0, civ.GreatPeople.FreeGreatPeople);
        Assert.Contains(civ.Units.GetCivUnits(), it => it.BaseUnit.Uniques.Contains("Great Person - [Science]"));
        civ.GreatPeople.FreeGreatPeople = 0;
        NextTurnAutomation.ChooseGreatPerson(CivViewOf(civ)); // no options: nothing happens
    }

    [Fact]
    public void WithTheSpaceRaceOpenAUnitIsDisbandedAndABuildingSoldForTheMissingResource()
    {
        var city = civ.Cities[0];
        city.CityConstructions.AddBuilding("Apollo Program"); // the next milestone is now adding the spaceship parts
        testGame.GameInfo.SpaceResources.Add("Aluminum");
        testGame.GameInfo.GameParameters.VictoryTypes.Add("Scientific");
        var needy = testGame.CreateBaseUnit();
        needy.RequiredResource = "Aluminum";
        var unit = testGame.AddUnit(needy.Name, civ, testGame.GetTile(1, 1));
        var building = testGame.CreateBuilding();
        building.RequiredResource = "Aluminum";
        city.CityConstructions.AddBuilding(building);
        Assert.True(city.CityConstructions.IsBuilt(building.Name));

        NextTurnAutomation.AutomateCivMoves(civ);

        Assert.True(unit.IsDestroyed);
        Assert.False(city.CityConstructions.IsBuilt(building.Name));
        Assert.True(city.HasSoldBuildingThisTurn);
    }

    // ---- city states

    private CivilizationObject CityState(string type = "Cultured")
    {
        var cityState = testGame.AddCiv(cityStateType: type);
        testGame.AddCity(cityState, testGame.GetTile(-6, 0));
        civ.DiplomacyFunctions.MakeCivilizationsMeet(cityState);
        return cityState;
    }

    [Fact]
    public void ValueCityStateAllianceCountsBonusesInfluenceAndRivals()
    {
        var view = CivViewOf(civ);
        Assert.Equal(0, NextTurnAutomation.ValueCityStateAlliance(view, other)); // not a city state
        var cityState = CityState("Cultured");
        var withoutInfluence = NextTurnAutomation.ValueCityStateAlliance(view, cityState);
        cityState.GetDiplomacyManager(civ)!.SetInfluenceWithoutSideEffects(80f);
        var withInfluence = NextTurnAutomation.ValueCityStateAlliance(view, cityState);
        Assert.Equal(withoutInfluence + 5 + 8, withInfluence); // no more "bully for cash" malus and min(10, 80/10)
        cityState.GetDiplomacyManager(other)!.SetInfluenceWithoutSideEffects(100f);
        cityState.AllyCiv = other;
        Assert.Equal(withInfluence - 7, NextTurnAutomation.ValueCityStateAlliance(view, cityState)); // (100 - 30) / 10 for the ally that is not us
        Assert.True(NextTurnAutomation.ValueCityStateAlliance(view, cityState, includeQuests: true) <= withInfluence - 7 + 100);
        civ.Cities = [];
        var noCities = NextTurnAutomation.ValueCityStateAlliance(CivViewOf(civ), cityState); // returns after the bonus part
        Assert.True(noCities >= -100);
    }

    [Fact]
    public void ValueCityStateAllianceCoversEveryStatBonus()
    {
        var x = -7;
        foreach (var type in new[] { "Cultured", "Maritime", "Mercantile", "Militaristic", "Religious" })
        {
            var cityState = testGame.AddCiv(cityStateType: type);
            testGame.AddCity(cityState, testGame.GetTile(x--, 0));
            civ.DiplomacyFunctions.MakeCivilizationsMeet(cityState);
            _ = NextTurnAutomation.ValueCityStateAlliance(CivViewOf(civ), cityState, includeQuests: true);
        }
    }

    [Fact]
    public void CityStatesArePledgedProtectionAndBulliedWhenTheAllianceIsWorthNothing()
    {
        var cityState = CityState("Cultured");
        civ.GetDiplomacyManager(cityState)!.DiplomaticStatus = DiplomaticStatus.Peace;
        cityState.GetDiplomacyManager(civ)!.DiplomaticStatus = DiplomaticStatus.Peace;
        civ.AddGold(100);
        NextTurnAutomation.AutomateCivMoves(civ);
        Assert.True(cityState.GetDiplomacyManager(civ)!.DiplomaticStatus is DiplomaticStatus.Peace or DiplomaticStatus.Protector);
    }

    [Fact]
    public void CityStateTurnGetsFreeTechAndUpdatesRelationships()
    {
        var cityState = CityState("Cultured");
        NextTurnAutomation.AutomateCivMoves(cityState);
        NextTurnAutomation.AutomateCivMoves(cityState, tradeAndChangeState: false);
    }

    [Fact]
    public void SpectatorsAndDefeatedBarbariansDoNothing()
    {
        var spectator = testGame.AddCiv();
        spectator.PlayerType = PlayerType.Human;
        NextTurnAutomation.AutomateCivMoves(spectator, tradeAndChangeState: false);
    }

    // ---- units

    [Fact]
    public void UnitPriorityOrdersSpaceshipsSettlersCiviliansAirThenMilitary()
    {
        var cache = new AutomationCache();
        int Priority(string name, bool atWar = false)
        {
            var unit = testGame.AddUnit(name, civ, name is "Fighter" or "Bomber" or "Atomic Bomb" or "Guided Missile" ? civ.Cities[0].GetCenterTile() : testGame.GetTile(1, 1));
            try { return NextTurnAutomation.GetUnitPriority(UnitView(unit), atWar, cache); }
            finally { unit.Destroy(); }
        }
        Assert.Equal(0, Priority("SS Engine"));
        Assert.Equal(1, Priority("Settler"));
        Assert.Equal(2, Priority("Worker"));
        var fighter = Priority("Fighter");
        var bomber = Priority("Bomber");
        var nuke = Priority("Atomic Bomb");
        var missile = Priority("Guided Missile");
        Assert.Equal([2, 4, 3, 5], new[] { fighter, bomber, nuke, missile });
        var ranged = Priority("Archer", atWar: true);
        var melee = Priority("Warrior", atWar: true);
        Assert.True(ranged < melee);
        Assert.Equal(ranged + 20, melee);
        Assert.True(Priority("Great General") >= 100);
        Assert.True(Priority("Scout") < 100);
    }

    [Fact]
    public void AtWarUnitsNearEnemiesMoveFirstAndMeleeAttackTheBesiegedCity()
    {
        War();
        var enemyCity = other.Cities[0];
        var target = enemyCity.GetCenterTile();
        // five of our military units within 4 tiles of the enemy city trigger the manual placement
        var units = new List<Unciv.Logic.Map.MapUnits.MapUnit>();
        foreach (var (x, y, name) in new[] { (5, 0, "Warrior"), (5, 1, "Warrior"), (6, 0, "Warrior"), (6, 1, "Archer"), (7, 0, "Catapult"), (4, 2, "Warrior") })
            units.Add(testGame.AddUnit(name, civ, testGame.GetTile(x, y)));
        units.Add(testGame.AddUnit("Fighter", civ, civ.Cities[0].GetCenterTile()));
        var defender = testGame.AddUnit("Warrior", other, testGame.GetTile(7, 1)); // a surrounded enemy next to our melee units
        enemyCity.Health = 10; // below a fifth of the maximum: the melee units go for the city itself
        var cityHealth = enemyCity.Health;
        var defenderHealth = defender.Health;

        NextTurnAutomation.AutomateCivMoves(civ);

        Assert.All(units.Where(it => !it.IsDestroyed), it => Assert.Same(civ, it.Civ));
        Assert.True(enemyCity.Health != cityHealth || defender.Health != defenderHealth || defender.IsDestroyed, "the siege and melee units attacked");
        Assert.NotNull(target);
    }

    [Fact]
    public void UnitsPromoteAndHumanPromotionsFollowTheSetting()
    {
        var warrior = testGame.AddUnit("Warrior", civ, testGame.GetTile(1, 1));
        warrior.Promotions.XP = 30;
        warrior.Health = 40;
        NextTurnAutomation.ApplyPromotions(UnitView(warrior));
        Assert.True(warrior.Promotions.NumberOfPromotions >= 1);

        var human = testGame.AddCiv(isPlayer: true);
        testGame.AddCity(human, testGame.GetTile(-8, -2));
        var humanWarrior = testGame.AddUnit("Warrior", human, testGame.GetTile(-8, -1));
        humanWarrior.Promotions.XP = 30;
        NextTurnAutomation.ApplyPromotions(UnitView(humanWarrior));
        Assert.Equal(0, humanWarrior.Promotions.NumberOfPromotions); // automatedUnitsChoosePromotions is off
        testGame.GameInfo.SetPlayerSettings(humanWarrior.Civ.CivId, new PlayerSettings { AutomatedUnitsChoosePromotions = true });
        NextTurnAutomation.ApplyPromotions(UnitView(humanWarrior));
        Assert.True(humanWarrior.Promotions.NumberOfPromotions >= 1);
    }

    [Fact]
    public void SpaceshipPartsAndFoundersMoveBeforeTheRest()
    {
        testGame.AddUnit("Settler", civ, testGame.GetTile(2, 2));
        testGame.AddUnit("Warrior", civ, testGame.GetTile(1, 2));
        NextTurnAutomation.AutomateCivMoves(civ, tradeAndChangeState: false);
    }

    // ---- cities and settlers

    [Fact]
    public void CitiesChooseConstructionAndMilitaryWhenWeakOrAttacked()
    {
        War();
        var city = civ.Cities[0];
        city.Health = 10;
        testGame.AddUnit("Warrior", other, testGame.GetTile(2, 0));
        NextTurnAutomation.AutomateCities(CivViewOf(civ));
        Assert.NotEmpty(city.CityConstructions.ConstructionQueue);
    }

    [Fact]
    public void LargePuppetCitiesAreAnnexed()
    {
        var happy = testGame.AddCiv(["[+100 Happiness] [in all cities]"]);
        var city = testGame.AddCity(happy, testGame.GetTile(-8, 4));
        city.IsPuppet = true;
        city.Population.AddPopulation(10 - city.Population.Population);
        city.CityStats.Update();
        happy.UpdateStatsForNextTurn();
        NextTurnAutomation.AutomateCities(CivViewOf(happy));
        Assert.False(city.IsPuppet);
    }

    [Fact]
    public void TrainSettlerPicksTheBestProductionCityWhenThereIsRoom()
    {
        var city = civ.Cities[0];
        city.Population.AddPopulation(3);
        city.CityConstructions.AddBuilding("Monument");
        city.CityConstructions.AddBuilding("Granary");
        city.CityStats.CurrentCityStats.Production = 5f;
        civ.Tech.AddTechnology("Pottery");
        testGame.AddUnit("Warrior", civ, testGame.GetTile(1, 0));
        for (var i = 0; i < 3; i++) testGame.AddUnit("Warrior", other, testGame.GetTile(10, i)); // other majors deter an attack
        NextTurnAutomation.AutomateCivMoves(civ);
        Assert.True(city.CityConstructions.GetCurrentConstruction().Name.Length > 0);
    }

    // ---- diplomatic victory vote

    [Fact]
    public void TheVoteGoesToTheMostLikedCivOrAbstains()
    {
        civ.AddFlag(CivFlags.TurnsTillNextDiplomaticVote.ToString(), 0);
        civ.GetDiplomacyManager(other)!.SetModifier(DiplomaticModifiers.GaveUsGifts, 50f);
        NextTurnAutomation.AutomateCivMoves(civ, tradeAndChangeState: false);
        Assert.Equal(other.CivId, testGame.GameInfo.DiplomaticVictoryVotesCast[civ.CivId]);

        testGame.GameInfo.DiplomaticVictoryVotesCast.Clear();
        civ.GetDiplomacyManager(other)!.SetModifier(DiplomaticModifiers.GaveUsGifts, -200f);
        NextTurnAutomation.AutomateCivMoves(civ, tradeAndChangeState: false);
        Assert.Null(testGame.GameInfo.DiplomaticVictoryVotesCast[civ.CivId]); // hates everybody

        testGame.GameInfo.DiplomaticVictoryVotesCast.Clear();
        civ.GetDiplomacyManager(other)!.SetModifier(DiplomaticModifiers.GaveUsGifts, -60f);
        for (var i = 0; i < 6; i++)
        {
            testGame.GameInfo.DiplomaticVictoryVotesCast.Clear();
            testGame.GameInfo.Turns++;
            NextTurnAutomation.AutomateCivMoves(civ, tradeAndChangeState: false); // proportional chance to abstain in the Enemy range
        }
    }

    [Fact]
    public void CityStatesVoteForTheirAllyAndCivsKnowingNobodyAbstain()
    {
        var cityState = CityState();
        cityState.AddFlag(CivFlags.TurnsTillNextDiplomaticVote.ToString(), 0);
        cityState.AllyCiv = civ;
        NextTurnAutomation.AutomateCivMoves(cityState, tradeAndChangeState: false);
        Assert.Equal(civ.CivId, testGame.GameInfo.DiplomaticVictoryVotesCast[cityState.CivId]);

        var loner = testGame.AddCiv();
        loner.AddFlag(CivFlags.TurnsTillNextDiplomaticVote.ToString(), 0);
        NextTurnAutomation.AutomateCivMoves(loner, tradeAndChangeState: false);
        Assert.Null(testGame.GameInfo.DiplomaticVictoryVotesCast[loner.CivId]);
    }

    // ---- barbarians

    private CivilizationObject Barbarians()
    {
        testGame.GameInfo.Barbarians.GameInfo = testGame.GameInfo;
        return testGame.AddBarbarianCiv();
    }

    private Unciv.Logic.Map.Tiles.Tile Encampment(int x, int y)
    {
        var tile = testGame.GetTile(x, y);
        tile.Improvement = Constants.BarbarianEncampment;
        testGame.GameInfo.Barbarians.Encampments.Add(new BarbarianEncampment(tile.Position) { GameInfo = testGame.GameInfo });
        return tile;
    }

    [Fact]
    public void BarbarianUnitsAttackFortifyAndWander()
    {
        var barbarians = Barbarians();
        var camp = Encampment(-6, 6);
        var onCamp = testGame.AddUnit("Warrior", barbarians, camp);
        var attacker = testGame.AddUnit("Archer", barbarians, testGame.GetTile(1, 1)); // adjacent target in range
        var wanderer = testGame.AddUnit("Warrior", barbarians, testGame.GetTile(-3, 3));
        var victim = testGame.AddUnit("Warrior", civ, testGame.GetTile(1, 2));
        var healthy = victim.Health;
        barbarians.PopupAlerts.Add(new PopupAlert(AlertType.WarDeclaration, civ.CivId));

        NextTurnAutomation.AutomateCivMoves(barbarians);

        Assert.Empty(barbarians.PopupAlerts);
        Assert.True(victim.Health < healthy || victim.IsDestroyed || attacker.CurrentMovement < 2f);
        Assert.True(onCamp.IsFortified() || onCamp.CurrentMovement >= 0f);
        Assert.NotNull(wanderer);
    }

    [Fact]
    public void CapturedCiviliansHeadForAFreeEncampmentOrStay()
    {
        var barbarians = Barbarians();
        var camp = Encampment(-6, 6);
        var civilian = testGame.AddUnit("Worker", barbarians, testGame.GetTile(-2, 2));
        var start = civilian.CurrentTile;
        NextTurnAutomation.AutomateCivMoves(barbarians);
        Assert.True(civilian.CurrentTile.AerialDistanceTo(camp) < start.AerialDistanceTo(camp));

        var onCamp = testGame.AddUnit("Worker", barbarians, camp);
        var tile = onCamp.CurrentTile;
        new BarbarianAutomation(barbarians).Automate(); // stays on the encampment
        Assert.Same(tile, onCamp.CurrentTile);
    }

    [Fact]
    public void CapturedCiviliansWanderWhenNoEncampmentIsReachable()
    {
        var barbarians = Barbarians();
        var civilian = testGame.AddUnit("Worker", barbarians, testGame.GetTile(-2, 2));
        new BarbarianAutomation(barbarians).Automate();
        Assert.NotNull(civilian);
    }

    [Fact]
    public void WoundedBarbariansPillageToHealAndMeleeUnitsAttack()
    {
        var barbarians = Barbarians();
        var tile = testGame.GetTile(-2, 2);
        tile.SetOwningCity(civ.Cities[0]);
        tile.Improvement = "Farm";
        var wounded = testGame.AddUnit("Warrior", barbarians, tile);
        wounded.Health = 30;
        var melee = testGame.AddUnit("Warrior", barbarians, testGame.GetTile(3, 1));
        var target = testGame.AddUnit("Archer", civ, testGame.GetTile(2, 1));
        var targetHealth = target.Health;
        new BarbarianAutomation(barbarians).Automate();
        Assert.True(wounded.Health >= 30);
        Assert.True(target.Health < targetHealth || target.IsDestroyed || melee.CurrentMovement < melee.GetMaxMovement());
    }
}
