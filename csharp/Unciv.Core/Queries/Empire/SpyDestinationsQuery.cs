using Unciv.View;

namespace Unciv.Queries.Empire;

// CityId, CityName: The city (the id MoveSpy takes)
// Civ: The owner, only when the viewer has met it (else empty)
// IsCityState: Whether the owner is a known city-state
public sealed record SpyDestination(string CityId, string CityName, string Civ, bool IsCityState);

// Name: The spy (the name MoveSpy and SetSpyCoup take)
// Rank, Action: Its rank and what it does now
// Location: The city's name, or the hideout
// IsAlive: A dead spy has no destinations
// CanMoveToHideout: MoveSpy with a null city would be accepted
// Destinations: The cities MoveSpy would accept for this spy, sorted by city name then id
// CanDoCoup, CoupChance: Whether SetSpyCoup could stage a coup, and its odds as the Kotlin UI shows them (0..1; the defending spy is not revealed)
public sealed record SpyEntry(string Name, int Rank, string Action, string Location, bool IsAlive, bool CanMoveToHideout,
    IReadOnlyList<SpyDestination> Destinations, bool CanDoCoup, float? CoupChance);

public sealed record SpyDestinationsResult(IReadOnlyList<SpyEntry> Spies);

// T220: the espionage screen's "where can this spy go" lists. Only explored cities are offered; the owner of a city is named only if the viewer knows it.
[QueryType("empire.SpyDestinations")]
public sealed record SpyDestinationsQuery : IQuery<SpyDestinationsResult>
{
    public string Type => "empire.SpyDestinations";

    public SpyDestinationsResult Run(GameView view)
    {
        var civ = view.CivView.Unwrap();
        if (!civ.GameInfo.IsEspionageEnabled() || civ.IsSpectator())
            throw new QueryException(EmpireQueryCodes.NotAvailable, "Espionage is not available");

        var cities = civ.GameInfo.GetCities()
            .Where(it => view.TileMapView.GetTile(it.GetCenterTile().Position) != null)
            .OrderBy(it => it.Name, StringComparer.Ordinal).ThenBy(it => it.Id, StringComparer.Ordinal).ToList();
        var spies = civ.EspionageManager.SpyList.Select(spy =>
        {
            bool alive = spy.IsAlive();
            var current = spy.GetCityOrNull();
            var destinations = alive
                ? cities.Where(it => it != current && !it.Espionage.HasSpyOf(civ) && spy.CanMoveTo(it))
                    .Select(it => new SpyDestination(it.Id, it.Name, civ.Knows(it.Civ) || it.Civ == civ ? it.Civ.CivName : "",
                        it.Civ.IsCityState && (civ.Knows(it.Civ) || it.Civ == civ))).ToList()
                : [];
            bool canCoup = alive && spy.CanDoCoup();
            return new SpyEntry(spy.Name, spy.Rank, spy.Action.ToString(), spy.GetLocationName(), alive, alive && current != null, destinations, canCoup,
                canCoup ? spy.GetCoupChanceOfSuccess(false) : null);
        }).ToList();
        return new SpyDestinationsResult(spies);
    }
}
