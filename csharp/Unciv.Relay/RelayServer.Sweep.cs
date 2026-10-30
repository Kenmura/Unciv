namespace Unciv.Relay;

public sealed partial class RelayServer
{
    private async Task SweepLoopAsync()
    {
        while (!stopping.IsCancellationRequested)
        {
            try { await Task.Delay(options.SweepIntervalMillis, stopping.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            try { await SweepAsync().ConfigureAwait(false); }
            catch (Exception e) when (e is IOException or InvalidOperationException) { Log.Error("sweep-failed", e.GetType().Name); }
        }
    }

    /// <summary>
    /// One pass of everything time-driven, on the injected clock: silent host channels (90 s), stale listings (30 min), remembered rooms past their ttl, idle or stuck splices,
    /// splices that never started a TLS record, expired bans, changed policy files and certificates, old log files. The background loop calls it every second; tests call it after advancing the clock.
    /// </summary>
    public async Task SweepAsync()
    {
        var now = clock.NowMillis;
        var closeControls = new List<ControlChannel>();
        var closeSplices = new List<(Splice Splice, string Reason)>();
        lock (gate)
        {
            foreach (var room in rooms.Values.ToList())
            {
                if (room.Control is { } control && now - control.LastSeen >= limits.ControlSilenceSeconds * 1000L) closeControls.Add(control);
                if (room.Listing != null && now - room.Listing.LastUpdate >= limits.ListingStaleMinutes * 60_000L) room.Listing = null;
                if (!room.Live && now - room.LastSeen >= limits.RoomTtlHours * 3_600_000L)
                {
                    rooms.Remove(room.Code);
                    room.Dispose();
                }
            }
            foreach (var splice in splices)
            {
                var idle = limits.IdleSeconds * 1000L;
                if (now - splice.LastActivity >= idle || (splice.StuckSince >= 0 && now - splice.StuckSince >= idle)) closeSplices.Add((splice, "idle"));
                else if (!splice.TlsSeen && now - splice.StartedAt >= limits.FirstBytesSeconds * 1000L) closeSplices.Add((splice, "no-tls"));
            }
            foreach (var (network, until) in temporaryBans.ToList()) if (now >= until) temporaryBans.Remove(network);
        }
        foreach (var control in closeControls) await control.CloseAsync("timeout").ConfigureAwait(false);
        foreach (var (splice, reason) in closeSplices) splice.Close(reason);
        handshakes.Cleanup();
        joinRate.Cleanup();
        lookupFailures.Cleanup();
        listingRate.Cleanup();
        httpRate.Cleanup();
        controlRate.Cleanup();
        await ReloadIfChangedAsync(force: false).ConfigureAwait(false);
        Log.Rotate();
    }

    /// <summary>SIGHUP: re-read the certificate and the ban, allow and deny files now, and apply them to live rooms.</summary>
    public Task ReloadAsync() => ReloadIfChangedAsync(force: true);

    private async Task ReloadIfChangedAsync(bool force)
    {
        if (options.TlsMode == RelayTlsMode.Certificate && (force || CertificateStamp() != certificateStamp))
        {
            try
            {
                var fresh = RelayCertificates.LoadPem(options.CertFile!, options.KeyFile!);
                var old = Interlocked.Exchange(ref certificate, fresh);
                certificateStamp = CertificateStamp();
                Log.Info("certificate-reloaded", reason: "ok");
                _ = old; // not disposed: a handshake in flight may still hold it; the finalizer frees it
            }
            catch (InvalidDataException e)
            {
                Log.Warn("certificate-reload-failed", reason: e.GetType().Name);
            }
        }
        if (!policy.Reload(force)) return;
        foreach (var warning in policy.Warnings) Log.Warn("policy-warning", reason: warning);
        Log.Info("policy-reloaded");
        List<ControlChannel> banned = [];
        lock (gate)
        {
            foreach (var room in rooms.Values)
            {
                if (room.Control is not { } control) continue;
                if (policy.IsKeyBanned(room.Spki) || policy.IsAddressBanned(control.Conn.Address) || (room.Listing != null && (policy.IsNameDenied(room.Listing.Fields.Name) || policy.IsNameDenied(room.Listing.Fields.HostName))))
                    banned.Add(control);
            }
        }
        foreach (var control in banned) await control.CloseAsync("banned").ConfigureAwait(false);
    }
}
