namespace Unciv.Assets;

/// <summary>
/// C044: whether the running game has progress no save holds. The game's command sequence number grows with every accepted command, so a game is unsaved exactly
/// when it is past the sequence number of the last save (or of the moment it was loaded or started). Godot-free so it is unit-tested.
/// </summary>
public sealed class SaveProgress
{
    private long savedSeq;

    /// <summary>A freshly started or loaded game has nothing to lose yet.</summary>
    public void Reset(long seq) => savedSeq = seq;

    /// <summary>A save that captured the game as of <paramref name="seq"/> (never moves the mark back).</summary>
    public void MarkSaved(long seq) => savedSeq = Math.Max(savedSeq, seq);

    public bool IsUnsaved(long currentSeq) => currentSeq > savedSeq;
}
