using System.Text;
using Unciv.Commands;
using Unciv.Logic;
using Unciv.Net;
using Unciv.Net.Persistence;
using Unciv.Net.Remote;
using Unciv.Net.Turns;

namespace Unciv.Server;

/// <summary>Tunable limits for the command gate (SERVER.md 3).</summary>
public sealed record CommandGateLimits(
    int MaxPayloadBytes = 64 * 1024,
    int Capacity = 30,
    int RefillPerSecond = 15
);

public enum CommandGateResultKind
{
    Accepted,
    DedupHit,
    Rejected,
}

public sealed record CommandGateResult(
    CommandGateResultKind Kind,
    CommandEnvelope? Envelope = null,
    long? Seq = null,
    bool Accepted = false,
    string? Code = null,
    string? Message = null
)
{
    public static CommandGateResult Pass(CommandEnvelope envelope) =>
        new(CommandGateResultKind.Accepted, Envelope: envelope);

    public static CommandGateResult Dedup(long seq, bool accepted, string? code) =>
        new(CommandGateResultKind.DedupHit, Seq: seq, Accepted: accepted, Code: code);

    public static CommandGateResult Reject(string code, string message) =>
        new(CommandGateResultKind.Rejected, Code: code, Message: message);
}

/// <summary>
/// Pre-engine gate for commands arriving from sessions (SERVER.md 3).
/// Injects authenticated actor id, validates size and rate, checks idempotent dedup,
/// runs turn gate checks (RequireActive), and sanitizes existence-oracle errors.
/// </summary>
public sealed class CommandGate
{
    private readonly IGameStore? store;
    private readonly CommandGateLimits limits;
    private readonly IClock clock;
    private readonly Dictionary<(string ActorId, string ClientCmdId), (long Seq, bool Accepted, string? Code)> dedup = new();
    private readonly Dictionary<string, TokenBucket> buckets = new(StringComparer.Ordinal);

    public CommandGate(IGameStore? store = null, CommandGateLimits? limits = null, IClock? clock = null)
    {
        this.store = store;
        this.limits = limits ?? new CommandGateLimits();
        this.clock = clock ?? SystemClock.Instance;
    }

    /// <summary>Cleans up session-specific rate limiting state on disconnect.</summary>
    public void EndSession(string sessionId) => buckets.Remove(sessionId);

    /// <summary>Records the outcome of an applied or rejected command for future idempotent dedup.</summary>
    public void RecordOutcome(string actorId, string clientCmdId, long seq, bool accepted, string? code)
    {
        dedup[(actorId, clientCmdId)] = (seq, accepted, code);
    }

    /// <summary>
    /// Evaluates an inbound command against size, rate, dedup, and RequireActive.
    /// Injects the authenticated boundCivId as the envelope actor.
    /// </summary>
    public CommandGateResult Filter(string sessionId, string boundCivId, Command command, GameInfo game)
    {
        // 1. Size limit
        string rawPayload = command.Payload.GetRawText();
        if (Encoding.UTF8.GetByteCount(rawPayload) > limits.MaxPayloadBytes)
        {
            return CommandGateResult.Reject(ErrorCodes.FrameTooLarge, "Command payload exceeds maximum allowed size");
        }

        // 2. Rate limit
        long nowMs = clock.NowMillis;
        var bucket = GetBucket(sessionId);
        if (!bucket.TryConsume(nowMs))
        {
            return CommandGateResult.Reject(QueryWireCodes.RateLimited, "Command rate limit exceeded");
        }

        // 3. Idempotent Dedup
        if (dedup.TryGetValue((boundCivId, command.ClientCmdId), out var existing))
        {
            return CommandGateResult.Dedup(existing.Seq, existing.Accepted, existing.Code);
        }
        if (store != null && store.FindOutcome(boundCivId, command.ClientCmdId) is { } outcome)
        {
            dedup[(boundCivId, command.ClientCmdId)] = (outcome.Seq, true, null);
            return CommandGateResult.Dedup(outcome.Seq, true, null);
        }

        // 4. Session and turn gate (RequireActive)
        var civ = game.GetCivilizationOrNull(boundCivId);
        if (civ == null)
        {
            return CommandGateResult.Reject(CommandErrors.UnknownCiv, $"Unknown civilization \"{boundCivId}\"");
        }
        if (!civ.IsHuman() || civ.IsSpectator())
        {
            return CommandGateResult.Reject(CommandErrors.NotHuman, $"{boundCivId} is not a human player");
        }

        bool allowEnded = command.CommandType is "undo-end-turn" or "respond-to-trade-request" or "respond-to-demand" or "respond-to-friendship" or "dismiss-trade-request";
        bool allowDefeated = command.CommandType is "end-turn" or "undo-end-turn";
        var requireActive = new CommandContext(game, boundCivId).RequireActive(allowDefeated, allowEnded);
        if (!requireActive.TryGet(out _, out var failure))
        {
            return CommandGateResult.Reject(failure.Code, failure.Message);
        }

        // 5. Construct envelope with injected actor ID (ignoring any actor in payload)
        var envelope = new CommandEnvelope(0, command.CommandType, boundCivId, command.Payload);
        return CommandGateResult.Pass(envelope);
    }

    /// <summary>
    /// Collapses NotYourUnit / UnknownUnit, NotYourCity / UnknownCity, and UnknownTile errors
    /// to prevent probing attacks against hidden map state (existence oracle protection).
    /// </summary>
    public static CommandValidation SanitizeValidation(CommandValidation validation)
    {
        if (validation.IsValid) return validation;
        if (validation.Code is CommandErrors.NotYourUnit or CommandErrors.UnknownUnit
            or CommandErrors.NotYourCity or CommandErrors.UnknownCity
            or CommandErrors.UnknownTile)
        {
            return CommandValidation.Fail(CommandErrors.NotAllowed, "Action not allowed");
        }
        return validation;
    }

    private TokenBucket GetBucket(string sessionId)
    {
        if (!buckets.TryGetValue(sessionId, out var bucket))
        {
            bucket = new TokenBucket(limits.Capacity, limits.RefillPerSecond);
            buckets[sessionId] = bucket;
        }
        return bucket;
    }

    private sealed class TokenBucket(int capacity, int refillPerSecond)
    {
        private readonly int capacity = capacity;
        private readonly int refillPerSecond = refillPerSecond;
        private double tokens = capacity;
        private long lastRefillMs;

        public bool TryConsume(long nowMs)
        {
            if (lastRefillMs == 0) lastRefillMs = nowMs;
            long elapsed = nowMs - lastRefillMs;
            if (elapsed > 0)
            {
                tokens = Math.Min(capacity, tokens + (elapsed * refillPerSecond / 1000.0));
                lastRefillMs = nowMs;
            }
            if (tokens < 1.0) return false;
            tokens -= 1.0;
            return true;
        }
    }
}
