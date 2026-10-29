using Unciv.Assets;
using Unciv.Queries.Empire;

namespace Unciv.Core.Tests.Assets;

public class EspionageModelTests
{
    private static SpyDestination Dest(string id, string name, string civ = "") => new(id, name, civ, false);

    private static SpyEntry Spy(string name, string location = "Spy Hideout", string action = "Moving", int rank = 1, bool alive = true, bool hideout = false,
        IReadOnlyList<SpyDestination>? destinations = null, bool coup = false, float? chance = null) =>
        new(name, rank, action, location, alive, hideout, destinations ?? [], coup, chance);

    [Fact]
    public void NoSpiesIsSaidInsteadOfAnEmptyTable()
    {
        var spec = EspionageModel.Build(new SpyDestinationsResult([]));
        Assert.Equal("You have no spies.", spec.Empty);
        Assert.Empty(spec.Spies);
        Assert.False(spec.CanAssign);
        Assert.Equal(["Spy Hideout"], spec.Locations.Select(it => it.Name));
    }

    [Fact]
    public void SpyRowsShowRankLocationActionAndDeadSpiesHaveNoPlace()
    {
        var spec = EspionageModel.Build(new SpyDestinationsResult(
        [
            Spy("Ann", "Ur", "Spying", 3, destinations: [Dest("c1", "Kish")]),
            Spy("Bob", "Spy Hideout", "Moving", 2, alive: false, hideout: true, destinations: [Dest("c1", "Kish")]),
        ]));
        Assert.Equal("", spec.Empty);
        Assert.Equal(("Ann", "3", "Ur", "Spying", true, true), (spec.Spies[0].Name, spec.Spies[0].Rank, spec.Spies[0].Location, spec.Spies[0].Action, spec.Spies[0].Alive, spec.Spies[0].CanMove));
        Assert.Equal(("Bob", "2", "-", "Dead", false, false), (spec.Spies[1].Name, spec.Spies[1].Rank, spec.Spies[1].Location, spec.Spies[1].Action, spec.Spies[1].Alive, spec.Spies[1].CanMove));
    }

    [Fact]
    public void ASpyCanMoveWhenTheEngineOffersTheHideoutOrACityButNotOtherwise()
    {
        var spec = EspionageModel.Build(new SpyDestinationsResult(
        [
            Spy("OnlyHideout", "Ur", hideout: true),
            Spy("OnlyCity", destinations: [Dest("c1", "Kish")]),
            Spy("Stuck"),
        ]));
        Assert.Equal([true, true, false], spec.Spies.Select(it => it.CanMove));
        Assert.True(spec.CanAssign);
        Assert.False(EspionageModel.Build(new SpyDestinationsResult([Spy("Stuck")])).CanAssign);
        Assert.False(EspionageModel.Build(new SpyDestinationsResult([Spy("Dead", alive: false, hideout: true, destinations: [Dest("c1", "Kish")])])).CanAssign);
    }

    [Fact]
    public void CoupButtonWordsTheOddsStagesOrCallsOffAndIsAbsentWhenTheEngineForbidsIt()
    {
        var spec = EspionageModel.Build(new SpyDestinationsResult(
        [
            Spy("Idle", "Sparta", "Surveillance", coup: true, chance: 0.455f),
            Spy("Staging", "Sparta", "Coup", coup: true, chance: 0.6f),
            Spy("No", "Sparta", "Surveillance"),
            Spy("NoOdds", "Sparta", "Surveillance", coup: true, chance: null),
            Spy("OddsButForbidden", "Sparta", "Surveillance", coup: false, chance: 0.5f),
        ]));
        Assert.Equal("Stage a coup (46% chance)", spec.Spies[0].CoupLabel);
        Assert.Equal("Coup 46%", spec.Spies[0].CoupShort);
        Assert.Equal(("Idle", true), (spec.Spies[0].Coup!.Spy, spec.Spies[0].Coup!.Stage));
        Assert.Equal("Call off the coup", spec.Spies[1].CoupLabel);
        Assert.Equal("Call off", spec.Spies[1].CoupShort);
        Assert.Equal(("Staging", false), (spec.Spies[1].Coup!.Spy, spec.Spies[1].Coup!.Stage));
        Assert.Null(spec.Spies[2].Coup);
        Assert.Equal("", spec.Spies[2].CoupLabel);
        Assert.Equal("", spec.Spies[3].CoupLabel); // the engine allows it but gave no odds: still a button, with no wording
        Assert.NotNull(spec.Spies[3].Coup);
        Assert.Equal("", spec.Spies[4].CoupLabel); // odds without the engine's permission are not worded
        Assert.Null(spec.Spies[4].Coup);
    }

    [Fact]
    public void LocationsAreTheHideoutThenOfferedCitiesByNameThenCitiesOnlyOccupied()
    {
        var spec = EspionageModel.Build(new SpyDestinationsResult(
        [
            Spy("Ann", "Zeta", "Spying", destinations: [Dest("c2", "Ur", "Babylon"), Dest("c1", "Kish")]),
            Spy("Bob", "Spy Hideout", destinations: [Dest("c1", "Kish"), Dest("c3", "Ur", "Rome")]),
            Spy("Cy", "Zeta", "Spying"),
            Spy("Dee", "Alpha", "Spying"),
            Spy("Eve", "Zeta", "Dead", alive: false, destinations: [Dest("c9", "Aaa")]),
        ]));
        Assert.Equal(["Spy Hideout", "Kish", "Ur", "Ur", "Alpha", "Zeta"], spec.Locations.Select(it => it.Name));
        Assert.Equal([null, "c1", "c2", "c3", null, null], spec.Locations.Select(it => it.CityId));
        Assert.Equal(["", "", "Babylon", "Rome", "", ""], spec.Locations.Select(it => it.Civ));
        Assert.Equal([true, false, false, false, false, false], spec.Locations.Select(it => it.IsHideout));
        // Only the viewer's own living spies are named; a dead one is not "present"
        Assert.Equal(["Bob", "", "", "", "Dee", "Ann, Cy"], spec.Locations.Select(it => it.SpiesPresent));
    }

    [Fact]
    public void LocationsSortByNameFirstThenByIdNotByIdAlone()
    {
        var spec = EspionageModel.Build(new SpyDestinationsResult([Spy("Ann", destinations: [Dest("z1", "Alpha"), Dest("b2", "Beta"), Dest("y1", "Alpha")])]));
        Assert.Equal(["Alpha", "Alpha", "Beta"], spec.Locations.Skip(1).Select(it => it.Name));
        Assert.Equal(["y1", "z1", "b2"], spec.Locations.Skip(1).Select(it => it.CityId));
    }

    [Fact]
    public void AnOfferedCityIsNotListedAgainWhereASpyAlreadySits()
    {
        var spec = EspionageModel.Build(new SpyDestinationsResult([Spy("Ann", "Kish", "Spying"), Spy("Bob", destinations: [Dest("c1", "Kish")])]));
        Assert.Equal(["Spy Hideout", "Kish"], spec.Locations.Select(it => it.Name));
        Assert.Equal("c1", spec.Locations[1].CityId);
        Assert.Equal("Ann", spec.Locations[1].SpiesPresent);
    }

    [Fact]
    public void TargetsAreTheHideoutThenTheEnginesDestinationsAndNothingForDeadOrUnknownSpies()
    {
        var result = new SpyDestinationsResult(
        [
            Spy("Ann", "Ur", hideout: true, destinations: [Dest("c2", "Kish"), Dest("c1", "Ur")]),
            Spy("Bob", destinations: [Dest("c1", "Ur")]),
            Spy("Cy", alive: false, hideout: true, destinations: [Dest("c1", "Ur")]),
        ]);
        var ann = EspionageModel.Targets(result, "Ann");
        Assert.Equal(["Hideout", "Kish", "Ur"], ann.Select(it => it.Label));
        Assert.Equal([null, "c2", "c1"], ann.Select(it => it.Command.CityId));
        Assert.All(ann, it => Assert.Equal("Ann", it.Command.Spy));
        Assert.Equal(["Ur"], EspionageModel.Targets(result, "Bob").Select(it => it.Label));
        Assert.Empty(EspionageModel.Targets(result, "Cy"));
        Assert.Empty(EspionageModel.Targets(result, "Nobody"));
    }

    [Fact]
    public void TargetForMatchesTheHideoutRowOnlyToTheHideoutAndACityRowByItsId()
    {
        var result = new SpyDestinationsResult([Spy("Ann", hideout: true, destinations: [Dest("c1", "Kish"), Dest("c2", "Ur")]), Spy("Bob", destinations: [Dest("c1", "Kish")])]);
        var spec = EspionageModel.Build(result);
        var ann = EspionageModel.Targets(result, "Ann");
        var bob = EspionageModel.Targets(result, "Bob");
        var hideout = spec.Locations.Single(it => it.IsHideout);
        var kish = spec.Locations.Single(it => it.CityId == "c1");
        var ur = spec.Locations.Single(it => it.CityId == "c2");
        Assert.Null(EspionageModel.TargetFor(ann, hideout)!.Command.CityId);
        Assert.Equal("c1", EspionageModel.TargetFor(ann, kish)!.Command.CityId);
        Assert.Equal("c2", EspionageModel.TargetFor(ann, ur)!.Command.CityId);
        Assert.Null(EspionageModel.TargetFor(bob, hideout)); // Bob has no hideout move, and a city target never matches the hideout row
        Assert.Null(EspionageModel.TargetFor(bob, ur));
        // A city only occupied (no id) matches nothing, not even the hideout target
        Assert.Null(EspionageModel.TargetFor(ann, new EspionageLocationRow(null, "Zeta", "", "")));
    }
}
