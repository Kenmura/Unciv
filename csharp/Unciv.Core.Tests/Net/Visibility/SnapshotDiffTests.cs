using System.Text.Json;
using Unciv.Commands;
using Unciv.Commands.City;
using Unciv.Commands.Empire;
using Unciv.Commands.Turn;
using Unciv.Commands.Units;
using Unciv.Harness;
using Unciv.Logic;
using Unciv.Logic.Map;
using Unciv.Net.Visibility;
using Civ = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Net.Visibility;

// T224: the client mirror follows the host's per-recipient snapshot diff, not the events. These tests drive random command sequences through a real two-human
// game and require that every recipient's mirror, advanced only by ViewDelta.Diff (through its JSON form), has the same viewHash as a fresh snapshot.
[Collection("RulesetCache")]
public sealed class SnapshotDiffTests
{
    private static readonly TurnStatusDto Status = new() { Ended = [], Phase = "playing" };

    // ---- hand-built snapshots -------------------------------------------------------------------------------------------------------------------

    private static TileDto Tile(int x, int y, string terrain = "Grassland") => new()
    {
        X = x,
        Y = y,
        Visible = true,
        BaseTerrain = terrain,
        Features = [],
        RiverBottom = false,
        RiverBottomLeft = false,
        RiverBottomRight = false
    };

    private static UnitDto Unit(int id, int x = 0, int y = 0) => new()
    {
        Id = id,
        Name = "Warrior",
        Owner = "A",
        X = x,
        Y = y,
        HealthBucket = 4,
        IsEmbarked = false,
        IsFortified = false
    };

    private static ClientSnapshot Snap(long seq, List<TileDto>? tiles = null, List<UnitDto>? units = null) => new()
    {
        Seq = seq,
        Turn = 1,
        Phase = "playing",
        TurnStatus = Status,
        You = "A",
        Civs = [],
        Tiles = tiles ?? [],
        Cities = [],
        Units = units ?? [],
        Diplomacy = [],
        PendingTrades = [],
        Notifications = [],
        PopupAlerts = [],
        PendingDecisions = []
    };

    private static ClientSnapshot RoundTrip(ClientSnapshot from, ClientSnapshot to)
    {
        var diff = SnapshotBuilder.FromElement<SnapshotDiff>(SnapshotBuilder.ToElement(SnapshotDiff.Compute(from, to)));
        return diff.Apply(from);
    }

    private static void AssertSame(ClientSnapshot expected, ClientSnapshot actual) =>
        Assert.Equal(SnapshotBuilder.ComputeViewHash(expected), actual.ViewHash);

    [Fact]
    public void Identical_SnapshotsGiveAnEmptyDiff_ExceptTheSeq()
    {
        var snap = Snap(1, [Tile(0, 0)], [Unit(1)]);
        var diff = SnapshotDiff.Compute(snap, snap with { Seq = 2 });
        Assert.Equal(2, diff.Seq);
        Assert.Null(diff.Tiles);
        Assert.Null(diff.Units);
        Assert.Null(diff.Notifications);
        Assert.Null(diff.Turn);
        Assert.Null(diff.Phase);
        Assert.Null(diff.TurnStatus);
        AssertSame(snap with { Seq = 2 }, diff.Apply(snap));
    }

    [Fact]
    public void AddedChangedAndRemovedItems_RoundTrip_InTheirNewPositions()
    {
        var before = Snap(1, [Tile(0, 0), Tile(1, 0), Tile(2, 0)], [Unit(1), Unit(2), Unit(3)]);
        var after = Snap(2, [Tile(-1, 0), Tile(0, 0, "Plains"), Tile(2, 0), Tile(3, 0)], [Unit(2, 5, 5), Unit(4), Unit(3)]);
        var diff = SnapshotDiff.Compute(before, after);
        Assert.Equal(["1,0"], diff.Tiles!.Removed);
        Assert.Equal([0, 3], diff.Tiles.AddedAt);
        Assert.Single(diff.Tiles.Changed);
        AssertSame(after, RoundTrip(before, after));
        Assert.Equal(SnapshotBuilder.ToCanonicalJson(after), SnapshotBuilder.ToCanonicalJson(RoundTrip(before, after) with { ViewHash = null }));
    }

    [Fact]
    public void SurvivorsThatChangeRelativeOrder_CarryTheFullOrder()
    {
        var before = Snap(1, units: [Unit(1), Unit(2), Unit(3)]);
        var after = Snap(2, units: [Unit(3), Unit(1), Unit(2)]);
        var diff = SnapshotDiff.Compute(before, after);
        Assert.Equal(["3", "1", "2"], diff.Units!.Order);
        AssertSame(after, RoundTrip(before, after));
        // An unchanged order is not sent, however many items come and go.
        Assert.Null(SnapshotDiff.Compute(before, Snap(3, units: [Unit(1), Unit(9), Unit(3)])).Units!.Order);
    }

    [Fact]
    public void UnkeyedListsAndHeaderFields_AreReplacedWhenTheyDiffer()
    {
        var before = Snap(1);
        var after = Snap(2) with
        {
            Turn = 2,
            Phase = "resolving",
            TurnStatus = new TurnStatusDto { Ended = ["A"], Phase = "resolving", Deadline = 5 },
            Notifications = [new NotificationDto { Category = "General", Text = "hi", Icons = [], Actions = [] }],
            PopupAlerts = [new PopupAlertDto { Type = "GoldenAge", Value = "" }],
            PendingDecisions = [new PendingDecisionDto { Kind = "Technology", Subject = "", ResolvedBy = "set-research-queue", Blocking = true, Index = -1 }],
            PendingTrades = [new PendingTradeDto { From = "A", To = "B", Offered = [], Requested = [] }],
            Diplomacy = [new DiplomacyDto { A = "A", B = "B", AtWar = true, Status = "War" }],
        };
        var applied = RoundTrip(before, after);
        AssertSame(after, applied);
        Assert.Equal("resolving", applied.Phase);
        var diff = SnapshotDiff.Compute(after, after with { Seq = 3 });
        Assert.Null(diff.PopupAlerts);
        Assert.Null(diff.PendingDecisions);
        Assert.Null(diff.PendingTrades);
        Assert.Null(diff.Diplomacy);
    }

    // ---- hostile or stale diffs (T224 review) ---------------------------------------------------------------------------------------------------

    [Fact]
    public void ADiffAppliedToTheWrongBase_GivesAHashThatDiffersFromTheHostsHash()
    {
        var before = Snap(1, [Tile(0, 0)], [Unit(1)]);
        var after = Snap(2, [Tile(0, 0), Tile(1, 0)], [Unit(1, 3, 3)]);
        var staleMirror = Snap(1, [Tile(0, 0)], [Unit(1), Unit(7)]); // the mirror missed an earlier delta
        var applied = SnapshotDiff.Compute(before, after).Apply(staleMirror);
        Assert.NotEqual(SnapshotBuilder.ComputeViewHash(after), applied.ViewHash);
        // A base too short for the insert positions is refused outright: the client resyncs either way.
        Assert.Throws<InvalidDataException>(() => SnapshotDiff.Compute(before, after).Apply(Snap(1)));
        // A changed item the mirror does not have is dropped, not invented: a mismatch.
        var missing = SnapshotDiff.Compute(before, Snap(2, [Tile(0, 0, "Plains")], [Unit(1)])).Apply(Snap(1));
        Assert.NotEqual(SnapshotBuilder.ComputeViewHash(Snap(2, [Tile(0, 0, "Plains")], [Unit(1)])), missing.ViewHash);
    }

    [Fact]
    public void MalformedDiffs_ThrowInvalidData_InsteadOfCorruptingOrCrashing()
    {
        var mirror = Snap(1, [Tile(0, 0), Tile(1, 0)], [Unit(1), Unit(2)]);
        KeyedDiff<UnitDto> Units(List<UnitDto>? changed = null, List<UnitDto>? added = null, List<int>? at = null, List<string>? removed = null, List<string>? order = null) =>
            new() { Changed = changed ?? [], Added = added ?? [], AddedAt = at ?? [], Removed = removed ?? [], Order = order };
        SnapshotDiff With(KeyedDiff<UnitDto> units) => new() { Seq = 2, Units = units };

        Assert.Throws<InvalidDataException>(() => With(Units(added: [Unit(5)], at: [])).Apply(mirror)); // index list shorter
        Assert.Throws<InvalidDataException>(() => With(Units(added: [Unit(5)], at: [99])).Apply(mirror)); // outside the list
        Assert.Throws<InvalidDataException>(() => With(Units(added: [Unit(5)], at: [-1])).Apply(mirror));
        Assert.Throws<InvalidDataException>(() => With(Units(added: [Unit(5), Unit(6)], at: [1, 1])).Apply(mirror)); // not strictly ascending
        Assert.Throws<InvalidDataException>(() => With(Units(changed: [Unit(1), Unit(1)])).Apply(mirror)); // duplicate key
        Assert.Throws<InvalidDataException>(() => With(Units(order: ["1", "9"])).Apply(mirror)); // order names an unknown key
        Assert.Throws<InvalidDataException>(() => With(Units(order: ["1", "1"])).Apply(mirror)); // duplicate in the order
        Assert.Throws<InvalidDataException>(() => With(Units(order: ["1"])).Apply(mirror)); // order drops a survivor
        Assert.Throws<InvalidDataException>(() => With(Units(changed: new List<UnitDto?> { null }.Cast<UnitDto>().ToList())).Apply(mirror));
        Assert.Throws<InvalidDataException>(() => With(new KeyedDiff<UnitDto> { Changed = null!, Added = [], AddedAt = [], Removed = [] }).Apply(mirror));
        // The mirror itself is an immutable value: a refused diff leaves it as it was.
        Assert.Equal(2, mirror.Units.Count);
    }

    [Fact]
    public void AVeryLargeDiff_RoundTripsInLinearTime()
    {
        var tiles = Enumerable.Range(0, 60000).Select(i => Tile(i % 300, i / 300)).ToList();
        var before = Snap(1, tiles.Where((_, i) => i % 2 == 0).ToList());
        var after = Snap(2, tiles);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        AssertSame(after, RoundTrip(before, after));
        Assert.True(watch.ElapsedMilliseconds < 20000, $"{watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void ATileLeavingVision_BecomesTheSameFoggedTileAFreshSnapshotHas_AndRemovalsNameOnlyKnownItems()
    {
        var seen = Tile(0, 0) with { Visible = true, Owner = "B", Road = "Road" };
        var fogged = Tile(0, 0) with { Visible = false };
        var before = Snap(1, [seen], [Unit(1), Unit(2, 4, 4)]);
        var after = Snap(2, [fogged], [Unit(1)]);
        var diff = SnapshotDiff.Compute(before, after);
        var changed = Assert.Single(diff.Tiles!.Changed);
        Assert.Equal(fogged, changed);
        Assert.Null(changed.Owner);
        Assert.Null(changed.Road);
        Assert.Equal(["2"], diff.Units!.Removed); // only an id the recipient already had
        AssertSame(after, RoundTrip(before, after));
        Assert.DoesNotContain(diff.Units.Removed, id => before.Units.All(it => it.Id.ToString() != id));
    }

    // ---- alerts and pending decisions -----------------------------------------------------------------------------------------------------------

    [Fact]
    public void Snapshot_CarriesTheRecipientsOwnAlertsAndDecisions_AndNobodyElses()
    {
        var game = ScenarioGame.Create(alerts: true).Game;
        var human = game.GetCivilization(ScenarioGame.HumanA);
        var snapshot = SnapshotBuilder.Build(game, human, 1, Status);
        Assert.Equal(human.PopupAlerts.Select(it => (it.Type.ToString(), it.Value)), snapshot.PopupAlerts.Select(it => (it.Type, it.Value)));
        Assert.True(snapshot.PopupAlerts.Count >= 4);
        var alertDecisions = snapshot.PendingDecisions.Where(it => it.Kind == nameof(DecisionKind.Alert)).ToList();
        Assert.Equal(snapshot.PopupAlerts.Select(it => it.Type), alertDecisions.Select(it => it.Subject));
        Assert.Equal(Enumerable.Range(0, snapshot.PopupAlerts.Count), alertDecisions.Select(it => it.Index));
        var byType = alertDecisions.GroupBy(it => it.Subject).ToDictionary(it => it.Key, it => it.First());
        Assert.Equal("respond-demand", byType["DemandToStopSpreadingReligion"].ResolvedBy);
        Assert.Equal("respond-friendship", byType["DeclarationOfFriendship"].ResolvedBy);
        Assert.Equal("respond-to-alert", byType["CityConquered"].ResolvedBy);
        Assert.Equal("ack-alert", byType["WonderBuilt"].ResolvedBy);
        Assert.True(byType["CityConquered"].Blocking);
        Assert.False(byType["WonderBuilt"].Blocking);
        var wire = SnapshotBuilder.ToMessage(snapshot);
        Assert.Equal(snapshot.PopupAlerts.Count, wire.PopupAlerts.Count);
        Assert.Equal(snapshot.PendingDecisions.Count, wire.PendingDecisions.Count);

        var other = game.GetCivilization(ScenarioGame.Ai);
        var foreign = SnapshotBuilder.Build(game, other, 1, Status);
        Assert.Equal(other.PopupAlerts.Select(it => it.Type.ToString()), foreign.PopupAlerts.Select(it => it.Type)); // its own, not the human's four
        Assert.Equal(other.PopupAlerts.Count, foreign.PendingDecisions.Count(it => it.Kind == nameof(DecisionKind.Alert)));
        Assert.DoesNotContain("CityConquered", SnapshotBuilder.ToCanonicalJson(foreign));
        Assert.DoesNotContain("DemandToStopSpreadingReligion", SnapshotBuilder.ToCanonicalJson(foreign));
    }

    [Fact]
    public void AnsweringAnAlert_ShowsUpInTheDiffAsTheAlertAndDecisionLeaving()
    {
        var session = ScenarioGame.Create(alerts: true);
        var game = session.Game;
        var human = game.GetCivilization(ScenarioGame.HumanA);
        var before = SnapshotBuilder.Build(game, human, session.LastSeq, Status);
        var mark = ViewDeltaBuilder.Capture(game, human);
        var index = human.PopupAlerts.FindIndex(it => it.Type == Unciv.Logic.Civilization.AlertType.WonderBuilt);
        var count = human.PopupAlerts.Count;
        var result = session.Submit(ScenarioGame.HumanA, new AckAlert { Index = index, AlertType = Unciv.Logic.Civilization.AlertType.WonderBuilt, Value = "Pyramids" });
        Assert.True(result.Accepted, result.Validation.Message);
        var (delta, after) = ViewDeltaBuilder.BuildWithDiff(game, human, before, mark, result.Events.OfType<Unciv.Commands.Events.EngineEvent>(), result.Seq, Status);
        var diff = SnapshotBuilder.FromElement<SnapshotDiff>(delta.Diff!.Value);
        Assert.Equal(count - 1, diff.PopupAlerts!.Count);
        Assert.DoesNotContain(diff.PendingDecisions!, it => it.Subject == "WonderBuilt");
        Assert.Equal(after.ViewHash, delta.ViewHash);
        Assert.Equal(after.ViewHash, diff.Apply(before).ViewHash);
    }

    // ---- random command sequences ---------------------------------------------------------------------------------------------------------------

    private sealed class Lcg(uint seed)
    {
        private uint state = seed;
        public int Next(int bound)
        {
            state = unchecked(state * 1664525u + 1013904223u);
            return (int)((state >> 8) % (uint)bound);
        }
    }

    private static readonly string[] Buildables = ["Warrior", "Scout", "Monument", "Worker", "Granary", "Library", "Barracks", "Settler", "Shrine"];

    private static GameCommand? RandomCommand(GameInfo game, Civ civ, Lcg rng)
    {
        var units = civ.Units.GetCivUnits().ToList();
        switch (rng.Next(7))
        {
            case 0 when units.Count > 0:
                var unit = units[rng.Next(units.Count)];
                var to = new HexCoord(unit.CurrentTile.Position.X + rng.Next(5) - 2, unit.CurrentTile.Position.Y + rng.Next(5) - 2);
                return new MoveUnit { UnitId = unit.Id, To = to };
            case 1 or 2 when units.Count > 0:
                var actor = units[rng.Next(units.Count)];
                var actions = UnitActionDescriptor.Describe(actor).Where(it => it.Action.Action != null).ToList();
                return actions.Count == 0 ? null : new ExecuteUnitAction { UnitId = actor.Id, Descriptor = actions[rng.Next(actions.Count)].Descriptor, Confirmed = true };
            case 3 when civ.Cities.Count > 0:
                return new QueueConstruction { CityId = civ.Cities[rng.Next(civ.Cities.Count)].Id, Name = Buildables[rng.Next(Buildables.Length)] };
            case 4:
                var techs = game.Ruleset.Technologies.Keys.ToList();
                return new SetResearchQueue { Techs = [techs[rng.Next(techs.Count)]] };
            case 5:
                var policies = game.Ruleset.PolicyBranches.Keys.ToList();
                return new AdoptPolicy { Policy = policies[rng.Next(policies.Count)] };
            case 6:
                return rng.Next(3) == 0 ? new EndTurn() : null;
            default:
                return null;
        }
    }

    private sealed class Mirror(Civ civ, GameInfo game, long seq)
    {
        public ClientSnapshot Before = SnapshotBuilder.Build(game, civ, seq, Status);
        public ClientSnapshot Mirrored = SnapshotBuilder.Build(game, civ, seq, Status);
        public ViewDeltaBuilder.ViewMark Mark = ViewDeltaBuilder.Capture(game, civ);
        public Civ Viewer = civ;
    }

    [Fact]
    public void RandomCommandSequences_KeepEveryMirrorEqualToAFreshSnapshot()
    {
        var totals = new[] { 1u, 2u, 3u, 4u }.Select(RunRandomSequence).Aggregate((x, y) => (x.Accepted + y.Accepted, x.ChangedUnits + y.ChangedUnits, x.EventlessChanges + y.EventlessChanges, x.TilesAdded + y.TilesAdded));
        Assert.True(totals.Accepted > 200, $"only {totals.Accepted} commands were accepted");
        Assert.True(totals.ChangedUnits > 5, "no own-unit state ever changed");
        Assert.True(totals.EventlessChanges > 0, "the fixture never changed a unit without an event, so it does not prove the diff replaces events");
        Assert.True(totals.TilesAdded > 0, "no tile was ever revealed");
    }

    private static (int Accepted, int ChangedUnits, int EventlessChanges, int TilesAdded) RunRandomSequence(uint seed)
    {
        var session = ScenarioGame.Create();
        var game = session.Game;
        var rng = new Lcg(seed);
        var mirrors = new[] { ScenarioGame.HumanA, ScenarioGame.HumanB }.Select(it => new Mirror(game.GetCivilization(it), game, session.LastSeq)).ToList();
        int accepted = 0, changedUnits = 0, eventlessChanges = 0, tilesAdded = 0;

        void Step(Func<Unciv.Commands.CommandResult> run)
        {
            foreach (var m in mirrors) m.Mark = ViewDeltaBuilder.Capture(game, m.Viewer);
            var result = run();
            if (!result.Accepted) return;
            accepted++;
            foreach (var m in mirrors)
            {
                var (delta, after) = ViewDeltaBuilder.BuildWithDiff(game, m.Viewer, m.Before, m.Mark, result.Events.OfType<Unciv.Commands.Events.EngineEvent>(), result.Seq, Status);
                var diff = SnapshotBuilder.FromElement<SnapshotDiff>(delta.Diff!.Value);
                m.Mirrored = diff.Apply(m.Mirrored);
                var fresh = SnapshotBuilder.Build(game, m.Viewer, result.Seq, Status);
                Assert.Equal(delta.ViewHash, m.Mirrored.ViewHash);
                Assert.Equal(fresh.ViewHash, m.Mirrored.ViewHash);
                Assert.Equal(SnapshotBuilder.ToCanonicalJson(fresh), SnapshotBuilder.ToCanonicalJson(m.Mirrored));
                if (diff.Units?.Changed.Count > 0) changedUnits++;
                if (delta.Events.Count == 0 && diff.Units?.Changed.Count > 0) eventlessChanges++;
                tilesAdded += diff.Tiles?.Added.Count ?? 0;
                m.Before = after;
            }
        }

        for (var step = 0; step < 400 && game.TurnPhase is not TurnPhase.GameOver; step++)
        {
            switch (game.TurnPhase)
            {
                case TurnPhase.AutoPlayers auto:
                    Step(() => session.SubmitAsServer(new RunAiTurn { CivId = auto.NextCiv.CivId }));
                    break;
                case TurnPhase.AwaitingInput waiting when !game.IsTurnStarted(waiting.Civ):
                    Step(() => session.SubmitAsServer(new BeginTurn { CivId = waiting.Civ.CivId }));
                    break;
                case TurnPhase.AwaitingInput waiting:
                    var command = RandomCommand(game, waiting.Civ, rng);
                    if (command != null) Step(() => session.Submit(waiting.Civ.CivId, command));
                    else if (rng.Next(4) == 0) Step(() => session.Submit(waiting.Civ.CivId, new EndTurn()));
                    break;
                default:
                    step = int.MaxValue - 1;
                    break;
            }
        }

        return (accepted, changedUnits, eventlessChanges, tilesAdded);
    }

    // ---- nothing the recipient cannot see ------------------------------------------------------------------------------------------------------

    [Fact]
    public void ADiff_ForAnotherCivsMove_NeverCarriesHiddenStateOrOwnDetailOfOthers()
    {
        var session = ScenarioGame.Create();
        var game = session.Game;
        var a = game.GetCivilization(ScenarioGame.HumanA);
        var b = game.GetCivilization(ScenarioGame.HumanB);
        // Begin A's turn and make B's every unit and city invisible to A: nothing of B may be in A's diff, and B's secrets stay out of every wire form.
        while (game.TurnPhase is TurnPhase.AwaitingInput w && !game.IsTurnStarted(w.Civ)) session.SubmitAsServer(new BeginTurn { CivId = w.Civ.CivId });
        Assert.Equal(a, ((TurnPhase.AwaitingInput)game.TurnPhase).Civ);
        b.AddGold(98765 - b.Gold);
        var before = SnapshotBuilder.Build(game, a, session.LastSeq, Status);
        Assert.DoesNotContain(before.Civs, it => it.CivId == b.CivId);
        var mark = ViewDeltaBuilder.Capture(game, a);

        // A queues a building: B's gold is untouched by the diff for A, and B's identity is not leaked either.
        var result = session.Submit(a.CivId, new QueueConstruction { CityId = a.Cities[0].Id, Name = "Monument" });
        Assert.True(result.Accepted, result.Validation.Message);
        var (delta, after) = ViewDeltaBuilder.BuildWithDiff(game, a, before, mark, result.Events.OfType<Unciv.Commands.Events.EngineEvent>(), result.Seq, Status);
        var text = delta.Diff!.Value.GetRawText() + JsonSerializer.Serialize(delta.Events);
        Assert.DoesNotContain("98765", text);
        Assert.DoesNotContain(b.CivId, text);
        Assert.DoesNotContain(b.CivId, SnapshotBuilder.ToCanonicalJson(after));

        // The same fresh-vs-mirror equality for B: its own detail is B's alone.
        var bSnapshot = SnapshotBuilder.Build(game, b, result.Seq, Status);
        Assert.Contains("98765", SnapshotBuilder.ToCanonicalJson(bSnapshot));
        Assert.DoesNotContain(bSnapshot.Civs, it => it.CivId == a.CivId && it.Own != null);
    }

    [Fact]
    public void FoggedTiles_AndForeignUnitsInThem_NeverEnterADiff()
    {
        var session = ScenarioGame.Create(solo: true);
        var game = session.Game;
        var a = game.GetCivilization(ScenarioGame.HumanA);
        var rome = game.GetCivilization(ScenarioGame.Ai);
        var before = SnapshotBuilder.Build(game, a, session.LastSeq, Status);
        var visible = before.Tiles.Where(it => it.Visible).Select(it => new HexCoord(it.X, it.Y)).ToHashSet();
        // Rome's units stand on tiles A has not explored; they move around while A only waits.
        Assert.All(rome.Units.GetCivUnits(), unit => Assert.DoesNotContain(unit.CurrentTile.Position, visible));
        var ended = session.SubmitAsServer(new ForceEndTurn { CivId = a.CivId, Now = ScenarioGame.FixedClockMillis + 5000 });
        Assert.True(ended.Accepted, ended.Validation.Message);
        before = SnapshotBuilder.Build(game, a, session.LastSeq, Status);
        var mark = ViewDeltaBuilder.Capture(game, a);
        var result = session.SubmitAsServer(new RunAiTurn { CivId = ScenarioGame.Ai });
        Assert.True(result.Accepted, result.Validation.Message);
        var (delta, after) = ViewDeltaBuilder.BuildWithDiff(game, a, before, mark, result.Events.OfType<Unciv.Commands.Events.EngineEvent>(), result.Seq, Status);
        var diff = SnapshotBuilder.FromElement<SnapshotDiff>(delta.Diff!.Value);
        var visibleNow = after.Tiles.Where(it => it.Visible).Select(it => new HexCoord(it.X, it.Y)).ToHashSet();
        foreach (var unit in (diff.Units?.Added ?? []).Concat(diff.Units?.Changed ?? []).Where(it => it.Owner == rome.CivId))
            Assert.Contains(new HexCoord(unit.X, unit.Y), visibleNow);
        Assert.All((diff.Tiles?.Added ?? []).Concat(diff.Tiles?.Changed ?? []), tile =>
        {
            if (!tile.Visible) { Assert.Null(tile.Owner); Assert.Null(tile.Road); }
        });
        Assert.DoesNotContain("\"owner\":\"" + rome.CivId, delta.Diff!.Value.GetRawText()); // fogged: no Rome tile ownership, unit or city
    }
}
