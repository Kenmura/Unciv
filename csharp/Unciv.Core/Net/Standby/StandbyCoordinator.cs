using Unciv.Net.Identity;
using Unciv.Net.Turns;

namespace Unciv.Net.Standby;

/// <summary>
/// T281: the host's side of backup-host designation (design/HOST-MIGRATION.md sections 1 and 3), shared by the lobby host and the game host. It knows who is designated, whether they consented, and whether
/// their announced endpoint passed the probe; it sends nothing and persists nothing itself (the hosts do, from <see cref="ToRecord"/>). Every state change goes through here, and every candidate message is
/// judged by the key the host authenticated, never by what the frame says.
/// </summary>
public sealed class StandbyCoordinator
{
    private readonly Lock gate = new();
    private readonly IClock clock;
    private StandbyLimits limits;
    private string? keyId;
    private bool accepted;
    private bool reachable;
    private List<string> endpoints = [];
    private string fingerprint = "";
    private int probeCursor;
    private long lastProbeMillis = long.MinValue;
    private int generation;
    private string? replication;
    private int lagEntries;

    public StandbyCoordinator(IClock? clock = null, IEndpointProber? prober = null, StandbyLimits? limits = null)
    {
        this.clock = clock ?? SystemClock.Instance;
        Prober = prober ?? new NullEndpointProber();
        this.limits = limits ?? new StandbyLimits();
    }

    /// <summary>The tunables this coordinator (and the replication stream that reports to it) runs with.</summary>
    public StandbyLimits Limits
    {
        get => limits;
        set => limits = value;
    }

    /// <summary>How an endpoint is dialled; set by the network layer that owns the host.</summary>
    public IEndpointProber Prober { get; set; }

    /// <summary>The designated candidate's player key, or null.</summary>
    public string? KeyId
    {
        get { lock (gate) return keyId; }
    }

    public string State
    {
        get { lock (gate) return StateLocked(); }
    }

    private string StateLocked() => keyId == null ? StandbyStates.None : !accepted ? StandbyStates.Offered : !reachable ? StandbyStates.Unreachable : replication ?? StandbyStates.Ready;

    /// <summary>How many log entries the subscribed successor is behind the host (0 while it is not subscribed).</summary>
    public int LagEntries
    {
        get { lock (gate) return keyId == null ? 0 : lagEntries; }
    }

    /// <summary>True when <paramref name="key"/> is the designated key and it consented (it may subscribe to the replication stream).</summary>
    public bool IsAcceptedCandidate(string? key)
    {
        lock (gate) return keyId != null && accepted && key != null && string.Equals(keyId, key, StringComparison.Ordinal);
    }

    /// <summary>
    /// T272: the host's replication stream reports where it stands: <see cref="StandbyStates.Syncing"/>, <see cref="StandbyStates.Ready"/> (caught up) or <see cref="StandbyStates.Lagging"/>, or null when
    /// there is no stream (the state then only reflects the probe). Returns true when the visible state changed, so the host can tell the candidate and the clients.
    /// </summary>
    public bool SetReplication(string? state, int lag)
    {
        lock (gate)
        {
            var before = StateLocked();
            replication = state;
            lagEntries = state == null ? 0 : Math.Max(0, lag);
            return !string.Equals(before, StateLocked(), StringComparison.Ordinal);
        }
    }

    /// <summary>True when <paramref name="key"/> is the designated key (constant-time not needed: the key id is public in the lobby).</summary>
    public bool IsDesignated(string? key)
    {
        lock (gate) return keyId != null && key != null && string.Equals(keyId, key, StringComparison.Ordinal);
    }

    /// <summary>Offers the role to <paramref name="candidateKeyId"/>, replacing any earlier designation (which ends: the old candidate must be told, <paramref name="previousKeyId"/>). Returns false when nothing changed.</summary>
    public bool Designate(string candidateKeyId, out string? previousKeyId)
    {
        lock (gate)
        {
            previousKeyId = keyId;
            if (string.Equals(keyId, candidateKeyId, StringComparison.Ordinal)) return false;
            ResetLocked();
            keyId = candidateKeyId;
            return true;
        }
    }

    /// <summary>Ends the designation (host revoked, seat vacated). Returns the key that was designated, or null when there was none.</summary>
    public string? Revoke()
    {
        lock (gate)
        {
            var previous = keyId;
            ResetLocked();
            return previous;
        }
    }

    /// <summary>The candidate declines or withdraws. <see cref="ErrorCodes.NotDesignated"/> from anyone else (also while nobody is designated), null on success.</summary>
    public string? Decline(string? senderKeyId)
    {
        lock (gate)
        {
            if (keyId == null || senderKeyId == null || !string.Equals(keyId, senderKeyId, StringComparison.Ordinal)) return ErrorCodes.NotDesignated;
            ResetLocked();
            return null;
        }
    }

    /// <summary>
    /// The candidate accepts. Judged in this order: the sender must be the designated key (<see cref="ErrorCodes.NotDesignated"/>, the same code whatever the reason), the endpoints and port must be usable
    /// (<see cref="ErrorCodes.InvalidEndpoint"/>), then one endpoint is probed unless a probe ran in the last minute (<see cref="ErrorCodes.ProbeRateLimited"/>; the acceptance itself stays recorded and the state is
    /// <see cref="StandbyStates.Unreachable"/>). A failed probe is <see cref="ErrorCodes.ProbeFailed"/> or <see cref="ErrorCodes.ProbeFingerprint"/>. Null when the standby is ready.
    /// </summary>
    public async Task<string?> AcceptAsync(string? senderKeyId, StandbyAccept accept, CancellationToken cancellationToken = default)
    {
        int gen;
        lock (gate)
        {
            if (keyId == null || senderKeyId == null || !string.Equals(keyId, senderKeyId, StringComparison.Ordinal)) return ErrorCodes.NotDesignated;
            var normalized = Normalize(accept);
            var normalizedFingerprint = HostKey.Normalize(accept.Fingerprint);
            if (normalized == null || normalizedFingerprint == null) return ErrorCodes.InvalidEndpoint;
            if (accepted && endpoints.SequenceEqual(normalized, StringComparer.Ordinal) && string.Equals(fingerprint, normalizedFingerprint, StringComparison.Ordinal))
            {
                // A repeated accept (client retry) changes nothing: it must not cancel a probe in flight or undo a ready standby.
                if (reachable) return null;
                gen = generation;
            }
            else
            {
                endpoints = normalized;
                fingerprint = normalizedFingerprint;
                accepted = true;
                reachable = false;
                probeCursor = 0;
                gen = ++generation;
            }
        }
        return await ProbeCoreAsync(gen, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Probes the announced endpoint again (the host's own periodic or manual re-check). Same one-per-minute limit; null when ready.</summary>
    public Task<string?> ReprobeAsync(CancellationToken cancellationToken = default)
    {
        int gen;
        lock (gate)
        {
            if (keyId == null || !accepted) return Task.FromResult<string?>(ErrorCodes.NotDesignated);
            gen = generation;
        }
        return ProbeCoreAsync(gen, cancellationToken);
    }

    /// <summary>T282: true when the periodic re-probe is due (a designated, accepted standby that was last probed at least <see cref="StandbyLimits.ReprobeIntervalMillis"/> ago, or never since this process started).</summary>
    public bool ReprobeDue
    {
        get
        {
            lock (gate)
            {
                if (keyId == null || !accepted) return false;
                return lastProbeMillis == long.MinValue || clock.NowMillis - lastProbeMillis >= Math.Max(limits.ReprobeIntervalMillis, limits.ProbeMinIntervalMillis);
            }
        }
    }

    /// <summary>
    /// T282: the periodic re-probe. Probes when <see cref="ReprobeDue"/>; returns true when the visible state changed (ready to unreachable on loss, back again), so the host announces it. A probe that was
    /// refused by the rate limit, or whose designation changed meanwhile, reports no change.
    /// </summary>
    public async Task<bool> ReprobeIfDueAsync(CancellationToken cancellationToken = default)
    {
        int gen;
        string before;
        lock (gate)
        {
            if (!ReprobeDue) return false;
            gen = generation;
            before = StateLocked();
        }
        var code = await ProbeCoreAsync(gen, cancellationToken).ConfigureAwait(false);
        int count;
        lock (gate) count = endpoints.Count;
        // The probe tries one endpoint per call: with several announced (a LAN and a public address) one dead one would flip ready/unreachable every interval. The periodic path tries the others before giving up.
        for (var extra = 1; extra < count && code is ErrorCodes.ProbeFailed or ErrorCodes.ProbeFingerprint; extra++)
            code = await ProbeCoreAsync(gen, cancellationToken, followUp: true).ConfigureAwait(false);
        if (code is ErrorCodes.NotDesignated or ErrorCodes.ProbeRateLimited) return false;
        lock (gate)
        {
            return gen == generation && !string.Equals(before, StateLocked(), StringComparison.Ordinal);
        }
    }

    private async Task<string?> ProbeCoreAsync(int gen, CancellationToken cancellationToken, bool followUp = false)
    {
        string endpoint;
        string expected;
        lock (gate)
        {
            if (gen != generation || keyId == null || !accepted) return ErrorCodes.NotDesignated;
            var now = clock.NowMillis;
            if (!followUp && lastProbeMillis != long.MinValue && now - lastProbeMillis < limits.ProbeMinIntervalMillis)
                return ErrorCodes.ProbeRateLimited; // never downgrades a ready standby: a refused probe says nothing about reachability
            lastProbeMillis = now;
            endpoint = endpoints[probeCursor % endpoints.Count];
            probeCursor++;
            expected = fingerprint;
        }

        var separator = endpoint.LastIndexOf(':');
        var host = endpoint[..separator].Trim('[', ']');
        var port = int.Parse(endpoint[(separator + 1)..], System.Globalization.CultureInfo.InvariantCulture);

        string? presented = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(limits.ProbeTimeout);
        try
        {
            presented = await Prober.PresentedFingerprintAsync(host, port, limits.ProbeTimeout, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            presented = null; // closed port, timeout, bad TLS: all the same answer, "nothing usable there"
        }

        lock (gate)
        {
            if (gen != generation) return ErrorCodes.NotDesignated; // revoked or re-accepted while the probe ran
            if (presented == null)
            {
                reachable = false;
                return ErrorCodes.ProbeFailed;
            }
            if (!HostKey.Matches(presented, expected))
            {
                reachable = false;
                return ErrorCodes.ProbeFingerprint;
            }
            reachable = true;
            return null;
        }
    }

    /// <summary>The wire status for the candidate (and the host screen).</summary>
    public StandbyStatus Status() => new() { State = State, LagEntries = LagEntries };

    /// <summary>The announcement for every client. The successor is named only while ready.</summary>
    public HostSuccessor Announcement(int epoch, IEnumerable<SuccessorSeat> roster)
    {
        lock (gate)
        {
            return new HostSuccessor
            {
                Epoch = epoch,
                Successor = keyId != null && accepted && reachable ? new SuccessorInfo { KeyId = keyId, Endpoints = [.. endpoints], Fingerprint = fingerprint } : null,
                Status = StateLocked(),
                Roster = [.. roster],
            };
        }
    }

    /// <summary>The announcement for <see cref="JoinOk"/>: null while nobody is designated (the join of a game without a backup host is unchanged).</summary>
    public HostSuccessor? AnnouncementForJoin(int epoch, IEnumerable<SuccessorSeat> roster)
    {
        lock (gate)
        {
            if (keyId == null) return null;
        }
        return Announcement(epoch, roster);
    }

    /// <summary>What to persist, or null when nobody is designated.</summary>
    public StandbyRecord? ToRecord()
    {
        lock (gate)
        {
            return keyId == null ? null : new StandbyRecord { KeyId = keyId, Accepted = accepted, Endpoints = [.. endpoints], Fingerprint = fingerprint };
        }
    }

    /// <summary>Takes a persisted designation back (after a restart). Never ready: the endpoint must pass the probe again.</summary>
    public void Restore(StandbyRecord? record)
    {
        lock (gate)
        {
            ResetLocked();
            if (record == null || record.KeyId.Length == 0) return;
            keyId = record.KeyId;
            var normalizedFingerprint = HostKey.Normalize(record.Fingerprint);
            accepted = record.Accepted && record.Endpoints.Count > 0 && normalizedFingerprint != null;
            if (accepted && record.Endpoints.Any(e => !ValidStoredEndpoint(e))) accepted = false; // a hand-edited file must not reach the dialler
            if (accepted && normalizedFingerprint != null)
            {
                endpoints = [.. record.Endpoints];
                fingerprint = normalizedFingerprint;
            }
        }
    }

    private static bool ValidStoredEndpoint(string endpoint)
    {
        var separator = endpoint.LastIndexOf(':');
        return separator > 0 && int.TryParse(endpoint[(separator + 1)..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var port) && port is >= 1024 and <= 65535
            && endpoint.Length <= 260 && endpoint[..separator].Trim('[', ']').All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or ':');
    }

    private void ResetLocked()
    {
        replication = null;
        lagEntries = 0;
        keyId = null;
        accepted = false;
        reachable = false;
        endpoints = [];
        fingerprint = "";
        probeCursor = 0;
        generation++;
    }

    /// <summary>The announced endpoints as <c>host:port</c>, or null when the list or port is unusable: 1 to <see cref="StandbyLimits.MaxEndpoints"/> distinct plain host names or IPs, a port of at least <see cref="StandbyLimits.MinPort"/>, a well-formed fingerprint.</summary>
    private List<string>? Normalize(StandbyAccept accept)
    {
        if (accept.Port < limits.MinPort || accept.Port > 65535) return null;
        if (HostKey.Normalize(accept.Fingerprint) == null) return null;
        if (accept.Endpoints.Count == 0 || accept.Endpoints.Count > limits.MaxEndpoints) return null;
        var result = new List<string>();
        foreach (var endpoint in accept.Endpoints)
        {
            var host = endpoint?.Trim().Trim('[', ']');
            if (string.IsNullOrEmpty(host) || host.Length > limits.MaxEndpointLength) return null;
            if (!host.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or ':')) return null;
            var text = (host.Contains(':') ? $"[{host}]" : host) + ":" + accept.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!result.Contains(text, StringComparer.OrdinalIgnoreCase)) result.Add(text);
        }
        return result;
    }
}
