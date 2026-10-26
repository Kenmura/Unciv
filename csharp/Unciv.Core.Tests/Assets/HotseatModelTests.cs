using Unciv.Assets;

namespace Unciv.Core.Tests.Assets;

public sealed class HotseatModelTests
{
    [Fact]
    public void HandOver_NamesTheNextPlayerByDisplayName_AndKeepsTheCivId()
    {
        var view = HotseatModel.HandOver("Greece", id => id.ToUpperInvariant());
        Assert.Equal("Greece", view.CivId);
        Assert.Equal("Pass the device to GREECE", view.Heading);
        Assert.Equal(HotseatModel.Message, view.Message);
        Assert.Equal("Ready", view.ButtonText);
    }

    [Fact]
    public void HandOver_MentionsOnlyTheNextPlayer()
    {
        var names = new List<string>();
        var view = HotseatModel.HandOver("Greece", id =>
        {
            names.Add(id);
            return id;
        });
        Assert.Equal(["Greece"], names);
        Assert.DoesNotContain("Babylon", view.Heading + view.Message + view.ButtonText, StringComparison.Ordinal);
    }

    [Fact]
    public void FailureText_IncludesTheReason() => Assert.Equal("Could not start the turn: busy", HotseatModel.FailureText("busy"));
}
