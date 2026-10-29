namespace Unciv.Net.Identity;

/// <summary>Stable <see cref="JoinRefused.Code"/> values for admission and slot binding.</summary>
public static class JoinRefusedCodes
{
    public const string BadCredentials = "bad-credentials";
    public const string RateLimited = "rate-limited";
    public const string BadKey = "bad-key";
    public const string SlotTaken = "slot-taken";
    public const string AuthFailed = "auth-failed";
    public const string RulesetMismatch = "ruleset-mismatch";
    public const string ProtocolMismatch = "protocol-mismatch";
    public const string LobbyFull = "lobby-full";
    public const string GameAlreadyStarted = "game-started";
    public const string Kicked = "kicked";

    /// <summary>T247: the rules differ and the host cannot offer its mods (it has no downloadable mod, or one is over the size caps).</summary>
    public const string ModSyncUnavailable = "mod-sync-unavailable";
}

public enum AdmissionResult { Admitted, BadCredentials, RateLimited }

/// <summary>
/// "May this person enter the lobby at all" (password or invite token). Says nothing about seats: that is
/// <see cref="SlotBindings"/>. Failures are counted per remote key; the reply never says which credential was wrong.
/// </summary>
public sealed class LobbyAdmission
{
    private readonly JoinPassword? password;
    private readonly InviteTokens tokens;
    private readonly AttemptLimiter limiter;

    /// <param name="password">Null together with <paramref name="inviteOnly"/> false means an open (LAN) lobby.</param>
    /// <param name="inviteOnly">Without a password, require an invite token.</param>
    /// <param name="tokens">Outstanding invite tokens.</param>
    /// <param name="limiter">Failed-attempt back-off.</param>
    public LobbyAdmission(JoinPassword? password, bool inviteOnly, InviteTokens tokens, AttemptLimiter limiter)
    {
        this.password = password;
        InviteOnly = inviteOnly;
        this.tokens = tokens;
        this.limiter = limiter;
    }

    public bool InviteOnly { get; }

    /// <summary><c>Welcome.RequiresPassword</c>.</summary>
    public bool RequiresPassword => password != null;

    /// <summary>
    /// The same decision as <see cref="Admit"/> (same back-off, same constant-work evaluation) for a connection that only downloads mods: a valid invite token is checked but not
    /// consumed, because the re-join after the download presents it again.
    /// </summary>
    public AdmissionResult AdmitForModSync(string remoteKey, string? passwordAttempt, string? inviteToken) => AdmitCore(remoteKey, passwordAttempt, inviteToken, redeem: false);

    public AdmissionResult Admit(string remoteKey, string? passwordAttempt, string? inviteToken) => AdmitCore(remoteKey, passwordAttempt, inviteToken, redeem: true);

    private AdmissionResult AdmitCore(string remoteKey, string? passwordAttempt, string? inviteToken, bool redeem)
    {
        if (password == null && !InviteOnly) return limiter.IsAllowed(remoteKey) ? AdmissionResult.Admitted : AdmissionResult.RateLimited;
        // Reserves an attempt atomically before the expensive check, so parallel guesses cannot all slip past the limit.
        if (!limiter.TryBeginAttempt(remoteKey)) return AdmissionResult.RateLimited;

        // Evaluate both, without short-circuiting, so a spent token or wrong password costs the same time.
        // Consequence (deliberate): a valid token presented together with a correct password is consumed.
        var passwordOk = password != null && password.Verify(passwordAttempt);
        var tokenOk = redeem ? tokens.Redeem(inviteToken) : tokens.IsValid(inviteToken);
        if (passwordOk || tokenOk)
        {
            limiter.RecordSuccess(remoteKey);
            return AdmissionResult.Admitted;
        }
        return AdmissionResult.BadCredentials;
    }
}

/// <summary>Slot to player-key bindings (trust on first use). A leaked password cannot take a seat that is already bound. Thread-safe.</summary>
public sealed class SlotBindings
{
    private readonly Lock gate = new();
    private readonly Dictionary<int, string> keyBySlot = new();

    /// <summary>Snapshot of the bindings (for persistence).</summary>
    public IReadOnlyDictionary<int, string> Bindings
    {
        get { lock (gate) return new Dictionary<int, string>(keyBySlot); }
    }

    /// <summary>Binds on first use. False when the key is malformed, the slot belongs to another key, or this key already owns another slot.</summary>
    public bool TryBind(int slot, string playerKey)
    {
        // PlayerKeys.IsValid is strict (canonical spelling only), so string equality is key equality.
        if (!PlayerKeys.IsValid(playerKey)) return false;
        lock (gate)
        {
            if (keyBySlot.TryGetValue(slot, out var owner)) return string.Equals(owner, playerKey, StringComparison.Ordinal);
            if (keyBySlot.ContainsValue(playerKey)) return false;
            keyBySlot[slot] = playerKey;
            return true;
        }
    }

    public bool IsOwner(int slot, string playerKey)
    {
        lock (gate) return keyBySlot.TryGetValue(slot, out var owner) && string.Equals(owner, playerKey, StringComparison.Ordinal);
    }

    public int? SlotOf(string playerKey)
    {
        lock (gate)
        {
            foreach (var (slot, key) in keyBySlot)
                if (string.Equals(key, playerKey, StringComparison.Ordinal)) return slot;
            return null;
        }
    }

    /// <summary>Host UI action for a lost key.</summary>
    public bool Clear(int slot)
    {
        lock (gate) return keyBySlot.Remove(slot);
    }

    /// <summary>Replaces all bindings from saved data and returns the slots that could not be restored (corrupt or duplicate key); the host must surface or refuse those seats.</summary>
    public List<int> Restore(IEnumerable<KeyValuePair<int, string>> saved)
    {
        var dropped = new List<int>();
        lock (gate) keyBySlot.Clear();
        foreach (var (slot, key) in saved)
            if (!TryBind(slot, key)) dropped.Add(slot);
        return dropped;
    }
}
