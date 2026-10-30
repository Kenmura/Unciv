using Unciv.Assets;
using Unciv.GodotClient.Settings;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Net.Visibility;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Assets;

/// <summary>C026: tutorial names, trigger detection on the viewer's snapshot, the popup queue and the settings flags.</summary>
[Collection("RulesetCache")]
public sealed class TutorialModelTests : IDisposable
{
    private readonly RulesetClass ruleset;

    public TutorialModelTests()
    {
        RulesetCache.Clear();
        var jsons = JsonsPath();
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
        ruleset = RulesetCache.GetVanillaRuleset();
    }

    public void Dispose() => RulesetCache.Clear();

    private static string JsonsPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "android", "assets", "jsons");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("android/assets/jsons not found above " + AppContext.BaseDirectory);
    }

    private static CivDto Civ(string id, bool barbarian = false, bool defeated = false, int? happiness = 10) => new()
    {
        CivId = id,
        Name = id,
        IsCityState = false,
        IsMajor = !barbarian,
        IsBarbarian = barbarian,
        IsDefeated = defeated,
        Own = id == "Me" && happiness != null ? new OwnCivDto { Gold = 0, Happiness = happiness.Value, Techs = [], Policies = [], Spies = [] } : null,
    };

    private static UnitDto Unit(int id, string name, string owner, int x, int y, int? health = 100) => new()
    {
        Id = id,
        Name = name,
        Owner = owner,
        X = x,
        Y = y,
        HealthBucket = 4,
        IsEmbarked = false,
        IsFortified = false,
        Own = owner == "Me" && health != null ? new OwnUnitDto { Health = health.Value, CurrentMovement = 2, Xp = 0, Promotions = [] } : null,
    };

    private static TileDto Tile(int x, int y, string? owner = null, string? resource = null, bool visible = true, string? wonder = null) => new()
    {
        X = x,
        Y = y,
        Visible = visible,
        BaseTerrain = "Grassland",
        Features = [],
        NaturalWonder = wonder,
        Resource = resource,
        RiverBottom = false,
        RiverBottomLeft = false,
        RiverBottomRight = false,
        Owner = owner,
    };

    private static CityDto City(int x) => new()
    {
        Name = "C" + x,
        Owner = "Me",
        X = x,
        Y = 0,
        Population = 1,
        IsCapital = x == 0,
        IsPuppet = false,
        IsBeingRazed = false,
        Health = 1,
        MaxHealth = 1,
        VisibleBuildings = [],
        Own = new OwnCityDto { Id = "c" + x, FoodStored = 0, CultureStored = 0, Buildings = [], ConstructionQueue = [] },
    };

    private static ClientSnapshot Snapshot(int turn = 0, List<CivDto>? civs = null, List<UnitDto>? units = null, List<TileDto>? tiles = null, List<CityDto>? cities = null, List<DiplomacyDto>? diplomacy = null) => new()
    {
        Seq = 1,
        Turn = turn,
        Phase = "Playing",
        TurnStatus = new TurnStatusDto { Ended = [], Phase = "Playing" },
        You = "Me",
        Civs = civs ?? [Civ("Me")],
        Tiles = tiles ?? [],
        Cities = cities ?? [],
        Units = units ?? [],
        Diplomacy = diplomacy ?? [],
        PendingTrades = [],
        Notifications = [],
    };

    [Fact]
    public void NamesAreTheJsonNamesAndSeenKeysTheEnumNames()
    {
        Assert.Equal("Slow Start", TutorialModel.NameOf(TutorialTrigger.SlowStart));
        Assert.Equal("Culture and Policies", TutorialModel.NameOf(TutorialTrigger.CultureAndPolicies));
        Assert.Equal("EnemyCityNeedsConqueringWithMeleeUnit", TutorialModel.SeenKey(TutorialTrigger.EnemyCityNeedsConqueringWithMeleeUnit));
    }

    [Fact]
    public void DetectFindsEveryConditionInKotlinsOrder()
    {
        var snapshot = Snapshot(turn: 100, civs: [Civ("Me", happiness: -1), Civ("Barb", barbarian: true), Civ("Rome")],
            units: [Unit(1, "Worker", "Me", 0, 0, health: 50), Unit(2, "Warrior", "Barb", 1, 0)],
            tiles: [Tile(0, 0, owner: "Me", resource: "Wheat"), Tile(1, 0, owner: "Me", resource: "Iron"), Tile(2, 0, owner: "Me", resource: "Silver"), Tile(3, 0, wonder: "Krakatoa")],
            cities: [City(0), City(1), City(2)], diplomacy: [new DiplomacyDto { A = "Me", B = "Rome", AtWar = false, Status = "Peace" }]);
        var found = TutorialModel.Detect(snapshot, ruleset);
        Assert.Equal(
        [
            TutorialTrigger.Introduction, TutorialTrigger.InjuredUnits, TutorialTrigger.Workers, TutorialTrigger.SlowStart, TutorialTrigger.RoadsAndRailroads,
            TutorialTrigger.Happiness, TutorialTrigger.Unhappiness, TutorialTrigger.IdleUnits, TutorialTrigger.ContactMe, TutorialTrigger.BarbarianEncountered,
            TutorialTrigger.OtherCivEncountered, TutorialTrigger.LuxuryResource, TutorialTrigger.StrategicResource, TutorialTrigger.NaturalWonders,
        ], found);
    }

    [Fact]
    public void AFreshGameOnlyTriggersTheIntroduction()
    {
        Assert.Equal([TutorialTrigger.Introduction], TutorialModel.Detect(Snapshot(), ruleset));
    }

    [Fact]
    public void ConditionsRespectWhatTheViewerCanSee()
    {
        // A barbarian on a fogged tile, a foreign unit's missing private detail, someone else's resources and a happy empire trigger nothing.
        var snapshot = Snapshot(turn: 1, civs: [Civ("Me"), Civ("Barb", barbarian: true)], units: [Unit(2, "Warrior", "Barb", 5, 5), Unit(3, "Warrior", "Rome", 0, 0, health: null)],
            tiles: [Tile(5, 5, visible: false), Tile(0, 0, owner: "Rome", resource: "Iron")]);
        Assert.Equal([TutorialTrigger.Introduction, TutorialTrigger.SlowStart], TutorialModel.Detect(snapshot, ruleset));
    }

    [Fact]
    public void ThresholdsSitOnTheExactBoundaries()
    {
        Assert.Equal([TutorialTrigger.Introduction], TutorialModel.Detect(Snapshot(civs: [Civ("Me", happiness: 5)]), ruleset));
        Assert.Equal([TutorialTrigger.Introduction, TutorialTrigger.Happiness], TutorialModel.Detect(Snapshot(civs: [Civ("Me", happiness: 4)]), ruleset));
        Assert.Equal([TutorialTrigger.Introduction, TutorialTrigger.Happiness], TutorialModel.Detect(Snapshot(civs: [Civ("Me", happiness: 0)]), ruleset));
        Assert.Equal([TutorialTrigger.Introduction, TutorialTrigger.Happiness, TutorialTrigger.Unhappiness], TutorialModel.Detect(Snapshot(civs: [Civ("Me", happiness: -1)]), ruleset));
        Assert.DoesNotContain(TutorialTrigger.IdleUnits, TutorialModel.Detect(Snapshot(turn: 49), ruleset));
        Assert.Contains(TutorialTrigger.IdleUnits, TutorialModel.Detect(Snapshot(turn: 50), ruleset));
    }

    [Fact]
    public void ForeignUnitsAndBarbariansAreNotAnotherCivilisationMet()
    {
        // Contact with the barbarians is not meeting a civilisation, and another civ's injured unit is not the viewer's.
        var snapshot = Snapshot(civs: [Civ("Me"), Civ("Barb", barbarian: true)], units: [Unit(2, "Warrior", "Rome", 0, 0, health: 50)],
            diplomacy: [new DiplomacyDto { A = "Me", B = "Barb", AtWar = true, Status = "War" }]);
        Assert.Equal([TutorialTrigger.Introduction], TutorialModel.Detect(snapshot, ruleset));
    }

    [Fact]
    public void ADefeatedViewerOrAMissingCivTriggersNothing()
    {
        Assert.Empty(TutorialModel.Detect(Snapshot(civs: [Civ("Me", defeated: true)]), ruleset));
        Assert.Empty(TutorialModel.Detect(Snapshot(civs: [Civ("Other")]), ruleset));
    }

    [Fact]
    public void UnknownNamesNeverTrigger()
    {
        var snapshot = Snapshot(units: [Unit(1, "NoSuchUnit", "Me", 0, 0)], tiles: [Tile(0, 0, owner: "Me", resource: "NoSuchResource")]);
        Assert.Equal([TutorialTrigger.Introduction], TutorialModel.Detect(snapshot, ruleset));
    }

    [Fact]
    public void QueueShowsEachTriggerOnceStepByStepAndReportsTheFinishedOne()
    {
        var queue = new TutorialQueue(ruleset);
        var settings = ClientSettings.Default;
        Assert.Null(queue.Current);
        Assert.Null(queue.Dismiss());
        Assert.Equal(TutorialEnqueue.Queued, queue.Enqueue(TutorialTrigger.CultureAndPolicies, settings));
        Assert.Equal(TutorialEnqueue.Ignored, queue.Enqueue(TutorialTrigger.CultureAndPolicies, settings));
        Assert.Equal(TutorialEnqueue.Queued, queue.Enqueue(TutorialTrigger.Introduction, settings));
        Assert.Equal(2, queue.PendingCount);

        var page = queue.Current!;
        Assert.Equal((TutorialTrigger.CultureAndPolicies, "Culture and Policies", 0, 3), (page.Trigger, page.Title, page.Step, page.StepCount));
        Assert.False(page.IsLast);
        Assert.Null(queue.Dismiss());
        Assert.Equal(1, queue.Current!.Step);
        Assert.Null(queue.Dismiss());
        Assert.True(queue.Current!.IsLast);
        Assert.Equal(TutorialTrigger.CultureAndPolicies, queue.Dismiss());
        Assert.Equal(TutorialTrigger.Introduction, queue.Current!.Trigger);
        Assert.Equal(0, queue.Current.Step);
        Assert.Equal(TutorialTrigger.Introduction, queue.Dismiss());
        Assert.False(queue.IsShowing);
    }

    [Fact]
    public void QueueIgnoresSeenTutorialsAndTutorialsSwitchedOff()
    {
        var queue = new TutorialQueue(ruleset);
        var seen = ClientSettings.Default.WithTutorialSeen("Introduction");
        Assert.Equal(TutorialEnqueue.Ignored, queue.Enqueue(TutorialTrigger.Introduction, seen));
        Assert.Equal(TutorialEnqueue.Ignored, queue.Enqueue(TutorialTrigger.Workers, ClientSettings.Default with { ShowTutorials = false }));
        Assert.False(queue.IsShowing);
        queue.Enqueue(TutorialTrigger.Workers, ClientSettings.Default);
        queue.Clear();
        Assert.False(queue.IsShowing);
    }

    [Fact]
    public void ATutorialWithoutStepsAsksTheCallerToMarkItSeen()
    {
        // "New Game" is Civilopedia-only in the shipped ruleset: it has text lines but no popup steps.
        var queue = new TutorialQueue(ruleset);
        Assert.Equal(TutorialEnqueue.NothingToShow, queue.Enqueue(TutorialTrigger.NewGame, ClientSettings.Default));
        Assert.False(queue.IsShowing);
    }

    [Fact]
    public void ListShowsEveryTutorialWithTextAndMarksTheSeenOnes()
    {
        var settings = ClientSettings.Default.WithTutorialSeen("Introduction");
        var list = TutorialModel.List(ruleset, settings);
        Assert.Contains(list, it => it.Name == "Introduction" && it.Seen && it.Text.StartsWith("Welcome to Unciv!", StringComparison.Ordinal));
        Assert.Contains(list, it => it.Name == "Culture and Policies" && !it.Seen && it.Text.Contains("\n\n", StringComparison.Ordinal));
        Assert.Contains(list, it => it.Name == "New Game" && !it.Seen && it.Text.Length > 0); // Civilopedia-only text is listed too
        Assert.All(list, it => Assert.NotEmpty(it.Text));
    }

    [Fact]
    public void SettingsKeepTheShowFlagAndSeenListThroughSaveAndLoad()
    {
        string dir = Directory.CreateTempSubdirectory("unciv-tutorial-").FullName;
        try
        {
            var store = new ClientSettingsStore(Path.Combine(dir, "settings.json"));
            Assert.True(ClientSettings.Default.ShowTutorials);
            Assert.True(store.Save(ClientSettings.Default.WithTutorialSeen("Workers") with { ShowTutorials = false }));
            var loaded = store.Load();
            Assert.False(loaded.ShowTutorials);
            Assert.Equal(["Workers"], loaded.TutorialsSeen);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void NeverShowTutorialsEmptiesTheQueueKeepsSeenFlagsAndIsWrittenToTheSettingsFile()
    {
        var queue = new TutorialQueue(ruleset);
        queue.Enqueue(TutorialTrigger.Introduction, ClientSettings.Default);
        queue.Enqueue(TutorialTrigger.Workers, ClientSettings.Default);
        Assert.Equal(2, queue.PendingCount);
        var off = ClientSettings.Default with { ShowTutorials = false };
        queue.Clear(); // what the client does when the setting goes off
        Assert.Equal(TutorialEnqueue.Ignored, queue.Enqueue(TutorialTrigger.Introduction, off));
        Assert.Equal(0, queue.PendingCount);
        Assert.Empty(off.TutorialsSeen); // dropped tutorials are not marked seen: turning the setting back on shows them again
        string dir = Directory.CreateTempSubdirectory("unciv-tutorial-").FullName;
        try
        {
            string path = Path.Combine(dir, "settings.json");
            Assert.True(new ClientSettingsStore(path).Save(off));
            Assert.Matches("\"showTutorials\"\\s*:\\s*false", File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
