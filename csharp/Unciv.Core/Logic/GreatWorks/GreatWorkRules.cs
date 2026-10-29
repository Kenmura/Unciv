using Unciv.Logic.Map.MapUnits;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using TileObject = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Logic.GreatWorks;

// Brave New World (ticket B007, spec id B030, bnw-spec/SPEC.md section 4.2). Not part of the Kotlin source.
//
// Deliberate deviations and unresolved spec items (no decompiler output or SDK source is available, so [K]/[?] items keep the spec value):
// - Which work a Great Person creates when its unit names none ([?] in 4.2.3): the first unused, non-archaeology work of the right class in
//   table order whose era is not later than the creator's, else the first unused one of the class. Spec proposal [X].
// - Which classes a Great Person creates: by unit name (Great Artist: Art, Great Writer: Writing, Great Musician: Music). A named work
//   (unit unique "Can create the Great Work [x]") overrides it. B041 may move this into ruleset data.
// - Closest slot: the city with the smallest aerial distance to the unit, ties by civ city order; inside it the first free slot of the
//   buildings ordered by name. Spec [I].
// - Swap rules beyond the spec text: not while at war, both civs alive and known to each other; Music cannot be offered (the engine has
//   no swap notification for it). The offered work may be the giver's own creation. Spec [K].
// - Theming rows [K]: a row without a creator or era never satisfies a condition that needs one.

/// <summary>A slot of a city, by reference.</summary>
public readonly record struct GreatWorkLocation(CityObject City, Building Building, int Slot);

public static class GreatWorkRules
{
    private static GreatWorkClass? GreatPersonClass(string unitName) => unitName switch
    {
        "Great Artist" => GreatWorkClass.Art,
        "Great Writer" => GreatWorkClass.Writing,
        "Great Musician" => GreatWorkClass.Music,
        _ => null,
    };

    private static GameInfo GameOf(CivilizationObject civ) => (GameInfo)civ.GameInfo;

    /// <summary>Does the ruleset turn Great Works on (ModOptions <c>Enables Great Works and tourism</c>)?</summary>
    public static bool IsEnabled(Ruleset ruleset) => ((IHasUniques)ruleset.ModOptions).HasUnique(UniqueType.EnablesGreatWorksAndTourism);

    /// <summary>The work this unit is named to create (unique <c>Can create the Great Work [x]</c>), when it names one.</summary>
    public static GreatWork? NamedWorkOf(MapUnit unit, Ruleset ruleset)
    {
        var unique = unit.GetMatchingUniques(UniqueType.CreatesGreatWork, GameContext.IgnoreConditionalsState).FirstOrDefault();
        return unique != null && ruleset.GreatWorks.TryGetValue(unique.Params[0], out var work) ? work : null;
    }

    /// <summary>The class of work this unit creates, or null when it creates none.</summary>
    public static GreatWorkClass? ClassCreatedBy(MapUnit unit, Ruleset ruleset)
    {
        if (NamedWorkOf(unit, ruleset) is { } named) return named.GreatWorkClass;
        return GreatPersonClass(unit.Name);
    }

    /// <summary>The work <paramref name="unit"/> would create for <paramref name="civ"/>, or null when there is none to create.</summary>
    public static GreatWork? ChooseWork(CivilizationObject civ, MapUnit unit)
    {
        var ruleset = civ.GameInfo.Ruleset;
        if (NamedWorkOf(unit, ruleset) is { } named) return GameOf(civ).IsGreatWorkUsed(named.Name) ? null : named;
        if (ClassCreatedBy(unit, ruleset) is not { } greatWorkClass) return null;
        int eraNumber = civ.GetEraNumber();
        var unused = ruleset.GreatWorks.Values
            .Where(it => it.GreatWorkClass == greatWorkClass && !it.ArchaeologyOnly && !GameOf(civ).IsGreatWorkUsed(it.Name))
            .ToList();
        return unused.FirstOrDefault(it => it.Era.Length == 0 || !ruleset.Eras.TryGetValue(it.Era, out var era) || era.EraNumber <= eraNumber)
            ?? unused.FirstOrDefault();
    }

    /// <summary>The free slot for a work of <paramref name="greatWorkClass"/> closest to <paramref name="from"/> among the civ's cities.</summary>
    public static GreatWorkLocation? FindClosestFreeSlot(CivilizationObject civ, TileObject from, GreatWorkClass greatWorkClass)
    {
        var slotType = greatWorkClass.SlotType();
        GreatWorkLocation? best = null;
        int bestDistance = int.MaxValue;
        foreach (var city in civ.Cities)
        {
            int distance = city.GetCenterTile().AerialDistanceTo(from);
            if (distance >= bestDistance) continue;
            foreach (var slot in city.GetGreatWorkSlots())
            {
                if (slot.Work != null || slot.Building.GreatWorkSlotType != slotType) continue;
                best = new GreatWorkLocation(city, slot.Building, slot.Index);
                bestDistance = distance;
                break;
            }
        }
        return best;
    }

    /// <summary>Records a new work of the game and puts it into the slot.</summary>
    public static GreatWorkInstance CreateAndPlace(CivilizationObject civ, GreatWork work, GreatWorkLocation location)
    {
        var game = GameOf(civ);
        var instance = new GreatWorkInstance
        {
            Name = work.Name,
            Creator = civ.CivId,
            Era = work.Era.Length != 0 ? work.Era : civ.GetEra().Name,
            Turn = game.Turns,
        };
        (game.GreatWorkInstances ??= []).Add(instance);
        location.City.SetGreatWorkIn(location.Building.Name, location.Slot, work.Name);
        location.City.CityStats.Update();
        return instance;
    }

    /// <summary>
    /// Places the works a building contains when built (<c>Contains the Great Work [x] when built</c>) into its free slots of their class.
    /// A work that was created before (the building was rebuilt) is not created again.
    /// </summary>
    public static void PlaceFreeWorks(CityObject city, Building building)
    {
        var ruleset = city.GetRuleset();
        if (!IsEnabled(ruleset)) return;
        foreach (var unique in ((IHasUniques)building).GetMatchingUniques(UniqueType.ContainsGreatWorkWhenBuilt, GameContext.IgnoreConditionalsState))
        {
            if (!ruleset.GreatWorks.TryGetValue(unique.Params[0], out var work) || GameOf(city.Civ).IsGreatWorkUsed(work.Name)) continue;
            if (building.GreatWorkSlotType != work.GreatWorkClass.SlotType()) continue;
            var free = city.GetGreatWorkSlots().FirstOrDefault(it => it.Building == building && it.Work == null);
            if (free.Building == null) continue;
            CreateAndPlace(city.Civ, work, new GreatWorkLocation(city, building, free.Index));
        }
    }

    /// <summary>Where the civ holds the named work, or null.</summary>
    public static GreatWorkLocation? FindWork(CivilizationObject civ, string work)
    {
        foreach (var city in civ.Cities)
            foreach (var slot in city.GetGreatWorkSlots())
                if (slot.Work == work) return new GreatWorkLocation(city, slot.Building, slot.Index);
        return null;
    }

    /// <summary>Does a slot of <paramref name="building"/> take the work (the slot exists and its type fits the class)?</summary>
    public static bool SlotFits(Building building, int slot, GreatWork work) =>
        slot >= 0 && slot < building.GreatWorkSlots && building.GreatWorkSlotType == work.GreatWorkClass.SlotType();

    /// <summary>
    /// Exchanges the contents of two slots of the civ's cities (an empty target just receives the work). The caller has checked that both
    /// slots exist and fit (<see cref="SlotFits"/>) and that the source is filled.
    /// </summary>
    public static void MoveWork(GreatWorkLocation from, GreatWorkLocation to)
    {
        string? moving = from.City.GetGreatWorkIn(from.Building.Name, from.Slot);
        string? other = to.City.GetGreatWorkIn(to.Building.Name, to.Slot);
        to.City.SetGreatWorkIn(to.Building.Name, to.Slot, moving);
        from.City.SetGreatWorkIn(from.Building.Name, from.Slot, other);
        from.City.CityStats.Update();
        if (!ReferenceEquals(from.City, to.City)) to.City.CityStats.Update();
    }

    /// <summary>The class a civ may offer for swapping: Art, Artifact or Writing (spec 4.2.3 lists no swap for Music).</summary>
    public static bool IsSwappable(GreatWorkClass greatWorkClass) => greatWorkClass != GreatWorkClass.Music;

    /// <summary>
    /// The offer of <paramref name="civ"/> for <paramref name="work"/>, if it still stands: the civ is alive and still holds the work.
    /// </summary>
    public static GreatWorkSwapOffer? FindValidOffer(GameInfo game, CivilizationObject civ, string work)
    {
        var offer = game.GreatWorkSwapOffers?.FirstOrDefault(it => it.Civ == civ.CivId && it.Work == work);
        return offer != null && civ.IsAlive() && FindWork(civ, work) != null ? offer : null;
    }

    /// <summary>Removes the offers of <paramref name="civ"/> for works of <paramref name="greatWorkClass"/>.</summary>
    public static void RemoveOffers(GameInfo game, CivilizationObject civ, GreatWorkClass greatWorkClass)
    {
        if (game.GreatWorkSwapOffers == null) return;
        game.GreatWorkSwapOffers.RemoveAll(it => it.Civ == civ.CivId
            && game.Ruleset.GreatWorks.TryGetValue(it.Work, out var work) && work.GreatWorkClass == greatWorkClass);
        if (game.GreatWorkSwapOffers.Count == 0) game.GreatWorkSwapOffers = null;
    }

    /// <summary>Removes one standing offer by work name.</summary>
    public static void RemoveOffer(GameInfo game, string civId, string work)
    {
        if (game.GreatWorkSwapOffers == null) return;
        game.GreatWorkSwapOffers.RemoveAll(it => it.Civ == civId && it.Work == work);
        if (game.GreatWorkSwapOffers.Count == 0) game.GreatWorkSwapOffers = null;
    }

    /// <summary>
    /// Does a theming row hold for these works (one entry per slot, all slots filled)? Spec 4.2.4 [K]. <paramref name="owner"/> is the civ
    /// id of the city's owner. A creator or era that is empty (unknown) never satisfies a condition that needs it.
    /// </summary>
    public static bool RowApplies(ThemingBonus row, IReadOnlyList<GreatWorkClass> classes, IReadOnlyList<string> eras,
        IReadOnlyList<string> creators, string owner)
    {
        if (classes.Count == 0) return false;
        if (row.SameEra && !(eras[0].Length != 0 && eras.All(it => it == eras[0]))) return false;
        if (row.UniqueEras && !(eras.All(it => it.Length != 0) && eras.Distinct(StringComparer.Ordinal).Count() == eras.Count)) return false;
        if (row.MustBeArt && !classes.All(it => it == GreatWorkClass.Art)) return false;
        if (row.MustBeArtifact && !classes.All(it => it == GreatWorkClass.Artifact)) return false;
        if (row.MustBeEqualArtArtifact)
        {
            int art = classes.Count(it => it == GreatWorkClass.Art);
            int artifacts = classes.Count(it => it == GreatWorkClass.Artifact);
            if (art == 0 || art != artifacts || art + artifacts != classes.Count) return false;
        }
        if (row.RequiresOwner && !(owner.Length != 0 && creators.All(it => it == owner))) return false;
        if (row.RequiresAnyButOwner && creators.Any(it => it == owner)) return false;
        if (row.RequiresSamePlayer && !(creators[0].Length != 0 && creators.All(it => it == creators[0]))) return false;
        if (row.RequiresUniquePlayers && !(creators.All(it => it.Length != 0) && creators.Distinct(StringComparer.Ordinal).Count() == creators.Count)) return false;
        return true;
    }
}
