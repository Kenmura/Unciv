using Unciv.Net.Relay;

namespace Unciv.Relay;

/// <summary>The relay's use of the shared listing rules (<see cref="ListingRules"/>, which a client runs on what a relay serves).</summary>
public static class ListingValidator
{
    /// <inheritdoc cref="ListingRules.TextOk"/>
    public static bool TextOk(string? text, int min, int max) => ListingRules.TextOk(text, min, max);

    /// <summary>Checks a body and returns its fields and <c>seq</c>, or the reason (always <c>listing-rejected</c> on the wire).</summary>
    internal static (ListingFields? Fields, long Seq, string? Error) Validate(string body)
    {
        var (info, error) = ListingRules.Parse(body);
        return info == null ? (null, 0, error) : (new ListingFields(info.Name, info.HostName, info.Players, info.Slots, info.Ruleset, info.Mods, info.PasswordRequired, info.Started, info.Version), info.Seq, null);
    }
}
