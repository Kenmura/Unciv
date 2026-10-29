using Unciv.Net;
using Unciv.Net.Standby;
using Unciv.Server.Transport;

namespace Unciv.Server;

// T281: backup-host designation (design/HOST-MIGRATION.md sections 1 and 3). Everything here runs on the actor; the one slow step, the endpoint probe, runs beside it and re-enters through the mailbox.
public sealed partial class GameHost
{
    /// <summary>Who is designated as backup host and whether they consented and were probed. Persisted in <c>game.json</c>.</summary>
    public StandbyCoordinator Standby { get; }

    private StandbyCoordinator InitStandby(StandbyCoordinator? given)
    {
        if (given != null)
        {
            PersistStandby(given);
            return given;
        }
        var created = new StandbyCoordinator(clock, new TlsEndpointProber());
        created.Restore(store?.ReadMeta().Standby);
        return created;
    }

    private void PersistStandby(StandbyCoordinator coordinator)
    {
        if (store == null) return;
        var record = coordinator.ToRecord();
        var meta = store.ReadMeta();
        if (meta.Standby == null && record == null) return;
        store.WriteMeta(meta with { Standby = record });
    }

    /// <summary>Joined clients learn the backup host in <see cref="JoinOk"/>: the announcement, or null while nobody is designated. Built on the actor so the roster is consistent.</summary>
    public async Task<HostSuccessor?> SuccessorForJoinAsync()
    {
        HostSuccessor? result = null;
        await EnqueueAsync(() =>
        {
            result = Standby.AnnouncementForJoin(store?.ReadMeta().HostEpoch ?? 0, SuccessorRoster());
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        return result;
    }

    private StandbyStatus StatusMessage() => Standby.Status() with { GameId = session.Game.GameId };

    private List<SuccessorSeat> SuccessorRoster()
    {
        var roster = new List<SuccessorSeat>();
        var meta = store?.ReadMeta();
        if (meta == null) return roster;
        foreach (var (civId, binding) in meta.Slots)
        {
            if (binding.PlayerKey.Length == 0) continue;
            roster.Add(new SuccessorSeat
            {
                KeyId = binding.PlayerKey,
                Connected = sessions.Values.Any(it => it.IsOpen && string.Equals(it.BoundCivId, civId, StringComparison.Ordinal)),
            });
        }
        return roster;
    }

    /// <summary>
    /// Host administration (never reachable from a client frame): offers the backup-host role to the player bound to <paramref name="civId"/>. Nothing flows until that player accepts. Null on success,
    /// else <c>unknown-civ</c>, <c>not-bound</c>, <c>no-store</c> or <c>own-key</c> (the host's own seat is the host, not a backup).
    /// </summary>
    public async Task<string?> DesignateBackupAsync(string civId, string? hostPlayerKey = null)
    {
        string? refusal = null;
        await EnqueueAsync(async () =>
        {
            if (store == null) { refusal = "no-store"; return; }
            var meta = store.ReadMeta();
            if (!meta.Slots.TryGetValue(civId, out var binding)) { refusal = session.Game.GetCivilizationOrNull(civId) == null ? "unknown-civ" : "not-bound"; return; }
            if (binding.PlayerKey.Length == 0) { refusal = "not-bound"; return; }
            if (hostPlayerKey != null && string.Equals(binding.PlayerKey, hostPlayerKey, StringComparison.Ordinal)) { refusal = "own-key"; return; }
            if (Standby.Designate(binding.PlayerKey, out var previous))
            {
                StopStandbyStream();
                PersistStandby(Standby);
                if (previous != null) await SendToKeyAsync(previous, new StandbyRevoke());
                await SendToKeyAsync(binding.PlayerKey, new StandbyOffer { KeyId = binding.PlayerKey });
                await AnnounceSuccessorAsync();
            }
        }).ConfigureAwait(false);
        return refusal;
    }

    /// <summary>Host administration: ends the designation (no-op when none). The candidate is told; the copy it may hold stays on its disk.</summary>
    public Task RevokeBackupAsync() => EnqueueAsync(RevokeBackupCoreAsync);

    private async Task RevokeBackupCoreAsync()
    {
        StopStandbyStream();
        var previous = Standby.Revoke();
        if (previous == null) return;
        PersistStandby(Standby);
        await SendToKeyAsync(previous, new StandbyRevoke());
        await AnnounceSuccessorAsync();
    }

    /// <summary>The designated candidate (re)connects: it is offered the role again, or told its status.</summary>
    private async Task SendStandbyHelloAsync(HostSession attached)
    {
        if (!Standby.IsDesignated(attached.PlayerKeyId)) return;
        if (Standby.State == StandbyStates.Offered) await SafeSendAsync(attached, new StandbyOffer { KeyId = attached.PlayerKeyId! });
        else await SafeSendAsync(attached, StatusMessage());
    }

    // Judged by the key the host authenticated for this session, never by a field of the frame. The probe can take seconds, so it must not hold the actor.
    private void ProcessStandbyAccept(HostSession from, StandbyAccept accept) => _ = AcceptStandbyAsync(from, accept);

    private async Task AcceptStandbyAsync(HostSession from, StandbyAccept accept)
    {
        try
        {
            var code = await Standby.AcceptAsync(from.PlayerKeyId, accept, cts.Token).ConfigureAwait(false); // suspends at the dial, so the actor keeps running
            await EnqueueAsync(async () =>
            {
                if (code != ErrorCodes.NotDesignated) PersistStandby(Standby);
                if (code != null) await SafeSendAsync(from, new Error { Code = code });
                if (code != ErrorCodes.NotDesignated)
                {
                    await SafeSendAsync(from, StatusMessage());
                    await AnnounceSuccessorAsync();
                }
            }).ConfigureAwait(false);
        }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException or OperationCanceledException) { /* the host closed meanwhile */ }
    }

    private async Task ProcessStandbyDeclineAsync(HostSession from)
    {
        var code = Standby.Decline(from.PlayerKeyId);
        if (code != null)
        {
            await SafeSendAsync(from, new Error { Code = code });
            return;
        }
        StopStandbyStream();
        PersistStandby(Standby);
        await SafeSendAsync(from, StatusMessage());
        await AnnounceSuccessorAsync();
    }

    /// <summary>Sends the current announcement to every open session (public information; no game state).</summary>
    private async Task AnnounceSuccessorAsync()
    {
        if (sessions.Count == 0) return;
        var announcement = Standby.Announcement(store?.ReadMeta().HostEpoch ?? 0, SuccessorRoster());
        foreach (var target in sessions.Values.ToList()) await SafeSendAsync(target, announcement);
    }

    private async Task SendToKeyAsync(string keyId, Message message)
    {
        foreach (var target in sessions.Values.Where(it => string.Equals(it.PlayerKeyId, keyId, StringComparison.Ordinal)).ToList())
            await SafeSendAsync(target, message);
    }
}
