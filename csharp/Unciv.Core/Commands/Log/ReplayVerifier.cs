using Unciv.Logic;
using Unciv.Logic.Files;
using Unciv.Utils;

namespace Unciv.Commands.Log;

/// <summary>
/// The outcome of replaying a log. <see cref="DivergedAtSeq"/> is the seq of the first checkpoint whose hash differs (the state went wrong at or before that
/// command); replay stops there. <see cref="Game"/> is the state at the stop.
/// </summary>
public sealed record ReplayResult(bool Matches, long? DivergedAtSeq, int CheckpointsVerified, long LastSeq, GameInfo Game, ulong FinalHash);

/// <summary>
/// Replays a command log from its header and compares the full state hash at every checkpoint (design/COMMANDS-IMPLEMENTATION.md section 6).
/// No UI or auto-play code is involved: the header's player settings are applied, every command is re-applied through the processor, and the engine reads
/// no clock (commands carry the one the authority stamped). A command that no longer validates throws <see cref="CommandDesyncException"/>.
/// </summary>
public static class ReplayVerifier
{
    /// <summary>The T102 state hash: FNV over the serialised <see cref="GameInfo"/>.</summary>
    public static ulong FullHash(GameInfo game) => StateHasher.ComputeHash(h => h.Add(UncivFiles.GameInfoToString(game, forceZip: false)));

    /// <summary>The game a log starts from. Refuses a log written by another engine version, format or ruleset.</summary>
    public static GameInfo StartGame(CommandLogHeader header)
    {
        if (header.FormatVersion != CommandLogHeader.CurrentFormatVersion)
            throw new CommandLogException($"Unsupported command log format {header.FormatVersion}");
        if (header.EngineVersion != CommandLogHeader.CurrentEngineVersion)
            throw new CommandLogException($"The log was written by engine {header.EngineVersion}, this is {CommandLogHeader.CurrentEngineVersion}; it cannot be replayed");
        if (header.RulesetHash != CommandLogHeader.RulesetHashOf(header.GameSetup.GameParameters))
            throw new CommandLogException("The ruleset differs from the one the log was played with");
        var game = GameStarter.StartNewGame(CommandLogHeader.Copy(header.GameSetup), header.GameSeed, header.StartClockMillis);
        foreach (var (civId, settings) in header.PlayerSettings) game.SetPlayerSettings(civId, settings);
        game.RandomSalt = header.HostSecret; // T230: set after the start, exactly like the host does
        return game;
    }

    public static ReplayResult Verify(CommandLog log) => Run(new GameSession(StartGame(log.Header)), log, 0);

    /// <summary>
    /// Continues from a snapshot taken after command <paramref name="snapshotSeq"/>: replays only the tail of the log and checks the checkpoints after it.
    /// </summary>
    public static ReplayResult VerifyTail(GameInfo snapshot, long snapshotSeq, CommandLog log)
    {
        snapshot.RandomSalt = log.Header.HostSecret; // T230: a saved game does not carry the secret; the log header does
        return Run(new GameSession(snapshot, lastSeq: snapshotSeq), log, snapshotSeq);
    }

    private static ReplayResult Run(GameSession session, CommandLog log, long after)
    {
        var verified = 0;
        foreach (var line in log.Lines)
        {
            switch (line)
            {
                case CommandLogLine.Command command when command.Envelope.Seq > after:
                    session.Replay(command.Envelope);
                    break;
                case CommandLogLine.Checkpoint checkpoint when checkpoint.Seq >= after:
                    if (checkpoint.Seq != session.LastSeq)
                        throw new CommandDesyncException($"Checkpoint for seq {checkpoint.Seq} found at seq {session.LastSeq}");
                    var hash = FullHash(session.Game);
                    if (hash != checkpoint.FullHash)
                        return new ReplayResult(false, checkpoint.Seq, verified, session.LastSeq, session.Game, hash);
                    verified++;
                    break;
            }
        }
        return new ReplayResult(true, null, verified, session.LastSeq, session.Game, FullHash(session.Game));
    }
}
