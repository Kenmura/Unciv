using Unciv.Assets;
using Unciv.Commands.Empire;
using Unciv.Queries.Empire;

namespace Unciv.Core.Tests.Assets;

public class PolicyScreenModelTests
{
    private static PolicyEntry Entry(string name, string kind = "Member", int row = 1, int column = 1, string state = "Locked", string? reason = null) =>
        new(name, kind, row, column, [], state, reason);

    private static PolicyBranchEntry Branch(string name, string era, int adopted, int total, params PolicyEntry[] policies) =>
        new(name, era, "Adoptable", null, adopted, total, Entry(name, "BranchStart", 0, 0, "Adoptable"), policies);

    private static PolicyTreeResult Tree(bool canAdopt = true, int stored = 50, int needed = 15, int free = 0, params PolicyBranchEntry[] branches) =>
        new(stored, needed, free, canAdopt, false, branches);

    private static PolicyBranchEntry Tradition() => Branch("Tradition", "Ancient era", 1, 6,
        Entry("Legalism", row: 1, column: 3, state: "Adoptable"), Entry("Aristocracy", row: 1, column: 1, state: "Adopted"),
        Entry("Landed Elite", row: 2, column: 2, reason: "requires:Legalism"), Entry("Tradition Complete", "BranchComplete", 0, 0, reason: "complete-automatically"));

    [Fact]
    public void HeaderGivesCultureAndWhatIsMissing()
    {
        Assert.Equal(["Culture: 50 / 15", "You can adopt a policy now"], PolicyScreenModel.HeaderLines(Tree()));
        Assert.Equal(["Culture: 15 / 40", "Next policy in 25 more culture"], PolicyScreenModel.HeaderLines(Tree(false, 15, 40)));
        Assert.Equal(["Culture: 0 / 40", "Free policies: 2", "You can adopt a policy now"], PolicyScreenModel.HeaderLines(Tree(true, 0, 40, 2)));
        Assert.Equal(["Culture: 0 / 40", "Free policies: 1", "You can adopt a policy now"], PolicyScreenModel.HeaderLines(Tree(true, 0, 40, 1)));
        Assert.Equal("Next policy in 0 more culture", PolicyScreenModel.HeaderLines(Tree(false, 50, 40))[1]); // never negative
    }

    [Fact]
    public void GoldenAgeLineShowsTurnsLeftOrStoredHappiness()
    {
        var running = new EmpireTopBarResult(0, 0, 0, 0f, 0f, 0f, 0f, 1, 4, 10, 100, []);
        Assert.Equal("Golden Age: 4 turns left", PolicyScreenModel.GoldenAgeLine(running));
        Assert.Equal("Golden Age: 1 turns left", PolicyScreenModel.GoldenAgeLine(running with { GoldenAgeTurnsLeft = 1 }));
        Assert.Equal("Golden Age: 10 / 100 happiness stored", PolicyScreenModel.GoldenAgeLine(running with { GoldenAgeTurnsLeft = 0 }));
    }

    [Fact]
    public void BranchesKeepEngineOrderAndPlaceMembersByRowAndColumn()
    {
        var views = PolicyScreenModel.Branches(Tree(true, 50, 15, 0, Tradition(), Branch("Liberty", "", 0, 6)));
        Assert.Equal(["Tradition", "Liberty"], views.Select(it => it.Name));
        var view = views[0];
        Assert.Equal("Tradition (Ancient era)", view.Title);
        Assert.Equal("1 / 6 adopted", view.Progress);
        Assert.Equal(["Aristocracy", "Legalism", "Landed Elite"], view.Members.Select(it => it.Name));
        Assert.Equal(3, view.Columns);
        Assert.Equal(2, view.Rows);
        Assert.Equal("Tradition Complete", view.Complete?.Name);
        Assert.True(view.Start.IsBranchStart);
        Assert.Equal("Liberty", views[1].Title); // no era: no brackets
        Assert.Equal((0, 0), (views[1].Columns, views[1].Rows));
        Assert.Null(views[1].Complete);
    }

    [Fact]
    public void ColumnCountIsTheHighestColumnNotTheMemberCount()
    {
        var view = PolicyScreenModel.Branches(Tree(true, 50, 15, 0, Branch("B", "", 0, 2, Entry("A", row: 1, column: 4), Entry("C", row: 1, column: 1))))[0];
        Assert.Equal(4, view.Columns);
        Assert.Equal(1, view.Rows);
    }

    [Fact]
    public void OnlyAnAdoptablePolicyIsPickableAndOnlyWhenTheEngineAllowsAdopting()
    {
        var branch = Tradition();
        var open = PolicyScreenModel.Cell(branch.Policies[0], Tree(), branch);
        Assert.True(open.Pickable);
        Assert.Equal("Can be adopted now", open.Hint);
        var poor = PolicyScreenModel.Cell(branch.Policies[0], Tree(false, 15, 40), branch);
        Assert.False(poor.Pickable);
        Assert.Equal("Needs 40 culture (15 stored)", poor.Hint);
        var adopted = PolicyScreenModel.Cell(branch.Policies[1], Tree(), branch);
        Assert.False(adopted.Pickable);
        Assert.True(adopted.IsAdopted);
        Assert.Equal("Adopted", adopted.Hint);
        Assert.Equal("Policy_Aristocracy", adopted.Id);
    }

    [Fact]
    public void LockedPoliciesNameTheirReason()
    {
        var branch = Tradition();
        string Hint(PolicyEntry entry) => PolicyScreenModel.Cell(entry, Tree(), branch).Hint;
        Assert.Equal("Needs Legalism first", Hint(branch.Policies[2]));
        Assert.Equal("Adopted automatically with the last policy of the branch", Hint(branch.Policies[3]));
        Assert.Equal("Opens in the Ancient era", Hint(Entry("X", reason: "era:Medieval era")));
        Assert.Equal("Opens in the Medieval era", PolicyScreenModel.Cell(Entry("X", reason: "era:Medieval era"), Tree(), Branch("B", "", 0, 1)).Hint);
        Assert.Equal("Not available to you", Hint(Entry("X", reason: "unavailable")));
        Assert.Equal("Not available to you", Hint(Entry("X")));
        Assert.False(PolicyScreenModel.Cell(branch.Policies[3], Tree(), branch).Pickable);
        Assert.True(PolicyScreenModel.Cell(branch.Policies[3], Tree(), branch).IsComplete);
    }

    [Fact]
    public void CompleteBonusIsNeverPickableEvenIfTheEngineCalledItAdoptable()
    {
        var branch = Branch("B", "Ancient era", 0, 1, Entry("B Complete", "BranchComplete", 0, 0, "Adoptable"));
        Assert.False(PolicyScreenModel.Cell(branch.Policies[0], Tree(), branch).Pickable);
    }

    [Fact]
    public void AdoptCommandIsOnlyOfferedForAPickablePolicy()
    {
        var branch = Tradition();
        var cell = PolicyScreenModel.Cell(branch.Policies[0], Tree(), branch);
        var command = Assert.IsType<AdoptPolicy>(PolicyScreenModel.AdoptCommand(cell));
        Assert.Equal("Legalism", command.Policy);
        Assert.Null(PolicyScreenModel.AdoptCommand(PolicyScreenModel.Cell(branch.Policies[0], Tree(false), branch)));
        Assert.Equal("Adopt Legalism", PolicyScreenModel.AdoptLabel(cell));
        Assert.Equal("Adopted", PolicyScreenModel.AdoptLabel(PolicyScreenModel.Cell(branch.Policies[1], Tree(), branch)));
    }

    [Fact]
    public void DetailListsNameHintAndEffectsWithoutRepeatingTheName()
    {
        var branch = Tradition();
        var cell = PolicyScreenModel.Cell(branch.Policies[0], Tree(), branch);
        Assert.Equal("Legalism\nCan be adopted now\n• +1 Culture", PolicyScreenModel.Detail(cell, "Legalism\n• +1 Culture"));
        Assert.Equal("Legalism\nCan be adopted now", PolicyScreenModel.Detail(cell, ""));
    }

    [Fact]
    public void PaletteDimsLockedAndHighlightsAdopted()
    {
        Assert.Equal(PlaceholderPalette.Accent, PlaceholderPalette.ForPolicyState("Adopted"));
        Assert.Equal(PlaceholderPalette.Text, PlaceholderPalette.ForPolicyState("Adoptable"));
        Assert.Equal(PlaceholderPalette.TextDisabled, PlaceholderPalette.ForPolicyState("Locked"));
    }
}
