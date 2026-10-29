using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;

namespace Unciv.Core.Tests.Models.Ruleset;

/// <summary>B027 (bnw-spec 4.2.1): the generated Brave New World ruleset carries the Great Works, the slot buildings, their theming rows and
/// the Parthenon frieze.</summary>
[Collection("RulesetCache")]
public sealed class BnwGreatWorksDataTests
{
    private static readonly (string Building, GreatWorkSlotType Type, int Slots)[] SlotBuildings =
    [
        ("Palace", GreatWorkSlotType.ArtArtifact, 1), ("Cathedral", GreatWorkSlotType.ArtArtifact, 1), ("Museum", GreatWorkSlotType.ArtArtifact, 2),
        ("Hermitage", GreatWorkSlotType.ArtArtifact, 3), ("Sistine Chapel", GreatWorkSlotType.ArtArtifact, 2),
        ("The Louvre", GreatWorkSlotType.ArtArtifact, 4), ("Uffizi", GreatWorkSlotType.ArtArtifact, 3), ("Parthenon", GreatWorkSlotType.ArtArtifact, 1),
        ("Heroic Epic", GreatWorkSlotType.Writing, 1), ("National Epic", GreatWorkSlotType.Writing, 1), ("Amphitheater", GreatWorkSlotType.Writing, 1),
        ("Royal Library", GreatWorkSlotType.Writing, 1), ("The Great Library", GreatWorkSlotType.Writing, 2),
        ("Oxford University", GreatWorkSlotType.Writing, 2), ("Globe Theatre", GreatWorkSlotType.Writing, 2),
        ("Opera House", GreatWorkSlotType.Music, 1), ("Broadcast Tower", GreatWorkSlotType.Music, 1), ("Ceilidh Hall", GreatWorkSlotType.Music, 1),
        ("Sydney Opera House", GreatWorkSlotType.Music, 2), ("Broadway", GreatWorkSlotType.Music, 3),
    ];

    [Fact]
    public void TheWorksHaveTheDocumentedClassCounts()
    {
        var works = BnwRulesetTests.LoadBnw().GreatWorks.Values.ToList();
        Assert.Equal(279, works.Count);
        Assert.Equal(94, works.Count(it => it.GreatWorkClass == GreatWorkClass.Art));
        Assert.Equal(30, works.Count(it => it.GreatWorkClass == GreatWorkClass.Artifact));
        Assert.Equal(85, works.Count(it => it.GreatWorkClass == GreatWorkClass.Writing));
        Assert.Equal(70, works.Count(it => it.GreatWorkClass == GreatWorkClass.Music));
        Assert.Equal(15, works.Count(it => it.ArchaeologyOnly));
        Assert.All(works.Where(it => it.Era.Length != 0), it => Assert.Contains(it.Era, BnwRulesetTests.LoadBnw().Eras.Keys));
        Assert.All(works.Where(it => it.GreatWorkClass == GreatWorkClass.Artifact), it => Assert.NotEqual("", it.ArtifactClass));
    }

    [Fact]
    public void NamedGreatPersonWorksPointAtTheirPerson()
    {
        var works = BnwRulesetTests.LoadBnw().GreatWorks;
        Assert.Equal(233, works.Values.Count(it => it.GreatPerson.Length != 0));
        Assert.Equal("Homer", works["Odyssey"].GreatPerson);
        Assert.Equal(GreatWorkClass.Writing, works["Odyssey"].GreatWorkClass);
    }

    [Fact]
    public void SlotBuildingsHaveTheirSlots()
    {
        var buildings = BnwRulesetTests.LoadBnw().Buildings;
        foreach (var (name, type, slots) in SlotBuildings)
        {
            Assert.Equal(type, buildings[name].GreatWorkSlotType);
            Assert.Equal(slots, buildings[name].GreatWorkSlots);
        }
        Assert.Equal(SlotBuildings.Length, buildings.Values.Count(it => it.GreatWorkSlots > 0));
    }

    [Fact]
    public void ThemingRowsComeInDataOrder()
    {
        var buildings = BnwRulesetTests.LoadBnw().Buildings;
        var museum = buildings["Museum"].ThemingBonuses;
        Assert.Equal(12, museum.Count);
        Assert.Equal(4, museum.Count(it => it.Bonus == 2));
        Assert.True(museum[0].SameEra && museum[0].MustBeArt && museum[0].RequiresOwner && museum[0].Bonus == 2);
        Assert.True(museum[1].RequiresAnyButOwner);
        Assert.Equal(1, museum[11].Bonus);
        Assert.True(museum[11].MustBeArtifact && museum[11].RequiresAnyButOwner && !museum[11].SameEra);
        var louvre = Assert.Single(buildings["The Louvre"].ThemingBonuses);
        Assert.True(louvre.Bonus == 4 && louvre.UniqueEras && louvre.MustBeEqualArtArtifact && louvre.RequiresUniquePlayers);
        var hermitage = Assert.Single(buildings["Hermitage"].ThemingBonuses);
        Assert.True(hermitage.Bonus == 3 && hermitage.UniqueEras && hermitage.MustBeArt && hermitage.RequiresUniquePlayers);
        Assert.Equal(21, buildings.Values.Sum(it => it.ThemingBonuses.Count));
    }

    [Fact]
    public void SlotBuildingsThatSurviveCaptureAreNotDestroyedWithTheCity()
    {
        var buildings = BnwRulesetTests.LoadBnw().Buildings;
        foreach (var name in new[] { "Opera House", "Museum", "Broadcast Tower", "Amphitheater", "Cathedral", "Ceilidh Hall" })
        {
            Assert.DoesNotContain("Destroyed when the city is captured", buildings[name].Uniques);
            Assert.Contains("Never destroyed when the city is captured", buildings[name].Uniques);
        }
    }

    [Fact]
    public void TheParthenonCreatesItsFrieze()
    {
        var r = BnwRulesetTests.LoadBnw();
        var unique = Assert.Single(((IHasUniques)r.Buildings["Parthenon"]).GetMatchingUniques(UniqueType.ContainsGreatWorkWhenBuilt));
        Assert.Equal("Parthenon Frieze", unique.Params[0]);
        Assert.Equal(GreatWorkClass.Art, r.GreatWorks["Parthenon Frieze"].GreatWorkClass);
    }

    // B011 (bnw-spec 4.5)

    [Fact]
    public void TheCultureSpecialistsGiveThreeCultureAndThreePointsOfTheirGreatPerson()
    {
        var r = BnwRulesetTests.LoadBnw();
        foreach (var (specialist, person) in new[] { ("Writer", "Great Writer"), ("Artist", "Great Artist"), ("Musician", "Great Musician") })
        {
            Assert.Equal(3f, r.Specialists[specialist].Culture);
            Assert.Equal(3, r.Specialists[specialist].GreatPersonPoints[person]);
        }
    }

    [Fact]
    public void TheGuildsHoldTwoSpecialistsEachAndAddGreatPersonPoints()
    {
        var b = BnwRulesetTests.LoadBnw().Buildings;
        foreach (var (guild, specialist, points) in new[] { ("Writers' Guild", "Writer", 1), ("Artists' Guild", "Artist", 2), ("Musicians' Guild", "Musician", 3) })
        {
            Assert.Equal(2, b[guild].SpecialistSlots[specialist]);
            Assert.Equal(points, b[guild].GreatPersonPoints["Great " + specialist]);
            Assert.True(b[guild].IsNationalWonder);
        }
    }

    [Fact]
    public void TheArtistWriterAndMusicianHaveTheirActions()
    {
        var u = BnwRulesetTests.LoadBnw().Units;
        Assert.Contains("Great Work counts as [8] turns of culture", u["Great Writer"].Uniques);
        Assert.Contains("Gives [10] Tourism with the target civ and [20]% with other civs when expended", u["Great Musician"].Uniques);
        Assert.Contains("Empire enters a [8]-turn Golden Age <by consuming this unit>", u["Great Artist"].Uniques);
    }

    [Fact]
    public void EveryArtistWriterAndMusicianNameIsThePersonOfExactlyOneWork()
    {
        var r = BnwRulesetTests.LoadBnw();
        foreach (var (group, cls, count) in new[] { ("Artist", GreatWorkClass.Art, 93), ("Writer", GreatWorkClass.Writing, 70), ("Musician", GreatWorkClass.Music, 70) })
        {
            var names = r.UnitNameGroups[group].UnitNames;
            Assert.Equal(count, names.Count);
            foreach (string name in names)
                Assert.Single(r.GreatWorks.Values, it => it.GreatWorkClass == cls && !it.ArchaeologyOnly && it.GreatPerson == name);
        }
    }

    [Fact]
    public void ToTheGloryOfGodAllowsAllGreatPeopleFromTheIndustrialEra()
    {
        var r = BnwRulesetTests.LoadBnw();
        var unique = Assert.Single(r.Beliefs["To the Glory of God"].Uniques);
        Assert.Equal("Can purchase all Great People with Faith <starting from the [Industrial era]>", unique);
        Assert.Equal(3, ((IHasUniques)r.GlobalUniques).GetMatchingUniques(UniqueType.BuyUnitsIncreasingCost, GameContext.IgnoreConditionalsState)
            .Count(it => it.Params[2] == "Faith" && r.Units[it.Params[0]].IsGreatPerson));
    }
}
