using Unciv.Commands;
using Unciv.Commands.Turn;
using Unciv.GodotClient.Settings;
using Unciv.Logic;

namespace Unciv.Tests.Godot;

// C043: robustness of the Godot-free client settings model (the file is compiled into this project).
public sealed class ClientSettingsTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("unciv-settings-").FullName;
    private string FilePath => Path.Combine(dir, "settings.json");

    public void Dispose()
    {
        try { Directory.Delete(dir, true); }
        catch (IOException) { }
    }

    private ClientSettings LoadText(string text)
    {
        File.WriteAllText(FilePath, text);
        return new ClientSettingsStore(FilePath).Load();
    }

    [Fact]
    public void MissingFileGivesDefaults() => Assert.Equal(ClientSettings.Default, new ClientSettingsStore(FilePath).Load());

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"version\":1,\"language\":\"Fre")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"version\":\"one\"}")]
    [InlineData("{\"version\":1,\"uiScale\":\"big\"}")]
    [InlineData("{\"version\":1,\"autosaveTurns\":true}")]
    [InlineData("{\"version\":1,\"tutorialsSeen\":5}")]
    [InlineData("{\"version\":3,\"language\":\"German\"}")]
    [InlineData("{\"version\":0,\"language\":\"German\"}")]
    [InlineData("{\"version\":-3,\"language\":\"German\"}")]
    public void UnusableFileGivesDefaults(string text) => Assert.Equal(ClientSettings.Default, LoadText(text));

    [Fact]
    public void DirectoryInPlaceOfFileGivesDefaults()
    {
        Directory.CreateDirectory(FilePath);
        Assert.Equal(ClientSettings.Default, new ClientSettingsStore(FilePath).Load());
    }

    [Fact]
    public void ValidFileRoundTrips()
    {
        var store = new ClientSettingsStore(FilePath);
        var settings = ClientSettings.Default with
        {
            Language = "German",
            UiScale = 2f,
            AssetPack = "Pack",
            AutosaveTurns = 7,
            PlayerName = "Ann",
            KeyBindings = new SortedDictionary<string, string> { ["EndTurn"] = "Q" },
            TutorialsSeen = ["a", "b"],
            Player = PlayerSettings.Default with { AutoAssignCityProduction = true },
        };
        Assert.True(store.Save(settings));
        var loaded = new ClientSettingsStore(FilePath).Load();
        Assert.Equal(settings.Language, loaded.Language);
        Assert.Equal(2f, loaded.UiScale);
        Assert.Equal(7, loaded.AutosaveTurns);
        Assert.Equal("Ann", loaded.PlayerName);
        Assert.Equal("Q", loaded.KeyBindings["EndTurn"]);
        Assert.Equal(["a", "b"], loaded.TutorialsSeen);
        Assert.True(loaded.Player.AutoAssignCityProduction);
    }

    [Theory]
    [InlineData("\"NaN\"")]
    [InlineData("1e40")]
    public void NonFiniteUiScaleBecomesDefault(string value)
    {
        // "NaN" as a string is not a number under default options, so the file is unusable; 1e40 overflows float to infinity or fails: either way defaults.
        Assert.Equal(1f, LoadText("{\"version\":1,\"uiScale\":" + value + "}").UiScale);
    }

    [Fact]
    public void SanitizedClampsAndRepairs()
    {
        Assert.Equal(1f, (ClientSettings.Default with { UiScale = float.NaN }).Sanitized().UiScale);
        Assert.Equal(1f, (ClientSettings.Default with { UiScale = float.PositiveInfinity }).Sanitized().UiScale);
        Assert.Equal(0.5f, (ClientSettings.Default with { UiScale = -2f }).Sanitized().UiScale);
        Assert.Equal(4f, (ClientSettings.Default with { UiScale = 100f }).Sanitized().UiScale);
        Assert.Equal(0, (ClientSettings.Default with { AutosaveTurns = -5 }).Sanitized().AutosaveTurns);
        Assert.Equal("English", (ClientSettings.Default with { Language = " " }).Sanitized().Language);
        Assert.Equal("Default", (ClientSettings.Default with { AssetPack = "" }).Sanitized().AssetPack);
        Assert.Equal("Player", (ClientSettings.Default with { PlayerName = null! }).Sanitized().PlayerName);
        Assert.Equal(0, (ClientSettings.Default with { Player = PlayerSettings.Default with { NotificationsLogMaxTurns = -1 } }).Sanitized().Player.NotificationsLogMaxTurns);
    }

    [Fact]
    public void NullMembersInFileBecomeDefaults()
    {
        var loaded = LoadText("{\"version\":1,\"player\":null,\"keyBindings\":null,\"tutorialsSeen\":null,\"language\":null}");
        Assert.Equal(PlayerSettings.Default, loaded.Player);
        Assert.Empty(loaded.KeyBindings);
        Assert.Empty(loaded.TutorialsSeen);
        Assert.Equal("English", loaded.Language);
    }

    [Fact]
    public void TutorialsSeenDropsNullsEmptiesAndDuplicates()
    {
        var loaded = LoadText("{\"version\":1,\"tutorialsSeen\":[\"a\",null,\"\",\"a\",\"b\"]}");
        Assert.Equal(["a", "b"], loaded.TutorialsSeen);
    }

    [Fact]
    public void NegativeAutosaveTurnsInFileBecomeZero() => Assert.Equal(0, LoadText("{\"version\":1,\"autosaveTurns\":-4}").AutosaveTurns);

    [Fact]
    public void WithTutorialSeenAddsOnce()
    {
        var once = ClientSettings.Default.WithTutorialSeen("t");
        Assert.Same(once, once.WithTutorialSeen("t"));
        Assert.Equal(["t"], once.TutorialsSeen);
    }

    [Fact]
    public void SaveIsAtomicAndLeavesNoTempFile()
    {
        var store = new ClientSettingsStore(Path.Combine(dir, "sub", "s.json"));
        Assert.True(store.Save(ClientSettings.Default));
        Assert.True(store.Save(ClientSettings.Default with { Language = "French" }));
        Assert.False(File.Exists(store.Path + ".tmp"));
        Assert.Equal("French", store.Load().Language);
    }

    [Fact]
    public void SaveToUnwritableLocationReturnsFalse()
    {
        // The parent "directory" is a regular file, so the directory cannot be created.
        string blocker = Path.Combine(dir, "blocker");
        File.WriteAllText(blocker, "x");
        var store = new ClientSettingsStore(Path.Combine(blocker, "s.json"));
        Assert.False(store.Save(ClientSettings.Default));
    }

    [Fact]
    public void SaveWhenTargetIsDirectoryReturnsFalse()
    {
        Directory.CreateDirectory(FilePath);
        File.WriteAllText(Path.Combine(FilePath, "inner"), "x");
        Assert.False(new ClientSettingsStore(FilePath).Save(ClientSettings.Default));
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void CorruptFileIsBackedUpBeforeFirstSave()
    {
        const string garbage = "{\"version\":1,\"lang";
        File.WriteAllText(FilePath, garbage);
        var store = new ClientSettingsStore(FilePath);
        store.Load();
        Assert.True(store.Save(ClientSettings.Default));
        Assert.Equal(garbage, File.ReadAllText(store.BackupPath));
        Assert.Equal(ClientSettings.Default.Language, store.Load().Language);
    }

    [Fact]
    public void NewerVersionFileIsBackedUpBeforeFirstSave()
    {
        const string newer = "{\"version\":9,\"language\":\"German\",\"future\":true}";
        File.WriteAllText(FilePath, newer);
        var service = new ClientSettingsService(new ClientSettingsStore(FilePath));
        Assert.Equal("English", service.Current.Language);
        Assert.True(service.Update(s => s with { UiScale = 2f }));
        Assert.Equal(newer, File.ReadAllText(FilePath + ".bak"));
        Assert.Equal(2f, new ClientSettingsStore(FilePath).Load().UiScale);
    }

    [Fact]
    public void BackupIsMadeOnlyOnceAndNotForGoodFiles()
    {
        var good = new ClientSettingsStore(FilePath);
        Assert.True(good.Save(ClientSettings.Default));
        good.Load();
        Assert.True(good.Save(ClientSettings.Default));
        Assert.False(File.Exists(good.BackupPath));

        File.WriteAllText(FilePath, "garbage");
        var store = new ClientSettingsStore(FilePath);
        store.Load();
        Assert.True(store.Save(ClientSettings.Default));
        File.WriteAllText(store.BackupPath, "kept");
        Assert.True(store.Save(ClientSettings.Default with { Language = "French" }));
        Assert.Equal("kept", File.ReadAllText(store.BackupPath));
    }

    [Fact]
    public void FailedBackupRefusesToOverwrite()
    {
        const string newer = "{\"version\":9}";
        File.WriteAllText(FilePath, newer);
        Directory.CreateDirectory(FilePath + ".bak");
        var store = new ClientSettingsStore(FilePath);
        store.Load();
        Assert.False(store.Save(ClientSettings.Default));
        Assert.Equal(newer, File.ReadAllText(FilePath));
    }

    [Fact]
    public void DiffCommandIsNullWhenNothingDiffers()
    {
        Assert.Null(ClientSettings.Default.ToDiffCommand(PlayerSettings.Default));
    }

    [Fact]
    public void DiffCommandSetsOnlyChangedMembers()
    {
        var settings = ClientSettings.Default with { Player = PlayerSettings.Default with { AutoAssignCityProduction = true, NotificationsLogMaxTurns = 9 } };
        var command = settings.ToDiffCommand(PlayerSettings.Default)!;
        Assert.True(command.AutoAssignCityProduction);
        Assert.Equal(9, command.NotificationsLogMaxTurns);
        Assert.Null(command.AutoAssignSpecialistsInNewCities);
        Assert.Null(command.AutomatedUnitsMoveOnTurnStart);
        Assert.Null(command.CitiesAutoBombardAtEndOfTurn);
        Assert.Null(command.AutomatedUnitsChoosePromotions);
        Assert.Null(command.AutoBuildingRoads);
        Assert.Null(command.AutomatedWorkersReplaceImprovements);
        Assert.Null(command.StopAutomatedWorkersRemoveVegetation);
        Assert.Null(command.HumanConquestPrompt);
    }

    [Fact]
    public void FullCommandSetsEveryMember()
    {
        var c = ClientSettings.Default.ToCommand();
        Assert.NotNull(c.AutoAssignCityProduction);
        Assert.NotNull(c.AutoAssignSpecialistsInNewCities);
        Assert.NotNull(c.AutomatedUnitsMoveOnTurnStart);
        Assert.NotNull(c.CitiesAutoBombardAtEndOfTurn);
        Assert.NotNull(c.NotificationsLogMaxTurns);
        Assert.NotNull(c.AutomatedUnitsChoosePromotions);
        Assert.NotNull(c.AutoBuildingRoads);
        Assert.NotNull(c.AutomatedWorkersReplaceImprovements);
        Assert.NotNull(c.StopAutomatedWorkersRemoveVegetation);
        Assert.NotNull(c.HumanConquestPrompt);
    }

    [Fact]
    public void UpdateSavesSubmitsAndRaisesChanged()
    {
        var submitted = new List<GameCommand>();
        var service = new ClientSettingsService(new ClientSettingsStore(FilePath), c => { submitted.Add(c); return true; });
        var raised = new List<ClientSettings>();
        service.Changed += raised.Add;

        Assert.True(service.Update(s => s with { Player = s.Player with { AutoBuildingRoads = false } }));

        Assert.Single(raised);
        Assert.Same(service.Current, raised[0]);
        var command = Assert.IsType<SetPlayerSettings>(Assert.Single(submitted));
        Assert.False(command.AutoBuildingRoads);
        Assert.Null(command.AutoAssignCityProduction);
        Assert.False(new ClientSettingsStore(FilePath).Load().Player.AutoBuildingRoads);
    }

    [Fact]
    public void UpdateWithoutEngineChangeSubmitsNothingButStillRaises()
    {
        var submitted = new List<GameCommand>();
        var service = new ClientSettingsService(new ClientSettingsStore(FilePath), c => { submitted.Add(c); return true; });
        int raised = 0;
        service.Changed += _ => raised++;
        Assert.True(service.Update(s => s with { UiScale = 1.5f }));
        Assert.Empty(submitted);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void UpdateSanitizesAndReportsUnsavedFile()
    {
        Directory.CreateDirectory(FilePath);
        var service = new ClientSettingsService(new ClientSettingsStore(FilePath));
        Assert.False(service.Update(s => s with { UiScale = float.NaN, AutosaveTurns = -1 }));
        Assert.Equal(1f, service.Current.UiScale);
        Assert.Equal(0, service.Current.AutosaveTurns);
    }

    [Fact]
    public void ApplyToNewGameSkipsDefaultsAndSendsFullSetOtherwise()
    {
        var submitted = new List<GameCommand>();
        var service = new ClientSettingsService(new ClientSettingsStore(FilePath), c => { submitted.Add(c); return true; });
        service.ApplyToNewGame();
        Assert.Empty(submitted);

        service.Update(s => s with { Player = s.Player with { HumanConquestPrompt = !s.Player.HumanConquestPrompt } });
        submitted.Clear();
        service.ApplyToNewGame();
        var command = Assert.IsType<SetPlayerSettings>(Assert.Single(submitted));
        Assert.NotNull(command.AutoAssignCityProduction);
    }

    [Fact]
    public void ServiceWithoutSubmitDoesNotThrow()
    {
        var service = new ClientSettingsService(new ClientSettingsStore(FilePath));
        service.Update(s => s with { Player = s.Player with { AutoAssignCityProduction = true } });
        service.ApplyToNewGame();
    }

    [Fact]
    public void AutoPlayOptions_AreSavedAndLoadedBack()
    {
        var store = new ClientSettingsStore(FilePath);
        var settings = ClientSettings.Default with { AutoPlay = new AutoPlaySettings { ShowButton = true, UntilEnd = true, MaxTurns = 33, FullAutoPlayAI = false } };
        Assert.True(store.Save(settings));
        Assert.Contains("\"autoPlay\"", File.ReadAllText(FilePath));
        Assert.Equal(settings.AutoPlay, new ClientSettingsStore(FilePath).Load().AutoPlay);
    }

    [Fact]
    public void AutoPlayOptions_OfAnOlderFileAreTheDefaults_AndHandEditedValuesAreMadeSafe()
    {
        Assert.Equal(new AutoPlaySettings(), LoadText("{\"version\":1}").AutoPlay);
        Assert.Equal(new AutoPlaySettings(), LoadText("{\"version\":1,\"autoPlay\":null}").AutoPlay);
        Assert.Equal(200, LoadText("{\"version\":1,\"autoPlay\":{\"maxTurns\":99999}}").AutoPlay.MaxTurns);
        Assert.Equal(1, LoadText("{\"version\":1,\"autoPlay\":{\"maxTurns\":-4,\"showButton\":true}}").AutoPlay.MaxTurns);
        Assert.True(LoadText("{\"version\":1,\"autoPlay\":{\"maxTurns\":-4,\"showButton\":true}}").AutoPlay.ShowButton);
    }

    [Fact]
    public void AutoPlayOptions_ChangeThroughTheServiceWithoutTouchingTheEngineOptions()
    {
        var submitted = new List<GameCommand>();
        var service = new ClientSettingsService(new ClientSettingsStore(FilePath), c => { submitted.Add(c); return true; });
        service.Update(s => s with { AutoPlay = s.AutoPlay with { MaxTurns = 500 } });
        Assert.Equal(200, service.Current.AutoPlay.MaxTurns);
        Assert.Empty(submitted); // AutoPlay options are client settings, only the explicit StartAutoPlay carries them
        Assert.Equal(200, new ClientSettingsStore(FilePath).Load().AutoPlay.MaxTurns);
    }
}
