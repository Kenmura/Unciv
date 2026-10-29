using Unciv.Logic.GreatWorks;
using Unciv.Logic.Map.MapUnit.Actions;
using Unciv.Logic.Map.MapUnits;
using Unciv.Logic.Tourism;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unit;
using Unciv.Models.StatTypes;
using Unciv.UI.Components;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using TileObject = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.Logic.GreatWorks;

// B011 (bnw-spec/SPEC.md 4.5, 4.3.6): the Great Writer, Artist and Musician actions, the one-shot tourism of a Great Person's birth,
// the faith purchase of Great People.
[Collection("RulesetCache")]
public sealed class GreatPersonTests
{
    private const string Treatise = "Great Work counts as [8] turns of culture";
    private const string Tour = "Gives [10] Tourism with the target civ and [20]% with other civs when expended";

    private static GreatWorkWorld World(params string[] aUniques)
    {
        var w = new GreatWorkWorld(aUniques);
        w.Game.Ruleset.ModOptions.Constants.MiniumumTourismBlastStrength = 100;
        w.Game.Ruleset.ModOptions.Constants.CultureLevelExotic = 10;
        w.Game.Ruleset.ModOptions.Constants.BaseTourismPerGreatWork = 2;
        return w;
    }

    private static MapUnit AddGreatPerson(GreatWorkWorld w, string name, CityObject city, string? instanceName = null, params string[] uniques)
    {
        var units = w.Game.Ruleset.Units;
        if (!units.ContainsKey(name))
        {
            var unit = new BaseUnit { Name = name, UnitType = units["Great Artist"].UnitType, Movement = 2 };
            foreach (var unique in uniques) unit.Uniques.Add(unique);
            unit.SetRuleset(w.Game.Ruleset);
            units.Put(name, unit);
        }
        var center = city.GetCenterTile();
        var tile = center.CivilianUnit == null ? center : center.GetTilesAtDistance(1).First(it => it.IsLand && it.CivilianUnit == null && it.MilitaryUnit == null);
        var mapUnit = w.Game.AddUnit(name, w.A, tile);
        mapUnit.InstanceName = instanceName;
        return mapUnit;
    }

    /// <summary>A unit of A in a tile that becomes B's only afterwards (the border may not be crossed): the tile is next to B's capital.</summary>
    private static MapUnit InForeignLand(GreatWorkWorld w, string name, params string[] uniques) =>
        InLandOf(w, w.CityB, w.Game.GetTile(2, 0), name, uniques);

    private static MapUnit InLandOf(GreatWorkWorld w, CityObject owner, TileObject tile, string name, params string[] uniques)
    {
        var unit = AddGreatPerson(w, name, w.CapA, null, uniques);
        unit.PutInTile(tile);
        unit.CurrentMovement = unit.GetMaxMovement();
        w.Game.AddTileToCity(owner, tile);
        tile.SetOwningCity(owner);
        return unit;
    }

    private static UnitAction? Single(MapUnit unit, UnitActionType type) => UnitActions.GetUnitActions(unit, type).SingleOrDefault();

    private static GreatWorkWorld WithPersonWorks()
    {
        var w = World();
        void Add(string name, string person, GreatWorkClass cls, bool hidden = false) =>
            w.Game.Ruleset.GreatWorks.Put(name, new GreatWork { Name = name, GreatWorkClass = cls, Era = GreatWorkWorld.Ancient, GreatPerson = person, ArchaeologyOnly = hidden });
        Add("Odyssey", "Homer", GreatWorkClass.Writing);
        Add("Hidden Homer", "Homer", GreatWorkClass.Writing, hidden: true);
        Add("Lady Chatterley", "D H Lawrence", GreatWorkClass.Writing);
        Add("Water Lilies", "Claude Monet", GreatWorkClass.Art);
        return w;
    }

    // --- which work a named Great Person creates

    [Theory]
    [InlineData("D. H. Lawrence", "dhlawrence")]
    [InlineData("Antonín Dvořák", "antoníndvořák")]
    [InlineData("Alphonse-Marie-Adolphe de Neuville", "alphonsemarieadolphedeneuville")]
    [InlineData("O'Keeffe", "okeeffe")]
    [InlineData("", "")]
    [InlineData(" -. ", "")]
    public void PersonKeysIgnoreCaseSpacesAndPunctuation(string name, string expected) =>
        Assert.Equal(expected, GreatWorkRules.PersonKey(name));

    [Fact]
    public void ANamedGreatPersonCreatesTheWorkOfItsNameWhateverTheTableOrder()
    {
        var w = WithPersonWorks();
        var writer = AddGreatPerson(w, "Great Writer", w.CapA, "D. H. Lawrence");
        Assert.Equal("Lady Chatterley", GreatWorkRules.ChooseWork(w.A, writer)!.Name);
        writer.InstanceName = "homer";
        Assert.Equal("Odyssey", GreatWorkRules.ChooseWork(w.A, writer)!.Name); // not the hidden work of the same person
    }

    [Fact]
    public void ANameThatMatchesNothingOrAWrongClassOrAUsedWorkFallsBackToTheGenericChoice()
    {
        var w = WithPersonWorks();
        var writer = AddGreatPerson(w, "Great Writer", w.CapA, "Nobody");
        string generic = GreatWorkRules.ChooseWork(w.A, writer)!.Name;
        Assert.Equal("WriteAncient", generic);
        writer.InstanceName = "Claude Monet"; // a painter's name on a writer: a writing is created
        Assert.Equal(generic, GreatWorkRules.ChooseWork(w.A, writer)!.Name);
        writer.InstanceName = null;
        Assert.Equal(generic, GreatWorkRules.ChooseWork(w.A, writer)!.Name);
        writer.InstanceName = "Homer";
        (w.Game.GameInfo.GreatWorkInstances ??= []).Add(new GreatWorkInstance { Name = "Odyssey" });
        Assert.Equal(generic, GreatWorkRules.ChooseWork(w.A, writer)!.Name); // the Odyssey exists already
    }

    [Fact]
    public void TheCreateGreatWorkActionPlacesTheNamedWorkAndConsumesTheUnit()
    {
        var w = WithPersonWorks();
        var gallery = w.Build(w.CapA, 1, GreatWorkSlotType.Writing);
        var writer = AddGreatPerson(w, "Great Writer", w.CapA, "Homer");
        var action = Single(writer, UnitActionType.CreateGreatWork)!;
        Assert.NotNull(action.Action);
        action.Action!();
        Assert.Equal("Odyssey", w.CapA.GetGreatWorkIn(gallery.Name, 0));
        Assert.DoesNotContain(w.A.Units.GetCivUnits(), it => it.Id == writer.Id);
        Assert.Equal(w.A.CivId, w.Game.GameInfo.GetGreatWork("Odyssey")!.Creator);
    }

    // --- the Create Great Work unit action

    [Fact]
    public void CreateGreatWorkIsOnlyEnabledInAnOwnCityWithAFreeSlot()
    {
        var w = World();
        var artist = AddGreatPerson(w, "Great Artist", w.CapA);
        Assert.Null(Single(artist, UnitActionType.CreateGreatWork)!.Action); // no slot
        w.Build(w.CapA, 1, GreatWorkSlotType.ArtArtifact);
        Assert.NotNull(Single(artist, UnitActionType.CreateGreatWork)!.Action);

        var abroad = InForeignLand(w, "Great Artist");
        Assert.Null(Single(abroad, UnitActionType.CreateGreatWork)!.Action); // foreign land

        var field = w.Game.AddUnit("Great Artist", w.A, w.CapA.GetCenterTile().GetTilesAtDistance(1).First(it => it.IsLand && it.CivilianUnit == null));
        Assert.Null(Single(field, UnitActionType.CreateGreatWork)!.Action); // outside a city

        artist.CurrentMovement = 0f;
        Assert.Null(Single(artist, UnitActionType.CreateGreatWork)!.Action); // no movement left
    }

    [Fact]
    public void CreateGreatWorkIsNotOfferedWithoutTheGateOrToOtherUnits()
    {
        var off = new GreatWorkWorld(enabled: false);
        var artist = off.Game.AddUnit("Great Artist", off.A, off.CapA.GetCenterTile());
        Assert.Null(Single(artist, UnitActionType.CreateGreatWork));
        Assert.Null(Single(artist, UnitActionType.ConcertTour));

        var w = World();
        var warrior = w.Game.AddUnit("Warrior", w.A, w.CapA.GetCenterTile());
        Assert.Null(Single(warrior, UnitActionType.CreateGreatWork));
    }

    [Fact]
    public void TheActionAndTheCommandLeaveTheSameState()
    {
        ulong ViaAction()
        {
            var w = World();
            w.Build(w.CapA, 1, GreatWorkSlotType.ArtArtifact);
            Single(AddGreatPerson(w, "Great Artist", w.CapA), UnitActionType.CreateGreatWork)!.Action!();
            return w.Hash();
        }
        ulong ViaCommand()
        {
            var w = World();
            w.Build(w.CapA, 1, GreatWorkSlotType.ArtArtifact);
            w.Ok(new Unciv.Commands.GreatWorks.CreateGreatWork { UnitId = AddGreatPerson(w, "Great Artist", w.CapA).Id });
            return w.Hash();
        }
        Assert.Equal(ViaCommand(), ViaAction());
    }

    // --- Political Treatise

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(1f, 8)]
    [InlineData(7.9f, 56)] // the rate is rounded down first, then multiplied
    [InlineData(-3f, 0)]
    public void APoliticalTreatiseGivesEightTurnsOfCulture(float perTurn, int expected)
    {
        var w = World();
        var writer = AddGreatPerson(w, "Great Writer", w.CapA, null, Treatise);
        w.A.Stats.StatsForNextTurn.Culture = perTurn;
        int before = w.A.Policies.StoredCulture;
        var action = Single(writer, UnitActionType.HurryPolicy)!;
        Assert.Equal($"{{Political Treatise}} ({Fonts.Death})", action.Title);
        action.Action!();
        Assert.Equal(before + expected, w.A.Policies.StoredCulture);
        Assert.DoesNotContain(w.A.Units.GetCivUnits(), it => it.Id == writer.Id);
    }

    [Fact]
    public void APoliticalTreatiseNeedsMovementAndTheUnique()
    {
        var w = World();
        var writer = AddGreatPerson(w, "Great Writer", w.CapA, null, Treatise);
        writer.CurrentMovement = 0f;
        Assert.Null(Single(writer, UnitActionType.HurryPolicy)!.Action);
        Assert.Null(Single(AddGreatPerson(w, "Great Artist", w.CapA), UnitActionType.HurryPolicy));
    }

    [Fact]
    public void ThePoliticalTreatiseSaturatesInsteadOfWrapping()
    {
        var w = World();
        w.A.Stats.StatsForNextTurn.Culture = 1_000_000_000f;
        Assert.Equal(int.MaxValue, GreatWorkRules.PoliticalTreatiseCulture(w.A, 8));
        Assert.Equal(0, GreatWorkRules.PoliticalTreatiseCulture(w.A, -8));
    }

    // --- Concert Tour

    private static (GreatWorkWorld W, MapUnit Musician, CivilizationObject Third, CivilizationObject Stranger) TourWorld()
    {
        var w = World();
        var third = w.Game.AddCiv(isPlayer: true);
        w.Game.AddCity(third, w.Game.GetTile(4, -6));
        w.A.DiplomacyFunctions.MakeCivilizationsMeet(third);
        var stranger = w.Game.AddCiv(isPlayer: true);
        w.Game.AddCity(stranger, w.Game.GetTile(0, -8));
        var musician = InForeignLand(w, "Great Musician", Tour);
        foreach (var other in w.Game.GameInfo.Civilizations.Where(it => it != stranger))
        {
            other.Diplomacy.Remove(stranger.CivId);
            stranger.Diplomacy.Remove(other.CivId);
        }
        return (w, musician, third, stranger);
    }

    [Fact]
    public void AConcertTourInAForeignCityGivesTheTargetTheBlastAndTheOthersAPercent()
    {
        var (w, musician, third, stranger) = TourWorld();
        var action = Single(musician, UnitActionType.ConcertTour)!;
        Assert.NotNull(action.Action);
        action.Action!();
        Assert.Equal(100, InfluenceRules.InfluenceOn(w.A, w.B)); // no tourism: the minimum
        Assert.Equal(20, InfluenceRules.InfluenceOn(w.A, third));
        Assert.Equal(0, InfluenceRules.InfluenceOn(w.A, stranger));
        Assert.DoesNotContain(w.A.Units.GetCivUnits(), it => it.Id == musician.Id);
    }

    [Fact]
    public void AConcertTourIsStrongerWithTourism()
    {
        var (w, musician, third, _) = TourWorld();
        var gallery = w.Build(w.CapA, 4, GreatWorkSlotType.ArtArtifact);
        foreach (var (name, slot) in new[] { ("ArtAncient", 0), ("ArtClassical", 1), ("ArtMedieval", 2), ("ArtAnyEra", 3) })
            w.Place(w.CapA, gallery, slot, name, w.A.CivId);
        Assert.True(TourismRules.PlayerTourism(w.A) > 0);
        int expected = InfluenceRules.BlastStrength(w.A, 10);
        Single(musician, UnitActionType.ConcertTour)!.Action!();
        Assert.Equal(expected, InfluenceRules.InfluenceOn(w.A, w.B));
        Assert.Equal(expected * 20 / 100, InfluenceRules.InfluenceOn(w.A, third));
    }

    [Fact]
    public void AConcertTourIsDisabledOutsideTheTerritoryOfAMetForeignMajorCiv()
    {
        var (w, _, _, stranger) = TourWorld();
        var home = AddGreatPerson(w, "Great Musician", w.CapA, null, Tour);
        Assert.True(Single(home, UnitActionType.ConcertTour)!.Action == null, "own territory"); // own territory

        var unowned = AddGreatPerson(w, "Great Musician", w.CapA, null, Tour);
        unowned.PutInTile(w.Game.GetTile(0, 0));
        Assert.Null(unowned.GetTile().GetOwner());
        Assert.True(Single(unowned, UnitActionType.ConcertTour)!.Action == null, "no owner"); // no owner

        var cityState = w.Game.AddCiv(cityStateType: "Cultured");
        var csCity = w.Game.AddCity(cityState, w.Game.GetTile(8, 0));
        w.A.DiplomacyFunctions.MakeCivilizationsMeet(cityState);
        var csUnit = InLandOf(w, csCity, w.Game.GetTile(7, 0), "Great Musician", Tour);
        Assert.True(Single(csUnit, UnitActionType.ConcertTour)!.Action == null, "a city-state is no major civ"); // a city-state is no major civ

        var abroad = AddGreatPerson(w, "Great Musician", w.CapA, null, Tour);
        w.Game.AddTileToCity(stranger.Cities.First(), abroad.GetTile());
        abroad.GetTile().SetOwningCity(stranger.Cities.First());
        w.A.Diplomacy.Remove(stranger.CivId); // standing on a civ's land may have introduced them
        stranger.Diplomacy.Remove(w.A.CivId);
        Assert.True(Single(abroad, UnitActionType.ConcertTour)!.Action == null, "never met"); // never met
    }

    [Fact]
    public void AConcertTourNeedsMovementAndTheUnique()
    {
        var (w, musician, _, _) = TourWorld();
        Assert.Null(Single(InLandOf(w, w.CityB, w.Game.GetTile(2, 1), "Great Artist"), UnitActionType.ConcertTour));
        musician.CurrentMovement = 0f;
        Assert.Null(Single(musician, UnitActionType.ConcertTour)!.Action);
    }

    // --- tourism of a Great Person's birth

    private static string Born(int amount, string person) => $"[{amount}] Tourism when a [{person}] is born";

    [Fact]
    public void TheBirthOfAGreatPersonGivesItsTourismToEveryMetCiv()
    {
        var w = World(Born(250, "Great Writer"));
        var third = w.Game.AddCiv(isPlayer: true);
        w.Game.AddCity(third, w.Game.GetTile(4, -6));
        w.A.DiplomacyFunctions.MakeCivilizationsMeet(third);
        Assert.Equal(250, InfluenceRules.ApplyBirthTourism(w.A, "Great Writer"));
        Assert.Equal(250, InfluenceRules.InfluenceOn(w.A, w.B));
        Assert.Equal(250, InfluenceRules.InfluenceOn(w.A, third));
        Assert.Equal(0, InfluenceRules.InfluenceOn(w.B, w.A));
    }

    [Fact]
    public void TheBirthTourismNeedsTheMatchingUniqueAndCountsNegativesAsZero()
    {
        var w = World(Born(250, "Great Writer"), Born(100, "Great Writer"), Born(-50, "Great Writer"), Born(40, "Great Artist"));
        Assert.Equal(0, InfluenceRules.ApplyBirthTourism(w.A, "Great Musician"));
        Assert.Equal(0, InfluenceRules.InfluenceOn(w.A, w.B));
        Assert.Equal(350, InfluenceRules.ApplyBirthTourism(w.A, "Great Writer"));
        Assert.Equal(350, InfluenceRules.InfluenceOn(w.A, w.B));
        Assert.Equal(0, InfluenceRules.ApplyBirthTourism(w.B, "Great Writer")); // B has no such unique
    }

    [Fact]
    public void TheBirthTourismSaturatesAndNeedsTheGate()
    {
        var w = World(Born(int.MaxValue, "Great Writer"), Born(5, "Great Writer"));
        Assert.Equal(int.MaxValue, InfluenceRules.ApplyBirthTourism(w.A, "Great Writer"));
        var off = new GreatWorkWorld([Born(250, "Great Writer")], enabled: false);
        Assert.Equal(0, InfluenceRules.ApplyBirthTourism(off.A, "Great Writer"));
        Assert.Null(off.A.Culture);
    }

    [Fact]
    public void AGreatPersonBornFromPointsGivesItsTourismAtTheStartOfTheTurn()
    {
        var w = World(Born(250, "Great Artist"));
        w.A.GreatPeople.GreatPersonPointsCounter["Great Artist"] = 100000;
        new Unciv.Logic.Civilization.Managers.TurnManager(w.A).StartTurn();
        Assert.Contains(w.A.Units.GetCivUnits(), it => it.Name == "Great Artist");
        Assert.True(InfluenceRules.InfluenceOn(w.A, w.B) >= 250);
    }

    [Fact]
    public void AFreeGreatPersonCountsAsBorn()
    {
        var w = World(Born(250, "Great Artist"));
        w.A.GreatPeople.FreeGreatPeople = 1;
        Assert.NotNull(w.A.GreatPeople.ChooseFreeGreatPerson("Great Artist"));
        Assert.Equal(250, InfluenceRules.InfluenceOn(w.A, w.B));
        Assert.Equal(0, w.A.GreatPeople.FreeGreatPeople);
    }

    // --- faith purchase of Great People

    private const string AllGreatPeople = "Can purchase all Great People with Faith";

    private static int? FaithCost(GreatWorkWorld w, string unit) =>
        w.Game.Ruleset.Units[unit].GetStatBuyCost(w.CapA, Stat.Faith);

    private static int Priced(GreatWorkWorld w, int price)
    {
        float modifier = w.Game.GameInfo.Speed.GetStatCostModifier(Stat.Faith);
        return KotlinMath.DoubleToInt(price * modifier / 10f) * 10;
    }

    [Fact]
    public void TheBeliefLetsACityBuyEveryGreatPersonWithFaith()
    {
        var plain = World();
        Assert.Null(FaithCost(plain, "Great Artist"));
        Assert.False(plain.Game.Ruleset.Units["Great Artist"].CostFunctions.CanBePurchasedWithStat(plain.CapA, Stat.Faith));

        var w = World(AllGreatPeople);
        Assert.True(w.Game.Ruleset.Units["Great Artist"].CostFunctions.CanBePurchasedWithStat(w.CapA, Stat.Faith));
        Assert.Equal(Priced(w, 1000), FaithCost(w, "Great Artist"));
        Assert.Equal(Priced(w, 1000), FaithCost(w, "Great Scientist"));
        Assert.False(w.Game.Ruleset.Units["Warrior"].CostFunctions.CanBePurchasedWithStat(w.CapA, Stat.Faith)); // not a Great Person
        Assert.False(w.Game.Ruleset.Units["Great Artist"].CostFunctions.CanBePurchasedWithStat(w.CapA, Stat.Gold)); // only with Faith
    }

    [Fact]
    public void EachGreatPersonBoughtMakesTheNextOfItsKindDearer()
    {
        var w = World(AllGreatPeople);
        w.A.CivConstructions.BoughtItemsWithIncreasingPrice.Add("Great Artist", 2);
        Assert.Equal(Priced(w, 2500), FaithCost(w, "Great Artist")); // 1000 + 500 / 2 * (2 * 2 + 2), the engine's increasing price
        Assert.Equal(Priced(w, 1000), FaithCost(w, "Great Scientist")); // its own count
    }

    [Fact]
    public void BuyingAGreatPersonCountsIt()
    {
        var w = World(AllGreatPeople);
        w.CapA.AddStat(Stat.Faith, 5000);
        Assert.True(w.CapA.CityConstructions.PurchaseConstruction("Great Artist", -1, automatic: false, Stat.Faith));
        Assert.Equal(1, w.A.CivConstructions.GetBoughtItemsWithIncreasingPrice("Great Artist"));
        Assert.Equal(Priced(w, 1500), FaithCost(w, "Great Artist"));
    }

    [Fact]
    public void TheBeliefOnlyWorksWhenItsConditionalsHold()
    {
        var late = World("Can purchase all Great People with Faith <starting from the [Information era]>");
        Assert.Null(FaithCost(late, "Great Artist"));
        var early = World("Can purchase all Great People with Faith <starting from the [Ancient era]>");
        Assert.Equal(Priced(early, 1000), FaithCost(early, "Great Artist"));
    }

    [Fact]
    public void AGreatProphetKeepsItsOwnPurchase()
    {
        var w = World(AllGreatPeople);
        var prophet = w.Game.Ruleset.Units.Values.First(it => it.IsGreatPersonOfType("Faith"));
        Assert.False(prophet.CostFunctions.IsFaithPurchasableGreatPerson(w.CapA, Stat.Faith));
    }
}
