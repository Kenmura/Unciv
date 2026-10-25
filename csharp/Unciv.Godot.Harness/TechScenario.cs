using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.Harness;
using Unciv.Queries.Empire;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// C019: the tech screen through the real game screen and the real engine. The tree on screen is the engine's: the nodes the screen marks researchable are exactly the techs
/// the engine says can be researched now. Choosing Bronze Working (the game starts with Agriculture) replaces the research queue with Mining and Bronze Working (prerequisites first) and makes
/// Mining the current research; adding Pottery appends it, taking it out removes it again; taking out Mining also drops Bronze Working, which needs it; and researching
/// Pottery replaces the whole queue. Every queue is asserted from the engine's own <c>TechManager</c>.
/// </summary>
public sealed class TechScenario : Scenario
{
    public override string Name => "tech";

    public override bool Uses3D => true;

    public override bool Solo => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Can I read the tree: which techs are done, queued, available and locked, and what each one needs?",
        "Is it clear what researching a locked tech will queue, and in which order?",
        "Does the unlock list say what a tech gives me?",
        "Is the tree easy to move around in when it has many columns?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        var human = game.GetCivilization(ScenarioGame.HumanA);
        var recording = new CityAScenario.RecordingSession(new LocalHost(session, ScenarioGame.HumanA, () => 1000L));
        var screen = new GameScreen();
        host.AddChild(screen);
        screen.Start(recording, AssetRegistry.ForRuleset(game.Ruleset));

        Vector2 Centre(Button? button) => button?.GetGlobalRect().GetCenter() ?? Vector2.Zero;
        Vector2 At(string tech) => Centre(screen.Tech.NodeButton(tech));
        Vector2 Do(string action) => Centre(screen.Tech.ActionButton(action));
        List<string> Queue() => human.Tech.TechsToResearch.ToList();
        TechTreeResult Tree() => recording.QueryAsync(new TechTreeQuery()).GetAwaiter().GetResult().NotNull();
        bool Enabled(string action) => screen.Tech.ActionButton(action) is { Disabled: false };
        bool Disabled(string action) => screen.Tech.ActionButton(action) is { Disabled: true };
        ScenarioStep[] Pick(string tech) =>
        [
            new Act("scroll " + tech + " into view", () => screen.Tech.EnsureVisible(tech)),
            new WaitFrames(2),
            new Click(() => At(tech)),
            new WaitFrames(1),
        ];
        // The engine's own answer to "which techs can I research now", independent of the screen and of the query
        List<string> ResearchableByEngine() => game.Ruleset.Technologies.Keys.Where(it => human.Tech.CanBeResearched(it) && !human.Tech.IsResearched(it)).Order(StringComparer.Ordinal).ToList();
        List<string> ResearchableOnScreen() => screen.Tech.Layout.Nodes.Where(it => it.State is TechScreenModel.Researchable or TechScreenModel.Queued).Select(it => it.Name).Order(StringComparer.Ordinal).ToList();

        var steps = new List<ScenarioStep>
        {
            new WaitFrames(3),
            new Act("dismiss the banner", () => screen.Hud.HideBanner()),
            new Click(() => Centre(screen.TechButton)),
            new WaitFrames(3),
            new AssertQuery("the screen is open and shows exactly the engine's placed techs, each as a button", () =>
                screen.IsTechVisible && screen.Tech.Tree != null && screen.Tech.Layout.Nodes.Count > 30
                && screen.Tech.Layout.Nodes.All(it => screen.Tech.NodeButton(it.Name) != null)
                && screen.Tech.Layout.Nodes.Select(it => it.Name).Order(StringComparer.Ordinal).SequenceEqual(Tree().Techs.Where(it => it.Column != null && it.State != "Unavailable").Select(it => it.Name).Order(StringComparer.Ordinal))),
            new AssertQuery("the techs shown as available are exactly the techs the engine can research now, and nothing is queued", () =>
                ResearchableOnScreen().Count > 0 && ResearchableOnScreen().SequenceEqual(ResearchableByEngine()) && Queue().Count == 0 && screen.Tech.HeaderText == "Nothing is being researched"),
            new AssertQuery("a locked tech is shown locked, the starting tech researched and its follow-ups researchable", () =>
                screen.Tech.Layout.Nodes.Single(it => it.Name == "Bronze Working").State == "Locked" && screen.Tech.Layout.Nodes.Single(it => it.Name == "Agriculture").State == "Researched" && screen.Tech.Layout.Nodes.Single(it => it.Name == "Mining").State == "Researchable"),
            new Capture("open", "The tech tree on first open: Agriculture researched, its follow-ups available, the rest locked"),
        };

        steps.AddRange(Pick("Bronze Working"));
        steps.AddRange(
        [
            new AssertQuery("the side panel names the tech and lists its unlocks, and the engine's text is on screen", () =>
                screen.Tech.SelectedTech == "Bronze Working" && screen.Tech.TitleText == "Bronze Working" && screen.Tech.UnlockText.StartsWith("Unlocks", StringComparison.Ordinal)
                && Tree().Techs.First(it => it.Name == "Bronze Working").Description.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).All(it => screen.Tech.UnlockText.Contains(it, StringComparison.Ordinal))),
            new AssertQuery("Research now and Add to queue are offered, Remove from queue is not (it is not queued)", () => Enabled("Research") && Enabled("Enqueue") && Disabled("Dequeue")),
            new Click(() => Do("Research")),
            new WaitFrames(3),
            new AssertQuery("the engine's queue is Mining, Bronze Working and the current research is Mining", () =>
                Queue().SequenceEqual(new[] { "Mining", "Bronze Working" }) && human.Tech.CurrentTechnologyName() == "Mining"
                && screen.Tech.Tree.NotNull().CurrentTech == "Mining" && screen.Tech.HeaderText.StartsWith("Researching Mining", StringComparison.Ordinal)),
            new AssertQuery("both techs are shown queued, Bronze Working is no longer counted researchable by the engine, and Research now is disabled", () =>
                new[] { "Mining", "Bronze Working" }.All(it => screen.Tech.Layout.Nodes.Single(n => n.Name == it).State == "Queued")
                && !ResearchableByEngine().Contains("Bronze Working") && Disabled("Research") && Enabled("Dequeue") && screen.Tech.QueueText.Contains("2. Bronze Working", StringComparison.Ordinal)),
            new Capture("chosen", "Bronze Working chosen: it and the techs it needs are queued in order and highlighted"),
        ]);

        steps.AddRange(Pick("Pottery"));
        steps.AddRange(
        [
            new Click(() => Do("Enqueue")),
            new WaitFrames(3),
            new AssertQuery("Add to queue appended Pottery after the others and left the current research alone", () =>
                Queue().SequenceEqual(new[] { "Mining", "Bronze Working", "Pottery" }) && human.Tech.CurrentTechnologyName() == "Mining" && Disabled("Enqueue")),
            new Capture("queued", "Pottery added to the end of the queue"),
            new Click(() => Do("Dequeue")),
            new WaitFrames(3),
            new AssertQuery("Remove from queue took Pottery out again", () => Queue().SequenceEqual(new[] { "Mining", "Bronze Working" }) && Enabled("Enqueue")),
        ]);

        steps.AddRange(Pick("Animal Husbandry"));
        steps.AddRange(
        [
            new Click(() => Do("Enqueue")),
            new WaitFrames(3),
            new AssertQuery("Animal Husbandry was appended", () => Queue().SequenceEqual(new[] { "Mining", "Bronze Working", "Animal Husbandry" })),
        ]);
        steps.AddRange(Pick("Mining"));
        steps.AddRange(
        [
            new Click(() => Do("Dequeue")),
            new WaitFrames(3),
            new AssertQuery("removing Mining also removed Bronze Working, which needs it, and kept Animal Husbandry", () =>
                Queue().SequenceEqual(new[] { "Animal Husbandry" }) && screen.Tech.Layout.Nodes.Single(it => it.Name == "Bronze Working").State == "Locked"),
        ]);

        steps.AddRange(Pick("Animal Husbandry"));
        steps.Add(new AssertQuery("the only queued tech cannot be removed (the engine refuses an empty queue), so the button says so", () => Disabled("Dequeue") && screen.Tech.ActionButton("Dequeue").NotNull().TooltipText.Contains("empty", StringComparison.Ordinal)));

        steps.AddRange(Pick("Pottery"));
        steps.AddRange(
        [
            new Click(() => Do("Research")),
            new WaitFrames(3),
            new AssertQuery("researching Pottery replaced the whole queue", () =>
                Queue().SequenceEqual(new[] { "Pottery" }) && human.Tech.CurrentTechnologyName() == "Pottery"
                && screen.Tech.Layout.Nodes.Single(it => it.Name == "Animal Husbandry").State == "Researchable"),
            new Capture("replaced", "Pottery researched instead: the queue holds only it"),
            new Press("Close technology"),
            new AssertQuery("the screen closed and nothing was refused", () => !screen.IsTechVisible && screen.Tech.MessageText.Length == 0),
        ]);
        return steps;
    }
}
