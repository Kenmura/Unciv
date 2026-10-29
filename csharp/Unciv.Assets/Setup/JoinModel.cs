using System.Globalization;
using Unciv.Net.Identity;

namespace Unciv.GodotClient.Setup;

/// <summary>
/// Where a join attempt goes. <see cref="Address"/> is the key into the known hosts (<c>host:port</c>, lower-cased); <see cref="Fingerprint"/> and <see cref="Token"/> come from an invite
/// code and are null for a typed address (which then needs the fingerprint prompt the first time).
/// </summary>
public sealed record JoinTarget(string Host, int Port, string? Fingerprint, string? Token)
{
    public string Address => (Host.Contains(':', StringComparison.Ordinal) ? $"[{Host}]" : Host).ToLowerInvariant() + ":" + Port.ToString(CultureInfo.InvariantCulture);
}

/// <summary>What <see cref="JoinModel.Parse"/> made of the typed text: a target, or the sentence that says what is wrong. A bad input is a result, never an exception.</summary>
public sealed record JoinParse(JoinTarget? Target, string? Problem);

/// <summary>What the reconnect overlay shows: nothing, a waiting message, or a final one with only Leave.</summary>
public sealed record ConnectionView(bool Visible, string Title, string Detail, bool Final);

/// <summary>
/// C006: the words of the join side, with no UI in them. <see cref="Parse"/> accepts an invite code (<c>unciv://host:port/fingerprint[/token]</c>), <c>host:port</c>, a bare host (default port)
/// and bracketed IPv6; <see cref="RefusalText"/> turns a refusal code into a sentence a friend can act on (the host's own refusals reuse <see cref="LobbyModel.RefusalText"/>);
/// <see cref="TrustText"/> and <see cref="KeyChangedText"/> are the fingerprint prompt and the hard stop; <see cref="Connection"/> is the reconnect overlay.
/// </summary>
public static class JoinModel
{
    // The client-side codes of Unciv.Client.Net.Session.ClientCodes, spelled out here because this project must not reference the client library.
    private const string ConnectFailed = "connect-failed";
    private const string ConnectionLost = "connection-lost";
    private const string HostKeyChanged = "host-key-changed";
    private const string HostKeyMismatch = "host-key-mismatch";
    private const string HostUntrusted = "host-untrusted";
    private const string HandshakeTimeout = "handshake-timeout";
    private const string ProtocolMismatch = "protocol-mismatch";
    private const string SessionClosed = "session-closed";

    public static JoinParse Parse(string? text)
    {
        var typed = text?.Trim() ?? "";
        if (typed.Length == 0) return new JoinParse(null, "Type the host's address (for example 192.168.1.20:47821) or paste the invite.");
        if (typed.StartsWith(InviteCode.Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return InviteCode.TryParse(typed, out var invite) && invite != null
                ? new JoinParse(new JoinTarget(invite.Host, invite.Port, invite.Fingerprint, invite.Token), null)
                : new JoinParse(null, "This invite is not valid. Copy it again from the host's screen.");
        }

        string host;
        string? portText = null;
        if (typed.StartsWith('['))
        {
            int close = typed.IndexOf(']', StringComparison.Ordinal);
            if (close < 0) return new JoinParse(null, "The address is missing a closing bracket.");
            host = typed[1..close];
            var rest = typed[(close + 1)..];
            if (rest.Length > 0)
            {
                if (!rest.StartsWith(':')) return new JoinParse(null, "After the bracketed address only :port may follow.");
                portText = rest[1..];
            }
        }
        else
        {
            int colon = typed.LastIndexOf(':');
            // Two or more colons without brackets is a bare IPv6 address, which has no port.
            if (colon >= 0 && typed.IndexOf(':', StringComparison.Ordinal) == colon)
            {
                host = typed[..colon];
                portText = typed[(colon + 1)..];
            }
            else
            {
                host = typed;
            }
        }

        if (host.Length == 0 || host.Any(char.IsWhiteSpace) || host.Contains('/', StringComparison.Ordinal)) return new JoinParse(null, "The host name or address is not valid.");
        int port = InviteCode.DefaultPort;
        if (portText != null && (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535))
            return new JoinParse(null, "The port must be a number from 1 to 65535.");
        return new JoinParse(new JoinTarget(host, port, null, null), null);
    }

    /// <summary>The sentence for a refusal or failure code of the join (client codes and the host's own).</summary>
    public static string RefusalText(string code) => code switch
    {
        ConnectFailed => "Could not reach the host. Check the address and port, that the host is hosting, and that the port is forwarded if you are not on the same network.",
        HandshakeTimeout => "The host did not answer in time. It may be busy or the address may be wrong.",
        ConnectionLost => "The connection to the host was lost.",
        SessionClosed => "You left the game.",
        HostUntrusted => "You did not trust this host, so nothing was sent to it.",
        HostKeyMismatch => "The host's key does not match the invite. Someone may be pretending to be the host. Ask the host for a fresh invite over a channel you trust.",
        HostKeyChanged => KeyChangedText("this host"),
        ProtocolMismatch => "Your game version and the host's cannot talk to each other. Both of you need the same version.",
        _ => LobbyModel.RefusalText(code),
    };

    /// <summary>The fingerprint prompt for a host seen for the first time through a typed address.</summary>
    public static string TrustText(string address, string fingerprint) =>
        $"You have never connected to {address} before.\n\nThe host's key fingerprint is:\n{HostKey.Normalize(fingerprint) ?? fingerprint}\n\nOnly continue if the host told you this exact fingerprint (an invite code carries it for you). If it differs, someone may be sitting between you and the host.";

    /// <summary>The hard stop for a host whose key is not the one pinned earlier. It offers no "continue anyway"; forgetting the old key is a separate, deliberate action.</summary>
    public static string KeyChangedText(string address) =>
        $"The key of {address} has changed since you last joined.\n\nThat happens when the host reinstalled or moved to another computer, but also when someone is pretending to be the host. You were not connected. Ask the host to confirm the change; only then forget the old key and join again.";

    /// <summary>The reconnect overlay for a game in progress.</summary>
    /// <param name="connected">the session is connected (or has no connection state yet).</param>
    /// <param name="hostClosing">the host announced it is shutting down.</param>
    /// <param name="endedReason">set when the session is over for good: the code of the reason.</param>
    public static ConnectionView Connection(bool connected, bool hostClosing, string? endedReason)
    {
        if (endedReason != null)
        {
            string detail = endedReason is ConnectionLost or ConnectFailed
                ? "Could not get back to the host. Your seat is kept: join the same address again later with the same key and you continue where you were."
                : RefusalText(endedReason);
            return new ConnectionView(true, "Disconnected", detail, true);
        }
        if (connected && !hostClosing) return new ConnectionView(false, "", "", false);
        return hostClosing
            ? new ConnectionView(true, "The host went away", "The host is shutting down or restarting. Waiting for it to come back; you will rejoin by yourself with the same key.", false)
            : new ConnectionView(true, "Connection lost", "Trying to reconnect to the host with the same key. Your commands are kept and sent once you are back.", false);
    }
}
