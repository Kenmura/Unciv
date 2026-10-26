using System.Globalization;
using Unciv.Net.Visibility;

namespace Unciv.Assets;

public enum NotificationTargetKind
{
    /// <summary>Centre the map camera on <see cref="NotificationTarget.X"/>, <see cref="NotificationTarget.Y"/>.</summary>
    Tile,

    /// <summary>Open the tech screen on <see cref="NotificationTarget.Name"/>.</summary>
    Tech,

    /// <summary>C052: select the unit <see cref="NotificationTarget.Id"/> and open its promotion picker.</summary>
    Promotion,

    /// <summary>C052: open the move-spies picker.</summary>
    Spies,
}

/// <summary>Where a click on a notification goes. A tile target only exists for a tile the snapshot builder let through (one the viewer can see).</summary>
public sealed record NotificationTarget(NotificationTargetKind Kind, int X, int Y, string? Name, int Id = 0);

/// <summary>One line of the notification list. <see cref="Index"/> is the position in the snapshot (oldest first); <see cref="Target"/> is null when a click has nowhere to go.</summary>
public sealed record NotificationRow(int Index, string Category, string Text, IReadOnlyList<string> Icons, NotificationTarget? Target);

/// <summary>
/// C013: the notification list and the turn-start summary as plain data, Godot-free so it is unit-tested. The notifications are the viewer's own
/// <see cref="ClientSnapshot.Notifications"/> (already filtered for the viewer: an action that points at a tile the viewer cannot see was dropped by the engine
/// before it reached the client), so nothing here can leak; this class only orders, words and picks a click target.
/// </summary>
public static class NotificationsModel
{
    /// <summary>The categories in the order the summary lists them (the engine's <c>NotificationCategory</c> declaration order).</summary>
    public static readonly IReadOnlyList<string> CategoryOrder =
        ["General", "Trade", "Diplomacy", "Production", "Units", "War", "Religion", "Espionage", "Cities"];

    /// <summary>Newest first (the engine appends; the player wants the latest on top). Ties cannot occur: the order is the position.</summary>
    public static IReadOnlyList<NotificationRow> Rows(IReadOnlyList<NotificationDto> notifications)
    {
        var rows = new List<NotificationRow>(notifications.Count);
        for (int i = notifications.Count - 1; i >= 0; i--)
        {
            var it = notifications[i];
            rows.Add(new NotificationRow(i, it.Category, Plain(it.Text), it.Icons, TargetOf(it.Actions)));
        }
        return rows;
    }

    /// <summary>
    /// The first action that can be followed. A picker (promote a unit, move the spies) wins over a place on the map, which wins over a tech; within a group the engine's order.
    /// Deliberate deviation from Kotlin (C052): the original runs every action of a notification in turn (centre the camera, then open the picker), the client follows one, and a
    /// picker is what the player asked for (the promoted unit is selected, so the map shows it anyway); Kotlin's espionage action opens the espionage screen, here the move-spies picker.
    /// </summary>
    public static NotificationTarget? TargetOf(IReadOnlyList<NotificationActionDto> actions)
    {
        foreach (var action in actions)
        {
            if (action.Kind == "promote" && action.Id is { } id) return new NotificationTarget(NotificationTargetKind.Promotion, 0, 0, null, id);
            if (action.Kind == "espionage") return new NotificationTarget(NotificationTargetKind.Spies, 0, 0, null);
        }
        foreach (var action in actions)
            if (action.Kind is "location" or "city" or "unit" && action.X is { } x && action.Y is { } y)
                return new NotificationTarget(NotificationTargetKind.Tile, x, y, null);
        foreach (var action in actions)
            if (action.Kind == "tech" && !string.IsNullOrEmpty(action.Name))
                return new NotificationTarget(NotificationTargetKind.Tech, 0, 0, action.Name);
        return null;
    }

    /// <summary>The engine writes names in square brackets (<c>[Rome] has declared war</c>) for the translator; without translation they are just noise.</summary>
    public static string Plain(string text) => text.Replace("[", "", StringComparison.Ordinal).Replace("]", "", StringComparison.Ordinal);

    /// <summary>"Turn 3: 4 notifications (2 Production, 1 War, 1 Cities)", categories in <see cref="CategoryOrder"/>, unknown ones last by name; "Turn 3: nothing new" when empty.</summary>
    public static string Summary(int turn, IReadOnlyList<NotificationRow> rows)
    {
        string head = "Turn " + turn.ToString(CultureInfo.InvariantCulture);
        if (rows.Count == 0) return head + ": nothing new";
        var counts = rows.GroupBy(it => it.Category, StringComparer.Ordinal)
            .OrderBy(it => CategoryRank(it.Key)).ThenBy(it => it.Key, StringComparer.Ordinal)
            .Select(it => $"{it.Count().ToString(CultureInfo.InvariantCulture)} {it.Key}");
        string noun = rows.Count == 1 ? "notification" : "notifications";
        return $"{head}: {rows.Count.ToString(CultureInfo.InvariantCulture)} {noun} ({string.Join(", ", counts)})";
    }

    /// <summary>The header of the list: its toggle shows the count.</summary>
    public static string ToggleLabel(int count, bool open) => (open ? "Hide notifications" : "Notifications") + " (" + count.ToString(CultureInfo.InvariantCulture) + ")";

    private static int CategoryRank(string category)
    {
        for (int i = 0; i < CategoryOrder.Count; i++)
            if (CategoryOrder[i] == category) return i;
        return CategoryOrder.Count;
    }
}
