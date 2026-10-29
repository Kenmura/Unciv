using System.Globalization;
using Unciv.Commands;
using Unciv.Commands.Events;
using Unciv.Net.Visibility;

namespace Unciv.Assets;

/// <summary>One human of a simultaneous game as the host's lobby describes them: their civ, the name to show, and whether they are connected right now.</summary>
public sealed record TurnRosterEntry(string CivId, string Name, bool Connected);

/// <summary>One line of the turn bar.</summary>
public sealed record TurnBarRow(string CivId, string Name, bool Ended, bool Connected, bool IsYou);

/// <summary>What the turn bar draws: a line per human, the headline ("Waiting for ..."), the countdown text (null without a timer) and whether the host is resolving the round.</summary>
public sealed record TurnBarData(IReadOnlyList<TurnBarRow> Rows, string Headline, string? Countdown, bool Resolving);

/// <summary>
/// C036: the multiplayer turn bar as plain data (Godot-free, unit-tested). It shows whom the game is waiting for from the host's <see cref="TurnStatusDto"/> (who ended,
/// the deadline, the phase) and the roster of humans (who is connected). It decides nothing about the rules: the host owns the turn.
/// </summary>
public static class TurnBarModel
{
    public const string ResolvingHeadline = "Resolving the turn...";
    public const string EveryoneEnded = "Everyone has ended the turn";
    public const string UndoLabel = "Undo end turn";

    /// <summary>The bar for the status and roster; null when there is no roster (a single-player game has no turn bar).</summary>
    public static TurnBarData? Of(TurnStatusDto status, IReadOnlyList<TurnRosterEntry> roster, string viewer, long nowMs)
    {
        if (roster.Count == 0) return null;
        var rows = roster
            .Select(it => new TurnBarRow(it.CivId, it.Name, status.Ended.Contains(it.CivId, StringComparer.Ordinal), it.Connected, it.CivId == viewer))
            .ToList();
        var resolving = status.Phase == "Resolving";
        return new TurnBarData(rows, Headline(rows, resolving), Countdown(status.Deadline, nowMs), resolving);
    }

    private static string Headline(IReadOnlyList<TurnBarRow> rows, bool resolving)
    {
        if (resolving) return ResolvingHeadline;
        var waiting = rows.Where(it => !it.Ended).Select(it => it.Connected ? it.Name : it.Name + " (offline)").ToList();
        return waiting.Count == 0 ? EveryoneEnded : "Waiting for " + string.Join(", ", waiting);
    }

    /// <summary>"m:ss" until the deadline (never negative); null when no timer runs.</summary>
    public static string? Countdown(long? deadlineMs, long nowMs)
    {
        if (deadlineMs is not { } deadline) return null;
        var seconds = Math.Max(0L, (deadline - nowMs + 999) / 1000);
        return string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");
    }

    /// <summary>
    /// The toast for an event the viewer received, or null. Only another roster human's visible actions count (the host's filter already removed everything the viewer may
    /// not see): ending or undoing the end of the turn, moving a unit, founding a city, declaring war, making peace.
    /// </summary>
    public static string? Toast(GameEvent gameEvent, IReadOnlyList<TurnRosterEntry> roster, string viewer)
    {
        string? Who(string civ) => civ == viewer ? null : roster.FirstOrDefault(it => it.CivId == civ)?.Name;
        return gameEvent switch
        {
            PlayerEndedTurn e when Who(e.CivId) is { } who => $"{who} ended their turn",
            PlayerUndidEndTurn e when Who(e.CivId) is { } who => $"{who} is playing again",
            UnitMoved e when Who(e.CivId) is { } who => $"{who} moved a {e.UnitName}",
            CityFounded e when Who(e.CivId) is { } who => $"{who} founded {e.CityName}",
            WarDeclared e when Who(e.DeclarerCivId) is { } who => $"{who} declared war on {e.TargetCivId}",
            PeaceMade e when Who(e.CivId) is { } who => $"{who} made peace with {e.OtherCivId}",
            _ => null
        };
    }
}
