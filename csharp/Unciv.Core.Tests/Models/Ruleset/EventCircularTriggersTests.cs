using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Validation;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Models.Ruleset;

// T103: port of tests/src/com/unciv/uniques/EventCircularTriggersTest.kt (the exact-text/order case is in RulesetValidatorUnitTests).
[Collection("RulesetCache")]
public sealed class EventCircularTriggersTests : IDisposable
{
    private readonly RulesetClass ruleset;

    public EventCircularTriggersTests()
    {
        if (RulesetCache.Count == 0) LoadRulesets();
        ruleset = RulesetCache.GetVanillaRuleset();
        ruleset.ModOptions.IsBaseRuleset = true;
    }

    public void Dispose() => RulesetCache.Clear();

    private static void LoadRulesets()
    {
        var jsons = JsonsPath();
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
    }

    private static string JsonsPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "android", "assets", "jsons");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("android/assets/jsons not found above " + AppContext.BaseDirectory);
    }

    private void AddEvent(string name, params string[][] choiceUniques)
    {
        var @event = new Event { Name = name };
        for (var i = 0; i < choiceUniques.Length; i++)
        {
            var choice = new EventChoice { Name = $"{name}-choice-{i}" };
            choice.Uniques.AddRange(choiceUniques[i]);
            @event.Choices.Add(choice);
        }
        ruleset.Events[name] = @event;
    }

    private bool HasCircularWarning() =>
        RulesetValidator.Create(ruleset).GetErrorList().Any(it => it.Text.Contains("Circular trigger in Events", StringComparison.Ordinal));

    [Fact]
    public void CircularEventSelfTriggerIsDetected()
    {
        AddEvent("Loop", ["Triggers a [Loop] event"]);
        Assert.True(HasCircularWarning());
    }

    [Fact]
    public void TwoEventsTriggeringEachOtherIsDetected()
    {
        AddEvent("PingEvent", ["Triggers a [PongEvent] event"]);
        AddEvent("PongEvent", ["Triggers a [PingEvent] event"]);
        Assert.True(HasCircularWarning());
    }

    [Fact]
    public void HumanOnlyChoiceSuppressesCircularTriggerWarning()
    {
        AddEvent("HumanLoop", [$"Only available <for [{Constants.HumanPlayer}] Civilizations>", "Triggers a [HumanLoop] event"]);
        Assert.False(HasCircularWarning());
    }

    [Fact]
    public void AiUnavailableChoiceSuppressesCircularTriggerWarning()
    {
        AddEvent("AiLoop", [$"Unavailable <for [{Constants.AiPlayer}] Civilizations>", "Triggers a [AiLoop] event"]);
        Assert.False(HasCircularWarning());
    }

    [Fact]
    public void HumanOnlyTriggerEventSuppressesCircularTriggerWarning()
    {
        AddEvent("HumanTrigger", [$"Triggers a [HumanTrigger] event <for [{Constants.HumanPlayer}] Civilizations>"]);
        Assert.False(HasCircularWarning());
    }
}
