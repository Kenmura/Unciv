using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Settings;

namespace Unciv.Tests.Godot;

// C089: the private map markers: rules, persistence in the client settings, and that they never reach the game.
public sealed class MapMarkerModelTests : IDisposable
{
    private const string Key = "game-1/Rome";
    private readonly string dir = Directory.CreateTempSubdirectory("unciv-markers-").FullName;
    private string FilePath => Path.Combine(dir, "settings.json");

    public void Dispose()
    {
        try { Directory.Delete(dir, true); }
        catch (IOException) { }
    }

    [Fact]
    public void PlaceAddsAndReplacesOnTheSameTileKeepingOrder()
    {
        var s = MapMarkerModel.Place(ClientSettings.Default, Key, 1, 2, "Star", "Iron");
        s = MapMarkerModel.Place(s, Key, 3, 4, "Pin", "");
        s = MapMarkerModel.Place(s, Key, 1, 2, "Danger", "Barbarians");
        var list = MapMarkerModel.For(s, Key);
        Assert.Equal([(1, 2, "Danger", "Barbarians"), (3, 4, "Pin", "")], list.Select(it => (it.X, it.Y, it.Icon, it.Label)));
        Assert.Equal("Danger", MapMarkerModel.At(s, Key, 1, 2)?.Icon);
        Assert.Null(MapMarkerModel.At(s, Key, 9, 9));
    }

    [Fact]
    public void PlaceDoesNotChangeTheEarlierSettings()
    {
        var before = ClientSettings.Default;
        var after = MapMarkerModel.Place(before, Key, 0, 0, "Pin", "a");
        Assert.Empty(before.MapMarkers);
        Assert.Single(after.MapMarkers);
    }

    [Fact]
    public void RemoveDropsTheMarkerAndAnEmptyGame()
    {
        var s = MapMarkerModel.Place(ClientSettings.Default, Key, 1, 2, "Pin", "a");
        Assert.Same(s, MapMarkerModel.Remove(s, Key, 5, 5));
        var removed = MapMarkerModel.Remove(s, Key, 1, 2);
        Assert.Empty(MapMarkerModel.For(removed, Key));
        Assert.Empty(removed.MapMarkers);
    }

    [Fact]
    public void GamesAndViewersHaveSeparateMarkers()
    {
        var s = MapMarkerModel.Place(ClientSettings.Default, "g1/Rome", 1, 1, "Pin", "mine");
        s = MapMarkerModel.Place(s, "g1/Greece", 2, 2, "Pin", "hotseat rival");
        s = MapMarkerModel.Place(s, "g2/Rome", 3, 3, "Pin", "other game");
        Assert.Equal(["mine"], MapMarkerModel.For(s, "g1/Rome").Select(it => it.Label));
        Assert.Equal(["hotseat rival"], MapMarkerModel.For(s, "g1/Greece").Select(it => it.Label));
        Assert.Empty(MapMarkerModel.For(s, "g3/Rome"));
        Assert.Empty(MapMarkerModel.For(s, null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void AGameWithoutAnIdHasNoKey(string? id) => Assert.Null(MapMarkerModel.Key(id, "Rome"));

    [Fact]
    public void KeyJoinsGameAndViewer() => Assert.Equal("abc/Rome", MapMarkerModel.Key("abc", "Rome"));

    [Theory]
    [InlineData("  Iron  ", "Iron")]
    [InlineData("a[b]{c}\"d\\e<f>", "abcdef")]
    [InlineData("tab\there\nnew", "tabherenew")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("123456789012345678901234567890", "123456789012345678901234")]
    [InlineData("1234567890123456789012 xyz", "1234567890123456789012 x")]
    [InlineData("12345678901234567890123 xyz", "12345678901234567890123")]
    public void LabelIsNormalized(string? typed, string expected) => Assert.Equal(expected, MapMarkerModel.NormalizeLabel(typed));

    [Fact]
    public void UnknownIconBecomesThePin() =>
        Assert.Equal("Pin", MapMarkerModel.Place(ClientSettings.Default, Key, 0, 0, "Banana", "").MapMarkers[Key][0].Icon);

    [Fact]
    public void EveryIconHasAGlyphAndADistinctColour()
    {
        Assert.Equal(MapMarkerModel.Icons.Count, MapMarkerModel.Icons.Select(MapMarkerModel.ColourOf).Distinct().Count());
        Assert.Equal("", MapMarkerModel.GlyphOf("Pin"));
        Assert.All(MapMarkerModel.Icons.Where(it => it != "Pin"), icon => Assert.NotEqual("", MapMarkerModel.GlyphOf(icon)));
        Assert.Equal(MapMarkerModel.ColourOf("Pin"), MapMarkerModel.ColourOf("unknown"));
    }

    [Fact]
    public void AFullGameRefusesANewTileButStillEditsOldOnes()
    {
        var s = ClientSettings.Default;
        for (int i = 0; i < MapMarkerModel.MaxPerGame; i++) s = MapMarkerModel.Place(s, Key, i, 0, "Pin", "");
        Assert.Same(s, MapMarkerModel.Place(s, Key, 999, 0, "Pin", ""));
        var edited = MapMarkerModel.Place(s, Key, 5, 0, "Star", "edited");
        Assert.Equal("edited", MapMarkerModel.At(edited, Key, 5, 0)?.Label);
        Assert.Equal(MapMarkerModel.MaxPerGame, MapMarkerModel.For(edited, Key).Count);
    }

    [Fact]
    public void SanitizeRepairsAHandEditedFile()
    {
        var raw = new Dictionary<string, List<MapMarker>>
        {
            [""] = [new MapMarker()],
            ["empty"] = [],
            ["g/Rome"] = [new MapMarker { X = 1, Y = 1, Icon = "??", Label = " [x]ok " }, new MapMarker { X = 1, Y = 1, Label = "later wins" }, new MapMarker { X = 2, Y = 2 }],
        };
        var clean = MapMarkerModel.Sanitize(raw);
        Assert.Equal(["g/Rome"], clean.Keys);
        Assert.Equal([(1, 1, "Pin", "later wins"), (2, 2, "Pin", "")], clean["g/Rome"].Select(it => (it.X, it.Y, it.Icon, it.Label)));
        Assert.Empty(MapMarkerModel.Sanitize(null));
    }

    [Fact]
    public void MarkersSurviveARestartThroughTheSettingsFile()
    {
        var service = new ClientSettingsService(new ClientSettingsStore(FilePath));
        Assert.True(service.Update(it => MapMarkerModel.Place(it, Key, 4, 5, "Home", "Capital site")));
        Assert.True(service.Update(it => MapMarkerModel.Place(it, "game-2/Rome", 1, 1, "Danger", "Other game")));

        var reloaded = new ClientSettingsService(new ClientSettingsStore(FilePath)).Current;
        Assert.Equal([(4, 5, "Home", "Capital site")], MapMarkerModel.For(reloaded, Key).Select(it => (it.X, it.Y, it.Icon, it.Label)));
        Assert.Equal("Other game", MapMarkerModel.At(reloaded, "game-2/Rome", 1, 1)?.Label);

        new ClientSettingsService(new ClientSettingsStore(FilePath)).Update(it => MapMarkerModel.Remove(it, Key, 4, 5));
        Assert.Empty(MapMarkerModel.For(new ClientSettingsStore(FilePath).Load(), Key));
    }

    [Fact]
    public void ADamagedMarkerSectionDoesNotLoseTheRestOfTheFile()
    {
        File.WriteAllText(FilePath, "{\"version\":1,\"language\":\"German\",\"mapMarkers\":{\"g/Rome\":[{\"x\":1,\"y\":2,\"icon\":\"Nope\",\"label\":\"  hi \"}]}}");
        var loaded = new ClientSettingsStore(FilePath).Load();
        Assert.Equal("German", loaded.Language);
        var marker = Assert.Single(MapMarkerModel.For(loaded, "g/Rome"));
        Assert.Equal(("Pin", "hi"), (marker.Icon, marker.Label));
    }

    [Fact]
    public void MarkersNeverBecomeAGameCommand()
    {
        var sent = new List<GameCommand>();
        var service = new ClientSettingsService(new ClientSettingsStore(FilePath), command =>
        {
            sent.Add(command);
            return true;
        });
        service.Update(it => MapMarkerModel.Place(it, Key, 1, 1, "Star", "x"));
        service.Update(it => MapMarkerModel.Remove(it, Key, 1, 1));
        Assert.Empty(sent);
        Assert.Null(MapMarkerModel.Place(ClientSettings.Default, Key, 1, 1, "Pin", "").ToDiffCommand(ClientSettings.Default.Player));
    }
}
