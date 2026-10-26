namespace Unciv.Net.Lobby;

/// <summary>The role or state of a lobby slot.</summary>
public enum SlotKind
{
    Open,
    Human,
    AI,
    Closed
}

/// <summary>
/// A single player slot in the lobby (SERVER.md 6.5).
/// Holds slot index, kind, nation or Random, color, bound player key, display name, ready, and connection state.
/// </summary>
public sealed class Slot
{
    public required int Index { get; init; }
    public SlotKind Kind { get; set; } = SlotKind.Open;
    public string Nation { get; set; } = Constants.Random;
    public string? Color { get; set; }
    public string? PlayerKeyId { get; set; }
    public string? DisplayName { get; set; }
    public bool Ready { get; set; }
    public bool Connected { get; set; }
    public string? SessionId { get; set; }
    public ITransport? Transport { get; set; }

    /// <summary>Converts to the wire representation for <see cref="LobbyState"/>.</summary>
    public Unciv.Net.LobbySlot ToWire() => new()
    {
        Slot = Index,
        PlayerName = DisplayName,
        Nation = Nation,
        Color = Color,
        Ready = Ready,
        Kind = Kind.ToString(),
        PlayerKeyId = PlayerKeyId,
        Connected = Connected
    };
}
