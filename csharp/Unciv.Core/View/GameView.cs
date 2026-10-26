using Unciv.Logic;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;
using Unciv.Logic.Map.Tiles;
using Unciv.Models;
using CityClass = Unciv.Logic.City.City;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;

namespace Unciv.View;

// Ported from core/src/com/unciv/view/GameView.kt (T084). Stripped: the purity annotations.
// T085 added the city/unit view factories and getUnitsWithVisibleMovementHistory.

/// <summary>View of a <see cref="GameInfo"/> from the perspective of <see cref="Viewer"/>.</summary>
public class GameView : View<GameInfo>
{
    private readonly CivilizationClass viewer;
    private readonly GameInfo gameInfo;
    private readonly MapVisualization mapVisualization;

    public GameView(GameInfo gameInfo, CivilizationClass viewer, bool spectatorMode = false) : base(gameInfo, viewer, spectatorMode)
    {
        this.gameInfo = gameInfo;
        this.viewer = viewer;
        CivView = new CivView(viewer, viewer, spectatorMode, this);
        TileMapView = new TileMapView(gameInfo.TileMap, viewer, spectatorMode, this);
        mapVisualization = new MapVisualization(gameInfo, viewer);
    }

    protected override CivilizationClass Viewer => viewer;

    /// <summary>T230: the host secret for state-based seeds. Internal on purpose: no view member, client or DTO exposes it.</summary>
    internal long RandomSalt => gameInfo.RandomSalt;

    public CivView CivView { get; }

    public TileMapView TileMapView { get; }

    // Navigation
    // These can be cached in the future if we see a need, for now - simplicity
    public CivView GetCivView(CivilizationClass civ) => new(civ, viewer, SpectatorMode, this);

    public CityView GetCityView(CityClass city) => new(city, viewer, SpectatorMode, this);

    public ForeignCityView GetForeignCityView(CityClass city) => new(city, viewer, SpectatorMode, this);

    public ForeignMapUnitView GetForeignMapUnitView(MapUnit unit) => new(unit, viewer, SpectatorMode, this);

    public MapUnitView GetMapUnitView(MapUnit unit) => new(unit, viewer, SpectatorMode, this);

    public ForeignCivView GetForeignCivView(CivilizationClass civ) => new(civ, viewer, SpectatorMode, this);

    public DiplomacyManagerView GetDiplomacyManagerView(DiplomacyManager diplomacyManager) => new(diplomacyManager, viewer, SpectatorMode, this);

    public SpyView GetSpyView(Spy spy) => new(spy, viewer, SpectatorMode, this);

    // Data retrieval
    public int GetTurn() => gameInfo.Turns;

    public TileView GetTile(Tile tile) => TileMapView.GetTile(tile);

    /// <summary>Resolves the same tile from this game's viewing perspective, e.g. after a spectator toggles fog of war.</summary>
    public TileView GetTile(TileView tileView) => TileMapView.GetTile(tileView.Unwrap());

    /// <summary>Units whose past movements may be displayed from this view's perspective.</summary>
    public IEnumerable<ForeignMapUnitView> GetUnitsWithVisibleMovementHistory() =>
        gameInfo.Civilizations
            .SelectMany(it => it.Units.GetCivUnits())
            .Where(mapVisualization.IsUnitPastVisible)
            .Select(GetForeignMapUnitView);

    /// <summary>Visible attacks, including records retained after the attacking unit has disappeared.</summary>
    public IEnumerable<(HexCoord, HexCoord)> GetVisibleAttacks()
    {
        var unitAttacks = gameInfo.Civilizations.SelectMany(civ =>
            civ.Units.GetCivUnits().SelectMany(unit =>
                unit.AttacksSinceTurnStart
                    .Where(it => mapVisualization.IsAttackVisible(civ, unit.GetTile().Position, it))
                    .Select(it => (unit.GetTile().Position, it))));
        var civilizationAttacks = gameInfo.Civilizations.SelectMany(civ =>
            civ.AttacksSinceTurnStart
                .Where(it => mapVisualization.IsAttackVisible(civ, it.Source, it.Target))
                .Select(it => (it.Source, it.Target)));
        return unitAttacks.Concat(civilizationAttacks);
    }
}
