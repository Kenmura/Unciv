using Unciv.Assets;
using Unciv.Commands.Empire;
using Unciv.Commands.Turn;
using Unciv.Commands.Units;
using Unciv.Logic;
using Unciv.Queries.Empire;
using Unciv.Queries.Units;

namespace Unciv.Core.Tests.Assets;

public class DecisionPickerModelTests
{
    private static string Describe(string name) => "about " + name;

    private static ReligionResult Religion(string state = "FoundingReligion", string[]? slots = null, bool enabled = true) =>
        new(enabled, state, 120, "Hinduism", "My Faith", [], true, false, 25, new Dictionary<string, int>(),
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["Pantheon"] = ["God of War", "Fertility"],
                ["Founder"] = ["Tithe", "Ceremonial"],
                ["Follower"] = ["Feed", "Pagoda"],
                ["Enhancer"] = ["Crusade"],
            }, slots ?? ["Founder", "Follower"], ["Hinduism", "Islam"]);

    private static PickerChoice Pick(params string?[] selected) => new(selected);

    [Fact]
    public void EveryDecisionKindHasARouteAndEveryPickerRouteHasAPicker()
    {
        foreach (var kind in Enum.GetValues<DecisionKind>())
        {
            var route = DecisionPickerModel.RouteOf(kind);
            Assert.Equal(route == DecisionRoute.Picker, DecisionPickerModel.PickerOf(kind) != null);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => DecisionPickerModel.RouteOf((DecisionKind)999));
    }

    [Fact]
    public void FreeGreatPersonListsTheEnginesOptionsAndSendsTheChoice()
    {
        var people = new GreatPeopleResult([], 0, 2, ["Great Artist", "Great Scientist"]);
        var spec = DecisionPickerModel.FreeGreatPerson(people, Describe)!;
        Assert.Equal(["Great Artist", "Great Scientist"], spec.Slots[0].Options.Select(it => it.Id));
        Assert.Equal("Free great people left: 2", spec.Prompt);
        Assert.Equal("Choose: Great person", DecisionPickerModel.Problem(spec, Pick((string?)null)));
        var command = Assert.IsType<ChooseFreeGreatPerson>(Assert.Single(DecisionPickerModel.Commands(spec, Pick("Great Artist"))));
        Assert.Equal("Great Artist", command.Unit);
        Assert.Empty(DecisionPickerModel.Commands(spec, Pick((string?)null)));
        Assert.Null(DecisionPickerModel.FreeGreatPerson(new GreatPeopleResult([], 0, 0, ["x"]), Describe));
        Assert.Null(DecisionPickerModel.FreeGreatPerson(new GreatPeopleResult([], 0, 1, []), Describe));
    }

    [Fact]
    public void PantheonOffersPantheonBeliefsOnly()
    {
        var found = DecisionPickerModel.Pantheon(Religion("None"), expand: false, Describe)!;
        Assert.Equal("Choose a pantheon", found.Title);
        Assert.Contains("a pantheon costs 25", found.Prompt, StringComparison.Ordinal);
        Assert.Equal(["God of War", "Fertility"], found.Slots[0].Options.Select(it => it.Id));
        Assert.Equal("Fertility", Assert.IsType<FoundPantheon>(Assert.Single(DecisionPickerModel.Commands(found, Pick("Fertility")))).Belief);
        var expand = DecisionPickerModel.Pantheon(Religion("Pantheon"), expand: true, Describe)!;
        Assert.Equal("Expand your pantheon", expand.Title);
        Assert.Equal("Add belief", expand.ConfirmLabel);
        Assert.Null(DecisionPickerModel.Pantheon(Religion(enabled: false), false, Describe));
    }

    [Fact]
    public void FoundReligionNeedsIconNameAndDistinctBeliefsInSlotOrder()
    {
        var spec = DecisionPickerModel.FoundReligion(Religion(), Describe)!;
        Assert.Equal(["icon", "belief0", "belief1"], spec.Slots.Select(it => it.Id));
        Assert.Equal(["Founder belief", "Follower belief"], spec.Slots.Skip(1).Select(it => it.Title));
        Assert.Equal(["Hinduism", "Islam"], spec.Slots[0].Options.Select(it => it.Id));
        Assert.Equal("Islam", DecisionPickerModel.DefaultText(spec, Pick("Islam", null, null)));
        Assert.Equal("", DecisionPickerModel.DefaultText(spec, new PickerChoice([])));
        Assert.Equal("Enter a name", DecisionPickerModel.Problem(spec, new PickerChoice(["Islam", "Tithe", "Feed"], "  ")));
        Assert.Equal("Choose: Religion", DecisionPickerModel.Problem(spec, new PickerChoice([null, "Tithe", "Feed"], "x")));
        Assert.Contains("at most 32", DecisionPickerModel.Problem(spec, new PickerChoice(["Islam", "Tithe", "Feed"], new string('a', 33)))!, StringComparison.Ordinal);
        Assert.Contains("cannot contain", DecisionPickerModel.Problem(spec, new PickerChoice(["Islam", "Tithe", "Feed"], "a<b"))!, StringComparison.Ordinal);
        var ok = new PickerChoice(["Islam", "Tithe", "Feed"], " Mine ");
        Assert.Null(DecisionPickerModel.Problem(spec, ok));
        var command = Assert.IsType<FoundReligion>(Assert.Single(DecisionPickerModel.Commands(spec, ok)));
        Assert.Equal(("Islam", "Mine"), (command.ReligionName, command.DisplayName));
        Assert.Equal(["Tithe", "Feed"], command.Beliefs);
        Assert.Null(DecisionPickerModel.FoundReligion(Religion("Religion"), Describe));
    }

    [Fact]
    public void BeliefSlotOfKindAnyTakesAnyNonPantheonBeliefAndTwiceIsRefused()
    {
        var spec = DecisionPickerModel.ChooseBeliefs(Religion("Religion", ["Any", "Any"]), reform: true, Describe)!;
        Assert.Equal("Reform your religion", spec.Title);
        Assert.Equal(["Any belief 1", "Any belief 2"], spec.Slots.Select(it => it.Title));
        Assert.Equal(["Tithe", "Ceremonial", "Feed", "Pagoda", "Crusade"], spec.Slots[0].Options.Select(it => it.Id));
        Assert.Equal("Feed is chosen twice", DecisionPickerModel.Problem(spec, Pick("Feed", "Feed")));
        Assert.Equal(["Feed", "Crusade"], Assert.IsType<ChooseBeliefs>(Assert.Single(DecisionPickerModel.Commands(spec, Pick("Feed", "Crusade")))).Beliefs);
        Assert.Equal("Enhance your religion", DecisionPickerModel.ChooseBeliefs(Religion("EnhancingReligion", ["Enhancer"]), false, Describe)!.Title);
        Assert.Null(DecisionPickerModel.ChooseBeliefs(Religion("Religion", []), false, Describe));
        Assert.Null(DecisionPickerModel.ChooseBeliefs(Religion(enabled: false), false, Describe));
    }

    [Fact]
    public void MoveSpiesSendsOneCommandPerChosenSpyAndDismissesWithTheEnginesCommand()
    {
        var spies = new SpyDestinationsResult(
        [
            new SpyEntry("Ann", 1, "Idle", "Hideout", true, false, [new SpyDestination("c1", "Rome", "Rome", false), new SpyDestination("c2", "Sparta", "", true)], false, null),
            new SpyEntry("Bob", 2, "Idle", "Rome", true, true, [], false, null),
            new SpyEntry("Cid", 1, "Dead", "", false, false, [], false, null),
        ]);
        var spec = DecisionPickerModel.MoveSpies(spies);
        Assert.Equal(["Ann", "Bob"], spec.Slots.Select(it => it.Id));
        Assert.Equal(["Rome (Rome)", "Sparta"], spec.Slots[0].Options.Select(it => it.Label));
        Assert.Equal([DecisionPickerModel.Hideout], spec.Slots[1].Options.Select(it => it.Id));
        Assert.Equal("Choose a destination for at least one spy", DecisionPickerModel.Problem(spec, Pick(null, null)));
        var commands = DecisionPickerModel.Commands(spec, Pick("c1", DecisionPickerModel.Hideout)).Cast<MoveSpy>().ToList();
        Assert.Equal([("Ann", "c1"), ("Bob", (string?)null)], commands.Select(it => (it.Spy, it.CityId)));
        Assert.Single(DecisionPickerModel.Commands(spec, Pick(null, DecisionPickerModel.Hideout)));
        Assert.IsType<DismissMoveSpies>(spec.Dismiss);
        Assert.Equal("None of your spies can move.", DecisionPickerModel.MoveSpies(new SpyDestinationsResult([])).Prompt);
    }

    [Fact]
    public void VoteListsCandidatesAndAbstainingSendsANullCiv()
    {
        Assert.Null(DecisionPickerModel.DiplomaticVote(new DiplomaticVoteResult(false, []), it => it));
        var spec = DecisionPickerModel.DiplomaticVote(new DiplomaticVoteResult(true, ["Rome", "Greece"]), it => it.ToUpperInvariant())!;
        Assert.Equal(["ROME", "GREECE", "Abstain"], spec.Slots[0].Options.Select(it => it.Label));
        Assert.Equal("Greece", Assert.IsType<VoteDiplomatic>(Assert.Single(DecisionPickerModel.Commands(spec, Pick("Greece")))).CivId);
        Assert.Null(Assert.IsType<VoteDiplomatic>(Assert.Single(DecisionPickerModel.Commands(spec, Pick(DecisionPickerModel.Abstain)))).CivId);
        Assert.Equal("Choose: Vote for", DecisionPickerModel.Problem(spec, Pick((string?)null)));
    }

    [Fact]
    public void PromotionDisablesWhatTheEngineRefusesAndCarriesTheDefaultFlag()
    {
        var result = new PromotionChoicesResult(7, 15, 30, true, true,
        [
            new PromotionChoice("Accuracy I", true, [], ["+25% vs ranged"]),
            new PromotionChoice("Accuracy II", false, ["Accuracy I", "Shock I"], ["more"]),
        ]);
        var spec = DecisionPickerModel.Promotion(result, "Archer")!;
        Assert.Equal("Promote Archer", spec.Title);
        Assert.Equal("Experience: 15 / 30", spec.Prompt);
        Assert.Contains("Requires: Accuracy I or Shock I", spec.Slots[0].Options[1].Detail, StringComparison.Ordinal);
        Assert.Equal("Not enough experience", DecisionPickerModel.Problem(spec, Pick("Accuracy II")));
        Assert.Empty(DecisionPickerModel.Commands(spec, Pick("Accuracy II")));
        var command = Assert.IsType<PromoteUnit>(Assert.Single(DecisionPickerModel.Commands(spec, new PickerChoice(["Accuracy I"], "", true))));
        Assert.Equal((7, "Accuracy I", true), (command.UnitId, command.Promotion, command.SaveAsDefault));
        Assert.NotNull(spec.FlagLabel);
        Assert.Null(DecisionPickerModel.Promotion(result with { CanSaveDefault = false }, "Archer")!.FlagLabel);
        Assert.Null(DecisionPickerModel.Promotion(result with { Promotions = [] }, "Archer"));
        Assert.Equal("Choose: Promotion", DecisionPickerModel.Problem(spec, Pick("nonsense")));
    }

    [Fact]
    public void EveryRouteAndPickerIsTheOneTheClientOpens()
    {
        var routes = new Dictionary<DecisionKind, (DecisionRoute Route, PickerKind? Picker)>
        {
            [DecisionKind.FreeGreatPerson] = (DecisionRoute.Picker, PickerKind.FreeGreatPerson),
            [DecisionKind.MoveSpies] = (DecisionRoute.Picker, PickerKind.MoveSpies),
            [DecisionKind.Pantheon] = (DecisionRoute.Picker, PickerKind.Pantheon),
            [DecisionKind.FoundReligion] = (DecisionRoute.Picker, PickerKind.FoundReligion),
            [DecisionKind.EnhanceReligion] = (DecisionRoute.Picker, PickerKind.Beliefs),
            [DecisionKind.ReformReligion] = (DecisionRoute.Picker, PickerKind.Beliefs),
            [DecisionKind.DiplomaticVote] = (DecisionRoute.Picker, PickerKind.DiplomaticVote),
            [DecisionKind.CityProduction] = (DecisionRoute.CityScreen, null),
            [DecisionKind.Technology] = (DecisionRoute.TechScreen, null),
            [DecisionKind.Policy] = (DecisionRoute.PolicyScreen, null),
            [DecisionKind.DueUnits] = (DecisionRoute.NextUnit, null),
            [DecisionKind.MoveAutomatedUnits] = (DecisionRoute.AutoCommand, null),
            [DecisionKind.Alert] = (DecisionRoute.AlertPopup, null),
            [DecisionKind.TradeRequest] = (DecisionRoute.DiplomacyScreen, null),
        };
        Assert.Equal(Enum.GetValues<DecisionKind>().Order(), routes.Keys.Order());
        foreach (var (kind, expected) in routes)
            Assert.Equal(expected, (DecisionPickerModel.RouteOf(kind), DecisionPickerModel.PickerOf(kind)));
    }

    [Fact]
    public void ReligionNameLimitsAndForbiddenCharactersAreExact()
    {
        var spec = DecisionPickerModel.FoundReligion(Religion(), Describe)!;
        PickerChoice Named(string name) => new(["Islam", "Tithe", "Feed"], name);
        Assert.Null(DecisionPickerModel.Problem(spec, Named(new string('a', DecisionPickerModel.MaxReligionNameLength))));
        foreach (char forbidden in "[]{}\"\\<>")
            Assert.Contains("cannot contain", DecisionPickerModel.Problem(spec, Named("a" + forbidden))!, StringComparison.Ordinal);
        Assert.Null(DecisionPickerModel.Problem(spec, Named("St. Mary's (the Great) / 2")));
    }

    [Fact]
    public void ABeliefChosenInTwoSlotsIsRefusedButTheIconMayShareABeliefsName()
    {
        var founding = DecisionPickerModel.FoundReligion(Religion(slots: ["Founder", "Founder"]), Describe)!;
        Assert.Equal("Tithe is chosen twice", DecisionPickerModel.Problem(founding, new PickerChoice(["Islam", "Tithe", "Tithe"], "x")));
        var shared = Religion() with { AvailableIcons = ["Feed", "Tithe"] };
        var spec = DecisionPickerModel.FoundReligion(shared, Describe)!;
        Assert.Null(DecisionPickerModel.Problem(spec, new PickerChoice(["Feed", "Tithe", "Feed"], "x")));
    }

    [Fact]
    public void ShortChoicesAreIncompleteNotACrashAndATextStartsFromANullChoiceAsEmpty()
    {
        var spec = DecisionPickerModel.FoundReligion(Religion(), Describe)!;
        Assert.Equal("Choose: Religion", DecisionPickerModel.Problem(spec, new PickerChoice([])));
        Assert.Equal("Choose: Founder belief", DecisionPickerModel.Problem(spec, new PickerChoice(["Islam"], "x")));
        Assert.Equal("", DecisionPickerModel.DefaultText(spec, Pick(null, "Tithe", "Feed")));
    }

    [Fact]
    public void BeliefsPromptNamesTheReligionByDisplayNameThenIdAndShowsFaith()
    {
        var spec = DecisionPickerModel.ChooseBeliefs(Religion("Religion", ["Follower"]), false, Describe)!;
        Assert.Equal("My Faith, faith: 120", spec.Prompt);
        var anon = Religion("Religion", ["Follower"]) with { DisplayName = null };
        Assert.Equal("Hinduism, faith: 120", DecisionPickerModel.ChooseBeliefs(anon, false, Describe)!.Prompt);
        var none = anon with { ReligionName = null };
        Assert.Equal("Faith: 120", DecisionPickerModel.ChooseBeliefs(none, false, Describe)!.Prompt);
        Assert.Equal("Faith: 120", DecisionPickerModel.Pantheon(Religion("Pantheon"), true, Describe)!.Prompt);
    }

    [Fact]
    public void SpyAndVoteAndPromotionOptionsAreWordedForThePlayer()
    {
        var spies = new SpyDestinationsResult(
        [
            new SpyEntry("Ann", 3, "Idle", "Hideout", true, true, [new SpyDestination("c1", "Rome", "Rome", false), new SpyDestination("c2", "Sparta", "", true)], false, null),
        ]);
        var spec = DecisionPickerModel.MoveSpies(spies);
        Assert.Equal("Choose where to send each spy; a spy left alone stays.", spec.Prompt);
        Assert.Equal("Ann (rank 3, Hideout)", spec.Slots[0].Title);
        Assert.Equal(["Back to the hideout", "", "A city-state"], spec.Slots[0].Options.Select(it => it.Detail));
        Assert.Equal("Vote for nobody", DecisionPickerModel.DiplomaticVote(new DiplomaticVoteResult(true, ["Rome"]), it => it)!.Slots[0].Options[^1].Detail);
        var result = new PromotionChoicesResult(7, 15, 30, true, true,
        [
            new PromotionChoice("Accuracy I", true, [], ["+25% vs ranged", "extra"]),
            new PromotionChoice("Accuracy II", false, ["Accuracy I"], ["more", "again"]),
        ]);
        var options = DecisionPickerModel.Promotion(result, "Archer")!.Slots[0].Options;
        Assert.Equal("+25% vs ranged\nextra", options[0].Detail);
        Assert.Equal("Requires: Accuracy I\nmore\nagain", options[1].Detail);
        Assert.Null(options[0].Reason);
        Assert.Equal("Not enough experience", options[1].Reason);
    }

    [Fact]
    public void ADisabledOptionWithoutAReasonIsNamedInTheProblem()
    {
        var spec = new PickerSpec(PickerKind.Promotion, "t", "p", [new PickerSlot("s", "S", [new PickerOption("a", "Alpha", "", false)])], "ok", _ => []);
        Assert.Equal("Alpha cannot be chosen", DecisionPickerModel.Problem(spec, Pick("a")));
    }
}
