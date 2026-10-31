namespace Unciv.Net.Relay;

/// <summary>
/// T354: the helpers around <see cref="RelayRoute"/>, the relay endpoint kind of a host-migration successor (design/RELAY.md section 5). A route comes from a peer (the candidate, then the host, then the clients), so every
/// consumer normalises it first: a plain <c>host:port</c> of a relay (never a fingerprint, path or query) and a well-formed room code.
/// </summary>
public static class RelayRoutes
{
    public const int MaxRelayLength = 260;

    /// <summary>The canonical form (lower-case relay <c>host:port</c> with the port always present, room in display form), or null when <paramref name="route"/> is not a usable route.</summary>
    public static RelayRoute? Normalize(RelayRoute? route)
    {
        if (route == null || route.Relay == null || route.Room == null || route.Relay.Length is 0 or > MaxRelayLength) return null;
        var text = route.Relay.Trim();
        if (!text.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or ':' or '[' or ']')) return null; // no scheme, path, query or fingerprint
        if (!RelayEndpoint.TryParse(RelayEndpoint.Scheme + text, out var endpoint) || endpoint == null) return null;
        if (RelayProtocol.NormalizeRoom(route.Room) is not { } room) return null;
        return new RelayRoute { Relay = endpoint.Key, Room = RelayProtocol.FormatRoom(room) };
    }

    /// <summary>The route of <paramref name="room"/> on <paramref name="relay"/> (its certificate pin is not part of a route).</summary>
    public static RelayRoute Of(RelayEndpoint relay, string room) =>
        new() { Relay = relay.Key, Room = RelayProtocol.NormalizeRoom(room) is { } canonical ? RelayProtocol.FormatRoom(canonical) : room };

    /// <summary>The relay a normalised route names, without any certificate pin.</summary>
    public static RelayEndpoint? EndpointOf(RelayRoute route) =>
        RelayEndpoint.TryParse(RelayEndpoint.Scheme + route.Relay, out var endpoint) ? endpoint : null;

    public static bool Same(RelayRoute? a, RelayRoute? b) =>
        a == null || b == null ? a == null && b == null
            : Normalize(a) is { } x && Normalize(b) is { } y && string.Equals(x.Relay, y.Relay, StringComparison.Ordinal) && string.Equals(x.Room, y.Room, StringComparison.Ordinal);
}
