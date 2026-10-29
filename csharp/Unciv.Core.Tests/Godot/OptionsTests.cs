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
        // the game screen's actions never share a default key; the unit actions are a scope of their own (C078)
        var screen = Hotkeys.Defaults.Where(it => !Hotkeys.IsUnitAction(it.Key)).Select(it => it.Value).ToList();
        Assert.Equal(screen.Count, screen.Distinct().Count());
    }

    [Fact]
    public void DefaultsAreTheKotlinKeys()
    {
        // C078 deliberate deviation from the earlier Space/N/T/P/F: the Kotlin KeyboardBinding defaults
        Assert.Equal("N", Hotkeys.Defaults[HotkeyAction.EndTurn]);
        Assert.Equal("Space", Hotkeys.Defaults[HotkeyAction.EndTurnAlternate]);
        Assert.Equal("F6", Hotkeys.Defaults[HotkeyAction.OpenTech]);
        Assert.Equal("F5", Hotkeys.Defaults[HotkeyAction.OpenPolicies]);
        Assert.Equal("E", Hotkeys.Defaults[HotkeyAction.OpenOverview]);
        Assert.Equal("Ctrl+S", Hotkeys.Defaults[HotkeyAction.SaveGame]);
        Assert.Equal("Delete", Hotkeys.Defaults[HotkeyAction.DisbandUnit]);
        Assert.Equal("X", Hotkeys.Defaults[HotkeyAction.Explore]);
    }

    [Fact]
    public void UnitActionsShareKeysAndResolveSeparately()
    {
        Assert.True(Hotkeys.IsUnitAction(HotkeyAction.Fortify));
        Assert.False(Hotkeys.IsUnitAction(HotkeyAction.OpenPedia));
        // Sleep and Fortify both default to F; the screen resolver ignores unit actions, the unit resolver lists both
        Assert.Null(Hotkeys.Resolve(NoBindings, "F"));
        Assert.Equal([HotkeyAction.Sleep, HotkeyAction.Fortify], Hotkeys.ResolveUnit(NoBindings, "f"));
        Assert.Empty(Hotkeys.ResolveUnit(NoBindings, "Q"));
        Assert.Empty(Hotkeys.ResolveUnit(NoBindings, null));
        // A (pan left) is also AirSweep's key: both resolve
        Assert.Equal(HotkeyAction.PanLeft, Hotkeys.Resolve(NoBindings, "A"));
        Assert.Equal([HotkeyAction.AirSweep], Hotkeys.ResolveUnit(NoBindings, "A"));
    }

    [Fact]
    public void UnitActionRebindDoesNotSwapButRefusesAScreenKey()
    {
        var bound = Hotkeys.Rebind(NoBindings, HotkeyAction.Fortify, "G", out var swapped); // G is also the Hurry/Found religion keys
        Assert.Null(swapped);
        Assert.Equal(new Dictionary<string, string> { ["Fortify"] = "G" }, bound);
        Assert.Equal("F", Hotkeys.KeyFor(bound!, HotkeyAction.Sleep));
        Assert.Null(Hotkeys.Rebind(NoBindings, HotkeyAction.Fortify, "N", out _)); // N is End turn
        Assert.Null(Hotkeys.Rebind(NoBindings, HotkeyAction.Fortify, "F6", out _));
        // a screen action may take a key unit actions hold: they stay bound but lose the press to the screen action
        var screen = Hotkeys.Rebind(NoBindings, HotkeyAction.OpenTech, "X", out var other);
        Assert.Null(other);
        Assert.Equal(HotkeyAction.OpenTech, Hotkeys.Resolve(screen!, "X"));
        Assert.Contains(HotkeyAction.Explore, Hotkeys.ResolveUnit(screen!, "X"));
        Assert.Equal(screen, Hotkeys.Sanitize(screen));
    }

    [Fact]
    public void ForEntryMapsEngineActionsByNameAndDisband()
    {
        Assert.Equal(HotkeyAction.DisbandUnit, Hotkeys.ForEntry(new Unciv.Assets.MenuEntry(Unciv.Assets.MenuEntryKind.Disband, "Disband unit", true)));
        Assert.Null(Hotkeys.ForEntry(new Unciv.Assets.MenuEntry(Unciv.Assets.MenuEntryKind.Improve, "Build: Farm", true, null, "Farm")));
        Assert.Null(Hotkeys.ForEntry(new Unciv.Assets.MenuEntry(Unciv.Assets.MenuEntryKind.Engine, "Odd", true)));
        Unciv.Assets.MenuEntry Engine(Unciv.Models.UnitActionType type) => new(Unciv.Assets.MenuEntryKind.Engine, "x", true,
            new Unciv.Queries.Units.UnitActionInfo(new Unciv.Commands.Units.UnitActionDescriptor(type), "x", true, false, null, null));
        Assert.Equal(HotkeyAction.Fortify, Hotkeys.ForEntry(Engine(Unciv.Models.UnitActionType.Fortify)));
        Assert.Null(Hotkeys.ForEntry(Engine(Unciv.Models.UnitActionType.Guard))); // Kotlin gives Guard no key
        // every unit action's name is an engine UnitActionType (except the panel's Disband), so ForEntry finds it
        foreach (var action in Enum.GetValues<HotkeyAction>().Where(Hotkeys.IsUnitAction).Where(a => a != HotkeyAction.DisbandUnit))
            Assert.True(Enum.TryParse<Unciv.Models.UnitActionType>(action.ToString(), out _), action.ToString());
    }

    [Fact]
    public void PediaHotkeyDefaultsToF1AndIsRebindable()
    {
        Assert.Equal("F1", Hotkeys.Defaults[HotkeyAction.OpenPedia]);
        Assert.Equal(HotkeyAction.OpenPedia, Hotkeys.Resolve(new Dictionary<string, string>(), "f1"));
        var bound = Hotkeys.Rebind(new Dictionary<string, string>(), HotkeyAction.OpenPedia, "F12", out var swapped);
        Assert.Null(swapped);
        Assert.Equal(HotkeyAction.OpenPedia, Hotkeys.Resolve(bound!, "F12"));
        Assert.Null(Hotkeys.Resolve(bound!, "F1"));
        var steal = Hotkeys.Rebind(new Dictionary<string, string>(), HotkeyAction.EndTurn, "F1", out var other);
        Assert.Equal(HotkeyAction.OpenPedia, other);
        Assert.Equal("N", Hotkeys.KeyFor(steal!, HotkeyAction.OpenPedia));
    }

    [Fact]
    public void ResolveUsesDefaultsAndNormalises()
    {
        Assert.Equal(HotkeyAction.EndTurnAlternate, Hotkeys.Resolve(NoBindings, "space"));
        Assert.Equal(HotkeyAction.EndTurn, Hotkeys.Resolve(NoBindings, "N"));
        Assert.Equal(HotkeyAction.NextUnit, Hotkeys.Resolve(NoBindings, "period"));
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
        Assert.Null(Hotkeys.Resolve(bound, "Period"));
        Assert.Equal("Q", Hotkeys.KeyFor(bound, HotkeyAction.NextUnit));
    }

    [Fact]
    public void RebindToAKeyInUseSwapsTheTwoActions()
    {
        var bound = Hotkeys.Rebind(NoBindings, HotkeyAction.NextUnit, "N", out var swapped);
        Assert.Equal(HotkeyAction.EndTurn, swapped);
        Assert.Equal("N", Hotkeys.KeyFor(bound!, HotkeyAction.NextUnit));
        Assert.Equal("Period", Hotkeys.KeyFor(bound!, HotkeyAction.EndTurn));
        Assert.Equal(2, bound!.Count);
        var effective = Enum.GetValues<HotkeyAction>().Where(a => !Hotkeys.IsUnitAction(a)).Select(a => Hotkeys.KeyFor(bound, a)).ToList();
        Assert.Equal(effective.Count, effective.Distinct().Count());
    }

    [Fact]
    public void RebindBackToTheDefaultRemovesTheEntry()
    {
        var bound = Hotkeys.Rebind(NoBindings, HotkeyAction.NextUnit, "Q", out _)!;
        var back = Hotkeys.Rebind(bound, HotkeyAction.NextUnit, "Period", out _);
        Assert.Empty(back!);
    }

    [Fact]
    public void RebindToTheSameKeyChangesNothing()
    {
        var same = Hotkeys.Rebind(NoBindings, HotkeyAction.EndTurn, "N", out var swapped);
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
        var kept = Hotkeys.Sanitize(new Dictionary<string, string> { ["NextUnit"] = "q", ["Bogus"] = "X", ["OpenTech"] = "Escape", ["EndTurn"] = "N", ["Fortify"] = "g" });
        Assert.Equal(new Dictionary<string, string> { ["NextUnit"] = "Q", ["Fortify"] = "G" }, kept);
        // NextUnit onto EndTurn's key without moving EndTurn is ambiguous: everything resets.
        Assert.Empty(Hotkeys.Sanitize(new Dictionary<string, string> { ["NextUnit"] = "N" }));
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
        Assert.Equal(["Default", "mine"], OptionsModel.PackNames(["unciv", "mine"]));
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

    [Theory]
    [InlineData("{\"soundVolume\":NaN,\"uiScale\":Infinity}")]
    [InlineData("{\"version\":1,\"soundVolume\":1e999,\"scrollSpeed\":-1e999,\"uiScale\":1e39}")]
    [InlineData("{\"version\":1,\"keyBindings\":null,\"tutorialsSeen\":null,\"player\":null,\"windowMode\":null,\"assetPack\":null}")]
    [InlineData("{\"version\":1,\"keyBindings\":{\"EndTurn\":null,\"Bogus\":\"X\",\"NextUnit\":\"T\"},\"soundVolme\":3}")]
    [InlineData("{\"version\":1,\"keyBindings\":[1,2],\"soundVolume\":\"loud\"}")]
    [InlineData("{\"version\":1,\"edgePan\":\"yes\"}")]
    [InlineData("[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[")]
    [InlineData("\0\0\0")]
    [InlineData("null")]
    [InlineData("")]
    public void HostileFilesNeverCrashAndLoadSaneValues(string text)
    {
        var dir = Directory.CreateTempSubdirectory("unciv-options-").FullName;
        try
        {
            var path = Path.Combine(dir, "settings.json");
            File.WriteAllText(path, text);
            var loaded = new ClientSettingsStore(path).Load();
            Assert.InRange(loaded.SoundVolume, 0f, 1f);
            Assert.InRange(loaded.ScrollSpeed, OptionsModel.MinScrollSpeed, OptionsModel.MaxScrollSpeed);
            Assert.InRange(loaded.UiScale, OptionsModel.MinUiScale, OptionsModel.MaxUiScale);
            Assert.Contains(loaded.WindowMode, OptionsModel.WindowModes);
            Assert.NotNull(loaded.KeyBindings);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
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
