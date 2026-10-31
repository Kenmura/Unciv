using Unciv.Net;
using Unciv.Net.Lobby;
using Unciv.Net.Relay;
using Unciv.Server.Transport;

namespace Unciv.Client.Net.Hosting;

/// <summary>
/// T339: what a host announces about its lobby on the relay directory (design/RELAY.md section 7): names, rules, speed and the player counts, and nothing else (never an address, password or token). The texts are cleaned to the
/// listing rules first, because the relay refuses a listing with a control or bidi character in it and the host should not lose its listing to a funny display name.
/// </summary>
public static class ListingFactory
{
    /// <param name="settings">The lobby's settings as the host started it.</param>
    /// <param name="hostName">The host's display name (cleaned and cut to 32 characters).</param>
    /// <param name="slots">The lobby's slots as the host sees them now: <c>Human</c> and <c>Open</c> slots are the human places, <c>Human</c> the ones taken.</param>
    /// <returns>The listing, or null when the game name cannot be listed (the Host screen refuses it earlier: <c>HostSettingsForm.Problems</c>).</returns>
    public static RelayListing? From(LobbySettings settings, string hostName, IReadOnlyList<LobbySlot> slots)
    {
        var name = ListingRules.Clean(settings.GameName, 40);
        if (!ListingRules.TextOk(name, 3, 40)) return null;
        return new RelayListing
        {
            Name = name,
            HostName = ListingRules.Clean(hostName, 32) is { Length: > 0 } cleaned ? cleaned : "Host",
            Players = slots.Count(it => it.Kind == "Human"),
            Slots = slots.Count(it => it.Kind is "Human" or "Open"),
            Ai = slots.Count(it => it.Kind == "AI"),
            Ruleset = Short(settings.Ruleset, "Civ V - Vanilla"),
            RulesetHash = settings.ComputeRulesetHash(),
            Mods = [.. settings.Mods.Select(it => ListingRules.Clean(it, 200)).Where(it => ListingRules.TextOk(it, 1, 200)).Take(16)],
            Speed = Short(settings.Speed, "Standard"),
            Map = Short($"{settings.Map.Type}, {settings.Map.MapSize.Name}", "-"),
            PasswordRequired = !string.IsNullOrEmpty(settings.Password),
            TurnMode = Short(settings.TurnMode.ToLowerInvariant(), "simultaneous"),
            Started = false,
        };
    }

    private static string Short(string? text, string fallback) => ListingRules.Clean(text ?? "", 64) is { Length: > 0 } cleaned ? cleaned : fallback;
}
