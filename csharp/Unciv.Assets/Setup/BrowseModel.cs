using System.Globalization;
using Unciv.Net;
using Unciv.Net.Identity;
using Unciv.Net.Lobby;
using Unciv.Net.Relay;

namespace Unciv.GodotClient.Setup;

/// <summary>Where the Browse games screen is in its life; each state has its own sentence (<see cref="BrowseModel.StateText"/>) and none is mistaken for another.</summary>
public enum BrowseStatus
{
    /// <summary>No relay address is set: the browser has nothing to ask. There is no default relay.</summary>
    NoRelay,
    Loading,
    /// <summary>The relay could not be reached or did not answer.</summary>
    Offline,
    /// <summary>The relay speaks an older protocol than this game.</summary>
    TooOld,
    /// <summary>The relay runs without a directory.</summary>
    ListingOff,
    RateLimited,
    /// <summary>Something else went wrong; the sentence has the detail.</summary>
    Failed,
    /// <summary>The relay answered and lists no game at all.</summary>
    Empty,
    /// <summary>Games exist but the search and filters hide all of them.</summary>
    NoMatch,
    Ready,
}

/// <summary>What the browser knows about this machine, as plain values so tests and scenarios supply fixed ones.</summary>
/// <param name="Proto">The wire protocol this game speaks; a listing with another one is greyed out.</param>
/// <param name="BaseRulesets">The base rulesets installed.</param>
/// <param name="Mods">The mods installed.</param>
/// <param name="LocalRulesetHash">The ruleset hash this machine computes for a ruleset and mod list, or null when it cannot (something is not installed). Equal to the listing's hash means the very same rules.</param>
/// <param name="IsKnownFingerprint">True when the player has joined a host with this key before (a pin in the known hosts).</param>
public sealed record BrowseContext(int Proto, IReadOnlyCollection<string> BaseRulesets, IReadOnlyCollection<string> Mods, Func<string, IReadOnlyList<string>, string?> LocalRulesetHash, Func<string, bool> IsKnownFingerprint);

/// <summary>One game of the list as the screen shows it, with the words already chosen.</summary>
/// <param name="Game">The verified listing.</param>
/// <param name="Title">The game name.</param>
/// <param name="Subtitle">Host, rules, speed and the password flag, on one line.</param>
/// <param name="Trailing">Players over slots, as the host reports them.</param>
/// <param name="Detail">The full text of the detail pane.</param>
/// <param name="KnownHost">The host key was seen before (a pin in the known hosts).</param>
/// <param name="MissingMods">Mods of the game that are not installed here.</param>
/// <param name="DisabledReason">Why the game cannot be joined (another protocol, no free slot, a ruleset this machine lacks), else null. The row is greyed out and shows the reason.</param>
/// <param name="Note">A hint that does not stop a join (mods that will be offered for download, rules that differ), else null.</param>
public sealed record BrowseRow(ListedGame Game, string Title, string Subtitle, string Trailing, string Detail, string? DisabledReason, string? Note, bool KnownHost, bool MissingMods)
{
    public bool CanJoin => DisabledReason == null;
}

/// <summary>
/// T339 (design/RELAY.md section 7, R10, R16): the words of the Browse games screen, with no UI in them. Every number about players is labelled as the host's claim (the relay and the directory cannot check it), the host
/// key is shown short with "first time you see this host" or "known host", and a game this client cannot join is explained, never hidden. Joining a stranger's game is trust on first use; <see cref="JoinPrompt"/> says so.
/// </summary>
public static class BrowseModel
{
    public const string FirstTime = "First time you see this host";
    public const string KnownHost = "Known host (you have played with this key before)";

    /// <summary>Builds the row for a verified listing.</summary>
    public static BrowseRow Describe(ListedGame game, BrowseContext context)
    {
        var info = game.Info;
        bool known = context.IsKnownFingerprint(game.Fingerprint);
        var missingMods = info.Mods.Where(it => !context.Mods.Contains(it)).ToList();
        bool haveBase = context.BaseRulesets.Contains(info.Ruleset);
        string? disabled = null;
        if (info.Proto != context.Proto) disabled = $"Needs another game version (protocol {Number(info.Proto)}, yours is {Number(context.Proto)})";
        else if (!haveBase) disabled = $"Needs the ruleset {info.Ruleset}, which you do not have";
        else if (info.Slots > 0 && info.Players >= info.Slots && !info.Started) disabled = "The game is full";
        string? note = null;
        if (disabled == null)
        {
            if (missingMods.Count > 0) note = $"Uses {(missingMods.Count == 1 ? "a mod" : "mods")} you do not have ({string.Join(", ", missingMods)}). Joining offers to download {(missingMods.Count == 1 ? "it" : "them")} first.";
            else if (context.LocalRulesetHash(info.Ruleset, info.Mods) is { } mine && mine != info.RulesetHash) note = "Your copy of these rules differs from the host's (another mod version?). Joining offers to download the host's mods or tells you what differs.";
        }
        var rules = info.Mods.Count == 0 ? info.Ruleset : $"{info.Ruleset} + {Number(info.Mods.Count)} {(info.Mods.Count == 1 ? "mod" : "mods")}";
        var subtitle = string.Join(" · ", new[] { info.HostName, info.PasswordRequired ? "password" : null, rules, info.Speed }.Where(it => it != null));
        return new BrowseRow(game, info.Name, subtitle, PlayersShort(info), DetailText(game, known, note, disabled), disabled, note, known, missingMods.Count > 0);
    }

    /// <summary>The context for this machine: the installed base rulesets and mods of <paramref name="form"/>, the hash the engine computes for a ruleset and mods, and the pins of <paramref name="known"/>.</summary>
    public static BrowseContext ContextFor(NewGameForm form, KnownHosts known)
    {
        var bases = form.BaseRulesets;
        var mods = form.AvailableMods;
        return new BrowseContext(ProtocolVersion.Current, bases, mods, (ruleset, listed) => RulesetHash(ruleset, listed, bases, mods), known.IsFingerprintKnown);
    }

    /// <summary>The ruleset hash of <paramref name="ruleset"/> with <paramref name="mods"/> as the engine computes it (what a join sends), or null when the ruleset or a mod is not installed.</summary>
    public static string? RulesetHash(string ruleset, IReadOnlyList<string> mods, IReadOnlyCollection<string> installedBases, IReadOnlyCollection<string> installedMods)
    {
        if (!installedBases.Contains(ruleset) || mods.Any(it => !installedMods.Contains(it))) return null;
        return new LobbySettings { Ruleset = ruleset, Mods = [.. mods] }.ComputeRulesetHash();
    }

    /// <summary>The relay invite that joins <paramref name="game"/> through <paramref name="relay"/> and checks the game's TLS against the key the listing was signed with.</summary>
    public static string InviteFor(ListedGame game, RelayEndpoint relay) => InviteCode.FormatRelay(relay.Host, relay.Port, game.Room, game.Fingerprint);

    /// <summary>The player count for the trailing column: <c>2/4</c>, or <c>started</c> for a game in progress with no open human slot.</summary>
    public static string PlayersShort(ListingInfo info) => info.Slots == 0 ? (info.Started ? "started" : "0") : $"{Number(info.Players)}/{Number(info.Slots)}";

    private static string DetailText(ListedGame game, bool known, string? note, string? disabled)
    {
        var info = game.Info;
        var lines = new List<string>
        {
            info.Name,
            $"Host: {info.HostName}",
            $"Host key: {game.ShortFingerprint}... - {(known ? KnownHost : FirstTime)}",
            $"Players: {Number(info.Players)} of {Number(info.Slots)} human slots, {Number(info.Ai)} AI (as reported by the host; the relay cannot check it)",
            $"Rules: {info.Ruleset}{(info.Mods.Count > 0 ? "; mods: " + string.Join(", ", info.Mods) : "")}",
            $"Speed: {info.Speed}; map: {info.Map}; turns: {info.TurnMode}",
            $"Password: {(info.PasswordRequired ? "required" : "none")}",
            $"State: {(info.Started ? "game in progress" : "waiting in the lobby")}",
            $"Game version {info.Version} (protocol {Number(info.Proto)})",
            $"Listed for {AgeText(game.AgeSeconds)} (the relay's clock)",
        };
        if (disabled != null) lines.Add("Cannot join: " + disabled + ".");
        else if (note != null) lines.Add(note);
        return string.Join("\n", lines);
    }

    /// <summary>The age as the relay reports it, in the largest sensible unit.</summary>
    public static string AgeText(long seconds) => seconds switch
    {
        < 60 => "under a minute",
        < 3600 => $"{Number(seconds / 60)} min",
        _ => $"{Number(seconds / 3600)} h {Number(seconds % 3600 / 60)} min",
    };

    /// <summary>What the join dialog says before anything is sent: whose key is trusted and what happens on a first join (SERVER.md 6.6, RELAY.md 9).</summary>
    public static string JoinPrompt(BrowseRow row)
    {
        var info = row.Game.Info;
        var lines = new List<string>
        {
            $"{info.Name} - hosted by {info.HostName}.",
            $"Host key {row.Game.ShortFingerprint}...: {(row.KnownHost ? KnownHost : FirstTime)}.",
        };
        if (!row.KnownHost)
            lines.Add("Joining a game you found here trusts that key from now on, like any first join to a host you have not met: the name and the key come from whoever listed the game, and anyone can list a game. Only join if you expect this game, and ask the host to confirm the key if you can.");
        if (row.Note != null) lines.Add(row.Note);
        if (info.PasswordRequired) lines.Add("This game needs a password.");
        return string.Join("\n\n", lines);
    }

    /// <summary>The sentence under the empty list (or in place of it) for a state.</summary>
    public static string StateText(BrowseStatus status, string? detail = null) => status switch
    {
        BrowseStatus.NoRelay => "No relay is set, so there is no game list to show. Set the relay address in Options (ask a friend which relay they use), or join with an invite or room code.",
        BrowseStatus.Loading => "Asking the relay for games ...",
        BrowseStatus.Offline => detail ?? "The relay could not be reached. Check the relay address in Options and your internet connection, then Refresh.",
        BrowseStatus.TooOld => "This relay is older than your game and cannot show its game list. Tell whoever runs it.",
        BrowseStatus.ListingOff => "This relay does not publish a game list. You can still join a friend's game with an invite or room code.",
        BrowseStatus.RateLimited => "The relay asked you to slow down. Wait a minute and Refresh.",
        BrowseStatus.Failed => detail ?? "The relay's answer could not be used.",
        BrowseStatus.Empty => "Nobody is listing a game on this relay right now. Host one and tick \"List this game publicly\", or refresh in a moment.",
        BrowseStatus.NoMatch => "No game matches the search and filters. Clear them to see the whole list.",
        _ => "",
    };

    /// <summary>Maps the wire code of a failed relay request (<c>RelayCodes</c>) to a state and its sentence.</summary>
    public static (BrowseStatus Status, string? Detail) FromRelayCode(string code) => code switch
    {
        "relay-listing-off" => (BrowseStatus.ListingOff, null),
        "relay-too-old" => (BrowseStatus.TooOld, null),
        "relay-rate-limited" or "relay-banned" => (BrowseStatus.RateLimited, null),
        "relay-unreachable" or "relay-certificate" or "relay-no-answer" => (BrowseStatus.Offline, code == "relay-no-answer" ? "The relay did not answer in time. Refresh in a moment." : JoinModel.RefusalText(code)),
        _ => (BrowseStatus.Failed, JoinModel.RefusalText(code)),
    };

    /// <summary>The header line about the relay: its name, who to ask, how many games and the measured round trip.</summary>
    public static string RelayLine(string name, string contact, int listed, long pingMillis) =>
        $"{name}{(contact.Length > 0 ? " - " + contact : "")} - {Number(listed)} {(listed == 1 ? "game" : "games")} listed - ping {Number(pingMillis)} ms";

    /// <summary>The text of the host screen checkbox explanation, by whether it can be ticked.</summary>
    public static string ListingHelp(bool relaySet, bool relayReach) =>
        !relaySet ? "To list this game publicly, set a relay address in Options first."
        : !relayReach ? "To list this game publicly, turn on \"Reach me through a relay\" in Options."
        : "Anyone using this relay can see the game's name, your name, the rules, the speed and how many players have joined (never your address or password). Off by default; the listing ends when you stop hosting.";

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
}
