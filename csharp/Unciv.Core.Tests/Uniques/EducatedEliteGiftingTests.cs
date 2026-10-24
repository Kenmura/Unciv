using Unciv.Core.Tests.Support;
using Unciv.Logic.Civilization;

namespace Unciv.Core.Tests.Uniques;

// T154: UniqueErrorTests.testEducatedEliteGreatPersonGifting over the real TestGame (Models/Ruleset/Validation/UniqueErrorTests.cs holds the rest).
// The Kotlin UncivGame.Current tutorial-task workaround is not needed: the headless engine has no settings/tutorial tasks.
[Collection("RulesetCache")]
public sealed class EducatedEliteGiftingTests
{
    [Fact]
    public void TestEducatedEliteGreatPersonGifting()
    {
        var failures = new List<string>();
        string flagName = CivFlags.CityStateGreatPersonGift.ToString();
        var game = new TestGame();
        game.MakeHexagonalMap(2);

        var civ = game.AddCiv();
        game.AddCity(civ, game.GetTile(-2, 0));
        game.GameInfo.CurrentPlayerCiv = civ;
        var cityState = game.AddCiv(cityStateType: "Mercantile");
        game.AddCity(cityState, game.GetTile(2, 0));

        // make the city-state allied
        civ.DiplomacyFunctions.MakeCivilizationsMeet(cityState);
        civ.AddGold(10000);
        ((Unciv.Logic.Civilization.Diplomacy.CityStateFunctions)cityState.CityStateFunctions).ReceiveGoldGift(civ, 10000);
        if (!ReferenceEquals(cityState.AllyCiv, civ))
            failures.Add("The test civ and city-state should be allied after a 10k gift");

        // adopt the GP-gift-enabling policy
        var ee = game.Ruleset.Policies["Educated Elite"];
        civ.Policies.FreePolicies++;
        civ.Policies.Adopt(ee);
        if (!civ.HasFlag(flagName))
            failures.Add($"The test civ should have the {flagName} flag after adopting Educated Elite");

        // Ensure automation won't use up the GP - a threat won't do as it will be placed in the capital
        foreach (var key in game.Ruleset.Units.Where(it => it.Value.IsGreatPerson).Select(it => it.Key).ToList())
            game.Ruleset.Units.Remove(key);
        game.CreateBaseUnit("Civilian", "Great Person - [Gold]", "Unbuildable", "Uncapturable");

        // Force the countdown and pass *one* turn, a GP should appear
        civ.AddFlag(flagName, 1);
        game.GameInfo.SimulateUntilWin = true;
        game.GameInfo.SimulateMaxTurns = 1;
        game.GameInfo.NextTurn(0L);
        var unit = civ.Units.GetCivUnits().FirstOrDefault(it => it.IsGreatPerson());
        if (unit == null)
            failures.Add($"No gifted Great Person unit found after setting {flagName} to 1 and passing a turn");

        Assert.Empty(failures);
    }
}
