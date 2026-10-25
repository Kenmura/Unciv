using Unciv.Logic.Civilization;
using Unciv.Queries.Units;
using Unciv.View;

namespace Unciv.Queries.Empire;

// Kind: The action class name without "Action" (Location, Tech, City, Diplomacy, MayaLongCount, MapUnit, Civilopedia, PromoteUnit,
//       Overview, Policy, Espionage, Link, Religion)
// Tile: Location of Location, City, MapUnit and PromoteUnit actions
// UnitId: Unit of MapUnit and PromoteUnit actions
// Name: Tech, civ, civilopedia link, unit name, overview page, policy, url or religion, whichever the kind has
// Detail: Overview: the entry to select
// ShowTrade: Diplomacy: open the trade screen
public sealed record NotificationActionEntry(string Kind, TilePos? Tile, int? UnitId, string? Name, string? Detail, bool ShowTrade);

public sealed record NotificationEntry(int Index, string Category, string Text, IReadOnlyList<string> Icons,
    IReadOnlyList<NotificationActionEntry> Actions);

public sealed record NotificationsResult(IReadOnlyList<NotificationEntry> Notifications);

// T197: the viewer's notifications in stored order. Actions are the serialisable ones of the save; an action pointing at a tile the
// viewer has not explored is dropped (design/SERVER.md 2.5), the notification itself stays.
[QueryType("empire.Notifications")]
public sealed record NotificationsQuery : IQuery<NotificationsResult>
{
    public string Type => "empire.Notifications";

    public NotificationsResult Run(GameView view)
    {
        var civ = view.CivView.Unwrap();
        var result = new List<NotificationEntry>();
        for (int i = 0; i < civ.Notifications.Count; i++)
        {
            var notification = civ.Notifications[i];
            var actions = notification.Actions.Select(it => Convert(view, it)).OfType<NotificationActionEntry>().ToList();
            result.Add(new NotificationEntry(i, notification.Category.ToString(), notification.Text, notification.Icons.ToList(), actions));
        }
        return new NotificationsResult(result);
    }

    // null: dropped
    private static NotificationActionEntry? Convert(GameView view, NotificationAction action)
    {
        switch (action)
        {
            case LocationAction a: return At(view, "Location", a.Location, null, null);
            case CityAction a: return At(view, "City", a.City, null, null);
            case MapUnitAction a: return At(view, "MapUnit", a.Location, a.Id, null);
            case PromoteUnitAction a: return At(view, "PromoteUnit", a.Location, a.Id, a.Name);
            case TechAction a: return new("Tech", null, null, a.TechName, null, false);
            case DiplomacyAction a: return new("Diplomacy", null, null, a.OtherCivName, null, a.ShowTrade);
            case MayaLongCountAction: return new("MayaLongCount", null, null, null, null, false);
            case CivilopediaAction a: return new("Civilopedia", null, null, a.Link, null, false);
            case OverviewAction a: return new("Overview", null, null, a.Page.ToString(), a.Select, false);
            case PolicyAction a: return new("Policy", null, null, a.Select, null, false);
            case EspionageAction: return new("Espionage", null, null, null, null, false);
            case LinkAction a: return new("Link", null, null, a.Url, null, false);
            case ReligionAction a: return new("Religion", null, null, a.ReligionName, null, false);
            default: return null;
        }
    }

    private static NotificationActionEntry? At(GameView view, string kind, Logic.Map.HexCoord location, int? unitId, string? name) =>
        view.TileMapView.GetTile(location) == null ? null : new(kind, new TilePos(location.X, location.Y), unitId, name, null, false);
}
