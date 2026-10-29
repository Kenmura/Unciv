using System.Globalization;
using Unciv.Commands;
using Unciv.Commands.Empire;
using Unciv.Commands.Turn;
using Unciv.Commands.Units;
using Unciv.Logic;
using Unciv.Models.Ruleset.Unique;
using Unciv.Queries.Empire;
using Unciv.Queries.Units;
using Unciv.UI.ObjectDescriptions;

namespace Unciv.Assets;

/// <summary>Which decision picker a <see cref="PickerSpec"/> is.</summary>
public enum PickerKind
{
    FreeGreatPerson,
    Pantheon,
    FoundReligion,
    Beliefs,
    MoveSpies,
    DiplomaticVote,
    Promotion,
}

/// <summary>Where the client takes the player for a pending <see cref="DecisionKind"/>.</summary>
public enum DecisionRoute
{
    /// <summary>A <see cref="PickerSpec"/> built by <see cref="DecisionPickerModel"/> and shown by the decision picker.</summary>
    Picker,
    TechScreen,
    PolicyScreen,
    CityScreen,
    AlertPopup,
    DiplomacyScreen,

    /// <summary>The client sends the engine's own command at once (move automated units).</summary>
    AutoCommand,

    /// <summary>Select the next unit that waits for orders.</summary>
    NextUnit,
}

/// <summary>One thing to choose. <see cref="Reason"/> says why a disabled option is; <see cref="Detail"/> is shown when it is highlighted.</summary>
public sealed record PickerOption(string Id, string Label, string Detail, bool Enabled = true, string? Reason = null);

/// <summary>One choice of the picker (a belief of one kind, a spy's destination...). A slot that is <see cref="Optional"/> may stay unchosen.</summary>
public sealed record PickerSlot(string Id, string Title, IReadOnlyList<PickerOption> Options, bool Optional = false);

/// <summary>What the player has entered: the chosen option id per slot (null = none), the name field and the checkbox.</summary>
public sealed record PickerChoice(IReadOnlyList<string?> Selected, string Text = "", bool Flag = false);

/// <summary>
/// A whole picker as plain data. <see cref="Build"/> turns a valid <see cref="PickerChoice"/> into the commands to send, in order; <see cref="Dismiss"/> is the command of
/// the "not now" button when the engine has one (spies), else null and the button just closes the picker (the decision stays pending).
/// <see cref="TextLabel"/> asks for a name that starts as the option chosen in slot <see cref="TextFollowsSlot"/>; <see cref="FlagLabel"/> adds a checkbox;
/// <see cref="DistinctSlots"/> forbids choosing one option in two slots; <see cref="NeedsAnyChoice"/> demands at least one slot be chosen even though each is optional.
/// </summary>
public sealed record PickerSpec(PickerKind Kind, string Title, string Prompt, IReadOnlyList<PickerSlot> Slots, string ConfirmLabel,
    Func<PickerChoice, IReadOnlyList<GameCommand>> Build, string? TextLabel = null, int TextFollowsSlot = -1, string? FlagLabel = null,
    bool DistinctSlots = false, bool NeedsAnyChoice = false, GameCommand? Dismiss = null, string DismissLabel = "Decide later");

/// <summary>
/// C038: the decision pickers (free great person, pantheon, found religion, enhance/reform religion, move spies, diplomatic vote, promotion) as plain data, Godot-free
/// so it is unit-tested. Every list of options is the engine's answer (<see cref="GreatPeopleResult"/>, <see cref="ReligionResult"/>, <see cref="SpyDestinationsResult"/>,
/// <see cref="DiplomaticVoteResult"/>, <see cref="PromotionChoicesResult"/>); this class words them, decides which command a confirmed choice sends, and checks the
/// entries the engine would refuse anyway (a missing choice, a belief chosen twice, a bad religion name) so the Confirm button can say why it is disabled.
/// </summary>
public static class DecisionPickerModel
{
    /// <summary>The option id of "send the spy back to the hideout".</summary>
    public const string Hideout = "<hideout>";

    /// <summary>The option id of abstaining in the World Congress vote.</summary>
    public const string Abstain = "";

    /// <summary>Kotlin <c>FoundReligion</c> rename popup: the longest name and the characters that are filtered out.</summary>
    public const int MaxReligionNameLength = 32;

    private const string ForbiddenNameChars = "[]{}\"\\<>";

    /// <summary>The route of every decision kind; the test that enumerates <see cref="DecisionKind"/> keeps this total.</summary>
    public static DecisionRoute RouteOf(DecisionKind kind) => kind switch
    {
        DecisionKind.FreeGreatPerson or DecisionKind.MoveSpies or DecisionKind.Pantheon or DecisionKind.FoundReligion or DecisionKind.EnhanceReligion
            or DecisionKind.ReformReligion or DecisionKind.DiplomaticVote => DecisionRoute.Picker,
        DecisionKind.CityProduction => DecisionRoute.CityScreen,
        DecisionKind.Technology => DecisionRoute.TechScreen,
        DecisionKind.Policy => DecisionRoute.PolicyScreen,
        DecisionKind.DueUnits => DecisionRoute.NextUnit,
        DecisionKind.MoveAutomatedUnits => DecisionRoute.AutoCommand,
        DecisionKind.Alert => DecisionRoute.AlertPopup,
        DecisionKind.TradeRequest => DecisionRoute.DiplomacyScreen,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "no route for this decision"),
    };

    /// <summary>The picker a <see cref="DecisionRoute.Picker"/> decision opens; null for the other routes.</summary>
    public static PickerKind? PickerOf(DecisionKind kind) => kind switch
    {
        DecisionKind.FreeGreatPerson => PickerKind.FreeGreatPerson,
        DecisionKind.MoveSpies => PickerKind.MoveSpies,
        DecisionKind.Pantheon => PickerKind.Pantheon,
        DecisionKind.FoundReligion => PickerKind.FoundReligion,
        DecisionKind.EnhanceReligion or DecisionKind.ReformReligion => PickerKind.Beliefs,
        DecisionKind.DiplomaticVote => PickerKind.DiplomaticVote,
        _ => null,
    };

    /// <summary>The free great person picker; null when the engine offers nothing to pick.</summary>
    public static PickerSpec? FreeGreatPerson(GreatPeopleResult people, Func<string, string> describe)
    {
        if (people.FreeGreatPeople <= 0 || people.FreeOptions.Count == 0) return null;
        var options = people.FreeOptions.Select(it => new PickerOption(it, it, describe(it))).ToList();
        return new PickerSpec(PickerKind.FreeGreatPerson, "Choose a free great person", $"Free great people left: {Int(people.FreeGreatPeople)}",
            [new PickerSlot("unit", "Great person", options)], "Choose",
            choice => [new ChooseFreeGreatPerson { Unit = choice.Selected[0].NotNullOrEmpty() }]);
    }

    /// <summary>Found a pantheon, or add a belief to the one you have (<paramref name="expand"/>); null when religion is off.</summary>
    public static PickerSpec? Pantheon(ReligionResult religion, bool expand, Func<string, string> describe)
    {
        if (!religion.Enabled) return null;
        var options = Beliefs(religion, "Pantheon", describe);
        string prompt = $"Faith: {Int(religion.Faith)}" + (expand ? "" : $" (a pantheon costs {Int(religion.FaithForPantheon)})");
        return new PickerSpec(PickerKind.Pantheon, expand ? "Expand your pantheon" : "Choose a pantheon", prompt,
            [new PickerSlot("belief", "Pantheon belief", options)], expand ? "Add belief" : "Found pantheon",
            choice => [new FoundPantheon { Belief = choice.Selected[0].NotNullOrEmpty() }]);
    }

    /// <summary>Found your religion: an icon, a name (the icon's by default) and a belief for every slot the engine lists.</summary>
    public static PickerSpec? FoundReligion(ReligionResult religion, Func<string, string> describe)
    {
        if (!religion.Enabled || religion.State != "FoundingReligion") return null;
        var slots = new List<PickerSlot>
        {
            new("icon", "Religion", religion.AvailableIcons.Select(it => new PickerOption(it, it, "")).ToList()),
        };
        slots.AddRange(BeliefSlots(religion, describe));
        return new PickerSpec(PickerKind.FoundReligion, "Found a religion", $"Faith: {Int(religion.Faith)}", slots, "Found religion",
            choice => [new FoundReligion { ReligionName = choice.Selected[0].NotNullOrEmpty(), DisplayName = choice.Text.Trim(), Beliefs = choice.Selected.Skip(1).Select(it => it.NotNullOrEmpty()).ToList() }],
            TextLabel: "Name of the religion", TextFollowsSlot: 0, DistinctSlots: true);
    }

    /// <summary>Choose the beliefs of an enhancement (<paramref name="reform"/> false) or the free beliefs of a reform; null when no belief is to be chosen.</summary>
    public static PickerSpec? ChooseBeliefs(ReligionResult religion, bool reform, Func<string, string> describe)
    {
        if (!religion.Enabled || religion.Slots.Count == 0) return null;
        string name = religion.DisplayName ?? religion.ReligionName ?? "";
        return new PickerSpec(PickerKind.Beliefs, reform ? "Reform your religion" : "Enhance your religion",
            name.Length > 0 ? $"{name}, faith: {Int(religion.Faith)}" : $"Faith: {Int(religion.Faith)}", BeliefSlots(religion, describe), "Choose beliefs",
            choice => [new ChooseBeliefs { Beliefs = choice.Selected.Select(it => it.NotNullOrEmpty()).ToList() }], DistinctSlots: true);
    }

    /// <summary>Where each of the viewer's living spies goes; every spy may stay where it is. "Decide later" sends the engine's dismiss command.</summary>
    public static PickerSpec MoveSpies(SpyDestinationsResult spies)
    {
        var slots = new List<PickerSlot>();
        foreach (var spy in spies.Spies.Where(it => it.IsAlive))
        {
            var options = new List<PickerOption>();
            if (spy.CanMoveToHideout) options.Add(new PickerOption(Hideout, "Hideout", "Back to the hideout"));
            foreach (var city in spy.Destinations)
                options.Add(new PickerOption(city.CityId, city.Civ.Length == 0 ? city.CityName : $"{city.CityName} ({city.Civ})", city.IsCityState ? "A city-state" : ""));
            slots.Add(new PickerSlot(spy.Name, $"{spy.Name} (rank {Int(spy.Rank)}, {spy.Location})", options, Optional: true));
        }
        return new PickerSpec(PickerKind.MoveSpies, "Move spies", slots.Count == 0 ? "None of your spies can move." : "Choose where to send each spy; a spy left alone stays.", slots,
            "Move spies",
            choice => slots.Select((slot, i) => (slot, pick: choice.Selected[i])).Where(it => it.pick != null)
                .Select(it => (GameCommand)new MoveSpy { Spy = it.slot.Id, CityId = it.pick == Hideout ? null : it.pick }).ToList(),
            NeedsAnyChoice: true, Dismiss: new DismissMoveSpies(), DismissLabel: "Dismiss");
    }

    /// <summary>The World Congress vote: every known civ, and abstaining. Null when the engine says the viewer cannot vote.</summary>
    public static PickerSpec? DiplomaticVote(DiplomaticVoteResult vote, Func<string, string> civName)
    {
        if (!vote.CanVote) return null;
        var options = vote.Candidates.Select(it => new PickerOption(it, civName(it), "")).Append(new PickerOption(Abstain, "Abstain", "Vote for nobody")).ToList();
        return new PickerSpec(PickerKind.DiplomaticVote, "World Congress", "Vote for the civilization that should lead the world.", [new PickerSlot("civ", "Vote for", options)],
            "Vote", choice => [new VoteDiplomatic { CivId = choice.Selected[0] is { Length: > 0 } id ? id : null }]);
    }

    /// <summary>The effect lines of a ruleset object for a picker: <c>Unique.GetDisplayText</c> translated (so "[+33]% Strength &lt;vs [Armored]&gt; units" reads "+33% Strength vs Armored units"), hidden uniques left out.</summary>
    public static string DescribeUniques(IHasUniques source) => string.Join("\n", source.UniquesToDescription());

    /// <summary>The promotions a unit may take; null when none is on offer.</summary>
    public static PickerSpec? Promotion(PromotionChoicesResult promotions, string unitName)
    {
        if (promotions.Promotions.Count == 0) return null;
        var options = promotions.Promotions.Select(it => new PickerOption(it.Name, it.Name,
            string.Join("\n", it.Prerequisites.Count == 0 ? it.Uniques : it.Uniques.Prepend("Requires: " + string.Join(" or ", it.Prerequisites))),
            it.Selectable, it.Selectable ? null : "Not enough experience")).ToList();
        return new PickerSpec(PickerKind.Promotion, $"Promote {unitName}", $"Experience: {Int(promotions.Xp)} / {Int(promotions.XpForNextPromotion)}",
            [new PickerSlot("promotion", "Promotion", options)], "Take promotion",
            choice => [new PromoteUnit { UnitId = promotions.UnitId, Promotion = choice.Selected[0].NotNullOrEmpty(), SaveAsDefault = choice.Flag }],
            FlagLabel: promotions.CanSaveDefault ? "Use this promotion for new units of this type from this city" : null);
    }

    /// <summary>The name a text field starts with: the option chosen in the slot it follows.</summary>
    public static string DefaultText(PickerSpec spec, PickerChoice choice) =>
        spec.TextFollowsSlot >= 0 && spec.TextFollowsSlot < choice.Selected.Count ? choice.Selected[spec.TextFollowsSlot] ?? "" : "";

    /// <summary>Why <paramref name="choice"/> cannot be confirmed yet, or null when it can. Wording is a template for the translator.</summary>
    public static string? Problem(PickerSpec spec, PickerChoice choice)
    {
        for (int i = 0; i < spec.Slots.Count; i++)
        {
            var slot = spec.Slots[i];
            string? pick = i < choice.Selected.Count ? choice.Selected[i] : null;
            if (pick == null)
            {
                if (!slot.Optional) return $"Choose: {slot.Title}";
                continue;
            }
            var option = slot.Options.FirstOrDefault(it => it.Id == pick);
            if (option == null) return $"Choose: {slot.Title}";
            if (!option.Enabled) return option.Reason ?? $"{option.Label} cannot be chosen";
        }
        if (spec.NeedsAnyChoice && choice.Selected.All(it => it == null)) return "Choose a destination for at least one spy";
        if (spec.DistinctSlots)
        {
            // slots that share their option ids (beliefs) may not take the same one twice; the icon slot's ids are religions, never beliefs
            var picks = spec.Slots.Select((slot, i) => (slot, pick: i < choice.Selected.Count ? choice.Selected[i] : null)).Where(it => it.pick != null && it.slot.Id != "icon").ToList();
            var twice = picks.GroupBy(it => it.pick, StringComparer.Ordinal).FirstOrDefault(it => it.Count() > 1);
            if (twice != null) return $"{twice.Key} is chosen twice";
        }
        if (spec.TextLabel != null)
        {
            string text = choice.Text.Trim();
            if (text.Length == 0) return "Enter a name";
            if (text.Length > MaxReligionNameLength) return $"The name can have at most {Int(MaxReligionNameLength)} characters";
            if (text.IndexOfAny(ForbiddenNameChars.ToCharArray()) >= 0) return "The name cannot contain any of [ ] { } \" \\ < >";
        }
        return null;
    }

    /// <summary>The commands a confirmed choice sends; empty while <see cref="Problem"/> says something.</summary>
    public static IReadOnlyList<GameCommand> Commands(PickerSpec spec, PickerChoice choice) => Problem(spec, choice) == null ? spec.Build(choice) : [];

    private static List<PickerOption> Beliefs(ReligionResult religion, string type, Func<string, string> describe) =>
        (religion.Available.TryGetValue(type, out var names) ? names : []).Select(it => new PickerOption(it, it, describe(it))).ToList();

    /// <summary>One slot per belief the engine wants, in its order; a slot of kind "Any" takes any belief that is not a pantheon.</summary>
    private static List<PickerSlot> BeliefSlots(ReligionResult religion, Func<string, string> describe)
    {
        var slots = new List<PickerSlot>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string type in religion.Slots)
        {
            int number = counts[type] = counts.GetValueOrDefault(type) + 1;
            var options = type == "Any"
                ? new[] { "Founder", "Follower", "Enhancer" }.SelectMany(it => Beliefs(religion, it, describe)).ToList()
                : Beliefs(religion, type, describe);
            int total = religion.Slots.Count(it => it == type);
            slots.Add(new PickerSlot($"belief{slots.Count.ToString(CultureInfo.InvariantCulture)}", total > 1 ? $"{type} belief {Int(number)}" : $"{type} belief", options));
        }
        return slots;
    }

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string NotNullOrEmpty(this string? value) => value ?? "";
}
