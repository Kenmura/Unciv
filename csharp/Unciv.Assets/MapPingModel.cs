using Unciv.Net;

namespace Unciv.Assets;

/// <summary>C090: one ping on screen. <see cref="Remaining"/> counts down to zero, then it is gone.</summary>
public sealed record ActivePing(string Sender, int X, int Y, string Kind, float Remaining);

/// <summary>
/// C090: what a relayed map ping looks like and how long it lasts, Godot-free so it is unit-tested. Pings are shown, never stored: they are not a command, not hashed, and
/// not written to the client settings. The host decides who receives one (allies who know the tile) and how often; the client only caps what it keeps on screen.
/// </summary>
public static class MapPingModel
{
    /// <summary>Seconds a ping stays on the map.</summary>
    public const float Lifetime = 8f;

    /// <summary>Pings kept on screen at once; a new one beyond this replaces the oldest.</summary>
    public const int MaxActive = 12;

    private static readonly (string Kind, string Text, Rgb Colour)[] Table =
    [
        (MapPingKinds.Look, "Look here", new Rgb(70, 160, 230)),
        (MapPingKinds.Danger, "Danger", new Rgb(225, 70, 55)),
        (MapPingKinds.Help, "Help", new Rgb(235, 190, 45)),
    ];

    public static IReadOnlyList<string> Kinds => MapPingKinds.All;

    public static bool IsKnown(string? kind) => Table.Any(it => it.Kind == kind);

    /// <summary>The wording of a kind (untranslated; the screen translates it); an unknown kind reads as Look here.</summary>
    public static string TextOf(string kind) => Table.FirstOrDefault(it => it.Kind == kind, Table[0]).Text;

    public static Rgb ColourOf(string kind) => Table.FirstOrDefault(it => it.Kind == kind, Table[0]).Colour;

    /// <summary>The pings with <paramref name="ping"/> added (a newer ping on the same tile replaces the older one; the oldest go beyond <see cref="MaxActive"/>). Unknown kinds are ignored.</summary>
    public static IReadOnlyList<ActivePing> Add(IReadOnlyList<ActivePing> active, MapPingNotice ping)
    {
        if (!IsKnown(ping.Kind)) return active;
        var result = active.Where(it => it.X != ping.X || it.Y != ping.Y).ToList();
        result.Add(new ActivePing(ping.Sender, ping.X, ping.Y, ping.Kind, Lifetime));
        return result.Count > MaxActive ? result.GetRange(result.Count - MaxActive, MaxActive) : result;
    }

    /// <summary>The pings after <paramref name="seconds"/> have passed; the ones that ran out are dropped.</summary>
    public static IReadOnlyList<ActivePing> Advance(IReadOnlyList<ActivePing> active, float seconds) =>
        [.. active.Select(it => it with { Remaining = it.Remaining - seconds }).Where(it => it.Remaining > 0f)];

    /// <summary>How far through its life a ping is, 0 (new) to 1 (about to go): drives the ring's growth and fade.</summary>
    public static float Progress(ActivePing ping) => Math.Clamp(1f - ping.Remaining / Lifetime, 0f, 1f);
}
