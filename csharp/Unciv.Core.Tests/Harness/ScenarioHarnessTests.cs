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
}
