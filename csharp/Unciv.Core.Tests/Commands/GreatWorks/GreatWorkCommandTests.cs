using Unciv.Commands;
using Unciv.Commands.Events;
using Unciv.Commands.GreatWorks;
using Unciv.Core.Tests.Logic.GreatWorks;
using Unciv.Logic.Civilization;
using Unciv.Logic.GreatWorks;
using Unciv.Models.Ruleset;

namespace Unciv.Core.Tests.Commands.GreatWorks;

// B007: the four Great Work commands (bnw-spec/SPEC.md 4.2.3).
[Collection("RulesetCache")]
public sealed class GreatWorkCommandTests
{
    private readonly GreatWorkSamples.Sample s = new();
    private GreatWorkWorld W => s.World;

    private static T Single<T>(CommandResult result) where T : EngineEvent => Assert.Single(result.Events.OfType<T>());

    // --- create

    [Fact]
    public void CreatingConsumesTheArtistAndFillsTheClosestSlot()
    {
        var result = W.Ok(GreatWorkSamples.Command("create-great-work", s));
        var created = Single<GreatWorkCreated>(result);
        Assert.Equal(("ArtAnyEra", "Art", W.CapA.Id, s.Gallery.Name, 1), (created.Work, created.GreatWorkClass, created.CityId, created.Building, created.Slot));
        Assert.Equal(W.A.CivId, created.CivId);
        Assert.Equal("ArtAnyEra", W.CapA.GetGreatWorkIn(s.Gallery.Name, 1)); // the era-less work is preferred: it is not later than the civ's era
        Assert.DoesNotContain(W.A.Units.GetCivUnits(), it => it.Id == s.ArtistId);
        var instance = W.Game.GameInfo.GetGreatWork("ArtAnyEra")!;
        Assert.Equal((W.A.CivId, W.A.GetEra().Name), (instance.Creator, instance.Era));
        Assert.Equal(2 * 2 + 0f, W.CapA.GetGreatWorkStats().Culture);
    }

    [Fact]
    public void CreatingIsRefusedWithoutAFittingSituation()
    {
        var create = new CreateGreatWork { UnitId = s.ArtistId };
        W.Rejected(create, CommandErrors.NotYourTurn, W.B.CivId);
        W.Rejected(new CreateGreatWork { UnitId = 99999 }, CommandErrors.UnknownUnit);

        int warrior = W.Game.AddUnit("Warrior", W.A, W.CapA.GetCenterTile()).Id;
        W.Rejected(new CreateGreatWork { UnitId = warrior }, CommandErrors.NotAllowed);

        int outside = W.Game.AddUnit("Great Artist", W.A, W.CapA.GetCenterTile().GetTilesAtDistance(1).First(it => it.IsLand && it.MilitaryUnit == null && it.CivilianUnit == null)).Id;
        W.Rejected(new CreateGreatWork { UnitId = outside }, CommandErrors.NotAllowed); // not inside a city

        int theirs = W.Game.AddUnit("Great Artist", W.B, W.CityB.GetCenterTile().GetTilesAtDistance(1).First(it => it.IsLand && it.MilitaryUnit == null && it.CivilianUnit == null)).Id;
        W.Rejected(new CreateGreatWork { UnitId = theirs }, CommandErrors.NotYourUnit);

        W.CapA.SetGreatWorkIn(s.Gallery.Name, 1, "ArtClassical"); // both slots full: no room
        W.Rejected(create, CommandErrors.NotAllowed);
    }

    [Fact]
    public void CreatingIsRefusedWhenEveryWorkOfTheClassExistsOrTheGateIsOff()
    {
        foreach (string name in new[] { "ArtClassical", "ArtMedieval", "ArtAnyEra" })
            (W.Game.GameInfo.GreatWorkInstances ??= []).Add(new GreatWorkInstance { Name = name });
        W.Rejected(new CreateGreatWork { UnitId = s.ArtistId }, CommandErrors.NotAllowed);

        var off = new GreatWorkWorld(enabled: false);
        int artist = off.Game.AddUnit("Great Artist", off.A, off.CapA.GetCenterTile()).Id;
        off.Rejected(new CreateGreatWork { UnitId = artist }, CommandErrors.NotAllowed);
    }

    // --- move

    [Fact]
    public void MovingIntoAnEmptySlotAndSwappingWithAFilledOne()
    {
        var result = W.Ok(GreatWorkSamples.Command("move-great-work", s));
        var moved = Single<GreatWorkMoved>(result);
        Assert.Equal(("ArtAncient", 0, 1), (moved.Work, moved.FromSlot, moved.ToSlot));
        Assert.Null(W.CapA.GetGreatWorkIn(s.Gallery.Name, 0));
        Assert.Equal("ArtAncient", W.CapA.GetGreatWorkIn(s.Gallery.Name, 1));
        Assert.Equal(1, W.CapA.GetNumberOfGreatWorks());

        W.Place(W.CapA, s.Gallery, 0, "ArtClassical", W.A.CivId); // both slots filled: moving exchanges them
        var back = new MoveGreatWork
        {
            FromCityId = W.CapA.Id,
            FromBuilding = s.Gallery.Name,
            FromSlot = 1,
            ToCityId = W.CapA.Id,
            ToBuilding = s.Gallery.Name,
            ToSlot = 0,
        };
        W.Ok(back);
        Assert.Equal("ArtAncient", W.CapA.GetGreatWorkIn(s.Gallery.Name, 0));
        Assert.Equal("ArtClassical", W.CapA.GetGreatWorkIn(s.Gallery.Name, 1));
    }

    [Fact]
    public void MovingBetweenCitiesAndBuildings()
    {
        var other = W.Build(W.CityA2, 1, GreatWorkSlotType.ArtArtifact);
        var writing = W.Build(W.CityA2, 1, GreatWorkSlotType.Writing);
        W.Ok(new MoveGreatWork
        {
            FromCityId = W.CapA.Id,
            FromBuilding = s.Gallery.Name,
            FromSlot = 0,
            ToCityId = W.CityA2.Id,
            ToBuilding = other.Name,
            ToSlot = 0,
        });
        Assert.Equal("ArtAncient", W.CityA2.GetGreatWorkIn(other.Name, 0));
        Assert.Equal(0, W.CapA.GetNumberOfGreatWorks());
        W.Rejected(new MoveGreatWork
        {
            FromCityId = W.CityA2.Id,
            FromBuilding = other.Name,
            FromSlot = 0,
            ToCityId = W.CityA2.Id,
            ToBuilding = writing.Name,
            ToSlot = 0,
        }, CommandErrors.NotAllowed); // an art work does not fit a writing slot
    }

    [Fact]
    public void MovingIsRefusedForBadSlots()
    {
        MoveGreatWork Move(string fromCity, string fromBuilding, int fromSlot, string toCity, string toBuilding, int toSlot) => new()
        {
            FromCityId = fromCity,
            FromBuilding = fromBuilding,
            FromSlot = fromSlot,
            ToCityId = toCity,
            ToBuilding = toBuilding,
            ToSlot = toSlot,
        };
        string cap = W.CapA.Id, gallery = s.Gallery.Name;
        W.Rejected(Move(cap, gallery, 0, cap, gallery, 1), CommandErrors.NotYourTurn, W.B.CivId);
        W.Rejected(Move(cap, gallery, 0, cap, gallery, 0), CommandErrors.InvalidTarget); // same slot
        W.Rejected(Move(cap, gallery, 1, cap, gallery, 0), CommandErrors.InvalidTarget); // empty source
        W.Rejected(Move(cap, gallery, 0, cap, gallery, 2), CommandErrors.InvalidTarget); // no such slot
        W.Rejected(Move(cap, gallery, 0, cap, gallery, -1), CommandErrors.InvalidTarget);
        W.Rejected(Move(cap, gallery, 0, cap, "Nowhere", 0), CommandErrors.InvalidTarget);
        W.Rejected(Move(cap, gallery, 0, W.CityB.Id, s.GalleryB.Name, 0), CommandErrors.NotYourCity);
        W.Rejected(Move(cap, gallery, 0, "no-such-city", gallery, 1), CommandErrors.UnknownCity);
        var unbuilt = W.MakeBuilding(1, GreatWorkSlotType.ArtArtifact);
        W.Rejected(Move(cap, gallery, 0, cap, unbuilt.Name, 0), CommandErrors.InvalidTarget); // not built in that city

        var off = new GreatWorkWorld(enabled: false);
        var gate = off.Build(off.CapA, 2, GreatWorkSlotType.ArtArtifact);
        off.Place(off.CapA, gate, 0, "ArtAncient");
        off.Rejected(Move(off.CapA.Id, gate.Name, 0, off.CapA.Id, gate.Name, 1), CommandErrors.NotAllowed);
    }

    // --- offer

    [Fact]
    public void OfferingAddsReplacesAndWithdraws()
    {
        W.Ok(GreatWorkSamples.Command("offer-great-work-swap", s));
        Assert.Contains(W.Game.GameInfo.GreatWorkSwapOffers!, it => it is { Civ: var c, Work: "ArtAncient" } && c == W.A.CivId);

        W.Rejected(GreatWorkSamples.Command("offer-great-work-swap", s), CommandErrors.NotAllowed); // already on offer

        W.Place(W.CapA, s.Gallery, 1, "ArtClassical", W.A.CivId);
        W.Ok(new OfferGreatWorkSwap { CityId = W.CapA.Id, Building = s.Gallery.Name, Slot = 1 }); // same class: replaces the earlier offer
        var mine = W.Game.GameInfo.GreatWorkSwapOffers!.Where(it => it.Civ == W.A.CivId).ToList();
        Assert.Equal("ArtClassical", Assert.Single(mine).Work);

        W.Rejected(new OfferGreatWorkSwap { CityId = W.CapA.Id, Building = s.Gallery.Name, Slot = 0, Withdraw = true }, CommandErrors.InvalidTarget);
        W.Ok(new OfferGreatWorkSwap { CityId = W.CapA.Id, Building = s.Gallery.Name, Slot = 1, Withdraw = true });
        Assert.DoesNotContain(W.Game.GameInfo.GreatWorkSwapOffers!, it => it.Civ == W.A.CivId);
    }

    [Fact]
    public void OfferingIsRefusedForMusicEmptySlotsAndStrangers()
    {
        var hall = W.Build(W.CityA2, 1, GreatWorkSlotType.Music);
        W.Place(W.CityA2, hall, 0, "MusicAncient", W.A.CivId);
        W.Rejected(new OfferGreatWorkSwap { CityId = W.CityA2.Id, Building = hall.Name, Slot = 0 }, CommandErrors.NotAllowed);
        W.Rejected(new OfferGreatWorkSwap { CityId = W.CapA.Id, Building = s.Gallery.Name, Slot = 1 }, CommandErrors.InvalidTarget);
        W.Rejected(new OfferGreatWorkSwap { CityId = W.CityB.Id, Building = s.GalleryB.Name, Slot = 0 }, CommandErrors.NotYourCity);
        W.Rejected(GreatWorkSamples.Command("offer-great-work-swap", s), CommandErrors.NotYourTurn, W.B.CivId);
    }

    // --- take

    [Fact]
    public void TakingAnOfferExchangesTheWorksAndUsesTheOfferUp()
    {
        W.CapA.CityStats.Update();
        var result = W.Ok(GreatWorkSamples.Command("take-great-work-swap", s));
        var swapped = Single<GreatWorkSwapped>(result);
        Assert.Equal((W.A.CivId, W.B.CivId, "ArtAncient", "ArtClassical"), (swapped.CivId, swapped.WithCivId, swapped.Given, swapped.Taken));
        Assert.Equal("ArtClassical", W.CapA.GetGreatWorkIn(s.Gallery.Name, 0));
        Assert.Equal("ArtAncient", W.CityB.GetGreatWorkIn(s.GalleryB.Name, 0));
        Assert.Null(W.Game.GameInfo.GreatWorkSwapOffers);
        Assert.Equal([W.A.CivId, W.B.CivId], swapped.Participants());
        Assert.Equal(2f, W.CityB.GetGreatWorkStats().Culture);
    }

    [Fact]
    public void TakingAnOfferTellsTheOfferer()
    {
        W.B.PlayerType = PlayerType.Human;
        W.Ok(GreatWorkSamples.Command("take-great-work-swap", s));
        var note = W.B.Notifications.Last();
        Assert.Contains("ArtClassical", note.Text);
        Assert.Contains("ArtAncient", note.Text);
        Assert.Contains(W.A.CivId, note.Text);
    }

    [Fact]
    public void TakingIsRefusedWhenTheOfferOrTheTradeIsInvalid()
    {
        var take = GreatWorkSamples.Command("take-great-work-swap", s);
        W.Rejected(take, CommandErrors.NotYourTurn, W.B.CivId);
        W.Rejected(new TakeGreatWorkSwap { CivId = W.A.CivId, Work = "ArtAncient", CityId = W.CapA.Id, Building = s.Gallery.Name, Slot = 0 }, CommandErrors.InvalidTarget); // self
        W.Rejected(new TakeGreatWorkSwap { CivId = "nobody", Work = "ArtClassical", CityId = W.CapA.Id, Building = s.Gallery.Name, Slot = 0 }, CommandErrors.UnknownCiv);
        W.Rejected(new TakeGreatWorkSwap { CivId = W.B.CivId, Work = "ArtMedieval", CityId = W.CapA.Id, Building = s.Gallery.Name, Slot = 0 }, CommandErrors.InvalidTarget); // not on offer
        W.Rejected(new TakeGreatWorkSwap { CivId = W.B.CivId, Work = "ArtClassical", CityId = W.CapA.Id, Building = s.Gallery.Name, Slot = 1 }, CommandErrors.InvalidTarget); // empty slot
        W.Rejected(new TakeGreatWorkSwap { CivId = W.B.CivId, Work = "ArtClassical", CityId = W.CityB.Id, Building = s.GalleryB.Name, Slot = 0 }, CommandErrors.NotYourCity);

        var hall = W.Build(W.CityA2, 1, GreatWorkSlotType.Music);
        W.Place(W.CityA2, hall, 0, "MusicAncient", W.A.CivId);
        W.Rejected(new TakeGreatWorkSwap { CivId = W.B.CivId, Work = "ArtClassical", CityId = W.CityA2.Id, Building = hall.Name, Slot = 0 }, CommandErrors.NotAllowed); // another class

        W.A.GetDiplomacyManager(W.B)!.DiplomaticStatus = Unciv.Logic.Civilization.Diplomacy.DiplomaticStatus.War;
        W.Rejected(take, CommandErrors.NotAllowed); // at war
    }

    [Fact]
    public void TakingRemovesTheTakersOwnOfferForTheWorkItGave()
    {
        W.Ok(GreatWorkSamples.Command("offer-great-work-swap", s)); // A offers ArtAncient
        W.Ok(GreatWorkSamples.Command("take-great-work-swap", s));  // A gives ArtAncient for B's ArtClassical
        Assert.Null(W.Game.GameInfo.GreatWorkSwapOffers);
    }

    [Fact]
    public void TakingIsRefusedBetweenStrangers()
    {
        var take = GreatWorkSamples.Command("take-great-work-swap", s);
        W.A.Diplomacy.Remove(W.B.CivId);
        W.Rejected(take, CommandErrors.NotAllowed);
    }

    [Fact]
    public void AnOfferLapsesWhenTheOffererNoLongerHoldsTheWork()
    {
        W.CityB.SetGreatWorkIn(s.GalleryB.Name, 0, null);
        W.Rejected(GreatWorkSamples.Command("take-great-work-swap", s), CommandErrors.InvalidTarget);
    }

    [Fact]
    public void AcceptedCommandsAreLoggedAndStateStaysConsistentThroughSaveAndLoad()
    {
        W.Ok(GreatWorkSamples.Command("move-great-work", s));
        W.Ok(GreatWorkSamples.Command("create-great-work", s));
        string json = Unciv.Json.UncivJson.Serialize(W.Game.GameInfo);
        var back = Unciv.Json.UncivJson.Deserialize<Unciv.Logic.GameInfo>(json)!;
        Assert.Equal(json, Unciv.Json.UncivJson.Serialize(back));
        Assert.Equal(3, W.Game.GameInfo.GreatWorks.Count); // the sample world already holds two
    }
}
