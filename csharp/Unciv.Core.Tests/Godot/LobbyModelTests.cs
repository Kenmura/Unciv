using System.Text.Json;
using Unciv.Assets;
using Unciv.GodotClient.Setup;
using Unciv.Net;
using Unciv.Net.Identity;

namespace Unciv.Tests.Godot;

// C035: the lobby screen's texts and Start rule (Godot-free).
public sealed class LobbyModelTests
{
    private static LobbySlot Slot(int index, string kind, string? name = null, bool ready = false, string? key = null, bool? connected = null) =>
        new() { Slot = index, Kind = kind, PlayerName = name, Ready = ready, PlayerKeyId = key, Connected = connected };

    private static LobbyState Lobby(bool aiFill, params LobbySlot[] slots) => new()
    {
        HostName = "Host",
        Slots = [.. slots],
        Settings = JsonSerializer.SerializeToElement(new
        {
            gameName = "Friday",
            ruleset = "Civ V - Vanilla",
            mods = new[] { "ModA", "ModB" },
            difficulty = "King",
            speed = "Epic",
            era = "Ancient era",
            maxMajorCivs = 4,
            cityStateCount = 2,
            aiFill,
            turnMode = "Simultaneous",
            turnTimerSeconds = 90,
        }),
    };

    [Fact]
    public void SlotTextNamesWhoSitsWhereAndWhatTheyAre()
    {
        Assert.Equal("1. Host (you, the host)", LobbyModel.SlotText(Slot(0, "Human", "Host", key: "k0"), "k0", "Host"));
        Assert.Equal("2. Ann (you)", LobbyModel.SlotText(Slot(1, "Human", "Ann", key: "k1"), "k1", "Host"));
        Assert.Equal("1. Host (host)", LobbyModel.SlotText(Slot(0, "Human", "Host", key: "k0"), "k1", "Host"));
        Assert.Equal("3. Bob (disconnected)", LobbyModel.SlotText(Slot(2, "Human", "Bob", connected: false), null, "Host"));
        Assert.Equal("2. Host", LobbyModel.SlotText(Slot(1, "Human", "Host"), null, "Host"));
        Assert.Equal("3. Player", LobbyModel.SlotText(Slot(2, "Human"), null, "Host"));
        Assert.Equal("4. AI player", LobbyModel.SlotText(Slot(3, "AI"), null, "Host"));
        Assert.Equal("5. Closed", LobbyModel.SlotText(Slot(4, "Closed"), null, "Host"));
        Assert.Equal("6. Open", LobbyModel.SlotText(Slot(5, "Open"), null, "Host"));
    }

    [Fact]
    public void SettingsLinesAreReadFromTheLobbyState()
    {
        var lobby = Lobby(true);
        Assert.Equal("Friday", LobbyModel.Title(lobby));
        Assert.Equal(
        [
            "Host: Host",
            "Ruleset: Civ V - Vanilla + ModA, ModB",
            "Difficulty: King",
            "Speed: Epic",
            "Starting era: Ancient era",
            "Players: up to 4, city-states: 2",
            "AI fills empty slots: yes",
            "Turns: Simultaneous, timer: 90 s",
        ], LobbyModel.SettingsLines(lobby));
    }

    [Fact]
    public void SettingsLinesSurviveMissingOrOddSettings()
    {
        var lobby = new LobbyState { HostName = "H", Slots = [], Settings = JsonSerializer.SerializeToElement(new { turnTimerSeconds = 0, mods = 5, maxMajorCivs = "x" }) };
        var lines = LobbyModel.SettingsLines(lobby);
        Assert.Contains("Ruleset: ", lines);
        Assert.Contains("Players: up to ?, city-states: ?", lines);
        Assert.Contains("Turns: , timer: none", lines);
        var empty = new LobbyState { HostName = "H", Slots = [], Settings = JsonSerializer.SerializeToElement(5) };
        Assert.Contains("Turns: , timer: ?", LobbyModel.SettingsLines(empty));
        Assert.Equal("", LobbyModel.Title(empty));
        var whole = Lobby(false) with { Settings = JsonSerializer.SerializeToElement(new { turnTimerSeconds = 120 }) };
        Assert.Contains("Turns: , timer: 2 min", LobbyModel.SettingsLines(whole));
        Assert.Contains("AI fills empty slots: no", LobbyModel.SettingsLines(whole));
    }

    [Fact]
    public void StartNeedsAHumanEverybodyReadyAndTwoPlayers()
    {
        Assert.Equal(new StartCheck(false, "Nobody has taken a seat yet."), LobbyModel.CanStart(Lobby(true, Slot(0, "Open"))));
        Assert.Equal(new StartCheck(false, "Waiting for Ann, Player to be ready."),
            LobbyModel.CanStart(Lobby(true, Slot(0, "Human", "Host", ready: true), Slot(1, "Human", "Ann"), Slot(2, "Human"))));
        Assert.False(LobbyModel.CanStart(Lobby(false, Slot(0, "Human", "Host", ready: true), Slot(1, "Open"))).CanStart);
        Assert.True(LobbyModel.CanStart(Lobby(true, Slot(0, "Human", "Host", ready: true), Slot(1, "Open"))).CanStart);
        Assert.True(LobbyModel.CanStart(Lobby(false, Slot(0, "Human", "Host", ready: true), Slot(1, "AI"))).CanStart);
        Assert.False(LobbyModel.CanStart(Lobby(true, Slot(0, "Human", "Host", ready: true), Slot(1, "Closed"))).CanStart);
    }

    [Fact]
    public void EveryRefusalCodeHasASentence()
    {
        string[] codes = [JoinRefusedCodes.RulesetMismatch, JoinRefusedCodes.ProtocolMismatch, JoinRefusedCodes.BadCredentials, JoinRefusedCodes.RateLimited, JoinRefusedCodes.LobbyFull,
            JoinRefusedCodes.GameAlreadyStarted, JoinRefusedCodes.Kicked, JoinRefusedCodes.SlotTaken, JoinRefusedCodes.AuthFailed, JoinRefusedCodes.BadKey];
        Assert.All(codes, code => Assert.DoesNotContain(code, LobbyModel.RefusalText(code), StringComparison.Ordinal));
        Assert.Equal(codes.Length - 1, codes.Select(LobbyModel.RefusalText).Distinct().Count()); // AuthFailed and BadKey share one
        Assert.Equal("The host refused the connection (weird).", LobbyModel.RefusalText("weird"));
    }

    [Fact]
    public void LobbyColoursAreDistinctNamedSwatchesAndAnUnknownNameIsGrey()
    {
        var names = PlaceholderPalette.LobbyColourNames;
        Assert.Equal(8, names.Count);
        Assert.Equal(names.Count, names.Select(it => PlaceholderPalette.ForLobbyColour(it)).Distinct().Count());
        Assert.Equal(PlaceholderPalette.TextDisabled, PlaceholderPalette.ForLobbyColour("Mauve"));
        Assert.Equal(PlaceholderPalette.TextDisabled, PlaceholderPalette.ForLobbyColour(null));
    }
}
