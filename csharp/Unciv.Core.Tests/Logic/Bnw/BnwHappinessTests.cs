using Unciv.Core.Tests.Support;
using Unciv.Logic.Battle;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Managers;
using Unciv.Models.Metadata;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Logic.Bnw;

// B019 (bnw-spec/SPEC.md 4.8): graded unhappiness penalties, uprisings with rebels and city revolt, on the real generated BNW ruleset.
[Collection("RulesetCache")]
public sealed class BnwHappinessTests
{
    private const int Popular = 60, Dominant = 200;

    private static TestGame NewGame(BaseRuleset ruleset = BaseRuleset.Civ_V_BNW, string? speed = null)
    {
        var game = new TestGame(ruleset);
        if (speed != null) game.SetSpeed(speed);
        game.MakeHexagonalMap(12);
        return game;
    }

    private static CivilizationObject Civ(TestGame game, int happiness, int cities = 1, int x = -6, string[]? uniques = null)
    {
        var civ = game.AddCiv(uniques, isPlayer: true); // human: AI civs get no notifications
        for (int i = 0; i < cities; i++) game.AddCity(civ, game.GetTile(x + 4 * i, -6));
        civ.Stats.Happiness = happiness;
        return civ;
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 0)]
    [InlineData(-1, -2)]
    [InlineData(-19, -38)]
    [InlineData(-20, -40)]
    [InlineData(-21, -40)]
    [InlineData(int.MinValue, -40)]
    public void PenaltyIsTwoPerPointCappedAtForty(int happiness, int expected)
    {
        var civ = Civ(NewGame(), happiness);
        Assert.Equal(expected, BnwHappinessRules.PenaltyPercent(civ));
    }

    [Fact]
    public void GodsAndKingsHasNoGradedPenaltyAndKeepsItsFixedGlobalUniques()
    {
        var game = NewGame(BaseRuleset.Civ_V_GnK);
        var civ = Civ(game, -30);
        Assert.Equal(0, BnwHappinessRules.PenaltyPercent(civ));
        Assert.False(BnwHappinessRules.IsVeryUnhappy(civ));
        Assert.Contains("[-50]% [Production] [in all cities] <when below [-10] [Happiness]>", game.Ruleset.GlobalUniques.Uniques);
    }

    [Fact]
    public void TheBnwRulesetDropsTheFixedGlobalPenaltiesAndKeepsGrowthAndSettlers()
    {
        var uniques = new TestGame(BaseRuleset.Civ_V_BNW).Ruleset.GlobalUniques.Uniques;
        Assert.DoesNotContain(uniques, it => it.Contains("Strength", StringComparison.Ordinal) || it.Contains("Rebel", StringComparison.Ordinal)
            || it.Contains("[Production] [in all cities] <when below", StringComparison.Ordinal));
        Assert.Contains("[-75]% growth [in all cities] <when between [-10] and [-1] [Happiness]>", uniques);
        Assert.Contains("Cannot build [Settler] units <when below [-10] [Happiness]>", uniques);
    }

    [Fact]
    public void CityProductionAndGoldAreReducedByThePenalty()
    {
        var game = NewGame();
        var civ = Civ(game, -5);
        var city = civ.Cities[0];
        city.CityStats.Update(updateCivStats: false);
        var total = city.CityStats.StatPercentBonusTree.TotalStats;
        Assert.Equal(-10f, total.Production);
        Assert.Equal(-10f, total.Gold);
        civ.Stats.Happiness = 3;
        city.CityStats.Update(updateCivStats: false);
        Assert.Equal(0f, city.CityStats.StatPercentBonusTree.TotalStats.Production);
    }

    [Fact]
    public void UnitStrengthIsReducedByThePenaltyAndOnlyWhenUnhappy()
    {
        var game = NewGame();
        var civ = Civ(game, -12);
        var other = game.AddCiv();
        var attacker = game.AddUnit("Warrior", civ, game.GetTile(1, 1));
        var defender = game.AddUnit("Warrior", other, game.GetTile(0, 1));
        civ.Stats.Happiness = -12;
        var modifiers = BattleDamage.GetAttackModifiers(new MapUnitCombatant(attacker), new MapUnitCombatant(defender), game.GetTile(1, 1));
        Assert.Equal(-24, modifiers["Unhappiness"]);
        civ.Stats.Happiness = 0;
        modifiers = BattleDamage.GetAttackModifiers(new MapUnitCombatant(attacker), new MapUnitCombatant(defender), game.GetTile(1, 1));
        Assert.DoesNotContain("Unhappiness", modifiers.Keys);
    }

    [Theory]
    [InlineData(-9, false)]
    [InlineData(-10, true)]
    [InlineData(-11, true)]
    public void TheUprisingCounterIsArmedAtTheVeryUnhappyThreshold(int happiness, bool armed)
    {
        var civ = Civ(NewGame(), happiness);
        BnwHappinessRules.UpdateCounters(civ);
        Assert.Equal(armed, civ.HasFlag(CivFlags.RevoltSpawning.ToString()));
        if (armed) Assert.InRange(civ.FlagsCountdown[CivFlags.RevoltSpawning.ToString()], 2, 3); // first arming: (4 + rand(3)) / 2
    }

    [Fact]
    public void TheCounterIsDroppedWhenTheCivIsNoLongerVeryUnhappyAndNotRearmedWhileRunning()
    {
        var civ = Civ(NewGame(), -15);
        BnwHappinessRules.UpdateCounters(civ);
        civ.FlagsCountdown[CivFlags.RevoltSpawning.ToString()] = 7;
        BnwHappinessRules.UpdateCounters(civ);
        Assert.Equal(7, civ.FlagsCountdown[CivFlags.RevoltSpawning.ToString()]);
        civ.Stats.Happiness = -2;
        BnwHappinessRules.UpdateCounters(civ);
        Assert.False(civ.HasFlag(CivFlags.RevoltSpawning.ToString()));
    }

    [Theory]
    [InlineData("Quick", 4, 6)]
    [InlineData("Standard", 4, 6)]
    [InlineData("Epic", 6, 9)]
    [InlineData("Marathon", 12, 18)]
    public void TheFullUprisingCounterScalesWithSlowSpeeds(string speed, int low, int high)
    {
        var civ = Civ(NewGame(speed: speed), -15);
        for (int i = 0; i < 20; i++)
            Assert.InRange(BnwHappinessRules.UprisingCounter(civ, first: false), low, high);
        Assert.InRange(BnwHappinessRules.UprisingCounter(civ, first: true), System.Math.Max(1, low / 2), high / 2);
    }

    [Theory]
    [InlineData("Quick", 5)]
    [InlineData("Standard", 5)]
    [InlineData("Epic", 7)]
    [InlineData("Marathon", 15)]
    public void TheRevoltCounterScalesWithSlowSpeeds(string speed, int expected)
    {
        var civ = Civ(NewGame(speed: speed), -25);
        Assert.Equal(expected, BnwHappinessRules.RevoltCounter(civ));
    }

    [Fact]
    public void RebelsSpawnAsBarbariansNearACityAndTheCounterIsRearmed()
    {
        var game = NewGame();
        var barbarians = game.AddBarbarianCiv();
        var civ = Civ(game, -15, cities: 2);
        civ.Tech.AddTechnology("Agriculture");
        civ.AddFlag(CivFlags.RevoltSpawning.ToString(), 0);
        civ.Stats.Happiness = -15;
        BnwHappinessRules.SpawnRebels(civ);

        var rebels = barbarians.Units.GetCivUnits().ToList();
        Assert.InRange(rebels.Count, 1, 2); // (rand(2 x 20 + 100) + 100) / 100
        Assert.All(rebels, it => Assert.Equal("Warrior", it.Name));
        Assert.Contains(civ.Notifications, it => it.Text.Contains("revolting", StringComparison.Ordinal));
        Assert.InRange(civ.FlagsCountdown[CivFlags.RevoltSpawning.ToString()], 4, 6);
    }

    [Fact]
    public void WithoutBarbariansOrWhenContentNothingSpawnsAndTheCounterIsHandledAnyway()
    {
        var game = NewGame();
        var civ = Civ(game, -15);
        civ.Tech.AddTechnology("Agriculture");
        civ.Stats.Happiness = -15;
        BnwHappinessRules.SpawnRebels(civ);
        Assert.DoesNotContain(civ.Notifications, it => it.Text.Contains("revolting", StringComparison.Ordinal));
        Assert.True(civ.HasFlag(CivFlags.RevoltSpawning.ToString()));

        var barbarians = game.AddBarbarianCiv();
        civ.Stats.Happiness = 2;
        BnwHappinessRules.SpawnRebels(civ);
        Assert.Empty(barbarians.Units.GetCivUnits());
        Assert.False(civ.HasFlag(CivFlags.RevoltSpawning.ToString()));
    }

    [Fact]
    public void ARealStartOfTurnArmsAndFiresTheCounter()
    {
        var game = NewGame();
        var barbarians = game.AddBarbarianCiv();
        var civ = Civ(game, 0, uniques: ["[-40 Happiness] [in all cities]"]);
        civ.Tech.AddTechnology("Agriculture");
        civ.AddFlag(CivFlags.RevoltSpawning.ToString(), 1);
        new TurnManager(civ).StartTurn();
        Assert.NotEmpty(barbarians.Units.GetCivUnits());
    }

    // ---- city revolt

    private sealed class RevoltWorld
    {
        public TestGame Game { get; } = NewGame();
        public List<CivilizationObject> Civs { get; } = [];

        public RevoltWorld(int cities)
        {
            var c = Game.Ruleset.ModOptions.Constants;
            (c.CultureLevelExotic, c.CultureLevelFamiliar, c.CultureLevelPopular, c.CultureLevelInfluential, c.CultureLevelDominant) = (10, 30, 60, 100, 200);
            for (int i = 0; i < 3; i++)
            {
                var civ = Game.AddCiv(isPlayer: true);
                civ.Tech.Era = Game.Ruleset.Eras["Modern era"];
                civ.TotalCultureForContests = 100;
                int n = i == 0 ? cities : 1;
                for (int k = 0; k < n; k++) Game.AddCity(civ, Game.GetTile(-8 + 4 * i, -6 + 3 * k), initialPopulation: 1);
                Civs.Add(civ);
            }
            foreach (var x in Civs) foreach (var y in Civs) if (x != y) x.DiplomacyFunctions.MakeCivilizationsMeet(y);
            Civs[0].Policies.ChooseIdeology(Game.Ruleset.PolicyBranches["Freedom"]);
            Civs[1].Policies.ChooseIdeology(Game.Ruleset.PolicyBranches["Order"]);
            Civs[2].Policies.ChooseIdeology(Game.Ruleset.PolicyBranches["Autocracy"]);
            (Civs[1].Culture ??= new()).GetOrAdd(Civs[0].CivId).Influence = Popular;
            (Civs[2].Culture ??= new()).GetOrAdd(Civs[0].CivId).Influence = Dominant;
            Civs[0].Stats.Happiness = -25;
        }
    }

    [Theory]
    [InlineData(-19, false)]
    [InlineData(-20, true)]
    public void CityRevoltIsArmedAtTheSuperUnhappyThresholdWithOpinionUnhappiness(int happiness, bool armed)
    {
        var w = new RevoltWorld(2);
        w.Civs[0].Stats.Happiness = happiness;
        BnwHappinessRules.UpdateCounters(w.Civs[0]);
        Assert.Equal(armed, w.Civs[0].HasFlag(CivFlags.CityRevolt.ToString()));
        if (armed) Assert.Equal(5, w.Civs[0].FlagsCountdown[CivFlags.CityRevolt.ToString()]);
    }

    [Fact]
    public void WithoutOpinionUnhappinessThereIsNoRevolt()
    {
        var w = new RevoltWorld(2);
        w.Civs[0].Culture = null;
        w.Civs[1].Culture = null;
        w.Civs[2].Culture = null;
        BnwHappinessRules.UpdateCounters(w.Civs[0]);
        Assert.False(w.Civs[0].HasFlag(CivFlags.CityRevolt.ToString()));
        Assert.True(w.Civs[0].HasFlag(CivFlags.RevoltSpawning.ToString())); // still very unhappy
    }

    [Fact]
    public void ACityLeavesForTheCivThePeoplePreferAndTheCapitalStays()
    {
        var w = new RevoltWorld(2);
        var capital = w.Civs[0].Cities[0];
        var second = w.Civs[0].Cities[1];
        Assert.True(capital.IsCapital());
        w.Civs[0].AddFlag(CivFlags.CityRevolt.ToString(), 0);
        BnwHappinessRules.RevoltCity(w.Civs[0]);
        // civ 2 (Autocracy) has the larger influence over civ 0, but the preferred ideology (largest pressure) is Autocracy
        Assert.Equal(w.Civs[2], second.Civ);
        Assert.Equal(w.Civs[0], capital.Civ);
        Assert.Single(w.Civs[0].Cities);
        Assert.Equal(2, w.Civs[2].Cities.Count);
        Assert.Equal(5, w.Civs[0].FlagsCountdown[CivFlags.CityRevolt.ToString()]);
        Assert.Contains(w.Civs[0].Notifications, it => it.Text.Contains("revolted", StringComparison.Ordinal));
        Assert.Contains(w.Civs[2].Notifications, it => it.Text.Contains("revolted", StringComparison.Ordinal));

        // only the capital left: nothing more to lose, the counter still re-arms
        w.Civs[0].AddFlag(CivFlags.CityRevolt.ToString(), 0);
        BnwHappinessRules.RevoltCity(w.Civs[0]);
        Assert.Single(w.Civs[0].Cities);
    }

    [Fact]
    public void TheCityNearestTheReceivingCivRevolts()
    {
        var w = new RevoltWorld(3);
        var cities = w.Civs[0].Cities.ToList();
        var targetCity = w.Civs[2].Cities[0];
        var expected = cities.Skip(1).MinBy(it => it.GetCenterTile().AerialDistanceTo(targetCity.GetCenterTile()))!;
        BnwHappinessRules.RevoltCity(w.Civs[0]);
        Assert.Equal(w.Civs[2], expected.Civ);
        Assert.Equal(2, w.Civs[0].Cities.Count);
    }

    [Fact]
    public void ARevoltIsNotRunWhenTheCivRecovered()
    {
        var w = new RevoltWorld(2);
        w.Civs[0].Stats.Happiness = 5;
        w.Civs[0].AddFlag(CivFlags.CityRevolt.ToString(), 0);
        BnwHappinessRules.RevoltCity(w.Civs[0]);
        Assert.Equal(2, w.Civs[0].Cities.Count);
        Assert.False(w.Civs[0].HasFlag(CivFlags.CityRevolt.ToString()));
    }
}
