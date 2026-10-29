using Unciv.Net;

namespace Unciv.Assets;

/// <summary>One line of the in-game chat; <see cref="Sender"/> is the name the host bound to the seat (the host overwrites whatever the sender claimed).</summary>
public sealed record ChatLine(string Sender, string Text);

/// <summary>What a player-list row says about a seat.</summary>
public enum SeatStatus
{
    Online,
    Offline,

    /// <summary>The host moved the seat to a new key; the civ plays as AI until that device connects.</summary>
    AwaitingDevice,

    /// <summary>The host kicked the player: nobody holds the key, the civ plays as AI.</summary>
    Released,
}

/// <summary>One row of the player list. <see cref="CanKick"/> and <see cref="CanRebind"/> are true only for the host, never on the host's own row.</summary>
public sealed record PlayerRow(string CivId, string Name, SeatStatus Status, bool IsYou, bool CanKick, bool CanRebind);

/// <summary>
/// C045: the in-game social panel as plain data (Godot-free, unit-tested): chat lines the panel keeps, and the player list built from the host's roster
/// (<see cref="LobbyState"/>, the same message the lobby uses). It decides nothing: the host enforces every limit and every admin action.
/// </summary>
public static class SocialModel
{
    /// <summary>Chat lines kept on screen; older ones are dropped.</summary>
    public const int MaxChatLines = 100;

    /// <summary>The longest line the host forwards (it cuts longer ones; the panel stops typing here).</summary>
    public const int MaxChatChars = 500;

    /// <summary>The text to send for what was typed (trimmed, cut to <see cref="MaxChatChars"/>), or null when it is blank.</summary>
    public static string? Clean(string? typed)
    {
        var text = typed?.Trim() ?? "";
        if (text.Length == 0) return null;
        return text.Length > MaxChatChars ? text[..MaxChatChars] : text;
    }

    /// <summary>The log with <paramref name="line"/> appended, oldest lines dropped beyond <see cref="MaxChatLines"/>.</summary>
    public static IReadOnlyList<ChatLine> Append(IReadOnlyList<ChatLine> log, ChatLine line)
    {
        var result = new List<ChatLine>(log.Count + 1);
        result.AddRange(log.Count >= MaxChatLines ? log.Skip(log.Count - MaxChatLines + 1) : log);
        result.Add(line);
        return result;
    }

    /// <summary>The players of the game in slot order from the host's roster; empty before the host sent one.</summary>
    public static IReadOnlyList<PlayerRow> Players(LobbyState? roster, string viewer, bool isHost)
    {
        if (roster == null) return [];
        var rows = new List<PlayerRow>();
        foreach (var slot in roster.Slots)
        {
            if (string.IsNullOrEmpty(slot.Nation)) continue;
            bool human = slot.Kind == "Human";
            bool bound = !string.IsNullOrEmpty(slot.PlayerKeyId);
            if (!human && !bound && string.IsNullOrEmpty(slot.PlayerName)) continue; // an AI civ nobody ever held
            var status = human ? slot.Connected == true ? SeatStatus.Online : SeatStatus.Offline : bound ? SeatStatus.AwaitingDevice : SeatStatus.Released;
            bool you = slot.Nation == viewer;
            rows.Add(new PlayerRow(slot.Nation, string.IsNullOrEmpty(slot.PlayerName) ? slot.Nation : slot.PlayerName, status, you, isHost && !you && human, isHost && !you));
        }
        return rows;
    }

    /// <summary>The words for a refusal code of kick or rebind.</summary>
    public static string Describe(string code) => code switch
    {
        "turn-resolving" => "the host is resolving the turn, try again in a moment",
        "bad-key" => "that is not a player key",
        "key-in-use" => "that key already holds a seat",
        "unchanged" => "that is already this seat's key",
        "own-seat" => "you cannot do that to your own seat",
        "not-bound" or "unknown-civ" => "that seat has no player",
        "not-allowed" => "that is not possible right now (the game needs at least one human player)",
        _ => code,
    };
}
