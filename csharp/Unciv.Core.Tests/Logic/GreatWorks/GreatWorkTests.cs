using Unciv.Json;
using Unciv.Logic;
using Unciv.Logic.GreatWorks;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unit;
using CityObject = Unciv.Logic.City.City;

namespace Unciv.Core.Tests.Logic.GreatWorks;

// B007 (bnw-spec/SPEC.md 4.2): slots, work selection, closest slot, theming, culture, building hooks, serialisation.
[Collection("RulesetCache")]
public sealed class GreatWorkTests
{
    private readonly GreatWorkWorld w = new();

    private static ThemingBonus[] MuseumRows() =>
    [
        new() { Bonus = 4, SameEra = true, MustBeArt = true, RequiresOwner = true },
        new() { Bonus = 3, SameEra = true, MustBeArtifact = true },
        new() { Bonus = 2, UniqueEras = true, MustBeEqualArtArtifact = true },
        new() { Bonus = 1, RequiresAnyButOwner = true },
    ];

    // --- slots

    [Fact]
    public void SlotsComeFromTheBuiltBuildingsOrderedByNameAndStartEmpty()
    {
        var zoo = w.Build(w.CapA, 1, GreatWorkSlotType.Music);
        var gallery = w.Build(w.CapA, 2, GreatWorkSlotType.ArtArtifact);
        w.CapA.CityConstructions.AddBuilding(w.Game.CreateBuilding()); // no slots: not listed
        var slots = w.CapA.GetGreatWorkSlots().ToList();
        Assert.Equal(3, slots.Count);
        Assert.All(slots, it => Assert.Null(it.Work));
        var ordered = new[] { zoo, gallery }.OrderBy(it => it.Name, StringComparer.Ordinal).ToList();
        Assert.Equal([ordered[0].Name, ordered[1].Name, ordered[1].Name], slots.Where(it => true).Select(it => it.Building.Name).Distinct().Count() == 2
            ? slots.Select(it => it.Building.Name) : []);
        Assert.Equal(0, w.CapA.GetNumberOfGreatWorks());
        Assert.Null(w.CapA.GreatWorkSlotData);
    }

    [Fact]
    public void SlotDataStaysCanonical()
    {
        var b1 = w.Build(w.CapA, 3, GreatWorkSlotType.ArtArtifact);
        var b2 = w.Build(w.CapA, 1, GreatWorkSlotType.Writing);
        w.Place(w.CapA, b1, 2, "ArtAncient");
        Assert.Equal([null, null, "ArtAncient"], w.CapA.GreatWorkSlotData![0].Works);
        w.Place(w.CapA, b1, 0, "ArtClassical");
        w.Place(w.CapA, b2, 0, "WriteAncient");
        Assert.Equal(new[] { b1.Name, b2.Name }.Order(StringComparer.Ordinal), w.CapA.GreatWorkSlotData.Select(it => it.Building));
        Assert.Equal(3, w.CapA.GetNumberOfGreatWorks());

        w.CapA.SetGreatWorkIn(b1.Name, 2, null); // trailing empty slots are trimmed
        Assert.Equal(["ArtClassical"], w.CapA.GreatWorkSlotData.Single(it => it.Building == b1.Name).Works);
        w.CapA.SetGreatWorkIn(b1.Name, 0, null); // an empty building disappears
        w.CapA.SetGreatWorkIn(b2.Name, 0, null);
        Assert.Null(w.CapA.GreatWorkSlotData);
        w.CapA.SetGreatWorkIn(b2.Name, 0, null); // clearing nothing changes nothing
        Assert.Null(w.CapA.GreatWorkSlotData);
        Assert.Null(w.CapA.GetGreatWorkIn(b1.Name, 7));
        Assert.Null(w.CapA.GetGreatWorkIn(b1.Name, -1));
    }

    [Fact]
    public void SellingOrLosingABuildingLosesItsWorksButTheyStayUsed()
    {
        var gallery = w.Build(w.CapA, 2, GreatWorkSlotType.ArtArtifact);
        w.Place(w.CapA, gallery, 0, "ArtAncient", w.A.CivId);
        w.CapA.CityConstructions.RemoveBuilding(gallery);
        Assert.Null(w.CapA.GreatWorkSlotData);
        Assert.Empty(w.CapA.GetGreatWorkSlots());
        Assert.True(w.Game.GameInfo.IsGreatWorkUsed("ArtAncient"));
        Assert.Equal(0, w.CapA.GetNumberOfGreatWorks());
    }

    // --- free works (Parthenon)

    [Fact]
    public void ABuildingContainingAWorkReceivesItWhenBuiltAndOnlyOnce()
    {
        var parthenon = w.MakeBuilding(1, GreatWorkSlotType.ArtArtifact);
        parthenon.Uniques.Add("Contains the Great Work [ArtAnyEra] when built");
        w.CapA.CityConstructions.AddBuilding(parthenon);
        Assert.Equal("ArtAnyEra", w.CapA.GetGreatWorkIn(parthenon.Name, 0));
        var instance = w.Game.GameInfo.GetGreatWork("ArtAnyEra")!;
        Assert.Equal((w.A.CivId, w.A.GetEra().Name), (instance.Creator, instance.Era)); // era "" takes the creator's era

        w.CapA.CityConstructions.RemoveBuilding(parthenon);
        w.CapA.CityConstructions.AddBuilding(parthenon); // rebuilt: the work was created once and is gone
        Assert.Null(w.CapA.GetGreatWorkIn(parthenon.Name, 0));
        Assert.Single(w.Game.GameInfo.GreatWorks);
    }

    [Fact]
    public void AFreeWorkNeedsASlotOfItsClassAndTheGate()
    {
        var wrong = w.MakeBuilding(1, GreatWorkSlotType.Music);
        wrong.Uniques.Add("Contains the Great Work [ArtAncient] when built");
        w.CapA.CityConstructions.AddBuilding(wrong);
        Assert.Null(w.CapA.GetGreatWorkIn(wrong.Name, 0));
        Assert.False(w.Game.GameInfo.IsGreatWorkUsed("ArtAncient"));

        var off = new GreatWorkWorld(enabled: false);
        var parthenon = off.MakeBuilding(1, GreatWorkSlotType.ArtArtifact);
        parthenon.Uniques.Add("Contains the Great Work [ArtAncient] when built");
        off.CapA.CityConstructions.AddBuilding(parthenon);
        Assert.Null(off.CapA.GreatWorkSlotData);

        var twice = w.MakeBuilding(1, GreatWorkSlotType.ArtArtifact); // two works, one slot: the first wins
        twice.Uniques.Add("Contains the Great Work [ArtAncient] when built");
        twice.Uniques.Add("Contains the Great Work [ArtClassical] when built");
        w.CapA.CityConstructions.AddBuilding(twice);
        Assert.Equal("ArtAncient", w.CapA.GetGreatWorkIn(twice.Name, 0));
        Assert.False(w.Game.GameInfo.IsGreatWorkUsed("ArtClassical"));

        var unknown = w.MakeBuilding(1, GreatWorkSlotType.ArtArtifact);
        unknown.Uniques.Add("Contains the Great Work [Nonexistent] when built");
        w.CapA.CityConstructions.AddBuilding(unknown);
        Assert.Null(w.CapA.GetGreatWorkIn(unknown.Name, 0));
    }

    // --- work selection

    private static BaseUnit AddGreatPerson(GreatWorkWorld world, string name)
    {
        var unit = new BaseUnit { Name = name, UnitType = world.Game.Ruleset.Units["Great Artist"].UnitType };
        unit.SetRuleset(world.Game.Ruleset);
        world.Game.Ruleset.Units.Put(name, unit);
        return unit;
    }

    [Fact]
    public void AGreatPersonCreatesTheFirstUnusedWorkOfItsClassPreferringItsEra()
    {
        var artist = w.Game.AddUnit("Great Artist", w.A, w.CapA.GetCenterTile());
        Assert.Equal(GreatWorkClass.Art, GreatWorkRules.ClassCreatedBy(artist, w.Game.Ruleset));
        Assert.Equal("ArtAncient", GreatWorkRules.ChooseWork(w.A, artist)!.Name); // table order, Ancient is not later than the civ's era
        w.Game.GameInfo.GreatWorkInstances = [new GreatWorkInstance { Name = "ArtAncient" }];
        Assert.Equal("ArtAnyEra", GreatWorkRules.ChooseWork(w.A, artist)!.Name); // Classical and Medieval are later; "" has no era
        w.Game.GameInfo.GreatWorkInstances.Add(new GreatWorkInstance { Name = "ArtAnyEra" });
        Assert.Equal("ArtClassical", GreatWorkRules.ChooseWork(w.A, artist)!.Name); // nothing earlier left: the first unused
        w.Game.GameInfo.GreatWorkInstances.Add(new GreatWorkInstance { Name = "ArtClassical" });
        Assert.Equal("ArtMedieval", GreatWorkRules.ChooseWork(w.A, artist)!.Name);
        w.Game.GameInfo.GreatWorkInstances.Add(new GreatWorkInstance { Name = "ArtMedieval" });
        Assert.Null(GreatWorkRules.ChooseWork(w.A, artist));
    }

    [Fact]
    public void ALaterEraAllowsLaterWorksAndArtifactsAreNotArtistWork()
    {
        var tech = w.Game.Ruleset.Technologies.Values.First(it => it.Era() == GreatWorkWorld.Medieval);
        w.A.Tech.AddTechnology(tech.Name);
        Assert.True(w.A.GetEraNumber() >= w.Game.Ruleset.Eras[GreatWorkWorld.Medieval].EraNumber);
        var artist = w.Game.AddUnit("Great Artist", w.A, w.CapA.GetCenterTile());
        w.Game.GameInfo.GreatWorkInstances = [new GreatWorkInstance { Name = "ArtAncient" }];
        Assert.Equal("ArtClassical", GreatWorkRules.ChooseWork(w.A, artist)!.Name); // Classical is not later than Medieval
        Assert.DoesNotContain(w.Game.Ruleset.GreatWorks.Values.Where(it => it.GreatWorkClass == GreatWorkClass.Artifact), it => it.Name == "ArtClassical");
    }

    [Fact]
    public void WritersAndMusiciansUseTheirClassAndNeverTheHiddenWritings()
    {
        AddGreatPerson(w, "Great Writer");
        AddGreatPerson(w, "Great Musician");
        var writer = w.Game.AddUnit("Great Writer", w.A, w.CapA.GetCenterTile());
        var musician = w.Game.AddUnit("Great Musician", w.A, w.CityA2.GetCenterTile());
        Assert.Equal("WriteAncient", GreatWorkRules.ChooseWork(w.A, writer)!.Name);
        Assert.Equal("MusicAncient", GreatWorkRules.ChooseWork(w.A, musician)!.Name);
        w.Game.GameInfo.GreatWorkInstances = [new GreatWorkInstance { Name = "WriteAncient" }];
        Assert.Equal("WriteClassical", GreatWorkRules.ChooseWork(w.A, writer)!.Name);
        w.Game.GameInfo.GreatWorkInstances.Add(new GreatWorkInstance { Name = "WriteClassical" });
        Assert.Null(GreatWorkRules.ChooseWork(w.A, writer)); // HiddenWriting is archaeology only
        var scientist = w.Game.AddUnit("Great Scientist", w.A, w.CapA.GetCenterTile().GetTilesAtDistance(1).First(it => it.IsLand && it.MilitaryUnit == null && it.CivilianUnit == null));
        Assert.Null(GreatWorkRules.ClassCreatedBy(scientist, w.Game.Ruleset));
        Assert.Null(GreatWorkRules.ChooseWork(w.A, scientist));
    }

    [Fact]
    public void ANamedGreatPersonCreatesExactlyItsWork()
    {
        var named = w.Game.CreateBaseUnit(w.Game.Ruleset.Units["Great Artist"].UnitType, "Can create the Great Work [FindClassical]");
        var unit = w.Game.AddUnit(named.Name, w.A, w.CapA.GetCenterTile());
        Assert.Equal(GreatWorkClass.Artifact, GreatWorkRules.ClassCreatedBy(unit, w.Game.Ruleset));
        Assert.Equal("FindClassical", GreatWorkRules.ChooseWork(w.A, unit)!.Name);
        w.Game.GameInfo.GreatWorkInstances = [new GreatWorkInstance { Name = "FindClassical" }];
        Assert.Null(GreatWorkRules.ChooseWork(w.A, unit)); // each named work exists once
    }

    // --- closest slot

    [Fact]
    public void TheClosestFreeSlotWinsTiesGoToTheEarlierCityAndTheSlotTypeMustFit()
    {
        var capGallery = w.Build(w.CapA, 1, GreatWorkSlotType.ArtArtifact);
        var otherGallery = w.Build(w.CityA2, 1, GreatWorkSlotType.ArtArtifact);
        w.Build(w.CapA, 1, GreatWorkSlotType.Music);
        var atCapital = w.CapA.GetCenterTile();
        var atOther = w.CityA2.GetCenterTile();

        Assert.Equal(new GreatWorkLocation(w.CapA, capGallery, 0), GreatWorkRules.FindClosestFreeSlot(w.A, atCapital, GreatWorkClass.Artifact));
        Assert.Equal(new GreatWorkLocation(w.CityA2, otherGallery, 0), GreatWorkRules.FindClosestFreeSlot(w.A, atOther, GreatWorkClass.Art));
        Assert.Null(GreatWorkRules.FindClosestFreeSlot(w.A, atCapital, GreatWorkClass.Writing));

        w.Place(w.CityA2, otherGallery, 0, "ArtAncient"); // full: the farther free slot is used
        Assert.Equal(new GreatWorkLocation(w.CapA, capGallery, 0), GreatWorkRules.FindClosestFreeSlot(w.A, atOther, GreatWorkClass.Art));
        w.Place(w.CapA, capGallery, 0, "ArtClassical");
        Assert.Null(GreatWorkRules.FindClosestFreeSlot(w.A, atOther, GreatWorkClass.Art));

        w.CapA.SetGreatWorkIn(capGallery.Name, 0, null);
        w.CityA2.SetGreatWorkIn(otherGallery.Name, 0, null);
        var equidistant = w.Game.TileMap.TileList.First(it => it.AerialDistanceTo(atCapital) == it.AerialDistanceTo(atOther) && !it.IsCityCenter());
        Assert.Equal(new GreatWorkLocation(w.CapA, capGallery, 0), GreatWorkRules.FindClosestFreeSlot(w.A, equidistant, GreatWorkClass.Art)); // city order
    }

    [Fact]
    public void InsideACityTheFirstBuildingByNameWithAFreeSlotIsUsed()
    {
        var first = w.Build(w.CapA, 2, GreatWorkSlotType.ArtArtifact);
        var second = w.Build(w.CapA, 2, GreatWorkSlotType.ArtArtifact);
        var (early, late) = string.CompareOrdinal(first.Name, second.Name) < 0 ? (first, second) : (second, first);
        Assert.Equal(new GreatWorkLocation(w.CapA, early, 0), GreatWorkRules.FindClosestFreeSlot(w.A, w.CapA.GetCenterTile(), GreatWorkClass.Art));
        w.Place(w.CapA, early, 0, "ArtAncient");
        Assert.Equal(new GreatWorkLocation(w.CapA, early, 1), GreatWorkRules.FindClosestFreeSlot(w.A, w.CapA.GetCenterTile(), GreatWorkClass.Art));
        w.Place(w.CapA, early, 1, "ArtClassical");
        Assert.Equal(new GreatWorkLocation(w.CapA, late, 0), GreatWorkRules.FindClosestFreeSlot(w.A, w.CapA.GetCenterTile(), GreatWorkClass.Art));
    }

    // --- theming rows (spec 4.2.4), one condition at a time

    private static bool Applies(ThemingBonus row, string[] classes, string[] eras, string[] creators, string owner = "me") =>
        GreatWorkRules.RowApplies(row, classes.Select(Enum.Parse<GreatWorkClass>).ToList(), eras, creators, owner);

    [Fact]
    public void SameEraNeedsOneKnownEra()
    {
        var row = new ThemingBonus { SameEra = true };
        Assert.True(Applies(row, ["Art", "Art"], ["a", "a"], ["x", "y"]));
        Assert.False(Applies(row, ["Art", "Art"], ["a", "b"], ["x", "y"]));
        Assert.True(Applies(row, ["Art"], ["a"], ["x"])); // one work is trivially one era
        Assert.False(Applies(row, ["Art", "Art"], ["", ""], ["x", "y"])); // unknown eras never match
    }

    [Fact]
    public void UniqueErasNeedAllDifferentKnownEras()
    {
        var row = new ThemingBonus { UniqueEras = true };
        Assert.True(Applies(row, ["Art", "Art", "Art"], ["a", "b", "c"], ["x", "y", "z"]));
        Assert.False(Applies(row, ["Art", "Art", "Art"], ["a", "b", "a"], ["x", "y", "z"]));
        Assert.False(Applies(row, ["Art", "Art"], ["a", ""], ["x", "y"]));
    }

    [Fact]
    public void ClassConditionsAreStrict()
    {
        string[] eras = ["a", "a"];
        string[] who = ["x", "y"];
        Assert.True(Applies(new ThemingBonus { MustBeArt = true }, ["Art", "Art"], eras, who));
        Assert.False(Applies(new ThemingBonus { MustBeArt = true }, ["Art", "Artifact"], eras, who));
        Assert.True(Applies(new ThemingBonus { MustBeArtifact = true }, ["Artifact", "Artifact"], eras, who));
        Assert.False(Applies(new ThemingBonus { MustBeArtifact = true }, ["Artifact", "Writing"], eras, who));
        var equal = new ThemingBonus { MustBeEqualArtArtifact = true };
        Assert.True(Applies(equal, ["Art", "Artifact"], eras, who));
        Assert.True(Applies(equal, ["Artifact", "Art"], eras, who));
        Assert.False(Applies(equal, ["Art", "Art"], eras, who));
        Assert.False(Applies(equal, ["Art", "Writing"], eras, who));
        Assert.False(Applies(equal, ["Art", "Artifact", "Art"], ["a", "a", "a"], ["x", "y", "z"])); // 2 vs 1
        Assert.True(Applies(equal, ["Art", "Artifact", "Art", "Artifact"], ["a", "a", "a", "a"], ["w", "x", "y", "z"]));
    }

    [Fact]
    public void CreatorConditionsAreStrictAndAnUnknownCreatorNeverCounts()
    {
        string[] classes = ["Art", "Art"];
        string[] eras = ["a", "a"];
        var owner = new ThemingBonus { RequiresOwner = true };
        Assert.True(Applies(owner, classes, eras, ["me", "me"]));
        Assert.False(Applies(owner, classes, eras, ["me", "you"]));
        Assert.False(Applies(owner, classes, eras, ["", ""]));
        var other = new ThemingBonus { RequiresAnyButOwner = true };
        Assert.True(Applies(other, classes, eras, ["you", "them"]));
        Assert.True(Applies(other, classes, eras, ["", "you"])); // no creator is not the owner
        Assert.False(Applies(other, classes, eras, ["you", "me"]));
        var same = new ThemingBonus { RequiresSamePlayer = true };
        Assert.True(Applies(same, classes, eras, ["you", "you"]));
        Assert.False(Applies(same, classes, eras, ["you", "them"]));
        Assert.False(Applies(same, classes, eras, ["", ""]));
        var unique = new ThemingBonus { RequiresUniquePlayers = true };
        Assert.True(Applies(unique, classes, eras, ["you", "them"]));
        Assert.False(Applies(unique, classes, eras, ["you", "you"]));
        Assert.False(Applies(unique, classes, eras, ["you", ""]));
    }

    [Fact]
    public void ARowWithoutConditionsAppliesAndAllConditionsOfARowMustHold()
    {
        Assert.True(Applies(new ThemingBonus(), ["Music"], ["a"], [""]));
        Assert.False(GreatWorkRules.RowApplies(new ThemingBonus(), [], [], [], "me"));
        var both = new ThemingBonus { SameEra = true, RequiresOwner = true };
        Assert.True(Applies(both, ["Art", "Art"], ["a", "a"], ["me", "me"]));
        Assert.False(Applies(both, ["Art", "Art"], ["a", "b"], ["me", "me"]));
        Assert.False(Applies(both, ["Art", "Art"], ["a", "a"], ["me", "you"]));
    }

    // --- theming of a building

    private Building Museum(CityObject city) => w.Build(city, 2, GreatWorkSlotType.ArtArtifact, MuseumRows());

    [Fact]
    public void ThemingNeedsEverySlotFilledAndTakesTheFirstMatchingRow()
    {
        var museum = Museum(w.CapA);
        string a = w.A.CivId, b = w.B.CivId;
        Assert.Equal(0, w.CapA.GetThemingBonus(museum)); // empty
        w.Place(w.CapA, museum, 0, "ArtAncient", a);
        Assert.Equal(0, w.CapA.GetThemingBonus(museum)); // one of two slots

        w.Place(w.CapA, museum, 1, "ArtAnyEra", a);
        Assert.Equal(4, w.CapA.GetThemingBonus(museum)); // row 1: same era, art, owner

        w.Place(w.CapA, museum, 1, "ArtClassical", a, GreatWorkWorld.Classical);
        Assert.Equal(0, w.CapA.GetThemingBonus(museum)); // eras differ, not equal art/artifact, but the owner made them: no row

        w.Place(w.CapA, museum, 0, "ArtAncient", b);
        w.Place(w.CapA, museum, 1, "ArtMedieval", b, GreatWorkWorld.Medieval);
        Assert.Equal(1, w.CapA.GetThemingBonus(museum)); // row 4: none by the owner

        w.Place(w.CapA, museum, 1, "FindAncient", a, GreatWorkWorld.Ancient);
        Assert.Equal(0, w.CapA.GetThemingBonus(museum)); // art + artifact of one era: only the owner's work spoils row 4

        w.Place(w.CapA, museum, 1, "FindAncient", b, GreatWorkWorld.Classical);
        Assert.Equal(2, w.CapA.GetThemingBonus(museum)); // row 3: unique eras, one art and one artifact
    }

    [Fact]
    public void ARowForSameEraArtifactsPaysThreeAndAnUnknownWorkPaysNothing()
    {
        var museum = Museum(w.CapA);
        w.Place(w.CapA, museum, 0, "FindAncient", w.B.CivId, GreatWorkWorld.Ancient);
        w.Place(w.CapA, museum, 1, "FindClassical", w.B.CivId, GreatWorkWorld.Ancient);
        Assert.Equal(3, w.CapA.GetThemingBonus(museum)); // row 2

        w.Game.Ruleset.GreatWorks.Remove("FindClassical");
        Assert.Equal(0, w.CapA.GetThemingBonus(museum)); // a work the ruleset no longer knows leaves the slot unthemed
        w.CapA.SetGreatWorkIn(museum.Name, 1, "NoInstance");
        Assert.Equal(0, w.CapA.GetThemingBonus(museum));
    }

    [Fact]
    public void ThemingOfABuildingWithoutRowsOrNotBuiltIsZero()
    {
        var plain = w.Build(w.CapA, 1, GreatWorkSlotType.Writing);
        w.Place(w.CapA, plain, 0, "WriteAncient", w.A.CivId);
        Assert.Equal(0, w.CapA.GetThemingBonus(plain));
        var notBuilt = w.MakeBuilding(1, GreatWorkSlotType.Music, new ThemingBonus { Bonus = 5 });
        w.CapA.SetGreatWorkIn(notBuilt.Name, 0, "MusicAncient");
        Assert.Equal(0, w.CapA.GetThemingBonus(notBuilt));
        Assert.Equal(0, w.CapA.GetThemingBonuses());
    }

    [Theory]
    [InlineData(4, 50, 6)]   // 4 * 150 / 100
    [InlineData(3, 50, 4)]   // 3 * 150 / 100 = 4.5, truncated
    [InlineData(1, 50, 1)]   // 1 * 150 / 100 = 1.5, truncated
    [InlineData(4, 0, 4)]
    [InlineData(2, -50, 1)]  // a negative modifier shrinks
    [InlineData(4, 100, 8)]
    public void ThemingPoliciesRaiseTheBonusTruncating(int bonus, int percent, int expected)
    {
        var world = new GreatWorkWorld([$"[{(percent >= 0 ? "+" : "")}{percent}]% theming bonuses [in all cities]"]);
        var building = world.Build(world.CapA, 1, GreatWorkSlotType.Music, new ThemingBonus { Bonus = bonus });
        world.Place(world.CapA, building, 0, "MusicAncient", world.A.CivId);
        Assert.Equal(expected, world.CapA.GetThemingBonus(building));
    }

    [Fact]
    public void TheCapitalBonusAppliesAfterThePolicyBonusAndOnlyInTheCapital()
    {
        var world = new GreatWorkWorld(["[+50]% theming bonuses [in all cities]", "[+100]% theming bonuses in your capital"]);
        var row = new ThemingBonus { Bonus = 4 };
        var inCapital = world.Build(world.CapA, 1, GreatWorkSlotType.Music, row);
        var inOther = world.Build(world.CityA2, 1, GreatWorkSlotType.Writing, row);
        world.Place(world.CapA, inCapital, 0, "MusicAncient", world.A.CivId);
        world.Place(world.CityA2, inOther, 0, "WriteAncient", world.A.CivId);
        Assert.True(world.CapA.IsCapital());
        Assert.Equal(12, world.CapA.GetThemingBonus(inCapital)); // 4 -> 6 -> 12
        Assert.Equal(6, world.CityA2.GetThemingBonus(inOther));
    }

    // --- culture

    [Fact]
    public void WorksGiveBaseCultureTimesTheirNumberPlusTheming()
    {
        var museum = Museum(w.CapA);
        Assert.True(w.CapA.GetGreatWorkStats().IsEmpty());
        w.Place(w.CapA, museum, 0, "ArtAncient", w.A.CivId);
        Assert.Equal(2f, w.CapA.GetGreatWorkStats().Culture); // 1 work * 2, no theming yet
        w.Place(w.CapA, museum, 1, "ArtAnyEra", w.A.CivId);
        Assert.Equal(8f, w.CapA.GetGreatWorkStats().Culture); // 2 * 2 + theming 4
        w.CapA.CityStats.Update();
        Assert.Equal(8f, w.CapA.CityStats.BaseStatTree.Children["Great Works"].TotalStats.Culture);
    }

    [Fact]
    public void PerWorkUniquesAddToTheCultureAndRespectTheirCityFilter()
    {
        var world = new GreatWorkWorld(["[+1 Culture] from each Great Work [in all cities]", "[+5 Faith] from each Great Work [in this city]"]);
        var museum = world.Build(world.CapA, 2, GreatWorkSlotType.ArtArtifact);
        world.Place(world.CapA, museum, 0, "ArtAncient");
        world.Place(world.CapA, museum, 1, "FindAncient");
        var stats = world.CapA.GetGreatWorkStats();
        Assert.Equal((2 * 2 + 2 * 1, 2 * 5), (stats.Culture, stats.Faith));
    }

    [Fact]
    public void WithoutTheGateWorksGiveNothing()
    {
        var off = new GreatWorkWorld(enabled: false);
        var gallery = off.Build(off.CapA, 1, GreatWorkSlotType.ArtArtifact, new ThemingBonus { Bonus = 4 });
        off.Place(off.CapA, gallery, 0, "ArtAncient", off.A.CivId);
        Assert.True(off.CapA.GetGreatWorkStats().IsEmpty());
        off.CapA.CityStats.Update();
        Assert.False(off.CapA.CityStats.BaseStatTree.Children.ContainsKey("Great Works"));
    }

    [Fact]
    public void AFilledSlotThatBelongsToAnotherCivStillCountsForTheCityOwner()
    {
        // A conquered city keeps its works: the owner is read from the city, so the theming rows see the new owner.
        var museum = Museum(w.CityB);
        w.Place(w.CityB, museum, 0, "ArtAncient", w.A.CivId);
        w.Place(w.CityB, museum, 1, "ArtAnyEra", w.A.CivId);
        Assert.Equal(1, w.CityB.GetThemingBonus(museum)); // B's city: not "owner", but "any but the owner" (none made by B) holds
    }

    // --- serialisation, clone

    [Fact]
    public void AGameWithoutWorksSerialisesWithoutAnyWorkState()
    {
        string json = UncivJson.Serialize(w.Game.GameInfo);
        Assert.DoesNotContain("greatWork", json, StringComparison.Ordinal);
        Assert.Empty(w.Game.GameInfo.GreatWorks);
    }

    [Fact]
    public void WorkStateRoundTripsThroughJsonAndCloneIsIsolated()
    {
        var museum = Museum(w.CapA);
        w.Place(w.CapA, museum, 1, "ArtAncient", w.A.CivId, GreatWorkWorld.Ancient);
        w.Game.GameInfo.GreatWorkSwapOffers = [new GreatWorkSwapOffer { Civ = w.A.CivId, Work = "ArtAncient" }];
        string json = UncivJson.Serialize(w.Game.GameInfo);
        Assert.Contains("\"greatWorks\":[{\"name\":\"ArtAncient\"", json, StringComparison.Ordinal);
        Assert.Contains("\"greatWorkSlots\":[{\"building\":\"" + museum.Name + "\",\"works\":[null,\"ArtAncient\"]}]", json, StringComparison.Ordinal);
        Assert.Contains("\"greatWorkSwapOffers\"", json, StringComparison.Ordinal);

        var back = UncivJson.Deserialize<GameInfo>(json)!;
        var instance = Assert.Single(back.GreatWorks);
        Assert.Equal(("ArtAncient", w.A.CivId, GreatWorkWorld.Ancient), (instance.Name, instance.Creator, instance.Era));
        Assert.Equal(w.A.CivId, Assert.Single(back.GreatWorkSwapOffers!).Civ);
        Assert.Equal([null, "ArtAncient"], back.Civilizations.SelectMany(it => it.Cities).Single(it => it.GreatWorkSlotData != null).GreatWorkSlotData![0].Works);
        Assert.Equal(json, UncivJson.Serialize(back));

        var clone = w.Game.GameInfo.Clone();
        clone.GreatWorkInstances![0].Era = "changed";
        clone.GreatWorkSwapOffers!.Clear();
        clone.Civilizations.SelectMany(it => it.Cities).First(it => it.Name == w.CapA.Name).GreatWorkSlotData![0].Works[1] = "other";
        Assert.Equal(GreatWorkWorld.Ancient, w.Game.GameInfo.GreatWorks[0].Era);
        Assert.Single(w.Game.GameInfo.GreatWorkSwapOffers!);
        Assert.Equal("ArtAncient", w.CapA.GetGreatWorkIn(museum.Name, 1));
    }
}
