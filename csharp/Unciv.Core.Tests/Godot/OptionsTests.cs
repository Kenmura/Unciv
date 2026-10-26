using Unciv.GodotClient.Settings;

namespace Unciv.Tests.Godot;

// C028: the Godot-free options logic (hotkeys, edge-pan, ranges) and the new settings fields.
public sealed class OptionsTests
{
    private static readonly IReadOnlyDictionary<string, string> NoBindings = new Dictionary<string, string>();

    [Theory]
    [InlineData("n", "N")]
    [InlineData(" space ", "Space")]
    [InlineData("SPACE", "Space")]
    [InlineData("shift+ctrl+z", "Ctrl+Shift+Z")]
    [InlineData("Alt+Ctrl+Shift+F5", "Ctrl+Alt+Shift+F5")]
    [InlineData("f5", "F5")]
    public void NormalizeGivesCanonicalText(string input, string expected) => Assert.Equal(expected, Hotkeys.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Ctrl")]
    [InlineData("Shift+Shift")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl++")]
    [InlineData("Escape")]
    [InlineData("Left")]
    [InlineData("up")]
    [InlineData("A B")]
    [InlineData("Foo+N")]
    public void NormalizeRejectsUnusableKeys(string? input) => Assert.Null(Hotkeys.Normalize(input));

    [Fact]
    public void DefaultsAreUniqueCanonicalAndCoverEveryAction()
    {
        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            Assert.True(Hotkeys.Defaults.ContainsKey(action), action.ToString());
            Assert.Equal(Hotkeys.Defaults[action], Hotkeys.Normalize(Hotkeys.Defaults[action]));
        }
        Assert.Equal(Hotkeys.Defaults.Count, Hotkeys.Defaults.Values.Distinct().Count());
    }

    [Fact]
    public void ResolveUsesDefaultsAndNormalises()
    {
        Assert.Equal(HotkeyAction.EndTurn, Hotkeys.Resolve(NoBindings, "space"));
        Assert.Equal(HotkeyAction.NextUnit, Hotkeys.Resolve(NoBindings, "N"));
        Assert.Null(Hotkeys.Resolve(NoBindings, "Q"));
        Assert.Null(Hotkeys.Resolve(NoBindings, null));
    }

    [Fact]
    public void RebindMovesTheActionAndFreesTheOldKey()
    {
        var bound = Hotkeys.Rebind(NoBindings, HotkeyAction.NextUnit, "q", out var swapped);
        Assert.NotNull(bound);
        Assert.Null(swapped);
        Assert.Equal(new Dictionary<string, string> { ["NextUnit"] = "Q" }, bound);
        Assert.Equal(HotkeyAction.NextUnit, Hotkeys.Resolve(bound, "Q"));
        Assert.Null(Hotkeys.Resolve(bound, "N"));
        Assert.Equal("Q", Hotkeys.KeyFor(bound, HotkeyAction.NextUnit));
    }

    [Fact]
    public void RebindToAKeyInUseSwapsTheTwoActions()
    {
        var bound = Hotkeys.Rebind(NoBindings, HotkeyAction.NextUnit, "Space", out var swapped);
        Assert.Equal(HotkeyAction.EndTurn, swapped);
        Assert.Equal("Space", Hotkeys.KeyFor(bound!, HotkeyAction.NextUnit));
        Assert.Equal("N", Hotkeys.KeyFor(bound!, HotkeyAction.EndTurn));
        Assert.Equal(2, bound!.Count);
        var effective = Enum.GetValues<HotkeyAction>().Select(a => Hotkeys.KeyFor(bound, a)).ToList();
        Assert.Equal(effective.Count, effective.Distinct().Count());
    }

    [Fact]
    public void RebindBackToTheDefaultRemovesTheEntry()
    {
        var bound = Hotkeys.Rebind(NoBindings, HotkeyAction.NextUnit, "Q", out _)!;
        var back = Hotkeys.Rebind(bound, HotkeyAction.NextUnit, "N", out _);
        Assert.Empty(back!);
    }

    [Fact]
    public void RebindToTheSameKeyChangesNothing()
    {
        var same = Hotkeys.Rebind(NoBindings, HotkeyAction.EndTurn, "Space", out var swapped);
        Assert.Empty(same!);
        Assert.Null(swapped);
    }

    [Fact]
    public void RebindRefusesUnusableKeys()
    {
        Assert.Null(Hotkeys.Rebind(NoBindings, HotkeyAction.EndTurn, "Escape", out var swapped));
        Assert.Null(swapped);
        Assert.Null(Hotkeys.Rebind(NoBindings, HotkeyAction.EndTurn, "Ctrl", out _));
    }

    [Fact]
    public void SanitizeDropsGarbageDefaultsAndConflicts()
    {
        var kept = Hotkeys.Sanitize(new Dictionary<string, string> { ["NextUnit"] = "q", ["Bogus"] = "X", ["OpenTech"] = "Escape", ["EndTurn"] = "Space" });
        Assert.Equal(new Dictionary<string, string> { ["NextUnit"] = "Q" }, kept);
        // NextUnit onto EndTurn's key without moving EndTurn is ambiguous: everything resets.
        Assert.Empty(Hotkeys.Sanitize(new Dictionary<string, string> { ["NextUnit"] = "Space" }));
        Assert.Empty(Hotkeys.Sanitize(null));
    }

    [Fact]
    public void ResetAllEmptiesTheBindings() => Assert.Empty(Hotkeys.ResetAll());

    [Fact]
    public void VolumeCurveIsLogarithmicAndClamped()
    {
        Assert.Equal(-80f, OptionsModel.VolumeDb(0f));
        Assert.Equal(-80f, OptionsModel.VolumeDb(-1f));
        Assert.Equal(-80f, OptionsModel.VolumeDb(float.NaN));
        Assert.Equal(0f, OptionsModel.VolumeDb(1f), 4);
        Assert.Equal(0f, OptionsModel.VolumeDb(5f), 4);
        Assert.Equal(-6.0206f, OptionsModel.VolumeDb(0.5f), 3);
        Assert.Equal(-80f, OptionsModel.VolumeDb(1e-9f));
    }

    [Fact]
    public void UiScaleStepsAndClamps()
    {
        Assert.Equal(1.25f, OptionsModel.StepUiScale(1f, 1));
        Assert.Equal(0.75f, OptionsModel.StepUiScale(1f, -1));
        Assert.Equal(4f, OptionsModel.StepUiScale(4f, 1));
        Assert.Equal(0.5f, OptionsModel.StepUiScale(0.5f, -1));
    }

    [Fact]
    public void PackNamesListDefaultFirstAndDropUnsafeNames()
    {
        Assert.Equal(["Default", "alpha", "beta"], OptionsModel.PackNames(["beta", "alpha", "Default", "..", "a/b", "", "alpha"]));
        Assert.Equal(["Default"], OptionsModel.PackNames([]));
        Assert.Null(OptionsModel.RegistryPack("Default"));
        Assert.Null(OptionsModel.RegistryPack(" "));
        Assert.Equal("alpha", OptionsModel.RegistryPack("alpha"));
    }

    [Theory]
    [InlineData(false, 0f, 0f, 0f, 0f)]
    [InlineData(true, 500f, 400f, 0f, 0f)]
    [InlineData(true, 0f, 400f, -1f, 0f)]
    [InlineData(true, 1000f, 400f, 1f, 0f)]
    [InlineData(true, 500f, 0f, 0f, -1f)]
    [InlineData(true, 500f, 800f, 0f, 1f)]
    [InlineData(true, 8f, 400f, -0.5f, 0f)]
    [InlineData(true, 1000f, 800f, 1f, 1f)]
    [InlineData(true, -1f, 400f, 0f, 0f)]
    [InlineData(true, 500f, 801f, 0f, 0f)]
    [InlineData(true, 1001f, 400f, 0f, 0f)]
    [InlineData(true, 992f, 400f, 0.5f, 0f)]
    public void EdgePanDirectionRampsInTheBand(bool enabled, float x, float y, float expectedX, float expectedY)
    {
        var (dx, dy) = OptionsModel.EdgePanDirection(enabled, x, y, 1000f, 800f);
        Assert.Equal(expectedX, dx, 4);
        Assert.Equal(expectedY, dy, 4);
    }

    [Fact]
    public void EdgePanIgnoresDegenerateWindows()
    {
        Assert.Equal((0f, 0f), OptionsModel.EdgePanDirection(true, 0f, 0f, 0f, 0f));
        Assert.Equal((0f, 0f), OptionsModel.EdgePanDirection(true, 0f, 0f, float.NaN, 10f));
        // A window smaller than twice the band uses half its size as the band.
        Assert.Equal((-1f, 0f), OptionsModel.EdgePanDirection(true, 0f, 5f, 10f, 10f));
    }

    [Fact]
    public void PanDistanceScalesWithSpeedZoomAndTimeAndClampsSpeed()
    {
        Assert.Equal(1.2f * 14f * 0.5f, OptionsModel.PanDistance(1f, 14f, 0.5), 4);
        Assert.Equal(2f * OptionsModel.PanDistance(1f, 14f, 0.5), OptionsModel.PanDistance(2f, 14f, 0.5), 4);
        Assert.Equal(OptionsModel.PanDistance(OptionsModel.MaxScrollSpeed, 10f, 1), OptionsModel.PanDistance(99f, 10f, 1));
        Assert.Equal(OptionsModel.PanDistance(OptionsModel.MinScrollSpeed, 10f, 1), OptionsModel.PanDistance(0f, 10f, 1));
    }

    [Fact]
    public void SettingsSanitizeTheNewFields()
    {
        var bad = ClientSettings.Default with { SoundVolume = 7f, WindowMode = "Weird", ScrollSpeed = float.NaN, KeyBindings = new SortedDictionary<string, string> { ["NextUnit"] = "Escape" } };
        var clean = bad.Sanitized();
        Assert.Equal(1f, clean.SoundVolume);
        Assert.Equal("Windowed", clean.WindowMode);
        Assert.Equal(1f, clean.ScrollSpeed);
        Assert.Empty(clean.KeyBindings);
        Assert.Equal(0f, (ClientSettings.Default with { SoundVolume = -3f }).Sanitized().SoundVolume);
        Assert.Equal(OptionsModel.MaxScrollSpeed, (ClientSettings.Default with { ScrollSpeed = 100f }).Sanitized().ScrollSpeed);
        Assert.Equal(OptionsModel.MinScrollSpeed, (ClientSettings.Default with { ScrollSpeed = 0f }).Sanitized().ScrollSpeed);
    }

    [Fact]
    public void NewFieldsAndBindingsRoundTripThroughTheFile()
    {
        var dir = Directory.CreateTempSubdirectory("unciv-options-").FullName;
        try
        {
            var path = Path.Combine(dir, "settings.json");
            var settings = ClientSettings.Default with
            {
                SoundVolume = 0.25f,
                WindowMode = "Fullscreen",
                ScrollSpeed = 2f,
                EdgePan = true,
                ShowResources = false,
                ShowYields = false,
                KeyBindings = Hotkeys.Rebind(NoBindings, HotkeyAction.NextUnit, "Q", out _)!,
            };
            Assert.True(new ClientSettingsStore(path).Save(settings));
            var loaded = new ClientSettingsStore(path).Load();
            Assert.Equal(0.25f, loaded.SoundVolume);
            Assert.Equal("Fullscreen", loaded.WindowMode);
            Assert.Equal(2f, loaded.ScrollSpeed);
            Assert.True(loaded.EdgePan);
            Assert.False(loaded.ShowResources);
            Assert.False(loaded.ShowYields);
            Assert.Equal("Q", loaded.KeyBindings["NextUnit"]);
            File.WriteAllText(path, "{\"version\":1,\"language\":\"German\"}");
            var old = new ClientSettingsStore(path).Load();
            Assert.Equal(ClientSettings.Default.SoundVolume, old.SoundVolume);
            Assert.True(old.ShowResources);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
