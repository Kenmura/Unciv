using Unciv.Assets;
using Unciv.Queries.Victory;

namespace Unciv.Core.Tests.Assets;

public class GameMenuModelTests
{
    private static GameMenuEntry Entry(string status, bool unsaved, GameMenuKind kind) => GameMenuModel.Build(status, unsaved).Single(it => it.Kind == kind);

    [Fact]
    public void ListsEveryEntryInOrderWithStableIds()
    {
        var entries = GameMenuModel.Build(GameOverQuery.Playing, false);
        Assert.Equal(["Resume", "Save", "Load", "NewGame", "VictoryStatus", "Options", "Resign", "MainMenu"], entries.Select(it => it.Id));
        Assert.All(entries, it => Assert.True(it.Enabled));
    }

    [Fact]
    public void ResignAlwaysAsksFirstAndIsGreyedOnlyWhenDefeated()
    {
        var resign = Entry(GameOverQuery.Playing, false, GameMenuKind.Resign);
        Assert.NotNull(resign.Confirm);
        Assert.True(resign.Enabled);
        Assert.True(Entry(GameOverQuery.Won, false, GameMenuKind.Resign).Enabled);
        var defeated = Entry(GameOverQuery.Defeated, false, GameMenuKind.Resign);
        Assert.False(defeated.Enabled);
        Assert.NotNull(defeated.DisabledReason);
    }

    [Theory]
    [InlineData(GameMenuKind.Load)]
    [InlineData(GameMenuKind.NewGame)]
    [InlineData(GameMenuKind.MainMenu)]
    public void LeavingAsksOnlyWithUnsavedProgress(GameMenuKind kind)
    {
        Assert.Null(Entry(GameOverQuery.Playing, false, kind).Confirm);
        var confirm = Entry(GameOverQuery.Playing, true, kind).Confirm;
        Assert.NotNull(confirm);
        Assert.Contains("not saved", confirm.Body);
    }

    [Fact]
    public void HarmlessEntriesNeverAsk()
    {
        foreach (var kind in new[] { GameMenuKind.Resume, GameMenuKind.Save, GameMenuKind.VictoryStatus, GameMenuKind.Options })
            Assert.Null(Entry(GameOverQuery.Playing, true, kind).Confirm);
    }

    [Fact]
    public void ResignedScreenOffersOnlyTheMainMenu()
    {
        var view = GameOverModel.BuildResigned(null, null);
        Assert.Equal("Resigned", view.Status);
        Assert.Equal(GameMenuModel.ResignedTitle, view.Title);
        Assert.Equal([GameOverButtonKind.MainMenu], view.Buttons.Select(it => it.Kind));
        Assert.Empty(view.Ranking);
    }
}

public class SaveProgressTests
{
    [Fact]
    public void ANewGameHasNothingToLoseUntilACommandIsAccepted()
    {
        var progress = new SaveProgress();
        progress.Reset(7);
        Assert.False(progress.IsUnsaved(7));
        Assert.True(progress.IsUnsaved(8));
    }

    [Fact]
    public void ASaveCoversUpToItsSequenceAndNeverMovesBack()
    {
        var progress = new SaveProgress();
        progress.Reset(0);
        progress.MarkSaved(5);
        Assert.False(progress.IsUnsaved(5));
        progress.MarkSaved(3);
        Assert.False(progress.IsUnsaved(5));
        Assert.True(progress.IsUnsaved(6));
    }
}

public class VictoryStatusModelTests
{
    private static MilestoneEntry Milestone(string text, string status, int? done = null, int? total = null) => new(text, "T", status, done, total);

    [Fact]
    public void NoResultIsNoSections()
    {
        Assert.Empty(VictoryStatusModel.Build(null));
        Assert.Empty(VictoryStatusModel.Build(new VictoryProgressResult([])));
    }

    [Fact]
    public void WordsOwnMilestonesAndRivalsKeepingTheEnginesOrder()
    {
        var next = Milestone("Capture all capitals", "Partially", 1, null);
        var result = new VictoryProgressResult(
        [
            new VictoryEntry("Domination", "Domination Victory", 2,
                [Milestone("Build it", "Completed", 1, 1), next, Milestone("No numbers", "Incomplete")],
                [
                    new VictoryCivEntry(new VictoryCivRef("Rome", false, false), 0, next),
                    new VictoryCivEntry(new VictoryCivRef(null, false, false), 1, next),
                    new VictoryCivEntry(new VictoryCivRef("Babylon", false, true), 2, null),
                ]),
        ]);
        var section = Assert.Single(VictoryStatusModel.Build(result));
        Assert.Equal("Domination", section.Name);
        Assert.Equal(["1/1", "1/?", ""], section.Own.Select(it => it.Progress));
        Assert.Equal(["Completed", "Partially", "Incomplete"], section.Own.Select(it => it.Status));
        Assert.Equal(["Rome", "???", "Babylon"], section.Civs.Select(it => it.Civ));
        Assert.Equal("Rome: 0/2, next: Capture all capitals", section.Civs[0].Text);
        Assert.Equal("Babylon: 2/2 (all done)", section.Civs[2].Text);
        Assert.True(section.Civs[2].IsViewer);
    }
}
