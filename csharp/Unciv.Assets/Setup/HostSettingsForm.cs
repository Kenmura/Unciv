using System.Globalization;
using Unciv.Net.Lobby;

namespace Unciv.GodotClient.Setup;

/// <summary>What <see cref="HostSettingsForm.Build"/> produced: the lobby settings, or the reasons there are none. A refusal is a result, never an exception.</summary>
public sealed record HostSettingsResult(LobbySettings? Settings, IReadOnlyList<string> Errors)
{
    public bool Ok => Settings != null;
}

/// <summary>
/// C034: the state behind the host screen, with no UI in it. The game side (ruleset, mods, speed, difficulty, era, victories, map, civ and city-state counts) is the C015
/// <see cref="NewGameForm"/> itself, so the host screen offers exactly the choices and refusals of the new-game screen; this adds what only a hosted game has: a game name,
/// the AI fill, the turn timer, the join password and the port. <see cref="Build"/> turns it into the <see cref="LobbySettings"/> of T204 (which forces God mode and trade hints off
/// itself when the lobby starts the game). The new-game form's advanced flags other than "no barbarians" (raging barbarians, one city, no city razing, nukes, espionage) have no lobby
/// setting yet, so a hosted game uses the defaults for them.
/// </summary>
public sealed class HostSettingsForm
{
    public const int DefaultPort = 47821;

    /// <summary>The turn timer choices in seconds (0 = no timer), as the host screen offers them.</summary>
    public static readonly IReadOnlyList<int> TimerChoices = [0, 60, 120, 300, 600];

    /// <summary>T252: the shortest turn the dynamic timer may give, in seconds, as the host screen offers them.</summary>
    public static readonly IReadOnlyList<int> MinTimerChoices = [15, 30, 60, 120];

    public HostSettingsForm(NewGameForm game)
    {
        Game = game;
    }

    /// <summary>The new-game form behind the game and map pages. Its first slot is the host's own civ.</summary>
    public NewGameForm Game { get; }

    public string GameName { get; set; } = "Multiplayer Game";

    /// <summary>Fill the civs nobody takes with AI when the game starts.</summary>
    public bool AiFill { get; set; } = true;

    public int TurnTimerSeconds { get; set; } = 120;

    /// <summary>T252: scale each turn's time with the game (era, units, wars) between <see cref="TurnTimerMinSeconds"/> and <see cref="TurnTimerSeconds"/>. Without a timer it has no effect.</summary>
    public bool DynamicTurnTimer { get; set; }

    public int TurnTimerMinSeconds { get; set; } = 30;

    /// <summary>Empty means an open game.</summary>
    public string Password { get; set; } = "";

    /// <summary>A public address or name the host typed for the invite; empty means use an address of this machine.</summary>
    public string PublicAddress { get; set; } = "";

    /// <summary>The TCP port friends connect to. The screen offers 1 to 65535; 0 (a free port) is for tests.</summary>
    public int Port { get; set; } = DefaultPort;

    /// <summary>Label of a timer choice, e.g. "No timer" or "2 min".</summary>
    public static string TimerLabel(int seconds) =>
        seconds == 0 ? "No timer" : seconds % 60 == 0 ? $"{(seconds / 60).ToString(CultureInfo.InvariantCulture)} min" : $"{seconds.ToString(CultureInfo.InvariantCulture)} s";

    /// <summary>The mistakes that can be found without building anything; cheap enough to show after every change.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>(Game.Problems());
        if (string.IsNullOrWhiteSpace(GameName)) problems.Add("The game needs a name.");
        if (TurnTimerSeconds < 0) problems.Add("The turn timer cannot be negative.");
        if (TurnTimerMinSeconds < 0) problems.Add("The shortest turn cannot be negative.");
        if (DynamicTurnTimer && TurnTimerSeconds > 0 && TurnTimerMinSeconds > TurnTimerSeconds) problems.Add("The shortest turn cannot be longer than the turn timer.");
        if (Port is < 0 or > 65535) problems.Add("The port must be a number from 1 to 65535.");
        if (!IsUsableInviteAddress(PublicAddress)) problems.Add("The public address must be a host name or IP address only (no port, slash or spaces).");
        return problems;
    }

    /// <summary>Whether a typed public address can be put in an invite and parsed back: empty (none typed), or a name/IPv4/IPv6 address without port, path or whitespace.</summary>
    public static bool IsUsableInviteAddress(string? address)
    {
        var text = (address ?? "").Trim();
        if (text.Length == 0) return true;
        if (text.Any(c => char.IsWhiteSpace(c) || c is '/' or '\\' or '@' or '?' or '#')) return false;
        if (text.StartsWith('[')) return text.EndsWith(']') && text.IndexOf(']') == text.Length - 1;
        return text.Count(c => c == ':') is 0 or > 1;
    }

    /// <summary>Parses the text of the port field; false (with the message) for anything but a whole number from 1 to 65535.</summary>
    public static bool TryParsePort(string? text, out int port, out string? problem)
    {
        problem = null;
        if (int.TryParse((text ?? "").Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out port) && port is >= 1 and <= 65535) return true;
        port = DefaultPort;
        problem = "The port must be a number from 1 to 65535.";
        return false;
    }

    /// <summary>
    /// The lobby settings the form describes. <paramref name="clockMillis"/> is the map seed when none was typed (see <see cref="NewGameForm.Build"/>); the engine's own setup validation has the last word.
    /// The lobby gets one open slot per civ, the host takes the first when it joins.
    /// </summary>
    public HostSettingsResult Build(long clockMillis)
    {
        var problems = Problems();
        if (problems.Count > 0) return new HostSettingsResult(null, problems);
        var built = Game.Build(clockMillis);
        if (built.Setup is not { } setup) return new HostSettingsResult(null, built.Errors);
        var p = setup.GameParameters;
        var settings = new LobbySettings
        {
            GameName = GameName.Trim(),
            Map = setup.MapParameters,
            Speed = p.Speed,
            Difficulty = p.Difficulty,
            Era = p.StartingEra,
            VictoryTypes = [.. p.VictoryTypes],
            MaxMajorCivs = Game.Slots.Count,
            CityStateCount = p.NumberOfCityStates,
            AiFill = AiFill,
            NoBarbarians = p.NoBarbarians,
            Ruleset = p.BaseRuleset,
            Mods = [.. p.Mods],
            TurnTimerSeconds = TurnTimerSeconds,
            DynamicTurnTimer = DynamicTurnTimer,
            TurnTimerMinSeconds = TurnTimerMinSeconds,
            Password = Password.Length == 0 ? null : Password,
            MaxPlayers = Game.Slots.Count,
        };
        return new HostSettingsResult(settings, []);
    }
}
