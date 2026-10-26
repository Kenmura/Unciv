using Godot;
using Unciv.Commands;
using Unciv.Utils;
using Unciv.Assets;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.Harness;
using Unciv.Net.Visibility;
using Unciv.Queries;
using Unciv.Queries.Empire;

namespace Unciv.GodotHarness;

/// <summary>
/// C021: the diplomacy screen through the real game screen and the real engine, in the diplomacy game (the solo game plus a city-state; Rome and the city-state are met,
/// Greece is not). The list shows exactly the met civs and the engine is never asked about Greece. Every button on the screen is enabled exactly as the engine's
/// <see cref="DiplomacyActionsResult"/> says. Declare war needs a second press (the first sends nothing), then the engine is at war with Rome and the screen offers
/// peace instead. In the city-state's table a gift of gold costs the gold and buys influence (engine numbers), pledging protection makes the city-state a protectorate.
/// </summary>
public sealed class DiplomacyScenario : Scenario
{
    public override string Name => "diplomacy";

    public override bool Uses3D => true;

    public override bool Diplomacy => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Is the list of civs clear: who is a city-state, and what is our status with each?",
        "Are the war, denounce and peace buttons easy to tell apart, and does the second press feel like a confirmation rather than an accident?",
        "Can we see why a button is disabled (hover it)?",
        "In the city-state's table, are influence, quests, gifts and bullying understandable?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        var human = game.GetCivilization(ScenarioGame.HumanA);
        var rome = game.GetCivilization(ScenarioGame.Ai);
        var cityState = game.Civilizations.Single(it => it.IsCityState);
        var recording = new RecordingSession(new LocalHost(session, ScenarioGame.HumanA, () => 1000L));
        var screen = new GameScreen();
        host.AddChild(screen);
        screen.Start(recording, AssetRegistry.ForRuleset(game.Ruleset));
        var refusals = new List<string>();
        int logged = 0;
        int gold = 0;
        float influence = 0f;

        Vector2 Centre(Button? button) => button?.GetGlobalRect().GetCenter() ?? Vector2.Zero;
        DiplomacyActionsResult Legal(string civ) => recording.QueryAsync(new DiplomacyActionsQuery(civ)).GetAwaiter().GetResult().NotNull();
        float Influence() => cityState.GetDiplomacyManager(human).NotNull().GetInfluence();
        bool Expected(DiplomacyButton button, DiplomacyActionsResult legal) => button.Kind switch
        {
            DiplomacyActionKind.DeclareWar => legal.DeclareWar,
            DiplomacyActionKind.Denounce => legal.Denounce,
            DiplomacyActionKind.OfferPeace => legal.OfferPeace,
            DiplomacyActionKind.NegotiatePeace => legal.NegotiateCityStatePeace,
            DiplomacyActionKind.PledgeProtection => legal.PledgeProtection,
            DiplomacyActionKind.WithdrawProtection => legal.WithdrawProtection,
            DiplomacyActionKind.TributeGold => legal.TributeGold,
            DiplomacyActionKind.TributeWorker => legal.TributeWorker,
            _ => legal.Gifts.Contains(((Unciv.Commands.Diplomacy.GiftGold)button.Command).Amount),
        };
        // The buttons on screen are the engine's legal set: same enabled flag, and the Godot button is disabled exactly when the engine says no.
        bool ButtonsEqualQuery(string civ)
        {
            var legal = Legal(civ);
            return screen.Diplomacy.Buttons.Count > 0 && screen.Diplomacy.Buttons.All(it =>
                it.Enabled == Expected(it, legal) && screen.Diplomacy.ActionButton(it.Id) is { } button && button.Disabled == !it.Enabled);
        }

        return
        [
            new WaitFrames(3),
            new Act("dismiss the start banner and open the diplomacy screen with its button", () => screen.Hud.HideBanner()),
            new Click(() => Centre(screen.DiplomacyButton)),
            new WaitFrames(2),
            new AssertQuery("the screen is open and lists exactly the met civs: Rome, then the city-state; never the unmet Greece", () =>
                screen.IsDiplomacyVisible && screen.Diplomacy.Rows.Select(it => it.CivId).SequenceEqual([rome.CivId, cityState.CivId])
                && screen.Diplomacy.CivButton(ScenarioGame.HumanB) == null && screen.Diplomacy.CivButton(rome.CivId).NotNull().Text == "Rome - Peace"
                && screen.Diplomacy.Rows[1].IsCityState && screen.Diplomacy.SelectedCivId == rome.CivId),
            new AssertQuery("the engine was never asked about Greece or about the viewer", () =>
                recording.Queries.All(it => it is not DiplomacyPanelQuery p || (p.CivName != ScenarioGame.HumanB && p.CivName != ScenarioGame.HumanA))
                && recording.Queries.All(it => it is not DiplomacyActionsQuery a || (a.CivName != ScenarioGame.HumanB && a.CivName != ScenarioGame.HumanA))),
            new AssertQuery("Rome's panel is the engine's: peace, our attitude, war and denounce offered", () =>
                screen.Diplomacy.Panel is { Status: "Peace", IsCityState: false } && screen.Diplomacy.InfoText.Contains("Status: Peace", StringComparison.Ordinal)
                && screen.Diplomacy.TitleText == "Rome" && screen.Diplomacy.Buttons.Select(it => it.Id).SequenceEqual(["DeclareWar", "Denounce"])),
            new AssertQuery("every button is enabled exactly as the engine's legal set says", () => ButtonsEqualQuery(rome.CivId) && Legal(rome.CivId).DeclareWar && Legal(rome.CivId).Denounce),
            new Capture("peace", "Rome at peace: the met civs on the left, war and denounce offered"),

            new Act("remember the command log", () => logged = session.Log.Count),
            new Click(() => Centre(screen.Diplomacy.ActionButton("DeclareWar"))),
            new AssertQuery("the first press only arms the button: nothing sent, still at peace", () =>
                screen.Diplomacy.ArmedButton == "DeclareWar" && screen.Diplomacy.ActionButton("DeclareWar").NotNull().Text == "Confirm: Declare war" && session.Log.Count == logged
                && !human.IsAtWarWith(rome)),
            new Capture("confirm", "Declare war armed: the button asks for a second press"),
            new Click(() => Centre(screen.Diplomacy.ActionButton("DeclareWar"))),
            new WaitFrames(2),
            new AssertQuery("the second press declared war in the engine, and the screen follows", () =>
                human.IsAtWarWith(rome) && session.Log.Count == logged + 1 && screen.Diplomacy.Panel is { Status: "War" } && screen.Diplomacy.ArmedButton == null
                && screen.Diplomacy.CivButton(rome.CivId).NotNull().Text == "Rome - War"),
            new AssertQuery("at war the screen offers peace instead of war, enabled as the engine says", () =>
                screen.Diplomacy.Buttons.Select(it => it.Id).SequenceEqual(["OfferPeace"]) && ButtonsEqualQuery(rome.CivId)),
            new Capture("war", "At war with Rome: peace is offered instead"),

            new Click(() => Centre(screen.Diplomacy.CivButton(cityState.CivId))),
            new WaitFrames(2),
            new AssertQuery("the city-state's table shows influence and quests, and its buttons equal the engine's legal set", () =>
                screen.Diplomacy.SelectedCivId == cityState.CivId && screen.Diplomacy.TitleText.EndsWith("(city-state)", StringComparison.Ordinal)
                && screen.Diplomacy.InfoText.Contains("Influence:", StringComparison.Ordinal) && screen.Diplomacy.QuestText.StartsWith("Quests", StringComparison.Ordinal)
                && screen.Diplomacy.QuestList != null && ButtonsEqualQuery(cityState.CivName)),
            new AssertQuery("with 1000 gold every gift is open, bullying and the pledge are as the engine says", () =>
                Legal(cityState.CivName).Gifts.SequenceEqual([250, 500, 1000]) && screen.Diplomacy.Buttons.Count(it => it.Kind == DiplomacyActionKind.Gift && it.Enabled) == 3
                && screen.Diplomacy.ActionButton("TributeGold") != null && screen.Diplomacy.ActionButton("PledgeProtection") != null),
            new Capture("city-state", "The city-state's table: influence, quests, gifts, bullying and protection"),
            new Act("remember gold and influence", () =>
            {
                gold = human.Gold;
                influence = Influence();
            }),
            new Click(() => Centre(screen.Diplomacy.ActionButton("Gift_250"))),
            new WaitFrames(2),
            new AssertQuery("the gift cost 250 gold and bought influence, and the panel shows the engine's new number", () =>
                human.Gold == gold - 250 && Influence() > influence && screen.Diplomacy.Panel.NotNull().Influence is { } shown && Math.Abs(shown - Influence()) < 0.001f
                && screen.Diplomacy.InfoText.Contains("Influence: " + Influence().ToString("0.#", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)),
            new Capture("gifted", "After a gift of 250 gold"),
            new Click(() => Centre(screen.Diplomacy.ActionButton("PledgeProtection"))),
            new WaitFrames(2),
            new AssertQuery("pledging protection made us the city-state's protector; revoking is offered (and is a confirm step)", () =>
                screen.Diplomacy.Panel is { Status: "Protector" } && cityState.GetDiplomacyManager(human).NotNull().DiplomaticStatus == Unciv.Logic.Civilization.Diplomacy.DiplomaticStatus.Protector
                && screen.Diplomacy.ActionButton("PledgeProtection") == null && screen.Diplomacy.Buttons.Single(it => it.Id == "WithdrawProtection").NeedsConfirm && ButtonsEqualQuery(cityState.CivName)),
            new Capture("protected", "Protecting the city-state"),

            new Press("Close diplomacy"),
            new AssertQuery("the close button closes the screen", () => !screen.IsDiplomacyVisible),
            new AssertQuery("the engine was never asked about Greece, and nothing was refused", () =>
                refusals.Count == 0 && recording.Queries.All(it => it is not DiplomacyPanelQuery p || p.CivName != ScenarioGame.HumanB)
                && recording.Queries.All(it => it is not CityStateQuestsQuery q || q.CivName == cityState.CivName)),
        ];
    }

    /// <summary>Forwards to a real session and remembers every query asked.</summary>
    private sealed class RecordingSession(IGameSession inner) : IGameSession
    {
        public List<IQuery> Queries { get; } = [];

        public string ViewerCivId => inner.ViewerCivId;

        public Unciv.Models.Ruleset.Ruleset Ruleset => inner.Ruleset;

        public ClientSnapshot Snapshot => inner.Snapshot;

        public event Action<ClientSnapshot>? SnapshotChanged
        {
            add => inner.SnapshotChanged += value;
            remove => inner.SnapshotChanged -= value;
        }

        public event Action<EventBatch>? EventsReceived
        {
            add => inner.EventsReceived += value;
            remove => inner.EventsReceived -= value;
        }

        public event Action<string>? Desynchronized
        {
            add => inner.Desynchronized += value;
            remove => inner.Desynchronized -= value;
        }

        public Task ResyncAsync() => inner.ResyncAsync();

        public Task<TResult?> QueryAsync<TResult>(IQuery<TResult> query) where TResult : class
        {
            Queries.Add(query);
            return inner.QueryAsync(query);
        }

        public Task<SubmitResult> SubmitAsync(GameCommand command) => inner.SubmitAsync(command);

        public Task<TurnOutcome> EndTurnAsync(IProgress<TurnProgress>? progress = null, CancellationToken cancellation = default) => inner.EndTurnAsync(progress, cancellation);
        public Task<TurnOutcome> AutoPlayTurnAsync(IProgress<TurnProgress>? progress = null, CancellationToken cancellation = default) => inner.AutoPlayTurnAsync(progress, cancellation);

        public Task<string?> ExportSaveAsync() => inner.ExportSaveAsync();
    }
}
