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
            new Capture("open", "The policy screen: branch columns with their openers and members, culture and golden-age lines, nothing chosen yet"),

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

            new Click(() => At("Landed Elite")),
            new WaitFrames(2),
            new AssertQuery("a policy whose prerequisite is missing cannot be adopted and names it", () =>
                screen.Policies.Selected is { Name: "Landed Elite", State: "Locked", Pickable: false } && screen.Policies.AdoptButton.Disabled
                && screen.Policies.DetailText.Contains("Needs Legalism first", StringComparison.Ordinal) && Entry("Landed Elite").LockedReason == "requires:Legalism"),
            new Click(() => At("Legalism")),
            new Press("Adopt Legalism"),
            new WaitFrames(3),
            new AssertQuery("Legalism adopted; it unlocked Landed Elite and Monarchy (the engine says adoptable)", () =>
                human.Policies.IsAdopted("Legalism") && Entry("Landed Elite").State == "Adoptable" && Entry("Monarchy").State == "Adoptable" && CellsEqualTree()),
            new Capture("adopted", "Tradition and Legalism adopted (gold); Landed Elite and Monarchy are now open; later branches are dimmed"),

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

            new PressKey(Key.Escape),
            new WaitFrames(2),
            new AssertQuery("Escape closes the screen and, with no pick pending, sends nothing", () => !screen.IsPolicyScreenVisible && session.Log.Count == logged),
        ];
    }
}
