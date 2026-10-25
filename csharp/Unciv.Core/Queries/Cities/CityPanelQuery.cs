using Unciv.Models.Ruleset;
using Unciv.Models.StatTypes;
using Unciv.Queries.Units;
using Unciv.View;

namespace Unciv.Queries.Cities;

// Name: Construction name
// Turns: Turns to finish, null for perpetual constructions
// Cost: Production cost, null for perpetual constructions
public sealed record QueueEntry(string Name, int? Turns, int? Cost, int WorkDone);

// SellPrice: Gold the building would sell for
public sealed record CityBuilding(string Name, int SellPrice);

// Position: The tile, Stats: what working it yields (nonzero stats)
public sealed record WorkedTile(TilePos Position, bool Locked, IReadOnlyList<StatAmount> Stats);

public sealed record CityGrowth(
    int Population, int FreePopulation, int FoodStored, int FoodToNextPopulation, int FoodForNextTurn,
    int? TurnsToNewPopulation, int? TurnsToStarvation);

public sealed record CityBorders(int CultureStored, int CultureToNextTile, bool CanChooseTile);

public sealed record CityPanelResult(
    string CityId, string Name, TilePos Position, bool IsCapital, bool IsPuppet, bool IsBeingRazed, bool IsInResistance,
    CityGrowth Growth, CityBorders Borders,
    IReadOnlyList<StatAmount> TotalStats, IReadOnlyList<StatSource> StatSources, IReadOnlyList<StatAmount> Happiness,
    string CurrentConstruction, IReadOnlyList<QueueEntry> Queue,
    IReadOnlyList<WorkedTile> WorkedTiles, IReadOnlyList<NamedAmount> Specialists, IReadOnlyList<NamedAmount> MaxSpecialists,
    bool ManualSpecialists, string CityFocus, bool AvoidGrowth,
    IReadOnlyList<CityBuilding> Buildings, string? Garrison,
    string? MajorityReligion, IReadOnlyList<NamedAmount> Followers, IReadOnlyList<NamedAmount> ReligionPressures,
    IReadOnlyList<NamedAmount> ResourceStockpiles);

// T196: everything the city screen shows in one record, built from the CityView accessors the screen uses. Worked tiles are
// ordered by coordinates; counters are ordered by name; stats stay float, only what the engine rounds is an int.
[QueryType("cities.CityPanel")]
public sealed record CityPanelQuery(string CityId) : IQuery<CityPanelResult>
{
    public string Type => "cities.CityPanel";

    public CityPanelResult Run(GameView view)
    {
        var city = Units.UnitQueryHelpers.OwnCity(view, CityId);
        var constructions = city.Constructions;

        var growth = new CityGrowth(city.GetPopulationCount(), city.GetFreePopulation(), city.GetFoodStored(), city.GetFoodToNextPopulation(),
            city.FoodForNextTurn(), city.GetNumTurnsToNewPopulation(), city.GetNumTurnsToStarvation());
        var borders = new CityBorders(city.GetCultureStored(), city.GetCultureToNextTile(), city.HasChoosableTiles());

        var sources = city.GetFinalStatList()
            .Select(it => new StatSource(it.Key, CityQueryHelpers.StatList(it.Value))).ToList();
        var happiness = city.GetHappinessList()
            .Where(it => it.Value != 0f).Select(it => new StatAmount(it.Key, it.Value)).ToList();

        var queue = constructions.ConstructionQueue.Select((name, index) => DescribeQueueEntry(city, name, index)).ToList();

        var locked = city.Unwrap().LockedTiles;
        var worked = city.Unwrap().WorkedTiles
            .Select(it => new WorkedTile(Units.UnitQueryHelpers.Pos(it), locked.Contains(it),
                CityQueryHelpers.StatList(view.TileMapView.GetTile(it)?.GetTileStats(view.CivView, city) ?? new Stats())))
            .OrderBy(it => it.Position.X).ThenBy(it => it.Position.Y).ToList();

        var buildings = city.GetBuiltBuildings()
            .Select(it => new CityBuilding(it.Name, city.GetGoldForSellingBuilding(it.Name))).ToList();

        var religion = city.GetMajorityReligion();
        return new CityPanelResult(CityId, city.Name, Units.UnitQueryHelpers.Pos(city.Location()), city.IsCapital(), city.IsPuppet(),
            city.IsBeingRazed(), city.IsInResistance(), growth, borders,
            CityQueryHelpers.StatList(city.GetCurrentCityStats()), sources, happiness,
            constructions.CurrentConstructionName(), queue, worked,
            CityQueryHelpers.Named(city.GetNewSpecialists()), CityQueryHelpers.Named(city.GetMaxSpecialists()),
            city.ManualSpecialists, city.GetCityFocus().ToString(), city.AvoidGrowth,
            buildings, city.GetGarrison()?.Name,
            religion?.Name, CityQueryHelpers.Named(city.GetNumberOfFollowers()), CityQueryHelpers.Named(city.GetPressuresFromSurroundingCities()),
            CityQueryHelpers.Named(city.GetResourceStockpiles()));
    }

    private static QueueEntry DescribeQueueEntry(CityView city, string name, int index)
    {
        var constructions = city.Constructions;
        if (constructions.GetConstruction(name) is not INonPerpetualConstruction construction)
            return new QueueEntry(name, null, null, 0);
        bool useStored = construction is Models.Ruleset.Building || constructions.IsFirstConstructionOfItsKind(index, name);
        return new QueueEntry(name, constructions.TurnsToConstruction(name, useStored), city.GetConstructionProductionCost(construction),
            constructions.GetWorkDone(name));
    }
}
