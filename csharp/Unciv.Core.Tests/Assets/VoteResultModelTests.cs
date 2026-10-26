using Unciv.Assets;
using Unciv.Queries.Empire;

namespace Unciv.Core.Tests.Assets;

public class VoteResultModelTests
{
    [Fact]
    public void NothingIsShownUntilTheEngineSaysTheResultIsDue()
    {
        Assert.Null(VoteResultModel.Build(null));
        Assert.Null(VoteResultModel.Build(new DiplomaticVoteBreakdownResult(false, null, null, "", [])));
    }

    [Fact]
    public void RowsWordTheEnginesAnswer()
    {
        var result = new DiplomaticVoteBreakdownResult(true, "United Nations", "Rome", "{No world leader was elected.}",
        [
            new VoteRow("Babylon", true, 12, 1, false, "Rome", false),
            new VoteRow("Rome", true, 0, 2, false, null, true),
            new VoteRow("Zurich", false, 0, 1, true, null, false),
        ]);
        var view = VoteResultModel.Build(result)!;
        Assert.Equal("World Congress", view.Title);
        Assert.Equal("{No world leader was elected.}", view.Verdict);
        Assert.Equal("Continue", view.ContinueLabel);
        Assert.Equal(new VoteResultRow("Babylon", "12", "[1] vote", null, "Voted for", "Rome"), view.Rows[0]);
        Assert.Equal(new VoteResultRow("Rome", "0", "[2] votes", "United Nations", "Voted for", DiplomaticVoteResultQuery.UnknownCiv), view.Rows[1]);
        Assert.Equal(new VoteResultRow("Zurich", "", "[1] vote", null, "Abstained", null), view.Rows[2]);
    }
}
