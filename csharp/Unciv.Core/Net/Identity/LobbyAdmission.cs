namespace Unciv.Net.Identity;

/// <summary>Stable <see cref="JoinRefused.Code"/> values for admission and slot binding.</summary>
public static class JoinRefusedCodes
{
    public const string BadCredentials = "bad-credentials";
    public const string RateLimited = "rate-limited";
    public const string BadKey = "bad-key";
    public const string SlotTaken = "slot-taken";
    public const string AuthFailed = "auth-failed";
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

    public AdmissionResult Admit(string remoteKey, string? passwordAttempt, string? inviteToken)
    {
        if (!limiter.IsAllowed(remoteKey)) return AdmissionResult.RateLimited;
        if (password == null && !InviteOnly) return AdmissionResult.Admitted;

        // Evaluate both, without short-circuiting, so a spent token or wrong password costs the same time.
        var passwordOk = password != null && password.Verify(passwordAttempt);
        var tokenOk = tokens.Redeem(inviteToken);
        if (passwordOk || tokenOk)
        {
            limiter.RecordSuccess(remoteKey);
            return AdmissionResult.Admitted;
        }
        limiter.RecordFailure(remoteKey);
        return AdmissionResult.BadCredentials;
    }
}

/// <summary>Slot to player-key bindings (trust on first use). A leaked password cannot take a seat that is already bound.</summary>
public sealed class SlotBindings
{
    private readonly Dictionary<int, string> keyBySlot = new();

    public IReadOnlyDictionary<int, string> Bindings => keyBySlot;

    /// <summary>Binds on first use. False when the key is malformed, the slot belongs to another key, or this key already owns another slot.</summary>
    public bool TryBind(int slot, string playerKey)
    {
        if (!PlayerKeys.IsValid(playerKey)) return false;
        if (keyBySlot.TryGetValue(slot, out var owner)) return string.Equals(owner, playerKey, StringComparison.Ordinal);
        if (keyBySlot.ContainsValue(playerKey)) return false;
        keyBySlot[slot] = playerKey;
        return true;
    }

    public bool IsOwner(int slot, string playerKey) => keyBySlot.TryGetValue(slot, out var owner) && string.Equals(owner, playerKey, StringComparison.Ordinal);

    public int? SlotOf(string playerKey) => keyBySlot.FirstOrDefault(p => string.Equals(p.Value, playerKey, StringComparison.Ordinal)) is { Value: not null } hit ? hit.Key : null;

    /// <summary>Host UI action for a lost key.</summary>
    public bool Clear(int slot) => keyBySlot.Remove(slot);

    public void Restore(IEnumerable<KeyValuePair<int, string>> saved)
    {
        keyBySlot.Clear();
        foreach (var (slot, key) in saved) TryBind(slot, key);
    }
}
