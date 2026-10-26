namespace Unciv.Assets;

/// <summary>What the pass-the-device screen draws: translation keys for the heading, the instruction and the button, and the civ id the screen hands the game to.</summary>
public sealed record HandOverViewData(string CivId, string Heading, string Message, string ButtonText);

/// <summary>
/// C029: the hotseat "pass the device" screen as plain data (Godot-free, unit-tested). It names the next player and nothing else: it must never carry anything about the
/// previous player's game (units, cities, research, the turn that just ended), because it is the one thing on screen while the device changes hands.
/// </summary>
public static class HotseatModel
{
    /// <summary>The heading (composed, so left as is by the client); its parameter is the next player's display name.</summary>
    public const string HeadingFormat = "Pass the device to {0}";

    /// <summary>The instruction under the heading (a translation key).</summary>
    public const string Message = "The previous player must look away. Press Ready when it is your turn.";

    /// <summary>The confirm button (a translation key).</summary>
    public const string ReadyButton = "Ready";

    /// <summary>The screen handing the game to <paramref name="civId"/>, shown as <paramref name="displayName"/> (the caller maps ids to names).</summary>
    public static HandOverViewData HandOver(string civId, Func<string, string> displayName) =>
        new(civId, string.Format(System.Globalization.CultureInfo.InvariantCulture, HeadingFormat, displayName(civId)), Message, ReadyButton);

    /// <summary>The text shown when taking over failed (the game is unchanged and the screen stays up so the player can try again).</summary>
    public static string FailureText(string reason) => "Could not start the turn: " + reason;
}
