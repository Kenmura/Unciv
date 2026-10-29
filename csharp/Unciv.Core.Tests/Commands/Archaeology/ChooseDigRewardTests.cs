using Unciv.Commands;
using Unciv.Commands.Archaeology;
using Unciv.Commands.Events;
using Unciv.Core.Tests.Logic.Archaeology;

namespace Unciv.Core.Tests.Commands.Archaeology;

// B010: choose-dig-reward (bnw-spec/SPEC.md 4.4.3).
[Collection("RulesetCache")]
public sealed class ChooseDigRewardTests
{
    private readonly ArchaeologySamples.Sample s = new();
    private Unciv.Core.Tests.Logic.GreatWorks.GreatWorkWorld W => s.World.W;

    [Fact]
    public void TakingTheArtifactFillsTheSlotWithBothPlayersAndClearsTheSite()
    {
        var result = W.Ok(ArchaeologySamples.Command("choose-dig-reward", s));
        var resolved = Assert.Single(result.Events.OfType<DigResolved>());
        Assert.Equal(("FindAncient", true), (resolved.Work, resolved.TookArtifact));
        Assert.Equal("FindAncient", W.CapA.GetGreatWorkIn(s.World.Gallery.Name, 0));
        var instance = W.Game.GameInfo.GetGreatWork("FindAncient")!;
        Assert.Equal(("P1", "P2", "Ancient era"), (instance.Creator, instance.Creator2, instance.Era));
        Assert.Null(s.Site.Improvement);
        Assert.Null(s.Site.Resource);
        Assert.Null(s.Site.ArchaeologyRecord);
        Assert.Null(W.Game.GameInfo.PendingDigs);
        Assert.Equal(2f, W.CapA.GetGreatWorkStats().Culture);
    }

    [Fact]
    public void ChoosingTheLandmarkBuildsItAndKeepsTheRecord()
    {
        var result = W.Ok(new ChooseDigReward { Site = s.Site.Position });
        Assert.False(Assert.Single(result.Events.OfType<DigResolved>()).TookArtifact);
        Assert.Equal(s.World.Landmark.Name, s.Site.Improvement);
        Assert.Null(s.Site.Resource);
        Assert.NotNull(s.Site.ArchaeologyRecord);
        Assert.Null(W.Game.GameInfo.PendingDigs);
        Assert.Null(W.CapA.GetGreatWorkIn(s.World.Gallery.Name, 0));
    }

    [Fact]
    public void RefusedWithoutAFinishedDigOrFreeSlotOrWork()
    {
        var take = new ChooseDigReward { Site = s.Site.Position, TakeArtifact = true };
        W.Rejected(take, CommandErrors.NotYourTurn, W.B.CivId);
        W.Rejected(new ChooseDigReward { Site = new Unciv.Logic.Map.HexCoord(9, 9), TakeArtifact = true }, CommandErrors.InvalidTarget);
        W.Rejected(new ChooseDigReward { Site = new Unciv.Logic.Map.HexCoord(500, 500) }, CommandErrors.UnknownTile);

        W.CapA.SetGreatWorkIn(s.World.Gallery.Name, 0, "ArtAncient");
        W.CapA.SetGreatWorkIn(s.World.Gallery.Name, 1, "ArtClassical"); // no free slot
        W.Rejected(take, CommandErrors.NotAllowed);
        W.CapA.SetGreatWorkIn(s.World.Gallery.Name, 1, null);
        foreach (var name in new[] { "FindAncient", "FindOther" })
            (W.Game.GameInfo.GreatWorkInstances ??= []).Add(new Unciv.Logic.GreatWorks.GreatWorkInstance { Name = name }); // nothing left to find
        W.Rejected(take, CommandErrors.NotAllowed);
        W.Ok(new ChooseDigReward { Site = s.Site.Position }); // the Landmark is still possible
        W.Rejected(new ChooseDigReward { Site = s.Site.Position }, CommandErrors.InvalidTarget); // and only once
    }

    [Fact]
    public void RefusedWhenArchaeologyIsOff()
    {
        var off = new ArchaeologyWorld(enabled: false);
        off.W.Rejected(new ChooseDigReward { Site = off.W.Game.GetTile(0, 8).Position }, CommandErrors.NotAllowed);
    }

    [Fact]
    public void ADigThatIsNoLongerADigCannotBeResolved()
    {
        s.Site.RemoveImprovement();
        W.Rejected(ArchaeologySamples.Command("choose-dig-reward", s), CommandErrors.InvalidTarget);
    }
}
