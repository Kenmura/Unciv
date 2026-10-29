using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnit.Actions;
using Unciv.Models;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Logic.Bnw;

// B022 (bnw-spec 4.11, 4.1.5): Brave New World religion on the REAL generated ruleset: the pressure scale constants, the pressure trade routes
// and spies carry, the Reformation-tier belief and the belief effects. The G&K tests prove the default scale is unchanged.
[Collection("RulesetCache")]
public sealed class BnwReligionTests : IDisposable
{
    public BnwReligionTests() => RulesetCache.Clear();

    public void Dispose() => RulesetCache.Clear();

    private static GameInfo NewGame(BaseRuleset ruleset, params string[] nations)
    {
        Models.Ruleset.BnwRulesetTests.LoadBnw();
        var players = nations.Select((it, i) => new Player(it, i == 0 ? PlayerType.Human : PlayerType.AI)).ToList();
        return GameStarter.StartNewGame(
            new GameSetupInfo(
                new GameParameters { BaseRuleset = ruleset.FullName(), NumberOfCityStates = 0, Players = players },
                new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Small }),
            5, 1000L);
    }

    private static CityObject FoundCity(CivilizationObject civ)
    {
        var settler = civ.Units.GetCivUnits().First(it => it.HasUnique(UniqueType.FoundCity));
        var city = civ.AddCity(settler.GetTile().Position, settler);
        foreach (var unit in civ.Units.GetCivUnits().Where(it => ReferenceEquals(it.GetTile(), city.GetCenterTile())).ToList()) unit.Destroy(); // the centre stays free for a prophet
        return city;
    }

    private static (GameInfo Game, CivilizationObject A, CivilizationObject B) Pair(string a = "Brazil", string b = "Poland", BaseRuleset ruleset = BaseRuleset.Civ_V_BNW)
    {
        var game = NewGame(ruleset, a, b);
        var civs = game.Civilizations.Where(it => it.IsMajorCiv()).ToList();
        return (game, civs[0], civs[1]);
    }

    private static string BeliefOf(Ruleset ruleset, BeliefType type) =>
        ruleset.Beliefs.Values.First(it => it.Type == type && !it.Uniques.Any(u => u.Contains("pressure", StringComparison.OrdinalIgnoreCase) || u.Contains("spread", StringComparison.OrdinalIgnoreCase))).Name;

    /// <summary>Founds a religion through the manager's own flow (prophet, founding, belief choice) and returns it.</summary>
    private static Religion Found(CivilizationObject civ, CityObject holy, params string[] extraFounderBeliefs)
    {
        var ruleset = civ.GameInfo.Ruleset;
        var manager = civ.ReligionManager;
        var prophet = civ.Units.AddUnit("Great Prophet", holy).NotNull();
        Assert.True(manager.MayFoundReligionHere(prophet.GetTile()));
        manager.FoundReligion(prophet);
        manager.FoundReligion(civ.CivName, $"Religion of {civ.CivName}");
        manager.ChooseBeliefs([ruleset.Beliefs[BeliefOf(ruleset, BeliefType.Founder)], ruleset.Beliefs[BeliefOf(ruleset, BeliefType.Follower)]]);
        Assert.Equal(ReligionState.Religion, manager.ReligionState);
        manager.Religion.NotNull().AddBelief(BeliefOf(ruleset, BeliefType.Pantheon)); // a religion grows out of a pantheon
        foreach (var belief in extraFounderBeliefs) manager.Religion.NotNull().AddBelief(belief);
        return manager.Religion.NotNull();
    }

    private static void Convert(CityObject city, Religion religion, int pressure = 100000)
    {
        city.Religion.AddPressure(religion.Name, pressure);
        Assert.Equal(religion.Name, city.Religion.GetMajorityReligionName());
    }

    private static void Route(GameInfo game, CityObject origin, CityObject dest, TradeRouteKind kind = TradeRouteKind.International) =>
        (game.TradeRoutes ??= []).Add(new TradeRoute
        {
            Id = (game.TradeRoutes?.Count ?? 0) + 1,
            OriginCityId = origin.Id,
            DestCityId = dest.Id,
            OriginOwner = origin.Civ.CivId,
            DestOwner = dest.Civ.CivId,
            Kind = kind,
        });

    // ---- the pressure scale (RELIGION_ATHEISM_PRESSURE_PER_POP, RELIGION_INITIAL_FOUNDING_CITY_PRESSURE, RELIGION_MISSIONARY_PRESSURE_MULTIPLIER)

    [Fact]
    public void TheBnwPressureConstantsAreScaledTenfoldAndGnkKeepsItsValues()
    {
        var bnw = Pair().Game.Ruleset.ModOptions.Constants;
        Assert.Equal((1000, 5000, 10), (bnw.ReligionAtheismPressurePerPop, bnw.ReligionInitialFoundingCityPressure, bnw.ReligionMissionaryPressureMultiplier));
        var gnk = Pair("America", "Rome", BaseRuleset.Civ_V_GnK).Game.Ruleset.ModOptions.Constants;
        Assert.Equal((100, 500, 1), (gnk.ReligionAtheismPressurePerPop, gnk.ReligionInitialFoundingCityPressure, gnk.ReligionMissionaryPressureMultiplier));
    }

    [Theory]
    [InlineData(BaseRuleset.Civ_V_BNW, 10)]
    [InlineData(BaseRuleset.Civ_V_GnK, 1)]
    public void PopulationGrowthFoundingAndMissionariesUseTheRulesetScale(BaseRuleset ruleset, int scale)
    {
        var (game, a, _) = Pair(ruleset == BaseRuleset.Civ_V_GnK ? "America" : "Brazil", ruleset == BaseRuleset.Civ_V_GnK ? "Rome" : "Poland", ruleset);
        var city = FoundCity(a);

        // growth of a city that follows nothing adds Constants.ReligionAtheismPressurePerPop per citizen (G&K 100)
        int before = city.Religion.GetPressures()[Constants.NoReligionName];
        city.Religion.UpdatePressureOnPopulationChange(2);
        Assert.Equal(before + 2 * 100 * scale, city.Religion.GetPressures()[Constants.NoReligionName]);

        // the holy city starts with the founding pressure per citizen (G&K 500)
        var religion = Found(a, city);
        Assert.Equal(city.Population.Population * 500 * scale, city.Religion.GetPressures()[religion.Name]);

        // a Missionary adds its religious strength times the multiplier (G&K 1)
        var target = game.Civilizations.First(it => it.IsMajorCiv() && it != a);
        var targetCity = FoundCity(target);
        var missionary = target.Units.AddUnit("Missionary", targetCity).NotNull();
        missionary.Religion = religion.Name;
        var action = UnitActions.GetUnitActions(missionary, UnitActionType.SpreadReligion).First();
        Assert.NotNull(action.Action);
        action.Action();
        Assert.Equal(missionary.BaseUnit.ReligiousStrength * scale, targetCity.Religion.GetPressures()[religion.Name]);
    }

    [Fact]
    public void ThePerTurnPressureOfACityIsOnTheSameScale()
    {
        foreach (var (ruleset, scale) in new[] { (BaseRuleset.Civ_V_BNW, 10), (BaseRuleset.Civ_V_GnK, 1) })
        {
            var (game, a, b) = Pair(ruleset == BaseRuleset.Civ_V_GnK ? "America" : "Brazil", ruleset == BaseRuleset.Civ_V_GnK ? "Rome" : "Poland", ruleset);
            var (home, other) = (FoundCity(a), FoundCity(b));
            Convert(home, Found(a, home));
            int perTurn = game.Speed.ReligiousPressureAdjacentCity * scale;
            // no range limit is assumed: the neighbour either gets the pressure of the nearby city, or nothing
            int received = other.Religion.GetPressuresFromSurroundingCities()[a.ReligionManager.Religion.NotNull().Name];
            Assert.True(received == 0 || received == perTurn, $"{ruleset}: {received} vs {perTurn}");
        }
    }

    // ---- trade routes carry pressure (spec 4.1.5)

    private static int Pressure(CityObject city, Religion religion) => city.Religion.GetPressuresFromSurroundingCities()[religion.Name];

    [Fact]
    public void AnInternationalRouteCarriesTheMajorityReligionOfEachEndToTheOther()
    {
        var (game, a, b) = Pair();
        var (home, far) = (FoundCity(a), FoundCity(b));
        var religion = Found(a, home);
        Convert(home, religion);
        int without = Pressure(far, religion);

        Route(game, home, far);
        int perTurn = game.Speed.ReligiousPressureAdjacentCity * 10;
        Assert.Equal(without + perTurn, Pressure(far, religion));

        // both ways: the destination's religion reaches the origin
        var other = Found(b, far);
        Convert(far, other);
        int back = Pressure(home, other);
        game.TradeRoutes = null;
        Assert.Equal(back - perTurn, Pressure(home, other));
    }

    [Fact]
    public void DomesticRoutesAndRoutesWithoutAMajorReligionCarryNothing()
    {
        var (game, a, b) = Pair();
        var (home, far) = (FoundCity(a), FoundCity(b));
        int without = far.Religion.GetPressuresFromSurroundingCities().Values.Sum();
        Route(game, home, far); // nobody follows a religion
        Assert.Equal(without, far.Religion.GetPressuresFromSurroundingCities().Values.Sum());

        var religion = Found(a, home);
        Convert(home, religion);
        game.TradeRoutes = null;
        int plain = far.Religion.GetPressuresFromSurroundingCities().Values.Sum();
        Route(game, home, far, TradeRouteKind.Food); // not an international route
        Assert.Equal(plain, far.Religion.GetPressuresFromSurroundingCities().Values.Sum());
        game.TradeRoutes = null;
    }

    [Fact]
    public void RoutePressureIsAddedToTheCityEveryTurnItsFollowersGrow()
    {
        var (game, a, b) = Pair();
        var (home, far) = (FoundCity(a), FoundCity(b));
        var religion = Found(a, home);
        Convert(home, religion);
        Route(game, home, far);
        int before = far.Religion.GetPressures()[religion.Name];
        far.Religion.GetType().GetMethod("GetAffectedBySurroundingCities", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(far.Religion, null);
        Assert.True(far.Religion.GetPressures()[religion.Name] >= before + game.Speed.ReligiousPressureAdjacentCity * 10);
    }

    [Fact]
    public void ShipsOfTheDesertDoublesThePressureOfArabianRoutesInBothDirections()
    {
        var (game, arabia, poland) = Pair("Arabia", "Poland");
        Assert.Contains("[+100]% religious pressure along trade routes", game.Ruleset.Nations["Arabia"].Uniques);
        var (mine, theirs) = (FoundCity(arabia), FoundCity(poland));
        var arabian = Found(arabia, mine);
        var polish = Found(poland, theirs);
        Convert(mine, arabian);
        Convert(theirs, polish);
        int perTurn = game.Speed.ReligiousPressureAdjacentCity * 10;
        int plainOut = Pressure(theirs, arabian), plainBack = Pressure(mine, polish);

        Route(game, mine, theirs); // an Arabian route
        Assert.Equal(plainOut + 2 * perTurn, Pressure(theirs, arabian));
        Assert.Equal(plainBack + 2 * perTurn, Pressure(mine, polish));

        game.TradeRoutes = null;
        Route(game, theirs, mine); // a Polish route: the owner's trait decides, so no bonus
        Assert.Equal(plainOut + perTurn, Pressure(theirs, arabian));
        Assert.Equal(plainBack + perTurn, Pressure(mine, polish));
        game.TradeRoutes = null;
    }

    // ---- belief effects

    [Fact]
    public void EvangelismCutsThePressureOtherReligionsAddToYourCities()
    {
        var (game, a, b) = Pair();
        var (home, far) = (FoundCity(a), FoundCity(b));
        var religion = Found(a, home);
        Convert(home, religion);
        Route(game, home, far);
        Assert.True(Pressure(far, religion) > 0);

        var polish = Found(b, far, "Evangelism");
        Assert.Equal(0, Pressure(far, religion));
        Assert.True(polish.HasBelief("Evangelism"));
        game.TradeRoutes = null;
    }

    [Fact]
    public void UnityOfTheProphetsLetsAReligionKeepHalfItsPressureWhenAnInquisitorCleansACity()
    {
        var (game, a, b) = Pair();
        var (mine, theirs) = (FoundCity(a), FoundCity(b));
        var unity = Found(a, mine, "Unity of the Prophets");
        var polish = Found(b, theirs);

        void Clean(bool byInquisitor, int expectUnity)
        {
            theirs.Religion.RemoveAllPressuresExceptFor(polish.Name); // reset to a known state
            theirs.Religion.AddPressure(polish.Name, 4000);
            theirs.Religion.AddPressure(unity.Name, 2000);
            theirs.Religion.RemoveAllPressuresExceptFor(polish.Name, byInquisitor);
            Assert.Equal(expectUnity, theirs.Religion.GetPressures()[unity.Name]);
        }
        Clean(false, 0);
        Clean(true, 1000);

        // and through the real Inquisitor action
        theirs.Religion.RemoveAllPressuresExceptFor(polish.Name);
        theirs.Religion.AddPressure(unity.Name, 2000);
        var inquisitor = b.Units.AddUnit("Inquisitor", theirs).NotNull();
        inquisitor.Religion = polish.Name;
        var action = UnitActions.GetUnitActions(inquisitor, UnitActionType.RemoveHeresy).First();
        Assert.NotNull(action.Action);
        action.Action();
        Assert.Equal(1000, theirs.Religion.GetPressures()[unity.Name]);
    }

    [Fact]
    public void UndergroundSectLetsASpyPressureTheCityItIsIn()
    {
        var (game, a, b) = Pair();
        var (mine, theirs) = (FoundCity(a), FoundCity(b));
        var religion = Found(a, mine);
        var spy = a.EspionageManager.AddSpy();
        spy.MoveTo(theirs);
        spy.SetAction(SpyAction.Surveillance);
        int plain = Pressure(theirs, religion);

        religion.AddBelief("Underground Sect");
        Assert.Equal(plain + 5 * 10, Pressure(theirs, religion));
        spy.MoveTo(null);
        Assert.Equal(plain, Pressure(theirs, religion));
    }

    [Fact]
    public void MessengerOfTheGodsGivesTwoScienceForEachInternationalRouteStartingInTheCity()
    {
        var (game, a, b) = Pair();
        var (home, far) = (FoundCity(a), FoundCity(b));
        Assert.Equal(["[+2 Science] for each international trade route [in this city]"], game.Ruleset.Beliefs["Messenger of the Gods"].Uniques);
        var religion = Found(a, home);
        religion.AddBelief("Messenger of the Gods");
        Convert(home, religion);

        float Science(CityObject city) { city.CityStats.Update(); return city.CityStats.CurrentCityStats.Science; }
        float plain = Science(home);
        Route(game, home, far, TradeRouteKind.Food);
        Assert.Equal(plain, Science(home)); // a domestic route does not count
        game.TradeRoutes = null;
        Route(game, home, far);
        Assert.Equal(2f, Science(home) - plain, 0.01f);
        game.TradeRoutes = null;
        Route(game, far, home); // the route of the other end does not count for this city
        Assert.Equal(0f, Science(home) - plain, 0.01f);
        game.TradeRoutes = null;
    }

    // ---- the Reformation tier and the second Pantheon (spec 4.11)

    [Fact]
    public void TheReformationPolicyGrantsOneFreeReformationBeliefOnceAReligionIsFounded()
    {
        var (game, a, _) = Pair();
        var home = FoundCity(a);
        var manager = a.ReligionManager;
        var reformation = game.Ruleset.Policies["Reformation"];
        a.Policies.FreePolicies = 10;

        a.Policies.Adopt(reformation); // before a religion exists: nothing yet
        Assert.False(manager.HasFreeBeliefs());

        Found(a, home);
        Assert.Equal(1, manager.FreeBeliefsAsEnums()[BeliefType.Reformation]);
        Assert.Contains(PendingDecisions.For(a), it => it.Kind == DecisionKind.ReformReligion);

        var belief = game.Ruleset.Beliefs.Values.First(it => it.Type == BeliefType.Reformation);
        manager.ChooseBeliefs([belief], manager.UsingFreeBeliefs());
        Assert.True(manager.Religion.NotNull().HasBelief(belief.Name));
        Assert.False(manager.HasFreeBeliefs());
        manager.GrantPolicyBeliefs();
        Assert.False(manager.HasFreeBeliefs()); // one only
    }

    [Fact]
    public void ReligiousToleranceGrantsASecondPantheonBeliefAndNothingIsGrantedWithoutThePolicy()
    {
        var (game, a, _) = Pair();
        var home = FoundCity(a);
        var manager = a.ReligionManager;
        Found(a, home);
        manager.GrantPolicyBeliefs();
        Assert.False(manager.HasFreeBeliefs());

        a.Policies.FreePolicies = 10;
        a.Policies.Adopt(game.Ruleset.Policies["Religious Tolerance"]);
        Assert.Equal(1, manager.FreeBeliefsAsEnums()[BeliefType.Pantheon]);
        var pantheon = game.Ruleset.Beliefs.Values.First(it => it.Type == BeliefType.Pantheon && !manager.Religion.NotNull().HasBelief(it.Name));
        manager.ChooseBeliefs([pantheon], manager.UsingFreeBeliefs());
        Assert.True(manager.Religion.NotNull().HasBelief(pantheon.Name));
        Assert.False(manager.HasFreeBeliefs());
    }

    [Fact]
    public void GnkNeverGrantsPolicyBeliefs()
    {
        var (_, a, _) = Pair("America", "Rome", BaseRuleset.Civ_V_GnK);
        var home = FoundCity(a);
        Found(a, home);
        a.ReligionManager.StartTurn();
        Assert.False(a.ReligionManager.HasFreeBeliefs());
    }

    // ---- faith purchases (Belief_BuildingClassFaithPurchase, FaithPurchaseAllGreatPeople)

    [Fact]
    public void JesuitEducationAndToTheGloryOfGodUnlockFaithPurchasesInCitiesOfTheReligion()
    {
        var (game, a, _) = Pair();
        var home = FoundCity(a);
        var university = game.Ruleset.Buildings["University"];
        Assert.False(university.CanBePurchasedWithStat(home, Stat.Faith));

        var religion = Found(a, home, "Jesuit Education", "To the Glory of God");
        Convert(home, religion);
        Assert.True(university.CanBePurchasedWithStat(home, Stat.Faith));
        Assert.False(game.Ruleset.Buildings["Library"].CanBePurchasedWithStat(home, Stat.Faith));

        a.Tech.Era = game.Ruleset.Eras["Industrial era"];
        var scientist = game.Ruleset.Units["Great Scientist"];
        Assert.True(scientist.CanBePurchasedWithStat(home, Stat.Faith));
    }
}
