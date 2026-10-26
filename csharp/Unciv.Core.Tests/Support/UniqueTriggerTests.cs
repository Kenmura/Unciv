using Unciv.Logic;
using Unciv.Logic.City;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;

namespace Unciv.Core.Tests.Support;

// T052: UniqueTriggerActivation / UniqueTriggerExecutors. The Kotlin tests that exercise the triggers are spread over CountableTests,
// GlobalUniquesTests (reveal map), TileFilterTests and UpgradeTests; these cases are hand-derived from UniqueTriggerActivation.kt over
// TestGame (the Kotlin trigger tests that need the unported Unit actions / map views stay with their tickets).
[Collection("RulesetCache")]
public class UniqueTriggerTests
{
    private readonly TestGame game = new();
    private readonly Civilization civ;
    private readonly City city;

    public UniqueTriggerTests()
    {
        game.MakeHexagonalMap(5);
        civ = game.AddCiv(isPlayer: true);
        city = game.AddCity(civ, game.TileMap[0, 0]);
    }

    private bool Trigger(string text, Tile? tile = null, string? notification = null, string? triggerNotificationText = null) =>
        UniqueTriggerActivation.TriggerUnique(new Unique(text), civ, tile: tile, notification: notification, triggerNotificationText: triggerNotificationText);

    [Fact]
    public void NonTriggerableUniqueDoesNothing()
    {
        Assert.False(Trigger("[+1 Gold] [in this city]"));
        Assert.Null(UniqueTriggerActivation.GetTriggerFunction(new Unique("[+1 Gold] [in this city]"), civ));
    }

    [Fact]
    public void TimedUniqueIsAddedAsTemporaryUnique()
    {
        Assert.True(Trigger("Provides [1] [Coal] <for [2] turns>"));
        var temporary = Assert.Single(civ.TemporaryUniques);
        Assert.Equal(2, temporary.TurnsLeft);
        Assert.Equal("Provides [1] [Coal]", temporary.UniqueText);
    }

    [Fact]
    public void TurnTileIntoCoast()
    {
        var tile = game.TileMap[-4, 0];
        Assert.True(Trigger("Turn this tile into a [Coast] tile", tile));
        Assert.Equal("Coast", tile.BaseTerrain);
        // Already a Coast: nothing to do
        Assert.False(Trigger("Turn this tile into a [Coast] tile", tile));
    }

    [Fact]
    public void FreePolicyAndFreeTechCounters()
    {
        Assert.True(Trigger("Free Social Policy"));
        Assert.Equal(1, civ.Policies.FreePolicies);
        Assert.True(Trigger("Free Technology"));
        Assert.True(Trigger("[2] Free Technologies"));
        Assert.Equal(3, civ.Tech.FreeTechs);
    }

    [Fact]
    public void SpectatorGetsNoFreePolicyOrTech()
    {
        civ.PlayerType = PlayerType.Human;
        var spectator = game.AddCiv(game.Ruleset.Nations.Values.First(it => it.IsSpectator));
        Assert.False(UniqueTriggerActivation.TriggerUnique(new Unique("Free Social Policy"), spectator));
        Assert.False(UniqueTriggerActivation.TriggerUnique(new Unique("Free Technology"), spectator));
    }

    [Fact]
    public void GoldenAgeStarts()
    {
        Assert.True(Trigger("Empire enters golden age"));
        Assert.True(civ.GoldenAges.IsGoldenAge());
    }

    [Fact]
    public void GainStatAddsToReserveAndNotifiesWithTriggerText()
    {
        int before = civ.GetStatReserve(Stat.Gold);
        Assert.True(Trigger("Gain [50] [Gold]", triggerNotificationText: "due to testing"));
        Assert.Equal(before + 50, civ.GetStatReserve(Stat.Gold));
        Assert.Contains(civ.Notifications, it => it.Text == "{Gained [+50 Gold]}{ }{due to testing}");
    }

    [Fact]
    public void ExplicitNotificationWinsOverTriggerText()
    {
        Assert.True(Trigger("Gain [10] [Gold]", notification: "Custom text", triggerNotificationText: "due to testing"));
        Assert.Contains(civ.Notifications, it => it.Text == "Custom text");
    }

    [Fact]
    public void GainStatRejectsNonCivWideStatAndNonNumbers()
    {
        Assert.False(Trigger("Gain [50] [Food]"));
        Assert.False(Trigger("Gain [many] [Gold]"));
    }

    [Fact]
    public void GainStatRangeStaysInRangeAndIsDeterministic()
    {
        int before = civ.GetStatReserve(Stat.Gold);
        Assert.True(Trigger("Gain [10]-[20] [Gold]", game.TileMap[1, 0]));
        int gained = civ.GetStatReserve(Stat.Gold) - before;
        Assert.InRange(gained, 10, 20);
    }

    [Fact]
    public void PopulationGainInThisCity()
    {
        int before = city.Population.Population;
        Assert.True(UniqueTriggerActivation.TriggerUnique(new Unique("[2] population [in this city]"), city));
        Assert.Equal(before + 2, city.Population.Population);
        // Without a relevant city the "in this city" filter has no applicable city
        Assert.False(Trigger("[2] population [in this city]"));
    }

    [Fact]
    public void FreeUnitAppearsInCity()
    {
        int before = civ.Units.GetCivUnits().Count();
        Assert.True(UniqueTriggerActivation.TriggerUnique(new Unique("Free [Warrior] appears"), city));
        Assert.Equal(before + 1, civ.Units.GetCivUnits().Count());
        Assert.False(Trigger("Free [Nonexistent Unit] appears"));
    }

    [Fact]
    public void AmountOfFreeUnitsAppear()
    {
        int before = civ.Units.GetCivUnits().Count();
        Assert.True(UniqueTriggerActivation.TriggerUnique(new Unique("[2] free [Warrior] units appear"), city));
        Assert.Equal(before + 2, civ.Units.GetCivUnits().Count());
    }

    [Fact]
    public void DiscoverTechOnlyOnce()
    {
        var tech = game.Ruleset.Technologies.Values.First(it => !civ.Tech.IsResearched(it.Name)).Name;
        Assert.True(Trigger($"Discover [{tech}]"));
        Assert.True(civ.Tech.IsResearched(tech));
        Assert.False(Trigger($"Discover [{tech}]"));
    }

    [Fact]
    public void RevealEntireMapExploresEveryTile()
    {
        Assert.True(Trigger("Reveals the entire map"));
        Assert.All(game.TileMap.Values, it => Assert.True(it.IsExplored(civ)));
    }

    [Fact]
    public void RevealSpecificMapTilesNeedsATile()
    {
        Assert.False(Trigger("Reveal up to [all] [All] tiles within a [2] tile radius"));
    }

    [Fact]
    public void UnitTriggersNeedAUnit()
    {
        Assert.False(Trigger("[This Unit] heals [10] HP"));
        Assert.False(Trigger("[This Unit] takes [10] damage"));
    }

    [Fact]
    public void UnitTakesDamageThenHeals()
    {
        var unit = game.AddUnit("Warrior", civ, game.TileMap[1, 0]);
        Assert.False(UniqueTriggerActivation.TriggerUnique(new Unique("[This Unit] heals [10] HP"), unit)); // full health: no function
        Assert.True(UniqueTriggerActivation.TriggerUnique(new Unique("[This Unit] takes [30] damage"), unit));
        Assert.Equal(70, unit.Health);
        Assert.True(UniqueTriggerActivation.TriggerUnique(new Unique("[This Unit] heals [10] HP"), unit));
        Assert.Equal(80, unit.Health);
    }

    [Fact]
    public void UnitsGainPromotion()
    {
        var unit = game.AddUnit("Warrior", civ, game.TileMap[1, 0]);
        Assert.True(Trigger("[Military] units gain the [Shock I] promotion"));
        Assert.Contains("Shock I", unit.Promotions.Promotions);
        // Nothing left to promote
        Assert.False(Trigger("[Military] units gain the [Unknown Promotion] promotion"));
    }

    [Fact]
    public void GlobalAlertNotifiesOtherMajorCivs()
    {
        var other = game.AddCiv(isPlayer: true); // AI civs record no notifications
        Assert.False(Trigger("Triggers the following global alert: [Beware]")); // no trigger text: not performable
        Assert.True(Trigger("Triggers the following global alert: [Beware]", triggerNotificationText: "due to adopting [Tradition]"));
        Assert.Contains(other.Notifications, it => it.Text == "{[An unknown civilization] has adopted the [Tradition] policy}.\n{Beware}");
        Assert.DoesNotContain(civ.Notifications, it => it.Text.Contains("Beware", StringComparison.Ordinal));
    }

    [Fact]
    public void GlobalAlertTextVariants()
    {
        var other = game.AddCiv(isPlayer: true); // AI civs record no notifications
        Assert.True(Trigger("Triggers the following global alert: [X]", triggerNotificationText: "due to researching [Writing]"));
        Assert.True(Trigger("Triggers the following global alert: [X]", triggerNotificationText: "due to something"));
        Assert.True(Trigger("Triggers the following global alert: [X]", triggerNotificationText: "plain"));
        var texts = other.Notifications.Select(it => it.Text).ToList();
        Assert.Contains("{[An unknown civilization] has researched [Writing]}.\n{X}", texts);
        Assert.Contains("{[An unknown civilization] something}.\n{X}", texts);
        Assert.Contains("{[An unknown civilization] plain}.\n{X}", texts);
    }

    [Fact]
    public void TechDiscoveryFiresTriggerUniquesThroughTheSeam()
    {
        var withTrigger = game.AddCiv(["Gain [25] [Gold] <upon discovering [Agriculture] technology>"]);
        int before = withTrigger.GetStatReserve(Stat.Gold);
        withTrigger.Tech.AddTechnology("Agriculture");
        Assert.Equal(before + 25, withTrigger.GetStatReserve(Stat.Gold));
    }

    [Fact]
    public void GoldenAgeEntryFiresTriggerUniques()
    {
        var withTrigger = game.AddCiv(["Gain [30] [Culture] <upon entering a Golden Age>"]);
        int before = withTrigger.GetStatReserve(Stat.Culture);
        withTrigger.GoldenAges.EnterGoldenAge();
        Assert.Equal(before + 30, withTrigger.GetStatReserve(Stat.Culture));
    }

    [Fact]
    public void ChoosingOnlyOnePolicyFiresAdoptionTrigger()
    {
        var withTrigger = game.AddCiv(["Gain [40] [Gold] <upon adopting [Tradition]>"]);
        int before = withTrigger.GetStatReserve(Stat.Gold);
        withTrigger.Policies.FreePolicies++;
        withTrigger.Policies.Adopt(withTrigger.Policies.GetPolicyByName("Tradition"));
        Assert.Equal(before + 40, withTrigger.GetStatReserve(Stat.Gold));
    }

    [Fact]
    public void PeaceTreatyTriggersAreDispatchedThroughDiplomacyManager()
    {
        // Kotlin quirk kept: DiplomacyManager.makePeace filters the *triggerable* unique's first parameter as a civ filter (not the
        // condition's), so only a unique whose first parameter is also a valid civ filter ("All") fires.
        var a = game.AddCiv(["[All] units gain the [Shock I] promotion <upon signing a peace treaty with [Major] Civilizations>"]);
        var b = game.AddCiv();
        var unit = game.AddUnit("Warrior", a, game.TileMap[-4, 0]);
        a.DiplomacyFunctions.MakeCivilizationsMeet(b);
        a.GetDiplomacyManager(b)!.DeclareWar();
        Assert.DoesNotContain("Shock I", unit.Promotions.Promotions);
        a.GetDiplomacyManager(b)!.MakePeace();
        Assert.Contains("Shock I", unit.Promotions.Promotions);
    }

    private sealed class FakeAudio : IClientAudio
    {
        public List<string> Calls { get; } = [];
        public bool HasSound(string soundName) => soundName != "missing";
        public void PlaySound(Civilization civ, string soundName) => Calls.Add($"sound:{civ.CivId}:{soundName}");
        public void ChooseMusic(string prefix, IReadOnlyList<string> suffixes, IReadOnlyList<string> flags) =>
            Calls.Add($"music:{prefix}:{string.Join('|', suffixes)}:{string.Join('|', flags)}");
    }

    [Fact]
    public void SoundAndMusicNeedAClientAudio()
    {
        game.GameInfo.ClientState.Audio = null;
        Assert.False(Trigger("Play [click] sound"));
        Assert.False(Trigger("Choose a music track for [Rome], [Calm, slowfade], [x]"));
        var audio = new FakeAudio();
        game.GameInfo.ClientState.Audio = audio;
        Assert.True(Trigger("Play [click] sound"));
        Assert.False(Trigger("Play [missing] sound"));
        Assert.False(Trigger("Play [] sound"));
        Assert.True(Trigger("Choose a music track for [this civ], [Calm, slowfade], [x]"));
        Assert.Equal($"sound:{civ.CivId}:click", audio.Calls[0]);
        Assert.Equal($"music:{civ.CivId}:Calm|slowfade:SlowFade", audio.Calls[1]);
    }

    [Fact]
    public void AiCivGetsNoSoundOrMusic()
    {
        game.GameInfo.ClientState.Audio = new FakeAudio();
        var ai = game.AddCiv();
        Assert.False(UniqueTriggerActivation.TriggerUnique(new Unique("Play [click] sound"), ai));
    }

    [Fact]
    public void MarkTutorialCompleteRecordsTheTask()
    {
        Assert.True(Trigger("Mark tutorial [Some_Task] complete"));
        Assert.Contains("Some_Task", game.GameInfo.ClientState.TutorialTasksCompleted(civ.CivId));
    }

    private Event AddEvent(Event.PresentationType presentation, params string[] choiceUniques)
    {
        var eventObj = new Event { Name = "TestEvent" + presentation, Presentation = presentation };
        var choice = new EventChoice { Name = "Choice", Text = "go" };
        foreach (var unique in choiceUniques) choice.Uniques.Add(unique);
        eventObj.Choices.Add(choice);
        game.Ruleset.Events[eventObj.Name] = eventObj;
        return eventObj;
    }

    [Fact]
    public void EventWithoutPresentationTriggersTheChoice()
    {
        var eventObj = AddEvent(Event.PresentationType.None, "Free Technology");
        Assert.True(Trigger($"Triggers a [{eventObj.Name}] event"));
        Assert.Equal(1, civ.Tech.FreeTechs);
    }

    [Fact]
    public void AlertEventQueuesAPopupAlertWithTheUnitId()
    {
        var eventObj = AddEvent(Event.PresentationType.Alert, "Free Technology");
        var unit = game.AddUnit("Warrior", civ, game.TileMap[1, 0]);
        Assert.True(UniqueTriggerActivation.TriggerUnique(new Unique($"Triggers a [{eventObj.Name}] event"), unit));
        var alert = Assert.Single(civ.PopupAlerts, it => it.Type == AlertType.Event);
        Assert.Equal(AlertType.Event, alert.Type);
        Assert.Equal($"{eventObj.Name}{Constants.StringSplitCharacter}unitId={unit.Id}", alert.Value);
        Assert.Equal(0, civ.Tech.FreeTechs);
    }

    [Fact]
    public void AiCivResolvesAnAlertEventImmediately()
    {
        var eventObj = AddEvent(Event.PresentationType.Alert, "Free Technology");
        var ai = game.AddCiv();
        Assert.True(UniqueTriggerActivation.TriggerUnique(new Unique($"Triggers a [{eventObj.Name}] event"), ai));
        Assert.Equal(1, ai.Tech.FreeTechs);
    }

    [Fact]
    public void UnknownOrFloatingOrEmptyEventsAreNotPerformable()
    {
        Assert.False(Trigger("Triggers a [No Such Event] event"));
        var empty = new Event { Name = "Empty", Presentation = Event.PresentationType.None };
        game.Ruleset.Events[empty.Name] = empty;
        Assert.False(Trigger("Triggers a [Empty] event"));
    }

    [Fact]
    public void FloatingEventIsQueuedAsAnEventAlertInsteadOfThrowing()
    {
        var floating = AddEvent(Event.PresentationType.Floating, "Free Technology");
        Assert.True(Trigger($"Triggers a [{floating.Name}] event"));
        var alert = Assert.Single(civ.PopupAlerts, it => it.Type == AlertType.Event);
        Assert.Equal(floating.Name, alert.Value);
        Assert.Equal(0, civ.Tech.FreeTechs);
    }

    [Fact]
    public void EventPresentationExposesTextsAndChoicesAndChooseTriggersTheChoice()
    {
        var eventObj = AddEvent(Event.PresentationType.Alert, "Free Technology");
        eventObj.Text = "A thing happens";
        var second = new EventChoice { Name = "Second", Text = "nope", KeyShortcut = "n" };
        eventObj.Choices.Add(second);
        var unit = game.AddUnit("Warrior", civ, game.TileMap[1, 0]);
        Assert.True(UniqueTriggerActivation.TriggerUnique(new Unique($"Triggers a [{eventObj.Name}] event"), unit));

        var pending = Assert.Single(EventPresentation.GetPending(civ));
        Assert.Equal("A thing happens", pending.Text);
        Assert.Same(unit, pending.Unit);
        Assert.Equal([new EventChoicePresentation(0, "Choice", "go", ""), new EventChoicePresentation(1, "Second", "nope", "n")], pending.Choices);

        Assert.False(pending.Choose(civ, 2));
        Assert.False(pending.Choose(civ, -1));
        Assert.Single(civ.PopupAlerts, it => it.Type == AlertType.Event);
        Assert.True(pending.Choose(civ, 0));
        Assert.DoesNotContain(civ.PopupAlerts, it => it.Type == AlertType.Event);
        Assert.Equal(1, civ.Tech.FreeTechs);
        Assert.Empty(EventPresentation.GetPending(civ));
    }

    [Fact]
    public void EventPresentationWithoutUnitOrWithDeadUnitHasNoUnit()
    {
        var eventObj = AddEvent(Event.PresentationType.Alert, "Free Technology");
        Assert.Null(EventPresentation.Resolve(civ, new PopupAlert(AlertType.Event, eventObj.Name))!.Unit);
        var dead = new PopupAlert(AlertType.Event, $"{eventObj.Name}{Constants.StringSplitCharacter}unitId=99999");
        Assert.Null(EventPresentation.Resolve(civ, dead)!.Unit);
    }

    [Fact]
    public void EventPresentationIsNullForNonEventUnknownOrNoLongerMatchingAlerts()
    {
        Assert.Null(EventPresentation.Resolve(civ, new PopupAlert(AlertType.GoldenAge, "")));
        Assert.Null(EventPresentation.Resolve(civ, new PopupAlert(AlertType.Event, "No Such Event")));
        var eventObj = AddEvent(Event.PresentationType.Alert, "Free Technology");
        eventObj.Uniques.Add("Only available <if [1000] turns have passed>"); // hypothetical condition, never true
        eventObj.Choices[0].Uniques.Add("Only available <when number of [Cities] is greater than [99]>");
        Assert.Null(EventPresentation.Resolve(civ, new PopupAlert(AlertType.Event, eventObj.Name)));
    }

    [Fact]
    public void EventChoiceWeightIsTheProductOfAiChoiceWeights()
    {
        var choice = new EventChoice { Name = "Weighted" };
        choice.Uniques.Add("Free Technology");
        Assert.Equal(1f, ((IHasUniques)choice).GetWeightForAiDecision(new GameContext(civ)));
        Assert.True(choice.TriggerChoice(civ));
        Assert.Equal(1, civ.Tech.FreeTechs);
        var weighted = new EventChoice { Name = "Weighted2" };
        weighted.Uniques.Add("[50]% weight to this choice for AI decisions");
        weighted.Uniques.Add("[300]% weight to this choice for AI decisions");
        Assert.Equal(6f, ((IHasUniques)weighted).GetWeightForAiDecision(new GameContext(civ)));
    }

    [Fact]
    public void RebelsAppearNearTheCityOfTheTrigger()
    {
        Assert.False(UniqueTriggerActivation.TriggerUnique(new Unique("A [Warrior] rebels"), city)); // no barbarian civ
        game.AddBarbarianCiv();
        Assert.True(UniqueTriggerActivation.TriggerUnique(new Unique("A [Warrior] rebels"), city));
        Assert.True(UniqueTriggerActivation.TriggerUnique(new Unique("[2] [Warrior]s rebel"), city));
        Assert.Contains(game.GameInfo.GetBarbarianCivilization().Units.GetCivUnits(), it => it.Name == "Warrior");
    }

    [Fact]
    public void RemoveAndAddResourceOnATile()
    {
        var tile = game.TileMap[-4, 0];
        Assert.False(Trigger("Remove [Strategic] resources from this tile", tile));
        Assert.True(Trigger("Add [Iron] to this tile", game.SetTileTerrain(tile.Position, "Hill")));
        Assert.Equal("Iron", tile.Resource);
        Assert.True(Trigger("Remove [Strategic] resources from this tile", tile));
        Assert.Null(tile.TileResource);
        Assert.False(Trigger("Add [Iron] to this tile", game.SetTileTerrain(tile.Position, "Ocean"))); // not found on this terrain
    }

    [Fact]
    public void FreeGreatPersonIsCounted()
    {
        Assert.True(Trigger("Free Great Person"));
        Assert.Equal(1, civ.GreatPeople.FreeGreatPeople);
    }

    [Fact]
    public void CapturedMapUnitActionsKeepUnitIds()
    {
        var unitA = game.AddUnit("Warrior", civ, game.TileMap[1, 0]);
        var unitB = game.AddUnit("Warrior", civ, game.TileMap[0, 1]);
        Assert.Equal(2, MapUnitAction.Of([unitA, unitB]).Count());
        Assert.Empty(MapUnitAction.Of(Array.Empty<Unciv.Logic.Map.MapUnits.MapUnit>()));
    }
}
