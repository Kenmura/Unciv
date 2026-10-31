using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Unciv.Client.Net.Session;
using Unciv.Net;
using Unciv.Net.Identity;
using Unciv.Net.Lobby;
using Unciv.Net.Relay;
using Unciv.Server;
using Unciv.Server.Transport;

namespace Unciv.Client.Net.Hosting;

/// <summary>Hosting could not start; the message is for the player (port in use, host seat refused, saved game unreadable).</summary>
public sealed class HostingException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// C034: the Host button's engine side. <see cref="StartLobbyAsync"/> opens a lobby in this process (T204 <see cref="ServerLobby"/>), listens for friends on TLS/TCP (T179) and seats the
/// host player in the first slot through the in-memory loopback, exactly like any joiner (so the host sees the same <see cref="LobbyState"/> the friends see).
/// <see cref="ResumeAsync"/> instead reopens a saved hosted game directory with <see cref="InProcessHost"/> (T205) and listens again. Either way <see cref="Invite"/> builds the
/// <c>unciv://host:port/fingerprint</c> string for an address. <see cref="DisposeAsync"/> stops listening and shuts the host down cleanly; the certificate and identity stay the caller's.
/// </summary>
public sealed class HostingSession : IAsyncDisposable
{
    private readonly CancellationTokenSource cts = new();
    private readonly TcpTlsListener? listener;
    private readonly Task? acceptLoop;
    private readonly RelayListener? lobbyRelay;
    private readonly Task? relayAcceptLoop;
    private bool disposed;

    private HostingSession(string fingerprint, int port, ServerLobby? lobby, ClientSession? hostPlayer, TcpTlsListener? listener, InProcessHost? resumed, string? directory, RelayListener? lobbyRelay = null)
    {
        this.lobbyRelay = lobbyRelay;
        Fingerprint = fingerprint;
        Port = port;
        Lobby = lobby;
        HostPlayer = hostPlayer;
        this.listener = listener;
        Resumed = resumed;
        Directory = directory;
        if (listener != null && lobby != null) acceptLoop = AcceptAsync(listener, lobby, cts.Token);
        if (lobbyRelay != null && lobby != null) relayAcceptLoop = AcceptRelayAsync(lobbyRelay, lobby, cts.Token);
    }

    /// <summary>T338: the relay registration of this game (null without a relay). Its room code arrives a moment after hosting starts, and again if the relay assigns a new one.</summary>
    public RelayListener? Relay => lobbyRelay ?? Resumed?.Relay;

    /// <summary>T338: the invite that works from anywhere through the relay (needs <see cref="Relay"/> registered), or null before the relay answered or without a relay.</summary>
    public string? RelayInvite(string? token = null) => Relay?.Invite(token);

    /// <summary>T338: publishes (or with null removes) the public listing of this game on the relay; no effect without a relay.</summary>
    public void SetListing(RelayListing? listing) => Relay?.SetListing(listing);

    /// <summary>The key fingerprint friends verify (it is part of every invite).</summary>
    public string Fingerprint { get; }

    /// <summary>The TCP port being listened on.</summary>
    public int Port { get; }

    /// <summary>The lobby of a new game; null for a resumed one.</summary>
    public ServerLobby? Lobby { get; }

    /// <summary>The host player's own session in the lobby; null for a resumed game.</summary>
    public ClientSession? HostPlayer { get; }

    /// <summary>The reopened game of a resumed session; null for a new lobby.</summary>
    public InProcessHost? Resumed { get; }

    /// <summary>The game directory of a resumed session; null for a new lobby.</summary>
    public string? Directory { get; }

    /// <summary>What the host sees of the lobby right now: the slots as the host's session received them (empty until the first lobby state).</summary>
    public IReadOnlyList<LobbySlot> Slots => HostPlayer?.Lobby?.Slots ?? [];

    /// <summary>The invite string for an address of this machine (or a public name/address the host typed).</summary>
    public string Invite(string address) => InviteCode.Format(address, Port, Fingerprint);

    /// <summary>
    /// Opens the lobby with <paramref name="settings"/>, listens on <paramref name="port"/> (0 picks a free port) and returns once the host sits in slot 1.
    /// </summary>
    /// <exception cref="HostingException">the port is taken or the lobby refused the host.</exception>
    public static async Task<HostingSession> StartLobbyAsync(LobbySettings settings, ClientIdentity identity, X509Certificate2 certificate, string displayName, int port, RelayEndpoint? relayEndpoint = null, RelayListenerOptions? relayOptions = null)
    {
        var fingerprint = HostCertificate.GetFingerprint(certificate);
        var lobby = new ServerLobby(settings, hostFingerprint: fingerprint, hostName: displayName);
        TcpTlsListener? tcp = null;
        ClientSession? hostPlayer = null;
        RelayListener? relay = null;
        try
        {
            tcp = new TcpTlsListener(certificate, port);
            try
            {
                tcp.Start();
            }
            catch (SocketException e)
            {
                throw new HostingException($"Port {port} cannot be used ({e.SocketErrorCode}). Another program may be using it; choose a different port.", e);
            }

            var (clientEnd, hostEnd) = InMemoryTransport.CreatePair();
            _ = lobby.AttachConnectionAsync(hostEnd);
            hostPlayer = new ClientSession(_ => Task.FromResult<ITransport>(clientEnd), new ClientSessionOptions
            {
                Identity = identity,
                DisplayName = displayName,
                RulesetHash = settings.ComputeRulesetHash(),
                ExpectedFingerprint = fingerprint,
                Password = settings.Password,
                MaxReconnectAttempts = 0,
            });
            var joined = await hostPlayer.JoinAsync().ConfigureAwait(false);
            if (!joined.Joined) throw new HostingException($"The lobby did not seat you as the host ({joined.Code}).");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                while (hostPlayer.Lobby == null)
                {
                    if (!await hostPlayer.Events.WaitToReadAsync(timeout.Token).ConfigureAwait(false)) break;
                    hostPlayer.Events.TryRead(out _);
                }
            }
            catch (OperationCanceledException)
            {
                // fall through to the check below
            }
            if (hostPlayer.Lobby == null) throw new HostingException("The lobby did not answer.");
            if (relayEndpoint != null)
            {
                relay = new RelayListener(relayEndpoint, certificate, RelayListener.NewGameId(), relayOptions); // a fresh game id: this lobby's room
                relay.Start();
            }
            return new HostingSession(fingerprint, tcp.Port, lobby, hostPlayer, tcp, null, null, relay);
        }
        catch
        {
            if (relay != null) await relay.DisposeAsync().ConfigureAwait(false);
            if (hostPlayer != null) await hostPlayer.DisposeAsync().ConfigureAwait(false);
            tcp?.Stop();
            await lobby.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Reopens the saved game in <paramref name="directory"/> (the player's key must own a civ in it) and listens for friends on <paramref name="port"/>.</summary>
    /// <exception cref="HostingException">the directory is not a readable game of this player, or the port is taken.</exception>
    public static Task<HostingSession> ResumeAsync(string directory, ClientIdentity identity, X509Certificate2 certificate, int port, RelayEndpoint? relayEndpoint = null, RelayListenerOptions? relayOptions = null)
    {
        InProcessHost host;
        try
        {
            host = InProcessHost.ResumeFromDirectory(directory, identity.PlayerKey);
        }
        catch (Exception e) when (e is Unciv.Net.Persistence.GameStoreException or IOException or System.Text.Json.JsonException)
        {
            throw new HostingException("This saved game cannot be hosted: " + e.Message, e);
        }
        try
        {
            host.Listen(certificate, port, relayEndpoint, relayOptions);
        }
        catch (SocketException e)
        {
            host.StopAsync().GetAwaiter().GetResult();
            throw new HostingException($"Port {port} cannot be used ({e.SocketErrorCode}). Another program may be using it; choose a different port.", e);
        }
        return Task.FromResult(new HostingSession(host.Fingerprint, host.Port ?? port, null, null, null, host, directory));
    }

    /// <summary>T273: wraps the host a <see cref="StandbyPromotion"/> started, so the successor's UI treats it like a resumed hosted game (invite, quit).</summary>
    public static HostingSession Adopt(InProcessHost promoted, string directory) =>
        new(promoted.Fingerprint, promoted.Port ?? 0, null, null, null, promoted, directory);

    private static async Task AcceptAsync(TcpTlsListener tcp, ServerLobby lobby, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            ITransport transport;
            try
            {
                transport = await tcp.AcceptTransportAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) when (token.IsCancellationRequested) { return; }
            catch (Exception) { continue; } // a failed handshake costs only that connection
            _ = lobby.AttachConnectionAsync(transport, token);
        }
    }

    private static async Task AcceptRelayAsync(RelayListener relay, ServerLobby lobby, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            ITransport transport;
            try
            {
                transport = await relay.AcceptTransportAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            _ = lobby.AttachConnectionAsync(transport, token);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        listener?.Stop();
        if (lobbyRelay != null) await lobbyRelay.DisposeAsync().ConfigureAwait(false);
        await cts.CancelAsync().ConfigureAwait(false);
        if (acceptLoop != null)
        {
            try { await acceptLoop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        if (relayAcceptLoop != null)
        {
            try { await relayAcceptLoop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        if (HostPlayer != null) await HostPlayer.DisposeAsync().ConfigureAwait(false);
        if (Lobby != null) await Lobby.DisposeAsync().ConfigureAwait(false);
        if (Resumed != null) await Resumed.StopAsync().ConfigureAwait(false);
        cts.Dispose();
    }
}
