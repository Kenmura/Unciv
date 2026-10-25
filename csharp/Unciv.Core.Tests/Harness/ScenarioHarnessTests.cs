using System.Text.Json;
using Unciv.Commands.City;
using Unciv.Harness;
using Unciv.Models.Ruleset;

namespace Unciv.Core.Tests.Harness;

// C008: the engine-only half of the Godot scenario runner (session factory, command-log replay, result file, review sheet layout).
[Collection("RulesetCache")]
public sealed class ScenarioHarnessTests : IDisposable
{
    public ScenarioHarnessTests() => RulesetCache.Clear();

    public void Dispose() => RulesetCache.Clear();

    private static ScenarioResult RenameScenario(out string hash)
    {
        var session = ScenarioGame.Create();
        string cityId = ScenarioGame.FirstCity(session.Game, ScenarioGame.HumanA).Id;
        var accepted = session.Submit(ScenarioGame.HumanA, new RenameCity { CityId = cityId, Name = "Ur" });
        Assert.True(accepted.Accepted, accepted.Validation.Message);
        hash = ScenarioGame.StateHash(session);
        return new ScenarioResult("rename", ScenarioGame.GameSeed, ScenarioGame.MapSeed, hash, session.Log, [new ScenarioCapture("after", "renamed")], [], []);
    }

    [Fact]
    public void FactoryBuildsTheFixedGameDeterministically()
    {
        var first = ScenarioGame.Create();
        var second = ScenarioGame.Create();
        Assert.Equal(ScenarioGame.StateHash(first), ScenarioGame.StateHash(second));
        Assert.Equal(ScenarioGame.HumanA, first.Game.CurrentPlayer);
        Assert.NotEmpty(first.Game.GetCivilization(ScenarioGame.HumanA).Cities);
        Assert.NotEmpty(first.Game.GetCivilization(ScenarioGame.HumanB).Cities);
        Assert.Empty(first.Game.GetCivilization(ScenarioGame.Ai).Cities);
        Assert.NotEqual(ScenarioGame.StateHash(first), ScenarioGame.StateHash(ScenarioGame.Create(ScenarioGame.GameSeed + 1)));
    }

    [Fact]
    public void ReplayOfTheLogReproducesTheHashAndATamperedLogDoesNot()
    {
        var result = RenameScenario(out string hash);
        Assert.Null(ScenarioReplay.Verify(result));
        Assert.Equal(hash, ScenarioReplay.ReplayHash(result.GameSeed, result.MapSeed, result.Commands));

        var missing = result with { Commands = [] };
        Assert.Contains("differs from the headless replay", ScenarioReplay.Verify(missing));
    }

    [Fact]
    public void ResultRoundTripsThroughJson()
    {
        var result = RenameScenario(out _);
        var back = ScenarioResult.FromJson(result.ToJson());
        Assert.Equal(result.StateHash, back.StateHash);
        Assert.Equal(result.Captures, back.Captures);
        Assert.True(back.Passed);
        Assert.Null(ScenarioReplay.Verify(back));
        Assert.Throws<JsonException>(() => ScenarioResult.FromJson("null"));
    }

    [Fact]
    public void LayoutPlacesCellsRowByRowAndSizesTheSheetToFitThem()
    {
        var layout = ContactSheetLayout.Compute(5, 640, 480, columns: 3, thumbWidth: 320, captionHeight: 40, margin: 8);
        Assert.Equal(5, layout.Cells.Count);
        Assert.Equal(240, layout.Cells[0].ThumbHeight);
        Assert.Equal(new SheetCell(8, 8, 320, 240, 40), layout.Cells[0]);
        Assert.Equal(8 + 328, layout.Cells[1].X);
        Assert.Equal(layout.Cells[0].Y + 240 + 40 + 8, layout.Cells[3].Y);
        Assert.Equal(8, layout.Cells[3].X);
        Assert.Equal(8 + 3 * 328, layout.Width);
        Assert.Equal(8 + 2 * 288, layout.Height);
        foreach (var cell in layout.Cells)
        {
            Assert.True(cell.X + cell.ThumbWidth <= layout.Width);
            Assert.True(cell.Y + cell.ThumbHeight + cell.CaptionHeight <= layout.Height);
        }
    }

    [Fact]
    public void LayoutHandlesFewCellsOddAspectAndRejectsBadInput()
    {
        var one = ContactSheetLayout.Compute(1, 1280, 720, columns: 4);
        Assert.Equal(180, one.Cells[0].ThumbHeight);
        Assert.Equal(8 + 328, one.Width);
        Assert.Equal(361, ContactSheetLayout.Compute(1, 3, 1, thumbWidth: 1083).Cells[0].ThumbHeight);
        Assert.Empty(ContactSheetLayout.Compute(0, 640, 480).Cells);
        Assert.Throws<ArgumentOutOfRangeException>(() => ContactSheetLayout.Compute(-1, 640, 480));
        Assert.Throws<ArgumentOutOfRangeException>(() => ContactSheetLayout.Compute(1, 0, 480));
        Assert.Throws<ArgumentOutOfRangeException>(() => ContactSheetLayout.Compute(1, 640, 480, columns: 0));
    }

    [Fact]
    public void IndexListsCapturesFailuresAndTheChecklist()
    {
        string md = ReviewIndex.Render(
        [
            new ReviewSection("rename", true, [], [new ScenarioCapture("before", "Start"), new ScenarioCapture("after", "Renamed")], ["Is the name readable?"]),
            new ReviewSection("broken", false, ["golden mismatch"], [new ScenarioCapture("only", "Only step")], []),
        ], imageRoot: "../");
        Assert.Contains("![contact sheet](contact-sheet.png)", md);
        Assert.Contains("2 scenario(s), 1 failing", md);
        Assert.Contains("## rename: PASS", md);
        Assert.Contains("![after](../rename/after.png)", md);
        Assert.Contains("- [ ] Is the name readable?", md);
        Assert.Contains("## broken: FAIL", md);
        Assert.Contains("- FAILED: golden mismatch", md);
        foreach (string item in ReviewIndex.GeneralChecklist) Assert.Contains("- [ ] " + item, md);
    }

    [Fact]
    public void SkirmishGameHasAdjacentEnemyAtWarAndAWorkerAndReplaysEqual()
    {
        var session = ScenarioGame.Create(skirmish: true);
        var human = session.Game.GetCivilization(ScenarioGame.HumanA);
        var ai = session.Game.GetCivilization(ScenarioGame.Ai);
        var warrior = human.Units.GetCivUnits().First(it => it.BaseUnit.Name == "Warrior");
        var enemy = ai.Units.GetCivUnits().First(it => it.BaseUnit.Name == "Warrior" && it.CurrentTile.AerialDistanceTo(warrior.CurrentTile) == 1);
        Assert.True(human.IsAtWarWith(ai));
        Assert.Contains(human.Units.GetCivUnits(), it => it.BaseUnit.Name == "Worker");
        Assert.Equal(ScenarioGame.StateHash(session), ScenarioGame.StateHash(ScenarioGame.Create(skirmish: true)));
        var result = session.Submit(ScenarioGame.HumanA, new Unciv.Commands.Units.AttackWithUnit { UnitId = warrior.Id, Target = enemy.CurrentTile.Position });
        Assert.True(result.Accepted);
        Assert.True(enemy.Health < 100);
        string hash = ScenarioGame.StateHash(session);
        Assert.Equal(hash, ScenarioReplay.ReplayHash(ScenarioGame.GameSeed, ScenarioGame.MapSeed, session.Log, skirmish: true));
    }

    [Fact]
    public void DiplomacyGameHasMetRomeAndACityStateAnUnmetGreeceAndGoldAndReplaysEqual()
    {
        var session = ScenarioGame.Create(diplomacy: true);
        var human = session.Game.GetCivilization(ScenarioGame.HumanA);
        var rome = session.Game.GetCivilization(ScenarioGame.Ai);
        var greece = session.Game.GetCivilization(ScenarioGame.HumanB);
        var cityStates = session.Game.Civilizations.Where(it => it.IsCityState).ToList();
        var cityState = Assert.Single(cityStates);
        Assert.True(human.Knows(rome));
        Assert.True(human.Knows(cityState));
        Assert.False(human.Knows(greece));
        Assert.Equal(1000, human.Gold);
        Assert.False(human.IsAtWarWith(rome));
        Assert.False(greece.IsHuman());
        Assert.Equal(ScenarioGame.StateHash(session), ScenarioGame.StateHash(ScenarioGame.Create(diplomacy: true)));
        Assert.NotEqual(ScenarioGame.StateHash(session), ScenarioGame.StateHash(ScenarioGame.Create(solo: true)));
        var result = session.Submit(ScenarioGame.HumanA, new Unciv.Commands.Diplomacy.DeclareWar { CivId = rome.CivId });
        Assert.True(result.Accepted);
        Assert.Equal(ScenarioGame.StateHash(session), ScenarioReplay.ReplayHash(ScenarioGame.GameSeed, ScenarioGame.MapSeed, session.Log, diplomacy: true));
        var json = new ScenarioResult("d", 1, 2, "h", [], [], [], [], Diplomacy: true).ToJson();
        Assert.True(ScenarioResult.FromJson(json).Diplomacy);
    }

    [Fact]
    public void RichGameGivesEveryHumanGoldAndBabylonAPuppetCityAndReplaysEqual()
    {
        var session = ScenarioGame.Create(rich: true);
        var babylon = session.Game.GetCivilization(ScenarioGame.HumanA);
        var greece = session.Game.GetCivilization(ScenarioGame.HumanB);
        Assert.Equal(ScenarioGame.RichGold, babylon.Gold);
        Assert.Equal(ScenarioGame.RichGold, greece.Gold);
        Assert.Equal(2, babylon.Cities.Count);
        Assert.False(babylon.Cities[0].IsPuppet);
        Assert.True(babylon.Cities[1].IsPuppet);
        Assert.True(babylon.Cities[1].GetCenterTile().AerialDistanceTo(babylon.Cities[0].GetCenterTile()) >= 5);
        Assert.Single(greece.Cities);
        Assert.Equal(ScenarioGame.StateHash(session), ScenarioGame.StateHash(ScenarioGame.Create(rich: true)));
        Assert.NotEqual(ScenarioGame.StateHash(session), ScenarioGame.StateHash(ScenarioGame.Create()));
        Assert.Equal(ScenarioGame.StateHash(session), ScenarioReplay.ReplayHash(ScenarioGame.GameSeed, ScenarioGame.MapSeed, session.Log, rich: true));
        Assert.True(ScenarioResult.FromJson(new ScenarioResult("r", 1, 2, "h", [], [], [], [], Rich: true).ToJson()).Rich);
        Assert.False(ScenarioResult.FromJson(new ScenarioResult("r", 1, 2, "h", [], [], [], []).ToJson()).Rich);
    }

    [Fact]
    public void PolicyGameHasACapitalStoredCultureAndAPendingPolicyPickAndReplaysEqual()
    {
        var session = ScenarioGame.Create(policies: true);
        var human = session.Game.GetCivilization(ScenarioGame.HumanA);
        Assert.Single(human.Cities);
        Assert.Equal(ScenarioGame.PolicyCulture, human.Policies.StoredCulture);
        Assert.True(human.Policies.ShouldShowPolicyPicker());
        Assert.False(human.ShouldOpenTechPicker());
        Assert.Equal(ScenarioGame.StateHash(session), ScenarioGame.StateHash(ScenarioGame.Create(policies: true)));
        Assert.NotEqual(ScenarioGame.StateHash(session), ScenarioGame.StateHash(ScenarioGame.Create(solo: true)));
        var result = session.Submit(ScenarioGame.HumanA, new Unciv.Commands.Empire.AdoptPolicy { Policy = "Tradition" });
        Assert.True(result.Accepted);
        Assert.Equal(ScenarioGame.StateHash(session), ScenarioReplay.ReplayHash(ScenarioGame.GameSeed, ScenarioGame.MapSeed, session.Log, policies: true));
        Assert.True(ScenarioResult.FromJson(new ScenarioResult("p", 1, 2, "h", [], [], [], [], Policies: true).ToJson()).Policies);
        Assert.False(ScenarioResult.FromJson(new ScenarioResult("p", 1, 2, "h", [], [], [], []).ToJson()).Policies);
    }

    [Fact]
    public void AlertsGameHasTheNotificationsAndAlertsAndReplaysEqual()
    {
        var session = ScenarioGame.Create(alerts: true);
        var human = session.Game.GetCivilization(ScenarioGame.HumanA);
        Assert.Single(human.Cities);
        Assert.Contains(human.Notifications, it => it.Text == "[Rome] wishes to speak with you");
        Assert.Contains(human.Notifications, it => it.Text.StartsWith("An enemy scout", StringComparison.Ordinal));
        Assert.Equal(["StartIntro", "FirstContact", "FirstContact", "DemandToStopSpreadingReligion", "DeclarationOfFriendship", "CityConquered", "WonderBuilt"], human.PopupAlerts.Select(it => it.Type.ToString()));
        Assert.Equal(ScenarioGame.StateHash(session), ScenarioGame.StateHash(ScenarioGame.Create(alerts: true)));
        Assert.NotEqual(ScenarioGame.StateHash(session), ScenarioGame.StateHash(ScenarioGame.Create(diplomacy: true)));
        var result = session.Submit(ScenarioGame.HumanA, new Unciv.Commands.Turn.AckAlert { Index = 6, AlertType = Unciv.Logic.Civilization.AlertType.WonderBuilt, Value = "Pyramids" });
        Assert.True(result.Accepted, "rejected");
        Assert.Equal(ScenarioGame.StateHash(session), ScenarioReplay.ReplayHash(ScenarioGame.GameSeed, ScenarioGame.MapSeed, session.Log, alerts: true));
        Assert.True(ScenarioResult.FromJson(new ScenarioResult("d", 1, 2, "h", [], [], [], [], Alerts: true).ToJson()).Alerts);
    }
}
