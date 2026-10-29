using Godot;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.Harness;
using Unciv.Queries.Empire;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// C020: the policy screen through the real game screen and the real engine, in the policy game (Babylon has a capital and 50 stored culture, <see cref="Scenario.Policies"/>).
/// The engine offers the policy picker, so the next-action button says "Pick a policy" and opens the screen. The branches on screen are the engine's tree and every policy is
/// pickable exactly when the engine says (<see cref="PolicyTreeResult"/>). Closing the screen with the pick pending dismisses the picker (one command); the Policies button
/// reopens it. Choosing a policy only selects it; the Adopt button sends the command, costs the culture the tree quoted, and unlocks the policies that required it. A policy
/// whose prerequisite is missing, and one the culture no longer covers, cannot be adopted and say why. The golden-age line is the top bar's.
/// </summary>
public sealed class PolicyScenario : Scenario
{
    public override string Name => "policy";

    public override bool Uses3D => true;

    public override bool Policies => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Can we tell at a glance which policies are adopted, which we can adopt now and which are locked?",
        "Is it clear what a policy does and why a locked or unaffordable one cannot be adopted yet?",
        "Do the branch columns read well, and is scrolling sideways to the later branches acceptable?",
        "Are the culture and golden-age lines easy to find?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        var human = game.GetCivilization(ScenarioGame.HumanA);
        var recording = new CityAScenario.RecordingSession(new LocalHost(session, ScenarioGame.HumanA, () => 1000L));
        var screen = new GameScreen();
        host.AddChild(screen);
        screen.Start(recording, AssetRegistry.ForRuleset(game.Ruleset));
        int logged = 0;
        int culture = 0;
        int cost = 0;

        Vector2 Centre(Button? button) => button?.GetGlobalRect().GetCenter() ?? Vector2.Zero;
        Vector2 At(string policy) => Centre(screen.Policies.PolicyButton(policy));
        ScenarioStep Show(string policy) => new Act("scroll " + policy + " into view", () => screen.Policies.EnsureVisible(policy));
        PolicyTreeResult Tree() => recording.QueryAsync(new PolicyTreeQuery()).GetAwaiter().GetResult().NotNull();
        PolicyEntry Entry(string name) => Tree().Branches.SelectMany(it => it.Policies.Append(it.Start)).First(it => it.Name == name);
        // The picker's cells are the engine's tree: same states, and pickable exactly when the engine says adoptable and the culture or a free policy is there.
        bool CellsEqualTree()
        {
            var tree = Tree();
            var cells = screen.Policies.Branches.SelectMany(it => new[] { it.Start }.Concat(it.Members).Concat(it.Complete == null ? [] : [it.Complete])).ToList();
            var entries = tree.Branches.SelectMany(it => it.Policies.Append(it.Start)).ToList();
            return cells.Count == entries.Count && cells.All(cell => entries.First(it => it.Name == cell.Name) is { } entry
                && entry.State == cell.State && cell.Pickable == (entry.State == "Adoptable" && tree.CanAdopt && entry.Kind != "BranchComplete")
                && screen.Policies.PolicyButton(cell.Name) != null);
        }

        return
        [
            new WaitFrames(3),
            new Act("dismiss the start banner", () => screen.Hud.HideBanner()),
            new AssertQuery("the engine offers a policy pick and the next-action button says so", () => Tree().ShouldShowPicker && Tree().CanAdopt && Tree().StoredCulture == ScenarioGame.PolicyCulture),
            new Press("Pick a policy"),
            new WaitFrames(2),
            new AssertQuery("the button opened the policy screen with the engine's branches, in its order", () =>
                screen.IsPolicyScreenVisible && screen.Policies.Branches.Select(it => it.Name).SequenceEqual(Tree().Branches.Select(it => it.Name))
                && screen.Policies.Branches[0].Name == "Tradition" && screen.Policies.Branches[0].Title == "Tradition (Ancient era)"),
            new AssertQuery("every policy on screen is in the engine's state, and pickable exactly when the engine allows it", () => CellsEqualTree() && screen.Policies.Branches.Count == 10),
            new AssertQuery("the culture line is the engine's numbers, and the golden-age line the top bar's", () =>
                screen.Policies.HeaderText.StartsWith($"Culture: {Tree().StoredCulture} / {Tree().CultureNeededForNextPolicy}", StringComparison.Ordinal)
                && screen.Policies.GoldenAgeText.StartsWith("Golden Age:", StringComparison.Ordinal)),
            .. OpenLayoutAsserts(screen),
            new Capture("open", "The policy screen at 640x480: culture bar and golden-age line on top, a tab per branch with the Tradition card (opener, members on the ruleset grid with connector lines, completion strip), nothing chosen yet"),
            .. SearchSteps(screen),

            new Act("remember the command log", () => logged = session.Log.Count),
            new Press("Close policies"),
            new WaitFrames(2),
            new AssertQuery("closing with the pick pending dismissed the picker with one command; the pick is no longer demanded", () =>
                !screen.IsPolicyScreenVisible && session.Log.Count == logged + 1 && session.Log[^1].Type == "dismiss-policy-picker" && !human.Policies.ShouldOpenPolicyPicker
                && !Tree().ShouldShowPicker && Tree().CanAdopt),
            new Click(() => Centre(screen.PolicyButton)),
            new WaitFrames(2),
            new AssertQuery("the Policies button reopens the screen", () => screen.IsPolicyScreenVisible && screen.Policies.Tree != null),

            new Act("remember the log and the culture", () =>
            {
                logged = session.Log.Count;
                culture = human.Policies.StoredCulture;
                cost = Tree().CultureNeededForNextPolicy;
            }),
            Show("Tradition"),
            new Click(() => At("Tradition")),
            new WaitFrames(2),
            new AssertQuery("choosing a policy only selects it: nothing sent, the detail lists its effects and Adopt is offered", () =>
                session.Log.Count == logged && screen.Policies.Selected is { Name: "Tradition", Pickable: true } && screen.Policies.DetailText.Contains("Culture", StringComparison.Ordinal)
                && !screen.Policies.AdoptButton.Disabled && screen.Policies.AdoptButton.Text == "Adopt Tradition" && !human.Policies.IsAdopted("Tradition")),
            new Capture("selected", "Tradition chosen: its effects on the right and an Adopt button; nothing is adopted until it is pressed"),
            new Press("Adopt Tradition"),
            new WaitFrames(3),
            new AssertQuery("Adopt sent one command, took exactly the quoted culture, and the screen shows Tradition adopted", () =>
                session.Log.Count == logged + 1 && session.Log[^1].Type == "adopt-policy" && session.Log[^1].Payload.GetProperty("Policy").GetString() == "Tradition" && human.Policies.IsAdopted("Tradition")
                && human.Policies.StoredCulture == culture - cost && Entry("Tradition").State == "Adopted" && CellsEqualTree()
                && screen.Policies.Selected is { State: "Adopted", Pickable: false } && screen.Policies.AdoptButton.Disabled && screen.Policies.AdoptButton.Text == "Adopted"
                && screen.Policies.Branches[0].Progress == "1 / 6 adopted"),
            new AssertQuery("the culture line follows the engine", () => screen.Policies.HeaderText.StartsWith($"Culture: {human.Policies.StoredCulture} / {Tree().CultureNeededForNextPolicy}", StringComparison.Ordinal)),

            Show("Landed Elite"),
            new Click(() => At("Landed Elite")),
            new WaitFrames(2),
            new AssertQuery("a policy whose prerequisite is missing cannot be adopted and names it", () =>
                screen.Policies.Selected is { Name: "Landed Elite", State: "Locked", Pickable: false } && screen.Policies.AdoptButton.Disabled
                && screen.Policies.DetailText.Contains("Needs Legalism first", StringComparison.Ordinal) && Entry("Landed Elite").LockedReason == "requires:Legalism"),
            Show("Legalism"),
            new Click(() => At("Legalism")),
            new Press("Adopt Legalism"),
            new WaitFrames(3),
            new AssertQuery("Legalism adopted; it unlocked Landed Elite and Monarchy (the engine says adoptable)", () =>
                human.Policies.IsAdopted("Legalism") && Entry("Landed Elite").State == "Adoptable" && Entry("Monarchy").State == "Adoptable" && CellsEqualTree()),
            new Capture("adopted", "Tradition and Legalism adopted (gold); Landed Elite and Monarchy are now open; later branches are dimmed"),

            Show("Landed Elite"),
            new Click(() => At("Landed Elite")),
            new WaitFrames(2),
            new AssertQuery("with the culture spent, an open policy is shown but cannot be adopted, and says what it costs", () =>
                !Tree().CanAdopt && Entry("Landed Elite").State == "Adoptable" && screen.Policies.Selected is { Name: "Landed Elite", Pickable: false }
                && screen.Policies.AdoptButton.Disabled && screen.Policies.DetailText.Contains("Needs", StringComparison.Ordinal) && screen.Policies.DetailText.Contains("culture", StringComparison.Ordinal)
                && CellsEqualTree()),
            new Capture("short", "Not enough culture: Landed Elite is open but Adopt is disabled and the hint gives the price"),
            new Act("remember the log", () => logged = session.Log.Count),
            new Click(() => Centre(screen.Policies.AdoptButton)),
            new AssertQuery("pressing the disabled Adopt button sent nothing", () => session.Log.Count == logged),

            new Act("widen the window to 1280x720", () => host.GetWindow().Size = new Vector2I(1280, 720)),
            new WaitFrames(4),
            new AssertQuery("the screen survived the rebuild for the new breakpoint: branch cards in a scrolled grid (no tabs), same selection, every cell still a node", () =>
                screen.IsPolicyScreenVisible && !screen.Policies.IsTabbed && screen.Policies.Selected is { Name: "Landed Elite" } && screen.Policies.BranchCards.Count == 10
                && screen.Policies.BranchCards.Values.All(it => it.IsInsideTree()) && CellsEqualTree()
                && screen.Policies.AdoptButton.IsInsideTree() && screen.Policies.AdoptButton.Text == "Adopt Landed Elite"),
            new Capture("wide", "1280x720: the branch cards flow in one scrolled grid (here one per row) beside a wider detail panel; Tradition and Legalism adopted (tinted, lit connectors), Landed Elite selected (bright ring), Adopt disabled"),
            new PressKey(Key.Escape),
            new WaitFrames(2),
            new AssertQuery("Escape closes the screen and, with no pick pending, sends nothing", () => !screen.IsPolicyScreenVisible && session.Log.Count == logged),
        ];
    }

    /// <summary>C071: the layout asserts of the freshly opened screen at 640x480 (tabs, no clipped labels, a theme variation per state, the summary).</summary>
    private static IEnumerable<ScenarioStep> OpenLayoutAsserts(GameScreen screen) =>
    [
            new AssertQuery("at the compact breakpoint one branch is shown at a time behind tabs, one tab per branch", () =>
                screen.Policies.IsTabbed && screen.Policies.BranchCards.Count == 10 && screen.Policies.BranchCards["Tradition"].IsVisibleInTree() && !screen.Policies.BranchCards["Liberty"].IsVisibleInTree()),
            new AssertQuery("no node label clips: each node's content fits its width and height, the name is ellipsized when long and the full name is in the tooltip", () =>
                screen.Policies.Branches.SelectMany(it => it.Cells).All(it => screen.Policies.PolicyButton(it.Name) is PolicyNodeCard card
                    ? card.Content.GetCombinedMinimumSize().X <= card.Size.X + 0.5f && card.Content.GetCombinedMinimumSize().Y <= card.Size.Y + 0.5f && card.NameLabel.TextOverrunBehavior == TextServer.OverrunBehavior.TrimEllipsis && card.TooltipText.Contains(it.Name, StringComparison.Ordinal)
                    : screen.Policies.PolicyButton(it.Name) is { } strip && it.IsComplete && strip.TooltipText.Contains(it.Name, StringComparison.Ordinal))),
            new AssertQuery("every node has the theme variation of its state, and the culture bar shows the engine's numbers", () =>
                screen.Policies.Branches.SelectMany(it => it.Cells).All(it => screen.Policies.PolicyButton(it.Name).NotNull().ThemeTypeVariation == Unciv.GodotClient.Ui.UiVariants.PolicyNode(it.State, it.Pickable))
                && screen.Policies.PolicyButton("Tradition").NotNull().ThemeTypeVariation == "PolicyNodeAvailable" && screen.Policies.PolicyButton("Liberty").NotNull().ThemeTypeVariation == "PolicyNodeAvailable"),
    ];

    /// <summary>C071: the search box dims what does not match, by name and by effect, and clearing it lights every policy again.</summary>
    private static IEnumerable<ScenarioStep> SearchSteps(GameScreen screen) =>
    [
            new Act("type 'legal' into the search box", () => screen.Policies.Search("legal")),
            new WaitFrames(3),
            new AssertQuery("search dims the policies that do not match: Legalism stays bright, Oligarchy is dimmed", () =>
                screen.Policies.MatchingPolicies.Contains("Legalism") && !screen.Policies.MatchingPolicies.Contains("Oligarchy")
                && screen.Policies.PolicyButton("Oligarchy") is PolicyNodeCard { Dimmed: true }
                && screen.Policies.PolicyButton("Legalism") is PolicyNodeCard { Dimmed: false }),
            new Capture("search", "Searching 'legal': Legalism stays bright, the other policies of the branch are dimmed"),
            new Act("search by effect ('culture')", () => screen.Policies.Search("culture")),
            new AssertQuery("the match also reads the effect lines: policies whose effects mention culture match, whatever their name", () =>
                screen.Policies.MatchingPolicies.Count > 0 && screen.Policies.MatchingPolicies.Count < screen.Policies.Branches.SelectMany(it => it.Cells).Count()
                && screen.Policies.Branches.SelectMany(it => it.Cells).Where(it => it.Effects.Any(e => e.Text.Contains("culture", StringComparison.OrdinalIgnoreCase)) && !it.Name.Contains("culture", StringComparison.OrdinalIgnoreCase))
                    .All(it => screen.Policies.MatchingPolicies.Contains(it.Name))),
            new Act("clear the search", () => screen.Policies.Search("")),
            new AssertQuery("clearing the search lights every policy again", () =>
                screen.Policies.MatchingPolicies.Count == screen.Policies.Branches.SelectMany(it => it.Cells).Count()
                && screen.Policies.Branches.SelectMany(it => it.Cells).All(it => screen.Policies.PolicyButton(it.Name) is not PolicyNodeCard { Dimmed: true })),
    ];
}
