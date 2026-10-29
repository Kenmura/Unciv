using System.Globalization;
using System.Text.Json;
using Unciv.Net;
using Unciv.Net.Identity;

namespace Unciv.GodotClient.Setup;

/// <summary>Whether the host may press Start, and when not, the sentence that says why.</summary>
public sealed record StartCheck(bool CanStart, string Reason);

/// <summary>
/// C035: what the lobby screen shows, with no UI in it: the one-line texts of the slot table, the read-only settings, the refusal wording and whether Start is allowed.
/// It only reads the <see cref="LobbyState"/> the host sent, so a joiner and the host see the same words; the host's own checks (who is ready, how many players)
/// are repeated here only to explain a disabled Start, the lobby itself still refuses a bad start.
/// </summary>
public static class LobbyModel
{
    /// <summary>The text of one slot row: number, who sits there and what they are (you, the host, disconnected).</summary>
    public static string SlotText(LobbySlot slot, string? ownKey, string hostName)
    {
        string number = (slot.Slot + 1).ToString(CultureInfo.InvariantCulture);
        switch (slot.Kind)
        {
            case "Human":
                string name = slot.PlayerName ?? "Player";
                bool you = ownKey != null && slot.PlayerKeyId == ownKey;
                // The host sits in the first slot; the name alone could also belong to a friend who chose the same display name.
                bool host = slot.Slot == 0 && slot.PlayerName == hostName;
                string note = (you, host) switch
                {
                    (true, true) => " (you, the host)",
                    (true, false) => " (you)",
                    (false, true) => " (host)",
                    _ => "",
                };
                return $"{number}. {name}{note}{(slot.Connected == false ? " (disconnected)" : "")}";
            case "AI":
                return $"{number}. AI player";
            case "Closed":
                return $"{number}. Closed";
            default:
                return $"{number}. Open";
        }
    }

    /// <summary>The game's name for the screen title.</summary>
    public static string Title(LobbyState lobby) => Text(lobby.Settings, "gameName");

    /// <summary>The settings a joiner may read (the host edits them on the Host screen), one "Label: value" line each, in a fixed order.</summary>
    public static IReadOnlyList<string> SettingsLines(LobbyState lobby)
    {
        var s = lobby.Settings;
        var lines = new List<string>
        {
            $"Host: {lobby.HostName}",
            $"Ruleset: {Text(s, "ruleset")}{ModsText(s)}",
            $"Difficulty: {Text(s, "difficulty")}",
            $"Speed: {Text(s, "speed")}",
            $"Starting era: {Text(s, "era")}",
            $"Players: up to {Number(s, "maxMajorCivs")}, city-states: {Number(s, "cityStateCount")}",
            $"AI fills empty slots: {(Flag(s, "aiFill") ? "yes" : "no")}",
            $"Turns: {Text(s, "turnMode")}, timer: {Timer(s)}",
        };
        return lines;
    }

    /// <summary>Start needs one human, everybody ready and at least two players once the empty slots are filled with AI (when that is on).</summary>
    public static StartCheck CanStart(LobbyState lobby)
    {
        var humans = lobby.Slots.Where(it => it.Kind == "Human").ToList();
        if (humans.Count == 0) return new StartCheck(false, "Nobody has taken a seat yet.");
        var waiting = humans.Where(it => !it.Ready).Select(it => it.PlayerName ?? "Player").ToList();
        if (waiting.Count > 0) return new StartCheck(false, $"Waiting for {string.Join(", ", waiting)} to be ready.");
        int players = humans.Count + lobby.Slots.Count(it => it.Kind == "AI") + (Flag(lobby.Settings, "aiFill") ? lobby.Slots.Count(it => it.Kind == "Open") : 0);
        if (players < 2) return new StartCheck(false, "A game needs at least two players: turn on AI fill, add an AI slot or wait for a friend.");
        return new StartCheck(true, "Everybody is ready.");
    }

    /// <summary>What the player is told when the host would not let them in (or removed them). Never says more than the code does.</summary>
    public static string RefusalText(string code) => code switch
    {
        JoinRefusedCodes.RulesetMismatch => "The host plays with different rules (base ruleset or mods) than you, so you cannot join. Ask the host which mods are on and install the same ones.",
        JoinRefusedCodes.ProtocolMismatch => "Your game version and the host's cannot talk to each other. Both of you need the same version.",
        JoinRefusedCodes.BadCredentials => "Wrong password or invite.",
        JoinRefusedCodes.RateLimited => "Too many wrong attempts. Wait a little and try again.",
        JoinRefusedCodes.LobbyFull => "The lobby is full.",
        JoinRefusedCodes.GameAlreadyStarted => "The game has already started.",
        JoinRefusedCodes.Kicked => "The host removed you from the lobby.",
        JoinRefusedCodes.SlotTaken => "That seat is taken.",
        JoinRefusedCodes.AuthFailed or JoinRefusedCodes.BadKey => "The host did not accept your identity.",
        _ => $"The host refused the connection ({code}).",
    };

    private static string Text(JsonElement settings, string name) =>
        settings.ValueKind == JsonValueKind.Object && settings.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static string Number(JsonElement settings, string name) =>
        settings.ValueKind == JsonValueKind.Object && settings.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) ? number.ToString(CultureInfo.InvariantCulture) : "?";

    private static bool Flag(JsonElement settings, string name) =>
        settings.ValueKind == JsonValueKind.Object && settings.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string ModsText(JsonElement settings)
    {
        if (settings.ValueKind != JsonValueKind.Object || !settings.TryGetProperty("mods", out var mods) || mods.ValueKind != JsonValueKind.Array) return "";
        var names = mods.EnumerateArray().Where(it => it.ValueKind == JsonValueKind.String).Select(it => it.GetString()).ToList();
        return names.Count == 0 ? "" : $" + {string.Join(", ", names)}";
    }

    private static string Timer(JsonElement settings)
    {
        if (settings.ValueKind != JsonValueKind.Object || !settings.TryGetProperty("turnTimerSeconds", out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int seconds)) return "?";
        if (seconds <= 0) return "none";
        return seconds % 60 == 0 ? $"{(seconds / 60).ToString(CultureInfo.InvariantCulture)} min" : $"{seconds.ToString(CultureInfo.InvariantCulture)} s";
    }
}
