using System.Text.Json;
using System.Text.RegularExpressions;
using Unciv.Commands;
using Unciv.Commands.Events;
using Unciv.Json;
using Unciv.Logic;
using Unciv.Logic.Map;
using Unciv.Net.Remote;
using Unciv.Net.Visibility;
using Unciv.Queries;
using Unciv.Queries.Cities;
using Unciv.Queries.Empire;
using Unciv.Queries.Tiles;
using Unciv.Queries.Units;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;
using WireQuery = Unciv.Net.Query;

namespace Unciv.Core.Tests.Commands;

// T243 (design/SIMULTANEOUS-TURNS.md section 7): the visibility property over a REAL started two-human game (GameStarter, real fog, the AI and barbarian
// turns of every resolve step), not a hand-built arena. After EVERY accepted command, server commands included, each human's filtered snapshot and diff are
// built with ViewDeltaBuilder.BuildWithDiff and that human's mirror is advanced only through the JSON form of the diff. Then:
//  - the mirror's view hash equals the fresh snapshot's (the diff lost nothing);
//  - the mirror agrees with the RAW engine fields (Tile exploredBy, Civilization.ViewableTiles, unit and city tiles, who Knows whom), which are read here and
//    not through the View classes under test: nothing is in it that the other human alone could see, and no private (Own) detail but the viewer's own;
//  - every event delivered to the viewer is a public one, or about the viewer, or along tiles the viewer sees;
//  - a sample of remote queries (the real RemoteQueryPolicy.Default through a RemoteQueryHost) answers a question about the other human's hidden units,
//    cities, tiles and unmet civ exactly as for one that does not exist, and no answer names a hidden city or an unmet civ.
// The positive controls at the end of the test prove each of these checks had something to check.
public sealed partial class SimultaneousTurnTests
{
    private sealed class VisibilityStats
    {
        public int Commands, ForeignUnitsSeen, ForeignUnitsHidden, OnlyOtherExploredTiles, HiddenCities, DeliveredForeignUnitMoves,
            WithheldLocatedEvents, TilesRevealedDelivered, FoggedTiles, QueryProbes, HiddenUnitQueries, HiddenCityQueries, HiddenTileQueries, UnmetCivQueries,
            ZeroArgumentQueries, KnownBoth, UnmetSamples;
    }

    /// <summary>One human's client: the host's last snapshot, the client's mirror (moved only by JSON diffs) and the pre-command mark.</summary>
    private sealed class Watch(GameInfo game, CivilizationClass viewer, CivilizationClass other, VisibilityStats stats)
    {
        public ClientSnapshot Host = SnapshotBuilder.Build(game, viewer, 0, StatusOf(game));
        public ClientSnapshot Mirror = SnapshotBuilder.Build(game, viewer, 0, StatusOf(game));
        private ViewDeltaBuilder.ViewMark mark = ViewDeltaBuilder.Capture(game, viewer);

        public CivilizationClass Viewer => viewer;
        public CivilizationClass Other => other;

        public static TurnStatusDto StatusOf(GameInfo game) =>
            new() { Ended = [.. game.TurnState!.Ended], Phase = game.TurnState.Phase.ToString() };

        private static readonly string[] PublicEvents =
        [
            nameof(TurnStarted), nameof(TurnEnded), nameof(AutoPlayerTurnProcessed), nameof(PlayerEndedTurn), nameof(PlayerUndidEndTurn), nameof(TurnResolving),
            nameof(ControllerChanged), nameof(EventsTruncated)
        ];

        private static readonly string[] LocatedEvents =
        [
            nameof(UnitMoved), nameof(UnitAttacked), nameof(UnitCreated), nameof(UnitCaptured), nameof(UnitDestroyed), nameof(CityFounded), nameof(CityCaptured),
            nameof(CityDestroyed), nameof(CityGrew), nameof(TileOwnerChanged), nameof(UnitAppeared), nameof(UnitDisappeared)
        ];

        public void Observe(CommandResult result)
        {
            stats.Commands++;
            var engineEvents = result.Events.OfType<EngineEvent>().ToList();
            var (delta, after) = ViewDeltaBuilder.BuildWithDiff(game, viewer, Host, mark, engineEvents, result.Seq, StatusOf(game));
            var diff = SnapshotBuilder.FromElement<SnapshotDiff>(delta.Diff!.Value);
            Mirror = diff.Apply(Mirror);
            Host = after;
            var where = $"{viewer.CivId} at seq {result.Seq} (turn {game.Turns})";
            Assert.True(delta.ViewHash == Mirror.ViewHash, "the mirror moved by the JSON diff differs from the fresh snapshot for " + where);
            Assert.True(after.ViewHash == Mirror.ViewHash, "fresh snapshot hash for " + where);

            CheckMirror(where);
            var delivered = ViewDeltaBuilder.BuildEvents(game, viewer, mark, engineEvents);
            Assert.Equal(delivered.Count, delta.Events.Count);
            CheckEvents(engineEvents, delivered, where);
            mark = ViewDeltaBuilder.Capture(game, viewer);
        }

        private HashSet<HexCoord> Viewable() => viewer.ViewableTiles.Select(it => it.Position).ToHashSet();

        private bool Explored(int x, int y) => game.TileMap[x, y].IsExplored(viewer);

        private static bool NameIn(string text, string name) =>
            Regex.IsMatch(text, @"(?<!\p{L})" + Regex.Escape(name) + @"(?!\p{L})", RegexOptions.CultureInvariant);

        /// <summary>The names the viewer must not be able to find in anything it receives: the other human's cities on tiles it never explored, and the other human when unmet.</summary>
        public List<string> ForbiddenNames() =>
            other.Cities.Where(it => !game.TileMap[it.Location].IsExplored(viewer)).Select(it => it.Name)
                .Where(name => Mirror.Civs.All(civ => civ.Name != name)) // a capital called like its civ (Babylon) is not a leak once the civ is named to the viewer
                .Concat(viewer.Knows(other) || Sighted() ? [] : [other.CivName]).ToList();

        /// <summary>A civ whose units, cities or land the viewer sees is named to it (T214), met or not.</summary>
        private bool Sighted() =>
            Mirror.Units.Any(it => it.Owner == other.CivId) || Mirror.Cities.Any(it => it.Owner == other.CivId) || Mirror.Tiles.Any(it => it.Owner == other.CivId);

        private void CheckMirror(string where)
        {
            var m = Mirror;
            var viewable = Viewable();
            var exploredRaw = game.TileMap.TileList.Where(it => it.IsExplored(viewer)).Select(it => it.Position).ToHashSet();
            // tiles: exactly the explored ones; visible exactly the viewable ones; fogged tiles carry no live state
            Assert.True(exploredRaw.SetEquals(m.Tiles.Select(it => new HexCoord(it.X, it.Y))), "explored tiles != mirror tiles for " + where);
            foreach (var tile in m.Tiles)
            {
                var position = new HexCoord(tile.X, tile.Y);
                Assert.True(tile.Visible == viewable.Contains(position), $"tile {position} visible flag for {where}");
                if (tile.Visible) continue;
                stats.FoggedTiles++;
                Assert.True(tile.Owner == null && tile.Road == null && tile.RoadPillaged == null && tile.ImprovementPillaged == null, $"fogged tile {position} shows live state for {where}");
            }
            stats.OnlyOtherExploredTiles += game.TileMap.TileList.Count(it => it.IsExplored(other) && !it.IsExplored(viewer));

            // units: the viewer's own with detail, the others only on a viewable tile, at their real tile, with no private detail
            var seenIds = new HashSet<int>();
            foreach (var unit in m.Units)
            {
                var real = game.GetCivilization(unit.Owner).Units.GetUnitById(unit.Id);
                Assert.True(real != null, $"mirror holds a unit {unit.Id} that does not exist for {where}");
                Assert.Equal(new HexCoord(unit.X, unit.Y), real.CurrentTile.Position);
                Assert.True(seenIds.Add(unit.Id), "unit twice in the mirror for " + where);
                if (unit.Owner == viewer.CivId) { Assert.NotNull(unit.Own); continue; }
                Assert.True(unit.Own == null, $"foreign unit {unit.Id} carries private detail for {where}");
                Assert.True(viewable.Contains(real.CurrentTile.Position), $"foreign unit {unit.Id} ({unit.Owner}) is on a tile the viewer does not see for {where}");
                if (unit.Owner == other.CivId) stats.ForeignUnitsSeen++;
            }
            Assert.True(viewer.Units.GetCivUnits().All(it => seenIds.Contains(it.Id)), "own unit missing from the mirror for " + where);
            foreach (var unit in other.Units.GetCivUnits().Where(it => !viewable.Contains(it.CurrentTile.Position)))
            {
                stats.ForeignUnitsHidden++;
                Assert.DoesNotContain(m.Units, it => it.Id == unit.Id);
            }

            // cities: own with detail, others only when explored and without private detail
            foreach (var city in m.Cities)
            {
                Assert.True(game.TileMap[city.X, city.Y].IsExplored(viewer), $"city {city.Name} on an unexplored tile for {where}");
                if (city.Owner == viewer.CivId) Assert.NotNull(city.Own);
                else Assert.True(city.Own == null, $"foreign city {city.Name} carries private detail for {where}");
            }
            foreach (var city in other.Cities.Where(it => !game.TileMap[it.Location].IsExplored(viewer)))
            {
                stats.HiddenCities++;
                Assert.DoesNotContain(m.Cities, it => it.Name == city.Name);
            }
            Assert.Equal(viewer.Cities.Count, m.Cities.Count(it => it.Own != null));

            // civs, diplomacy, trades, notifications
            Assert.All(m.Civs, civ => Assert.True((civ.Own != null) == (civ.CivId == viewer.CivId), $"private civ detail of {civ.CivId} for {where}"));
            var referenced = m.Units.Select(it => it.Owner).Concat(m.Cities.Select(it => it.Owner)).Concat(m.Tiles.Select(it => it.Owner).OfType<string>()).ToHashSet();
            foreach (var civ in m.Civs)
                Assert.True(civ.CivId == viewer.CivId || viewer.Knows(civ.CivId) || referenced.Contains(civ.CivId), $"civ {civ.CivId} is neither known nor seen for {where}");
            var civIds = m.Civs.Select(it => it.CivId).ToHashSet();
            Assert.All(m.Diplomacy, d =>
            {
                Assert.Contains(d.A, civIds);
                Assert.Contains(d.B, civIds);
                Assert.True(d.A == viewer.CivId || d.B == viewer.CivId || d.AtWar, $"a peace between third parties is listed for {where}");
            });
            Assert.All(m.PendingTrades, t => Assert.True(t.From == viewer.CivId || t.To == viewer.CivId, "a trade of two others for " + where));
            Assert.All(m.Notifications.SelectMany(it => it.Actions).Where(it => it.X != null), a => Assert.True(viewable.Contains(new HexCoord(a.X!.Value, a.Y!.Value)), "a notification points at a fogged tile for " + where));

            // structural: private detail exists once per viewer-owned thing, never for the other human
            // the list of humans that ended their turn is public to the lobby by design (SIMULTANEOUS-TURNS.md section 7), so it is not part of the scan
            var text = SnapshotBuilder.ToCanonicalJson(m with { TurnStatus = new TurnStatusDto { Ended = [], Phase = m.Phase } });
            Assert.Equal(1, Count(text, "\"gold\":"));
            Assert.Equal(1, Count(text, "\"techs\":"));
            Assert.Equal(1, Count(text, "\"policies\":"));
            Assert.Equal(1, Count(text, "\"spies\":"));
            Assert.Equal(viewer.Cities.Count, Count(text, "\"constructionQueue\":"));
            Assert.Equal(viewer.Units.GetCivUnits().Count(), Count(text, "\"promotions\":"));
            var researching = m.Civs.First(it => it.CivId == viewer.CivId).Own!.CurrentTech;
            foreach (var tech in other.Tech.TechsResearched.Where(it => !viewer.Tech.TechsResearched.Contains(it) && it != researching))
                Assert.True(!text.Contains($"\"{tech}\"", StringComparison.Ordinal), $"the other human's tech {tech} is in the mirror for {where}");
            foreach (var policy in other.Policies.GetAdoptedPolicies().Where(it => !viewer.Policies.IsAdopted(it)))
                Assert.True(!text.Contains($"\"{policy}\"", StringComparison.Ordinal), $"the other human's policy {policy} is in the mirror for {where}");
            foreach (var city in other.Cities.Where(it => !game.TileMap[it.Location].IsExplored(viewer)))
                Assert.True(!text.Contains(city.Id, StringComparison.Ordinal), $"the id of a hidden city is in the mirror for {where}");
            foreach (var name in ForbiddenNames()) Assert.False(NameIn(text, name), $"{name} is named in the mirror for {where}: ...{text.Substring(System.Math.Max(0, text.IndexOf(name, StringComparison.Ordinal) - 150), 300)}");
            if (viewer.Knows(other)) stats.KnownBoth++;
            else stats.UnmetSamples++;
        }

        private static int Count(string text, string needle)
        {
            int count = 0;
            for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + 1, StringComparison.Ordinal)) count++;
            return count;
        }

        private void CheckEvents(List<EngineEvent> raw, IReadOnlyList<EngineEvent> delivered, string where)
        {
            var viewable = Viewable();
            foreach (var e in delivered)
            {
                var type = e.GetType().Name;
                if (e is TilesRevealed revealed)
                {
                    stats.TilesRevealedDelivered++;
                    Assert.Equal(viewer.CivId, revealed.CivId);
                    Assert.All(revealed.Tiles, t => Assert.True(Explored(t.X, t.Y), $"a revealed tile {t.X},{t.Y} is not explored for {where}"));
                    Assert.All(revealed.Units, u => Assert.True(viewable.Contains(new HexCoord(u.X, u.Y)) && (u.Owner == viewer.CivId || u.Own == null), "a revealed unit is in fog for " + where));
                    Assert.All(revealed.Civs ?? [], c => Assert.True((c.Own != null) == (c.CivId == viewer.CivId), "a revealed civ carries private detail for " + where));
                    continue;
                }
                if (e is StatsChanged stat) { Assert.Equal(viewer.CivId, stat.CivId); continue; }
                if (e.Participants().Contains(viewer.CivId) && e is not UnitAppeared and not UnitDisappeared) continue;
                if (Array.IndexOf(PublicEvents, type) >= 0) continue;
                if (e is WarDeclared or PeaceMade)
                {
                    Assert.True(e.Participants().All(it => it == viewer.CivId || viewer.Knows(it)), $"{type} between civs the viewer has not met reached it for {where}");
                    continue;
                }
                Assert.True(Array.IndexOf(LocatedEvents, type) >= 0, $"{type} about {string.Join(',', e.Participants())} reached {viewer.CivId} without being its own or public for {where}");
                var positions = e switch { UnitAppeared a => [a.Position], UnitDisappeared d => [d.Position], _ => e.Positions().ToList() };
                Assert.NotEmpty(positions);
                Assert.True(positions.All(viewable.Contains), $"{type} reached {viewer.CivId} along tiles it does not see for {where}");
                if (e is UnitMoved moved && moved.CivId == other.CivId) stats.DeliveredForeignUnitMoves++;
            }
            // the other human's located events that the viewer got nothing about (positive control for the filter doing its work)
            foreach (var e in raw.Where(it => it.Participants().Contains(other.CivId) && !it.Participants().Contains(viewer.CivId) && it.Positions().Any()))
                if (!e.Positions().All(viewable.Contains) && !delivered.Any(d => d is UnitAppeared or UnitDisappeared)) stats.WithheldLocatedEvents++;
        }
    }

    private sealed class QueryProbe(GameInfo game, VisibilityStats stats)
    {
        private readonly QueryRegistry registry = Registry();
        // MaxCacheEntries 1: every answer is computed by the query, none is a cache hit of an earlier one.
        private readonly RemoteQueryHost host = new(Registry(), RemoteQueryPolicy.Default, new RemoteQueryLimits(Capacity: 10_000_000, RefillPerSecond: 1_000_000, MaxCacheEntries: 1));
        private long now;

        private static QueryRegistry Registry()
        {
            var registry = new QueryRegistry(UncivJson.DefaultOptions);
            registry.RegisterFromAssembly(typeof(QueryRegistry).Assembly);
            return registry;
        }

        private Unciv.Net.QueryResult Ask(CivilizationClass viewer, long seq, string type, JsonElement payload)
        {
            now += 1000;
            return host.Handle("probe-" + viewer.CivId, viewer.CivId, new WireQuery { QueryId = 1, QueryType = type, Payload = payload }, new QueryContext(game), seq, false, now);
        }

        private Unciv.Net.QueryResult Ask(CivilizationClass viewer, long seq, IQuery query)
        {
            var envelope = registry.ToEnvelope("", query);
            return Ask(viewer, seq, envelope.Type, envelope.Payload);
        }

        private static string Wire(Unciv.Net.QueryResult result) => Unciv.Net.ProtocolCodec.Encode(result);

        /// <summary>Asks about <paramref name="hidden"/> and about a thing that does not exist, and demands the same wire answer: no oracle for the existence of what the viewer may not know.</summary>
        private void SameAsNothing(CivilizationClass viewer, long seq, IQuery hidden, IQuery nothing, string what)
        {
            var asked = Ask(viewer, seq, hidden);
            var control = Ask(viewer, seq, nothing);
            Assert.False(control.Ok);
            Assert.True(Wire(asked) == Wire(control), $"{viewer.CivId} learns something from {what}: {Wire(asked)} vs {Wire(control)}");
        }

        public void Probe(Watch watch, long seq)
        {
            var viewer = watch.Viewer;
            var other = watch.Other;
            stats.QueryProbes++;
            var mirrorUnits = watch.Mirror.Units.Select(it => it.Id).ToHashSet();

            // the other human's units: never the viewer's own, so every unit query refuses, seen or not
            foreach (var unit in other.Units.GetCivUnits())
            {
                var p = unit.CurrentTile.Position;
                const int Nothing = 987654321;
                SameAsNothing(viewer, seq, new UnitActionsQuery(unit.Id), new UnitActionsQuery(Nothing), "UnitActions");
                SameAsNothing(viewer, seq, new PromotionChoicesQuery(unit.Id), new PromotionChoicesQuery(Nothing), "PromotionChoices");
                SameAsNothing(viewer, seq, new ReachableTilesQuery(unit.Id), new ReachableTilesQuery(Nothing), "ReachableTiles");
                SameAsNothing(viewer, seq, new PathPreviewQuery(unit.Id, p.x, p.y), new PathPreviewQuery(Nothing, p.x, p.y), "PathPreview");
                SameAsNothing(viewer, seq, new AttackableTargetsQuery(unit.Id), new AttackableTargetsQuery(Nothing), "AttackableTargets");
                SameAsNothing(viewer, seq, new AttackPreviewQuery(p.x, p.y, unit.Id), new AttackPreviewQuery(p.x, p.y, Nothing), "AttackPreview");
                stats.HiddenUnitQueries++;
            }

            // the other human's cities: its private panels refuse, and its city panel is a "no such city" until the viewer explored the city's tile
            var noCity = "no-such-city-id";
            foreach (var city in other.Cities)
            {
                SameAsNothing(viewer, seq, new CityTilesQuery(city.Id), new CityTilesQuery(noCity), "CityTiles");
                SameAsNothing(viewer, seq, new BuyableTilesQuery(city.Id), new BuyableTilesQuery(noCity), "BuyableTiles");
                SameAsNothing(viewer, seq, new ConstructionListQuery(city.Id), new ConstructionListQuery(noCity), "ConstructionList");
                if (!game.TileMap[city.Location].IsExplored(viewer))
                    SameAsNothing(viewer, seq, new CityPanelQuery(city.Id), new CityPanelQuery(noCity), "CityPanel of an unexplored city");
                stats.HiddenCityQueries++;
            }

            // tiles: unexplored ones are indistinguishable from ones nobody explored, fogged ones carry no live state, visible ones list only the units the viewer sees
            var viewable = viewer.ViewableTiles.Select(it => it.Position).ToHashSet();
            var onlyOther = game.TileMap.TileList.Where(it => it.IsExplored(other) && !it.IsExplored(viewer)).Select(it => it.Position).ToList();
            // control: a tile nobody explored (an off-map coordinate fails differently, which only tells the viewer the map's bounds)
            var nobody = game.TileMap.TileList.Where(it => !it.IsExplored(viewer) && !it.IsExplored(other)).Select(it => it.Position).Cast<HexCoord?>().FirstOrDefault();
            if (nobody != null)
                foreach (var p in onlyOther.Where((_, i) => i % 5 == 0).Take(12))
                {
                    SameAsNothing(viewer, seq, new TileInfoQuery(p.x, p.y), new TileInfoQuery(nobody.Value.x, nobody.Value.y), "TileInfo of an unexplored tile");
                    SameAsNothing(viewer, seq, new CitySiteQuery(p.x, p.y), new CitySiteQuery(nobody.Value.x, nobody.Value.y), "CitySite of an unexplored tile");
                    stats.HiddenTileQueries++;
                }
            var explored = game.TileMap.TileList.Where(it => it.IsExplored(viewer)).Select(it => it.Position).ToList();
            foreach (var p in explored.Where((_, i) => i % 7 == 0).Take(16))
            {
                var answer = Ask(viewer, seq, new TileInfoQuery(p.x, p.y));
                Assert.True(answer.Ok);
                var info = answer.Payload!.Value.Deserialize<TileInfoResult>(UncivJson.DefaultOptions)!;
                Assert.Equal(viewable.Contains(p), info.IsVisible);
                if (!info.IsVisible) Assert.True(info.Owner == null && info.Units.Count == 0 && info.WorkedBy == null && info.OwningCity == null, $"a fogged tile {p} shows live state to {viewer.CivId}");
                else Assert.All(info.Units, u => Assert.Contains(u.Id, mirrorUnits));
            }

            // an unmet civ: its name opens nothing and appears in no answer
            if (!viewer.Knows(other) && !watch.Mirror.Civs.Any(it => it.CivId == other.CivId))
            {
                SameAsNothing(viewer, seq, new DiplomacyPanelQuery(other.CivName), new DiplomacyPanelQuery("Atlantis"), "DiplomacyPanel of an unmet civ");
                SameAsNothing(viewer, seq, new DiplomacyActionsQuery(other.CivName), new DiplomacyActionsQuery("Atlantis"), "DiplomacyActions of an unmet civ");
                SameAsNothing(viewer, seq, new TradeOptionsQuery(other.CivName), new TradeOptionsQuery("Atlantis"), "TradeOptions of an unmet civ");
                SameAsNothing(viewer, seq, new CityStateQuestsQuery(other.CivName), new CityStateQuestsQuery("Atlantis"), "CityStateQuests of an unmet civ");
                stats.UnmetCivQueries++;
            }

            // every parameterless query the policy allows: no answer names what the viewer may not know
            var forbidden = watch.ForbiddenNames();
            var empty = JsonDocument.Parse("{}").RootElement.Clone();
            foreach (var type in registry.RegisteredTypes.Where(it => RemoteQueryPolicy.Default.Get(it).Allowed && !RemoteQueryPolicy.Default.Get(it).AskBudget))
            {
                var answer = Ask(viewer, seq, type, empty);
                if (!answer.Ok) continue;
                var text = answer.Payload!.Value.GetRawText();
                foreach (var name in forbidden)
                    Assert.False(Regex.IsMatch(text, @"(?<!\p{L})" + Regex.Escape(name) + @"(?!\p{L})", RegexOptions.CultureInvariant), $"{type} names {name} to {viewer.CivId}");
                stats.ZeroArgumentQueries++;
            }
        }
    }

    [Theory]
    [InlineData(1L, 3, 11UL, 45)]
    [InlineData(77L, 11, 12UL, 45)]
    [InlineData(5L, 8, 3UL, 45)]
    [InlineData(9L, 5, 21UL, 45)]

    public void NeitherHumansMirrorNorEventsNorQueriesEverShowWhatOnlyTheOtherHumanCanSee(long gameSeed, int mapSeed, ulong interleaving, int turns)
    {
        var live = Start(gameSeed, mapSeed);
        var game = live.Session.Game;
        var stats = new VisibilityStats();
        var watches = new[]
        {
            new Watch(game, game.GetCivilization(Babylon), game.GetCivilization(Greece), stats),
            new Watch(game, game.GetCivilization(Greece), game.GetCivilization(Babylon), stats),
        };
        var probe = new QueryProbe(game, stats);
        void Observe(CommandResult result)
        {
            foreach (var watch in watches) watch.Observe(result);
            if (game.TurnState!.Phase == SimultaneousPhase.Playing && (live.Session.LastSeq % 61 == 0 || (watches.Any(it => !it.Viewer.Knows(it.Other)) && live.Session.LastSeq % 5 == 0) || result.Events.Any(it => it is TurnStarted) && game.Turns % 4 == 0))
                foreach (var watch in watches) probe.Probe(watch, result.Seq);
        }
        foreach (var human in live.Humans) human.Observer = Observe;
        Play(live, interleaving, turns, Observe);

        var log = Unciv.Commands.Log.CommandLogReader.Read(live.Text.ToString());
        var servers = log.Lines.OfType<Unciv.Commands.Log.CommandLogLine.Command>().Count(it => it.Envelope.ActorId == CommandContext.ServerActor);
        Assert.Equal(live.Session.LastSeq, stats.Commands / 2); // every accepted command, the server's included, was observed once per human
        Assert.True(servers > turns, "the resolve steps were not part of the observed commands");

        // positive controls: each check above had material
        Assert.True(stats.ForeignUnitsSeen > 0, "a human never saw the other's units");
        Assert.True(stats.ForeignUnitsHidden > 100, "the other human's units were never hidden: " + stats.ForeignUnitsHidden);
        Assert.True(stats.OnlyOtherExploredTiles > 1000, "there were no tiles only the other human knew: " + stats.OnlyOtherExploredTiles);
        Assert.True(stats.FoggedTiles > 1000, "no fogged tile was ever checked");
        Assert.True(stats.HiddenCities > 20, "the other human's cities were never hidden: " + stats.HiddenCities);
        Assert.True(stats.WithheldLocatedEvents > 5, "no event of the other human was ever withheld: " + stats.WithheldLocatedEvents);
        Assert.True(stats.TilesRevealedDelivered > 10, "no tiles-revealed event was delivered");
        Assert.True(stats.QueryProbes >= 6 && stats.HiddenUnitQueries > 50 && stats.HiddenCityQueries > 10 && stats.HiddenTileQueries > 10 && stats.ZeroArgumentQueries > 100, $"too few remote queries ({stats.QueryProbes} probes, {stats.HiddenUnitQueries}/{stats.HiddenCityQueries}/{stats.HiddenTileQueries}/{stats.ZeroArgumentQueries})");
        Assert.True(stats.UnmetSamples > 50 && stats.UnmetCivQueries >= 1, $"the humans met too early to test an unmet civ ({stats.UnmetSamples} samples, {stats.UnmetCivQueries} probes), known {stats.KnownBoth}");
    }
}
