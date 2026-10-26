using System.Globalization;
using Unciv.Net.Visibility;

namespace Unciv.Assets;

/// <summary>What the next-turn overlay draws: a fixed heading (a translation key), the civ line (composed, so left as is by the client), and a 0..1 bar fill.</summary>
public sealed record NextTurnProgressViewData(string Heading, string Detail, double Fraction);

/// <summary>
/// C040: the next-turn progress overlay as plain data, Godot-free so it is unit-tested. It is fed the <see cref="TurnProgress"/> the turn pump reports
/// before each automatic civ plays ("civ n of m") and decides nothing else. The civ is shown by its display name (the caller maps ids to names).
/// </summary>
public static class NextTurnProgressModel
{
    /// <summary>The heading of the overlay (a translation key).</summary>
    public const string Heading = "Processing the other civilizations";

    /// <summary>The heading before the first civ was reported: the human's own end of turn runs first.</summary>
    public const string Starting = "Ending your turn";

    /// <summary>The view of the moment before any civ was reported.</summary>
    public static NextTurnProgressViewData Begin() => new(Starting, "", 0.0);

    /// <summary>"Rome - 3 of 8" for the civ about to play (1-based position); the bar is full when the last civ is reached.</summary>
    public static NextTurnProgressViewData Of(TurnProgress progress, Func<string, string> displayName)
    {
        var total = Math.Max(progress.Total, 1);
        var position = Math.Clamp(progress.Played + 1, 1, total);
        var detail = string.Create(CultureInfo.InvariantCulture, $"{displayName(progress.CivId)} - {position} of {total}");
        return new NextTurnProgressViewData(Heading, detail, Math.Clamp((double)progress.Played / total, 0.0, 1.0));
    }
}
