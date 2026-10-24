using Unciv.Logic.Battle;
using Unciv.Logic.City;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Map;
using Unciv.Models;
using Unciv.UI.Screens.Civilopedia;
using CityClass = Unciv.Logic.City.City;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;

namespace Unciv.View;

// Ported from core/src/com/unciv/view/ForeignCityView.kt (T085). Stripped: the purity annotations.
// DebugUtils.VISIBLE_MAP is the viewer game's EngineOptions.VisibleMap (T048).

/// <summary>Should contain information that should be knowable to us about foreign cities. Superclass of <see cref="CityView"/>.</summary>
public class ForeignCityView(CityClass city, CivilizationClass viewer, bool spectatorMode, GameView gameView)
    : OwnedView<CityClass>(city, viewer, spectatorMode, gameView)
{
    protected CityClass City { get; } = city;

    internal override CivilizationClass Owner() => City.Civ;

    public string Name => City.Name;

    /// <summary>The owning civ's <see cref="CivView"/>, as visible from the viewer's perspective. For the viewing player's full CivView, use <see cref="CityView.ViewingCiv"/>.</summary>
    public CivView OwningCivView => GameView.GetCivView(City.Civ);

    // Navigation
    /// <summary>The owning civ of this city, as visible from the viewer's perspective. For the viewing player's full CivView, use <see cref="CityView.ViewingCiv"/>.</summary>
    public virtual ForeignCivView OwningCiv() => GameView.GetForeignCivView(City.Civ);

    /// <summary>Get from a foreign view to an inner view</summary>
    public CityView? TryGetCityView()
    {
        var canSeeCityData = Viewer.IsSpectator() // not posing, actual spectator
                             || City.Civ == Viewer
                             || SpyIsSetUpAtCity(Viewer)
                             || Viewer.GameInfo.EngineOptions.VisibleMap;
        if (!canSeeCityData) return null;
        return GameView.GetCityView(City);
    }

    // Data retrieval
    public int GetHealth() => City.Health;

    public int GetMaxHealth() => City.GetMaxHealth();

    public int GetDefendingStrength() => new CityCombatant(City).GetDefendingStrength();

    public int GetAttackingStrength() => new CityCombatant(City).GetAttackingStrength();

    public List<TileView> GetBombardableTiles() =>
        TargetHelper.GetBombardableTiles(City).Select(it => GameView.TileMapView.GetTile(it)).ToList();

    public TileView GetCenterTile()
    {
        var tile = City.GetCenterTile();
        return GameView.TileMapView.GetTile(tile);
    }

    public bool CanBombard() => City.CanBombard();

    public int GetBombardRange() => City.GetBombardRange();

    /// <summary>Cities (of any civ) within <paramref name="maxDistance"/> of this city</summary>
    public List<ForeignCityView> GetNeighboringCities(int maxDistance = 12) =>
        City.GetNeighboringCities(maxDistance).Select(it => GameView.GetForeignCityView(it)).ToList();

    public int GetPopulationCount() => City.Population.Population;

    public bool IsCapital() => City.IsCapital();

    public bool IsPuppet() => City.IsPuppet;

    public bool IsBeingRazed() => City.IsBeingRazed;

    public bool IsInResistance() => City.IsInResistance();

    public bool IsWeLoveTheKingDayActive() => City.IsWeLoveTheKingDayActive();

    public bool IsBlockaded() => City.IsBlockaded();

    public bool IsConnectedToCapital() => City.IsConnectedToCapital();

    public FormattedLine GetProductionMarkup() => City.CityConstructions.GetProductionMarkup(City.GetRuleset());

    public string GetCityStateTypeName() => City.Civ.CityStateType.Name;

    public bool IsAtWarWith(CivilizationClass civ) => City.Civ.IsAtWarWith(civ);

    public bool CivKnows(CivilizationClass civ) => City.Civ.Knows(civ);

    public bool IsKnownTo(CivilizationClass civ) => civ.Knows(City.Civ);

    public DiplomacyManager? GetDiplomacyManagerWith(CivilizationClass civ) => City.Civ.GetDiplomacyManager(civ);

    public bool IsEspionageEnabled() => City.Civ.GameInfo.IsEspionageEnabled();

    public bool IsReligionEnabled() => City.Civ.GameInfo.IsReligionEnabled();

    public bool SpyIsSetUpAtCity(CivilizationClass viewer) => viewer.EspionageManager.GetSpyAssignedToCity(City)?.IsSetUp() == true;

    // Religion
    public Religion? GetMajorityReligion() => City.Religion.GetMajorityReligion();

    public Counter<string> GetNumberOfFollowers() => City.Religion.GetNumberOfFollowers();

    public Counter<string> GetPressuresFromSurroundingCities() => City.Religion.GetPressuresFromSurroundingCities();

    public string? GetReligionThisIsTheHolyCityOf() => City.Religion.ReligionThisIsTheHolyCityOf;

    public bool IsBlockedHolyCity() => City.Religion.IsBlockedHolyCity;

    public Religion? GetReligion(string? religionName) => religionName == null ? null : City.Civ.GameInfo.Religions.TryGetValue(religionName, out var religion) ? religion : null;

    /// <summary>Wraps the city as a <see cref="CityCombatantView"/> for battle purposes.</summary>
    public CityCombatantView AsCombatant() => new(this, City, Viewer, SpectatorMode, GameView);
}

// Helpers built on top of the view API, rather than being part of it
public static class ForeignCityViewExtensions
{
    public static HexCoord Location(this ForeignCityView view) => view.GetCenterTile().Position();

    public static bool IsCityState(this ForeignCityView view) => view.OwningCiv().IsCityState;

    public static ImmutableColor GetNationOuterColor(this ForeignCityView view) => view.OwningCiv().GetOuterColor();

    public static ImmutableColor GetCivInnerColor(this ForeignCityView view) => view.OwningCiv().GetInnerColor();
}
