using System.Text.Json;
using Unciv.Assets;
using Unciv.Net;

namespace Unciv.Core.Tests.Assets;

public class SocialModelTests
{
    private static LobbyState Roster(params LobbySlot[] slots) =>
        new() { Settings = JsonDocument.Parse("{}").RootElement.Clone(), Slots = [.. slots], HostName = "" };

    private static LobbySlot Slot(string civ, string kind, string? name, string? key, bool connected = true) =>
        new() { Slot = 0, Nation = civ, Kind = kind, PlayerName = name, PlayerKeyId = key, Connected = connected, Ready = false };

    [Fact]
    public void Clean_TrimsCutsAndDropsBlank()
    {
        Assert.Null(SocialModel.Clean(null));
        Assert.Null(SocialModel.Clean("  \t"));
        Assert.Equal("hi", SocialModel.Clean("  hi "));
        Assert.Equal(SocialModel.MaxChatChars, SocialModel.Clean(new string('x', 900))!.Length);
    }

    [Fact]
    public void Append_KeepsTheNewestLines()
    {
        IReadOnlyList<ChatLine> log = [];
        for (int i = 0; i < SocialModel.MaxChatLines + 5; i++) log = SocialModel.Append(log, new ChatLine("a", i.ToString()));
        Assert.Equal(SocialModel.MaxChatLines, log.Count);
        Assert.Equal("5", log[0].Text);
        Assert.Equal((SocialModel.MaxChatLines + 4).ToString(), log[^1].Text);
    }

    [Fact]
    public void Players_HostSeesAdminButtonsOnOtherHumansOnly()
    {
        var roster = Roster(Slot("Rome", "Human", "Alice", "ka"), Slot("Greece", "Human", "Bob", "kb", connected: false), Slot("Egypt", "AI", null, null));
        var rows = SocialModel.Players(roster, "Rome", isHost: true);
        Assert.Equal(["Rome", "Greece"], rows.Select(it => it.CivId));
        Assert.Equal([(true, false, false), (false, true, true)], rows.Select(it => (it.IsYou, it.CanKick, it.CanRebind)));
        Assert.Equal([SeatStatus.Online, SeatStatus.Offline], rows.Select(it => it.Status));
    }

    [Fact]
    public void Players_GuestSeesNoAdminButtons()
    {
        var rows = SocialModel.Players(Roster(Slot("Rome", "Human", "Alice", "ka"), Slot("Greece", "Human", "Bob", "kb")), "Greece", isHost: false);
        Assert.All(rows, it => Assert.False(it.CanKick || it.CanRebind));
    }

    [Fact]
    public void Players_ReleasedAndAwaitingSeatsAreTold()
    {
        var roster = Roster(Slot("Rome", "Human", "Alice", "ka"), Slot("Greece", "AI", "Bob", null, false), Slot("Egypt", "AI", null, "kc", false));
        var rows = SocialModel.Players(roster, "Rome", isHost: true);
        Assert.Equal([SeatStatus.Online, SeatStatus.Released, SeatStatus.AwaitingDevice], rows.Select(it => it.Status));
        Assert.Equal("Egypt", rows[2].Name); // no name until the new device joins
        Assert.Equal([false, false], rows.Skip(1).Select(it => it.CanKick)); // nobody to kick
        Assert.Equal([true, true], rows.Skip(1).Select(it => it.CanRebind));
    }

    [Fact]
    public void Players_NoRosterMeansNoRows() => Assert.Empty(SocialModel.Players(null, "Rome", true));

    [Theory]
    [InlineData("bad-key", "that is not a player key")]
    [InlineData("turn-resolving", "the host is resolving the turn, try again in a moment")]
    [InlineData("something-else", "something-else")]
    public void Describe_UsesWordsForKnownCodes(string code, string text) => Assert.Equal(text, SocialModel.Describe(code));
}
