using System.Text.Json.Nodes;
using Unciv.Commands;
using Unciv.Commands.City;
using Unciv.Commands.Diplomacy;
using Unciv.Commands.Empire;
using Unciv.Commands.Events;
using Unciv.Commands.Turn;
using Unciv.Commands.Units;
using Unciv.Core.Tests.Commands.Diplomacy;
using Unciv.Core.Tests.Support;
using Unciv.Logic;
using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Logic.Trade;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Net.Visibility;
using Unciv.Utils;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using DeclareWarCommand = Unciv.Commands.Diplomacy.DeclareWar;
using MapUnitObject = Unciv.Logic.Map.MapUnits.MapUnit;

namespace Unciv.Core.Tests.Commands;

// T203 (design/SIMULTANEOUS-TURNS.md section 5 and 7): the conflicts between two humans in the same turn, one test per row of the conflict table, each
// played in both orders. The order of the log decides who wins; the loser gets a stable rejection that changes nothing (state, log and seq are the same
// afterwards); the order itself is data, so a replay of either log reproduces the played state. Also: independent commands commute on rule state, and
// the second human's mirror never contains what only the first can see.
[Collection("RulesetCache")]
public sealed class SimultaneousTurnConflictTests
{
    // ----- the arena: two humans (A, B) and an AI civ (C) on a desert hexagon, all met, everything visible unless a test says otherwise -----

    private sealed class Arena
    {
        public TestGame Game { get; } = new();
        public CivilizationObject A { get; }
        public CivilizationObject B { get; }
        public CivilizationObject C { get; }
        public GameSession Session { get; }

        public Arena()
        {
            Game.MakeHexagonalMap(8);
            A = Game.AddCiv(isPlayer: true);
            B = Game.AddCiv(isPlayer: true);
            C = Game.AddCiv();
            foreach (var x in new[] { A, B, C })
                foreach (var y in new[] { A, B, C })
                    if (x != y) x.DiplomacyFunctions.MakeCivilizationsMeet(y);
            // anchors in the corners so that no civ is defeated for lack of units
            Unit("Warrior", A, -8, 0);
            Unit("Warrior", B, 8, 0);
            Unit("Warrior", C, 0, 8);
            foreach (var civ in Game.GameInfo.Civilizations) civ.PopupAlerts.Clear();
            Game.GameInfo.CurrentPlayerCiv = A;
            Game.GameInfo.CurrentPlayer = A.CivId;
            Game.GameInfo.StartSimultaneousTurns();
            Session = new GameSession(Game.GameInfo);
            SeeAll();
        }

        public Unciv.Logic.Map.Tiles.Tile Tile(int x, int y) => Game.GetTile(x, y);

        public MapUnitObject Unit(string name, CivilizationObject civ, int x, int y)
        {
            var unit = Game.AddUnit(name, civ, Tile(x, y));
            unit.CurrentMovement = unit.GetMaxMovement();
            return unit;
        }

        public void SeeAll()
        {
            foreach (var civ in Game.GameInfo.Civilizations) civ.ViewableTiles = Game.TileMap.TileList.ToHashSet();
        }

        public ulong Hash() => HashOf(Game.GameInfo);

        public void War(CivilizationObject x, CivilizationObject y) => x.GetDiplomacyManager(y)!.DeclareWar();

        public CommandResult Ok(CivilizationObject actor, GameCommand command)
        {
            var result = Session.Submit(actor.CivId, command);
            Assert.True(result.Accepted, command.Type + " by " + actor.CivId + ": " + result.Validation.Code + " " + result.Validation.Message);
            return result;
        }

        // A rejection changes nothing and says the same thing every time (T164 purity rule).
        public void Rejected(CivilizationObject actor, GameCommand command, params string[] codes)
        {
            var (hash, count, seq) = (Hash(), Session.Log.Count, Session.LastSeq);
            var result = Session.Submit(actor.CivId, command);
            Assert.False(result.Accepted);
            Assert.Contains(result.Validation.Code, codes);
            Assert.False(string.IsNullOrEmpty(result.Validation.Message));
            Assert.Equal((hash, count, seq), (Hash(), Session.Log.Count, Session.LastSeq));
        }
    }

    private static string Save(GameInfo game) => UncivFiles.GameInfoToString(game, forceZip: false);

    private static ulong HashOf(GameInfo game) => StateHasher.ComputeHash(h => h.Add(Save(game)));

    // The rule state without the allocations that depend on the order: notification and trade request lists are sorted, and city names are blanked (a
    // nation's name list is shared, so the second founder of "The Capital" gets "New [The Capital]"). Everything else must be order independent.
    private static ulong Canonical(GameInfo game)
    {
        var root = JsonNode.Parse(Save(game))!;
        foreach (var civ in root["civilizations"]!.AsArray())
        {
            foreach (var list in new[] { "notifications", "tradeRequests" })
            {
                if (civ![list] is not JsonArray items) continue;
                var sorted = items.Select(it => it!.ToJsonString()).Order(StringComparer.Ordinal).ToList();
                civ.AsObject()[list] = new JsonArray(sorted.Select(it => JsonNode.Parse(it)).ToArray());
            }
            if (civ!["cities"] is JsonArray cities)
                foreach (var city in cities) city!.AsObject()["name"] = "";
        }
        return StateHasher.ComputeHash(h => h.Add(root.ToJsonString()));
    }

    // The log of a played arena replays, on an identical fresh arena, to the played state.
    private static void AssertReplays(Arena played, Func<Arena> build)
    {
        var fresh = build();
        var follower = new GameSession(fresh.Game.GameInfo);
        foreach (var envelope in played.Session.Log) follower.Replay(envelope);
        Assert.Equal(played.Hash(), fresh.Hash());
        Assert.Equal(played.Session.LastSeq, follower.LastSeq);
    }

    private static CivilizationObject[] Order(Arena arena, bool aFirst) => aFirst ? [arena.A, arena.B] : [arena.B, arena.A];

    // ----- row 1: two units of different civs move to the same tile -----

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void TwoUnitsMovingToTheSameTile_TheFirstArrivesAndTheSecondIsRejected(bool aFirst, bool atWar)
    {
        MapUnitObject a = null!, b = null!;
        Arena Build()
        {
            var arena = new Arena();
            if (atWar) arena.War(arena.A, arena.B);
            a = arena.Unit("Warrior", arena.A, -2, 0);
            b = arena.Unit("Warrior", arena.B, 2, 0);
            return arena;
        }
        var arena = Build();
        var target = new HexCoord(0, 0);
        var moves = new Dictionary<CivilizationObject, GameCommand>
        {
            [arena.A] = new MoveUnit { UnitId = a.Id, To = target },
            [arena.B] = new MoveUnit { UnitId = b.Id, To = target },
        };
        var (winner, loser) = (Order(arena, aFirst)[0], Order(arena, aFirst)[1]);
        arena.Ok(winner, moves[winner]);
        arena.Rejected(loser, moves[loser], CommandErrors.InvalidTarget);
        arena.Rejected(loser, moves[loser], CommandErrors.InvalidTarget); // stable: the same code again, still nothing changed

        Assert.Equal(winner.CivId, arena.Tile(0, 0).MilitaryUnit!.Civ.CivId);
        Assert.Equal(loser == arena.A ? new HexCoord(-2, 0) : new HexCoord(2, 0), (loser == arena.A ? a : b).CurrentTile.Position); // the loser did not move
        AssertReplays(arena, Build);
    }

    [Fact]
    public void TheOrderOfTwoMovesToTheSameTileIsVisibleInTheState()
    {
        ulong Run(bool aFirst)
        {
            var arena = new Arena();
            var a = arena.Unit("Warrior", arena.A, -2, 0);
            var b = arena.Unit("Warrior", arena.B, 2, 0);
            var moves = new Dictionary<CivilizationObject, GameCommand>
            {
                [arena.A] = new MoveUnit { UnitId = a.Id, To = new HexCoord(0, 0) },
                [arena.B] = new MoveUnit { UnitId = b.Id, To = new HexCoord(0, 0) },
            };
            foreach (var civ in Order(arena, aFirst)) arena.Session.Submit(civ.CivId, moves[civ]);
            return arena.Hash();
        }
        Assert.NotEqual(Run(true), Run(false));
        Assert.Equal(Run(true), Run(true));
    }

    // ----- row 2: two units attack the same unit or city -----

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 1)]
    [InlineData(true, 100)]
    [InlineData(false, 100)]
    public void TwoUnitsAttackingTheSameUnit_AreSerialisedAndTheSecondRecomputesAgainstTheNewState(bool aFirst, int defenderHealth)
    {
        MapUnitObject a = null!, b = null!, target = null!;
        Arena Build()
        {
            var arena = new Arena();
            arena.War(arena.A, arena.C);
            arena.War(arena.B, arena.C);
            a = arena.Unit("Warrior", arena.A, -1, 0);
            b = arena.Unit("Warrior", arena.B, 1, 0);
            target = arena.Unit("Warrior", arena.C, 0, 0);
            target.Health = defenderHealth;
            arena.SeeAll();
            return arena;
        }
        var arena = Build();
        var attacks = new Dictionary<CivilizationObject, GameCommand>
        {
            [arena.A] = new AttackWithUnit { UnitId = a.Id, Target = new HexCoord(0, 0) },
            [arena.B] = new AttackWithUnit { UnitId = b.Id, Target = new HexCoord(0, 0) },
        };
        var (first, second) = (Order(arena, aFirst)[0], Order(arena, aFirst)[1]);
        arena.Ok(first, attacks[first]);
        var afterFirst = target.Health;
        if (defenderHealth == 1)
        {
            Assert.True(target.IsDestroyed); // the first attack killed it ...
            arena.Rejected(second, attacks[second], CommandErrors.InvalidTarget); // ... so the second has nothing to attack
            arena.Rejected(second, attacks[second], CommandErrors.InvalidTarget);
        }
        else
        {
            Assert.True(afterFirst < 100 && !target.IsDestroyed);
            arena.Ok(second, attacks[second]);
            Assert.True(target.Health < afterFirst || target.IsDestroyed); // the second fought what the first left
        }
        AssertReplays(arena, Build);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TwoUnitsAttackingTheSameCity_TheFirstTakesItAndTheSecondDoesNotSeeTheOldOwner(bool aFirst)
    {
        MapUnitObject a = null!, b = null!;
        Unciv.Logic.City.City city = null!;
        Arena Build()
        {
            var arena = new Arena();
            arena.War(arena.A, arena.C);
            arena.War(arena.B, arena.C);
            arena.War(arena.A, arena.B);
            city = arena.Game.AddCity(arena.C, arena.Tile(0, 0));
            a = arena.Unit("Warrior", arena.A, -1, 0);
            b = arena.Unit("Warrior", arena.B, 1, 0);
            city.Health = 1;
            arena.SeeAll();
            return arena;
        }
        var arena = Build();
        var attacks = new Dictionary<CivilizationObject, GameCommand>
        {
            [arena.A] = new AttackWithUnit { UnitId = a.Id, Target = new HexCoord(0, 0) },
            [arena.B] = new AttackWithUnit { UnitId = b.Id, Target = new HexCoord(0, 0) },
        };
        var (first, second) = (Order(arena, aFirst)[0], Order(arena, aFirst)[1]);
        arena.Ok(first, attacks[first]);
        Assert.Equal(first.CivId, city.Civ.CivId); // the first applied owns the city
        // the second command was validated against the new owner, not the old: either it is a fight with the new owner's garrison or it is refused cleanly
        var (hash, count) = (arena.Hash(), arena.Session.Log.Count);
        var result = arena.Session.Submit(second.CivId, attacks[second]);
        if (!result.Accepted)
        {
            Assert.Equal((hash, count), (arena.Hash(), arena.Session.Log.Count));
            Assert.Contains(result.Validation.Code, new[] { CommandErrors.InvalidTarget, CommandErrors.NotAllowed });
        }
        Assert.NotEqual(arena.C.CivId, city.Civ.CivId);
        AssertReplays(arena, Build);
    }

    // ----- row 3: found a city on the same or a neighbouring tile -----

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    [InlineData(false, 2)]
    public void TwoSettlersFoundingNearEachOther_TheFirstFoundsAndTheSecondKeepsItsSettler(bool aFirst, int distance)
    {
        MapUnitObject a = null!, b = null!;
        Arena Build()
        {
            var arena = new Arena();
            a = arena.Unit("Settler", arena.A, 0, 0);
            b = arena.Unit("Settler", arena.B, distance, 0);
            return arena;
        }
        var arena = Build();
        var found = new Dictionary<CivilizationObject, GameCommand>
        {
            [arena.A] = new ExecuteUnitAction { UnitId = a.Id, Descriptor = Found(a), Confirmed = true },
            [arena.B] = new ExecuteUnitAction { UnitId = b.Id, Descriptor = Found(b), Confirmed = true },
        };
        var (first, second) = (Order(arena, aFirst)[0], Order(arena, aFirst)[1]);
        arena.Ok(first, found[first]);
        Assert.Single(first.Cities);
        arena.Rejected(second, found[second], CommandErrors.NotAllowed, CommandErrors.StaleReference);
        Assert.Empty(second.Cities);
        Assert.False((second == arena.A ? a : b).IsDestroyed); // not consumed: the player is told by the rejection and can walk on
        AssertReplays(arena, Build);
    }

    private static UnitActionDescriptor Found(MapUnitObject settler) =>
        UnitActionDescriptor.Describe(settler).First(it => it.Descriptor.ActionType == UnitActionType.FoundCity).Descriptor;

    // ----- row 6: both buy the same tile -----

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TwoCivsBuyingTheSameTile_TheFirstOwnsItAndTheSecondCannotBuyIt(bool aFirst)
    {
        Unciv.Logic.City.City cityA = null!, cityB = null!;
        HexCoord tile = default;
        Arena Build()
        {
            var arena = new Arena();
            cityA = arena.Game.AddCity(arena.A, arena.Tile(-2, 0));
            cityB = arena.Game.AddCity(arena.B, arena.Tile(2, 0));
            arena.A.AddGold(5000 - arena.A.Gold);
            arena.B.AddGold(5000 - arena.B.Gold);
            tile = arena.Game.TileMap.TileList.First(it => cityA.Expansion.CanBuyTile(it) && cityB.Expansion.CanBuyTile(it)).Position;
            return arena;
        }
        var arena = Build();
        var buys = new Dictionary<CivilizationObject, GameCommand>
        {
            [arena.A] = new BuyTile { CityId = cityA.Id, Tile = tile },
            [arena.B] = new BuyTile { CityId = cityB.Id, Tile = tile },
        };
        var (first, second) = (Order(arena, aFirst)[0], Order(arena, aFirst)[1]);
        var goldBefore = second.Gold;
        var firstGoldBefore = first.Gold;
        arena.Ok(first, buys[first]);
        Assert.True(first.Gold < firstGoldBefore); // the winner paid
        Assert.Equal(first.CivId, arena.Game.GetTile(tile).GetOwner()!.CivId);
        arena.Rejected(second, buys[second], CommandErrors.InvalidTarget);
        Assert.Equal(goldBefore, second.Gold);
        AssertReplays(arena, Build);
    }

    // ----- independent commands commute on rule state -----

    [Fact]
    public void IndependentCommandsOfDifferentCivsGiveTheSameStateInEitherOrder()
    {
        MapUnitObject a = null!, b = null!, sa = null!, sb = null!;
        Arena Build()
        {
            var arena = new Arena();
            a = arena.Unit("Warrior", arena.A, -4, 0);
            b = arena.Unit("Warrior", arena.B, 4, 0);
            sa = arena.Unit("Settler", arena.A, -4, 4);
            sb = arena.Unit("Settler", arena.B, 4, -4);
            return arena;
        }
        (ulong Full, ulong Canon) Run(bool aFirst)
        {
            var arena = Build();
            var script = new (CivilizationObject Civ, Func<GameCommand> Command)[]
            {
                (arena.A, () => new MoveUnit { UnitId = a.Id, To = new HexCoord(-3, 0) }),
                (arena.B, () => new MoveUnit { UnitId = b.Id, To = new HexCoord(3, 0) }),
                (arena.A, () => new ExecuteUnitAction { UnitId = sa.Id, Descriptor = Found(sa), Confirmed = true }),
                (arena.B, () => new ExecuteUnitAction { UnitId = sb.Id, Descriptor = Found(sb), Confirmed = true }),
            };
            foreach (var (civ, command) in aFirst ? script : script.Reverse()) arena.Ok(civ, command());
            return (arena.Hash(), Canonical(arena.Game.GameInfo));
        }
        var (ab, ba) = (Run(true), Run(false));
        Assert.Equal(ab.Canon, ba.Canon);
        Assert.NotEqual(ab.Full, ba.Full); // the only order dependence is the allocation of city names (a shared name list): the log order is visible in the state
        Assert.Equal(ab, Run(true));
    }

    [Fact]
    public void IndependentProposalsToTheSameCivCommuteOnCanonicalStateButTheRequestOrderIsTheLogOrder()
    {
        (ulong Full, ulong Canon, string[] Order) Run(bool aFirst)
        {
            var arena = new Arena();
            arena.A.AddGold(100 - arena.A.Gold);
            arena.B.AddGold(100 - arena.B.Gold);
            foreach (var civ in Order(arena, aFirst))
                arena.Ok(civ, new ProposeTrade { CivId = arena.C.CivId, OurOffers = [new TradeOfferSpec(Constants.FlatGold, nameof(TradeOfferType.Gold), 50)] });
            return (arena.Hash(), Canonical(arena.Game.GameInfo), arena.C.TradeRequests.Select(it => it.RequestingCiv).ToArray());
        }
        var ab = Run(true);
        var ba = Run(false);
        Assert.Equal(2, ab.Order.Length);
        Assert.NotEqual(ab.Order, ba.Order); // what the log order decides ...
        Assert.NotEqual(ab.Full, ba.Full);
        Assert.Equal(ab.Order.Order(StringComparer.Ordinal), ba.Order.Order(StringComparer.Ordinal));
        Assert.Equal(ab.Canon, ba.Canon); // ... and nothing else
        Assert.Equal(ab.Full, Run(true).Full); // same log twice, same state
    }

    // ----- row 4, 5 and the ended humans: diplomacy -----

    private static DiplomacyWorld Sim()
    {
        var w = new DiplomacyWorld();
        w.A.AddGold(300 - w.A.Gold);
        w.B.AddGold(50 - w.B.Gold);
        w.Game.GameInfo.StartSimultaneousTurns();
        return w;
    }

    private static TradeOfferSpec Gold(int amount) => new(Constants.FlatGold, nameof(TradeOfferType.Gold), amount);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DeclaringWarWhileTheOtherAcceptsATrade_FirstAppliedWins(bool warFirst)
    {
        var w = Sim();
        w.Ok(new ProposeTrade { CivId = w.B.CivId, OurOffers = [Gold(100)] }); // A gifts B 100 gold, B has to accept
        var accept = new RespondToTradeRequest { CivId = w.A.CivId, Accept = true };
        var war = new DeclareWarCommand { CivId = w.B.CivId };
        if (warFirst)
        {
            w.Ok(war);
            Assert.Empty(w.B.TradeRequests); // DeclareWar clears the pending requests between the pair
            w.Rejected(accept, CommandErrors.StaleReference, w.B.CivId);
            Assert.Equal((300, 50), (w.A.Gold, w.B.Gold));
            Assert.True(w.A.IsAtWarWith(w.B));
        }
        else
        {
            w.Ok(accept, w.B.CivId);
            Assert.Equal((200, 150), (w.A.Gold, w.B.Gold));
            w.Ok(war); // an ordinary declaration against a civ at peace: the trade stays done
            Assert.Equal((200, 150), (w.A.Gold, w.B.Gold));
            Assert.True(w.A.IsAtWarWith(w.B));
        }
    }

    [Fact]
    public void AcceptingPeaseWhileTheOtherRedeclaresWar_PeaceFirstBlocksTheDeclarationAndWarFirstIsANoOp()
    {
        ulong Run(bool peaceFirst)
        {
            var w = Sim();
            w.MakeWar(w.A, w.B);
            var peace = new TradeOfferSpec(Constants.PeaceTreaty, nameof(TradeOfferType.Treaty));
            w.Ok(new ProposeTrade { CivId = w.B.CivId, OurOffers = [peace] });
            var accept = new RespondToTradeRequest { CivId = w.A.CivId, Accept = true };
            var war = new DeclareWarCommand { CivId = w.B.CivId };
            if (peaceFirst)
            {
                w.Ok(accept, w.B.CivId);
                Assert.False(w.A.IsAtWarWith(w.B));
                w.Rejected(war, CommandErrors.NotAllowed); // the peace treaty is in force
                w.Rejected(war, CommandErrors.NotAllowed);
            }
            else
            {
                w.Rejected(war, CommandErrors.NotAllowed); // already at war
                w.Ok(accept, w.B.CivId);
                Assert.False(w.A.IsAtWarWith(w.B));
            }
            return w.Hash();
        }
        Assert.Equal(Run(true), Run(false)); // a rejection leaves no trace, so the two logs end in the same state
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConflictingTradesOverTheSameGold_TheFirstAcceptanceWinsAndTheSecondIsStale(bool bAcceptsFirst)
    {
        var w = Sim();
        w.Ok(new ProposeTrade { CivId = w.B.CivId, OurOffers = [Gold(200)] }); // A gives 200
        w.Ok(new ProposeTrade { CivId = w.A.CivId, TheirOffers = [Gold(200)] }, w.B.CivId); // B asks A for 200: both fit in A's 300 until one is paid
        Assert.Single(w.A.TradeRequests);
        Assert.Single(w.B.TradeRequests);
        var acceptOfA = (Cmd: new RespondToTradeRequest { CivId = w.A.CivId, Accept = true }, Actor: w.B.CivId);
        var acceptOfB = (Cmd: new RespondToTradeRequest { CivId = w.B.CivId, Accept = true }, Actor: w.A.CivId);
        var (win, lose) = bAcceptsFirst ? (acceptOfA, acceptOfB) : (acceptOfB, acceptOfA);
        w.Ok(win.Cmd, win.Actor);
        Assert.Equal((100, 250), (w.A.Gold, w.B.Gold)); // A paid once
        Assert.DoesNotContain(win.Actor == w.B.CivId ? w.B.TradeRequests : w.A.TradeRequests, it => it.RequestingCiv == (win.Actor == w.B.CivId ? w.A.CivId : w.B.CivId)); // the answered request is consumed
        w.Rejected(lose.Cmd, CommandErrors.StaleReference, lose.Actor); // A cannot pay again
        w.Rejected(lose.Cmd, CommandErrors.StaleReference, lose.Actor);
        Assert.Equal((100, 250), (w.A.Gold, w.B.Gold));
    }

    [Fact]
    public void AnEndedHumanCanStillAnswerAndCannotPropose_AndNobodyAnswersWhileResolving()
    {
        var w = Sim();
        var session = w.Session;
        var proposed = new ProposeTrade { CivId = w.B.CivId, OurOffers = [Gold(100)] };
        Assert.True(session.SubmitAsServer(new ForceEndTurn { CivId = w.B.CivId }).Accepted);
        w.Ok(proposed); // B has ended; A is still playing and may ask B
        w.Rejected(new ProposeTrade { CivId = w.A.CivId, TheirOffers = [Gold(10)] }, CommandErrors.AlreadyEnded, w.B.CivId); // the ended B cannot propose
        Assert.True(session.SubmitAsServer(new ForceEndTurn { CivId = w.A.CivId }).Accepted); // everybody ended: the round resolves
        Assert.Equal(SimultaneousPhase.Resolving, w.Game.GameInfo.TurnState!.Phase);
        w.Rejected(new RespondToTradeRequest { CivId = w.A.CivId, Accept = true }, CommandErrors.TurnResolving, w.B.CivId);
        Assert.Single(w.B.TradeRequests); // still waiting for the next round

        // and the ended human answering while the other one still plays
        var v = Sim();
        v.Ok(new ProposeTrade { CivId = v.B.CivId, OurOffers = [Gold(100)] });
        Assert.True(v.Session.SubmitAsServer(new ForceEndTurn { CivId = v.B.CivId }).Accepted);
        v.Ok(new RespondToTradeRequest { CivId = v.A.CivId, Accept = true }, v.B.CivId);
        Assert.Equal((200, 150), (v.A.Gold, v.B.Gold));
    }

    // ----- visibility under interleaving (section 7) -----

    [Fact]
    public void TheOtherHumansMirrorNeverContainsWhatOnlyTheFirstHumanSees()
    {
        var arena = new Arena();
        var game = arena.Game;
        var info = game.GameInfo;
        var marker = "Hidden Fort";
        game.AddCity(arena.A, arena.Tile(-6, 0)).Name = marker;
        arena.A.AddGold(4711 - arena.A.Gold);
        var scout = arena.Unit("Warrior", arena.A, -3, 0);
        var anchorA = arena.A.Units.GetCivUnits().First(it => it.CurrentTile.Position == new HexCoord(-8, 0));
        var walker = arena.Unit("Warrior", arena.B, 3, 0);
        arena.Unit("Warrior", arena.B, 6, 0);

        void Vision()
        {
            foreach (var civ in new[] { arena.A, arena.B })
            {
                var seen = game.TileMap.TileList
                    .Where(t => civ.Units.GetCivUnits().Any(u => HexMath.GetDistance(u.CurrentTile.Position, t.Position) <= 2)
                        || civ.Cities.Any(c => HexMath.GetDistance(c.Location, t.Position) <= 2))
                    .ToHashSet();
                foreach (var tile in seen) tile.SetExplored(civ, true);
                civ.ViewableTiles = seen;
            }
        }

        var status = new TurnStatusDto { Ended = [], Phase = "Playing" };
        Vision();
        var filter = new ViewerEventFilter(info, arena.B.CivId);
        var mirror = SnapshotBuilder.Build(info, arena.B, 0, status);
        var seenByB = new HashSet<int>();
        var hiddenFromB = new HashSet<int>();
        var path = new[] { -2, -1, 0, 1, 2 };
        for (var step = 0; step < path.Length * 2; step++)
        {
            var aTurn = step % 2 == 0;
            Vision();
            var mark = filter.Mark();
            var before = SnapshotBuilder.Build(info, arena.B, step, status);
            var x = path[step / 2];
            scout.CurrentMovement = scout.GetMaxMovement();
            walker.CurrentMovement = walker.GetMaxMovement();
            var result = aTurn
                ? arena.Ok(arena.A, new MoveUnit { UnitId = scout.Id, To = new HexCoord(x, 0) })
                : arena.Ok(arena.B, new MoveUnit { UnitId = walker.Id, To = new HexCoord((step / 2) % 2 == 0 ? 4 : 3, 0) });
            Vision();
            var after = SnapshotBuilder.Build(info, arena.B, step + 1, status);
            var delivered = filter.Filter(mark, result.Events);

            // 1. the mirror built from deltas is exactly the viewer's own fresh snapshot, whatever the other human did in between
            mirror = SnapshotDiff.Compute(before, after).Apply(mirror with { Seq = before.Seq });
            Assert.Equal(SnapshotBuilder.ToCanonicalJson(after), SnapshotBuilder.ToCanonicalJson(mirror with { ViewHash = null }));

            // 2. what the mirror holds about A's units is only what B sees: on a tile that is visible to B, and never the anchor, the gold or the fort
            var visibleToB = after.Tiles.Where(it => it.Visible).Select(it => (it.X, it.Y)).ToHashSet();
            foreach (var unit in after.Units.Where(it => it.Owner == arena.A.CivId))
            {
                Assert.Contains((unit.X, unit.Y), visibleToB);
                Assert.Null(unit.Own);
                seenByB.Add(unit.Id);
            }
            if (!after.Units.Any(it => it.Id == scout.Id)) hiddenFromB.Add(scout.Id);
            Assert.DoesNotContain(after.Units, it => it.Id == anchorA.Id);
            var text = SnapshotBuilder.ToCanonicalJson(after);
            Assert.DoesNotContain("4711", text);
            Assert.DoesNotContain(marker, text);
            Assert.DoesNotContain(after.Cities, it => it.Owner == arena.A.CivId);
            Assert.All(after.Tiles, tile => Assert.True(game.GetTile(tile.X, tile.Y).IsExplored(arena.B)));
            Assert.DoesNotContain(after.Tiles, it => it.X < -4);

            // 3. an event about A's unit reaches B only along tiles B could see before or after the command
            var visibleBefore = before.Tiles.Where(it => it.Visible).Select(it => new HexCoord(it.X, it.Y)).ToHashSet();
            var visibleAfter = visibleToB.Select(it => new HexCoord(it.X, it.Y)).ToHashSet();
            foreach (var moved in delivered.OfType<UnitMoved>().Where(it => it.CivId == arena.A.CivId))
                Assert.All(moved.Path, tile => Assert.True(visibleBefore.Contains(tile) || visibleAfter.Contains(tile)));
            Assert.DoesNotContain(delivered.OfType<UnitMoved>(), it => it.CivId == arena.A.CivId && it.UnitId == anchorA.Id);
        }
        Assert.Contains(scout.Id, seenByB); // positive control: B did see A's scout once it came close
        Assert.Contains(scout.Id, hiddenFromB); // and did not while it was far away
    }

    // ----- section 3.2: a unit killed by the other human before its queued order runs -----

    [Fact]
    public void AMoveOrderOfAUnitKilledByTheOtherHumanIsANoOp()
    {
        var arena = new Arena();
        arena.War(arena.A, arena.B);
        var victim = arena.Unit("Warrior", arena.A, 0, 0);
        var killer = arena.Unit("Warrior", arena.B, 1, 0);
        victim.Health = 1;
        arena.SeeAll();
        victim.CurrentMovement = 0f;
        victim.Action = "moveTo -6,0"; // the stored multi-turn order, executed in BeginTurn
        arena.Ok(arena.B, new AttackWithUnit { UnitId = killer.Id, Target = victim.CurrentTile.Position });
        Assert.True(victim.IsDestroyed);
        var hash = arena.Hash();
        arena.Rejected(arena.A, new SetMoveOrder { UnitId = victim.Id, To = new HexCoord(-6, 0) }, CommandErrors.UnknownUnit);
        Assert.Equal(hash, arena.Hash());
        victim.DoAction(); // the order running on a dead unit does nothing
        Assert.Equal(hash, arena.Hash());
    }

    // ----- T242, row 7: one-per-game wonders (queued by both, completed at turn processing or live by a Great Engineer) -----

    private static GameCommand StepCommand(ResolveStep step) => step switch
    {
        ResolveStep.ProcessEndTurn s => new ProcessEndTurn { CivId = s.CivId },
        ResolveStep.RunAiTurn s => new RunAiTurn { CivId = s.CivId },
        ResolveStep.StartNextTurn => new StartNextTurn(),
        ResolveStep.BeginTurn s => new BeginTurn { CivId = s.CivId },
        _ => throw new InvalidOperationException(step.ToString()),
    };

    // Both humans end their turn (in the given order), then the host drives the round to the end like the real server does.
    private static void EndRound(Arena arena, bool aFirst)
    {
        foreach (var civ in Order(arena, aFirst)) arena.Ok(civ, new EndTurn());
        var game = arena.Game.GameInfo;
        Assert.Equal(SimultaneousPhase.Resolving, game.TurnState!.Phase);
        while (game.NextResolveStep() is { } step)
        {
            var result = arena.Session.SubmitAsServer(StepCommand(step));
            Assert.True(result.Accepted, result.Validation.Code + ": " + result.Validation.Message);
        }
        Assert.Equal(SimultaneousPhase.Playing, game.TurnState!.Phase);
    }

    // A human with nothing to research is blocked from ending the turn (a pending Technology decision): give both something to do.
    private static void GiveResearch(Arena arena)
    {
        foreach (var civ in new[] { arena.A, arena.B }) civ.Tech.TechsToResearch.Add(arena.Game.Ruleset.Technologies.Keys.First());
    }

    private sealed record WonderRace(Arena Arena, string Wonder, Unciv.Logic.City.City CityA, Unciv.Logic.City.City CityB);

    private static WonderRace BuildWonderRace(int cost, int progress)
    {
        var arena = new Arena();
        var wonder = arena.Game.CreateWonder();
        wonder.Cost = cost;
        GiveResearch(arena);
        var cityA = arena.Game.AddCity(arena.A, arena.Tile(-3, 0));
        var cityB = arena.Game.AddCity(arena.B, arena.Tile(3, 0));
        foreach (var city in new[] { cityA, cityB })
            if (progress > 0) city.CityConstructions.InProgressConstructions[wonder.Name] = progress;
        return new WonderRace(arena, wonder.Name, cityA, cityB);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TwoHumansQueueingTheSameWonderAreBothAcceptedAndTheCivIndexOrderDecidesWhoCompletesIt(bool aFirst)
    {
        var race = BuildWonderRace(cost: 100, progress: 100);
        var arena = race.Arena;
        var queue = new Dictionary<CivilizationObject, GameCommand>
        {
            [arena.A] = new QueueConstruction { CityId = race.CityA.Id, Name = race.Wonder, AddToTop = true },
            [arena.B] = new QueueConstruction { CityId = race.CityB.Id, Name = race.Wonder, AddToTop = true },
        };
        foreach (var civ in Order(arena, aFirst)) arena.Ok(civ, queue[civ]); // nothing is built yet: the wonder is an open race, both may queue it
        Assert.All(new[] { race.CityA, race.CityB }, city => Assert.Equal(race.Wonder, city.CityConstructions.CurrentConstructionName()));
        Assert.False(race.CityA.CityConstructions.IsBuilt(race.Wonder) || race.CityB.CityConstructions.IsBuilt(race.Wonder));

        EndRound(arena, aFirst);

        // the completion happens in BeginTurn (StartTurn -> ConstructIfEnough), in civ-index order: A comes before B, whatever the log order was
        Assert.True(race.CityA.CityConstructions.IsBuilt(race.Wonder));
        Assert.False(race.CityB.CityConstructions.IsBuilt(race.Wonder));
        Assert.DoesNotContain(race.Wonder, race.CityB.CityConstructions.ConstructionQueue);
        // the loser's production is not lost: it is refunded as gold, once, at the next validation
        Assert.Equal(1, arena.B.Notifications.Count(it => it.Text.Contains("Excess production for [" + race.Wonder + "]", StringComparison.Ordinal)));
        Assert.False(race.CityB.CityConstructions.InProgressConstructions.ContainsKey(race.Wonder));
        AssertReplays(arena, () => BuildWonderRace(cost: 100, progress: 100).Arena);
    }

    private static UnitActionDescriptor Hurry(MapUnitObject engineer) =>
        UnitActionDescriptor.Describe(engineer).First(it => it.Descriptor.ActionType == UnitActionType.HurryWonder).Descriptor;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TwoGreatEngineersHurryingTheSameWonder_TheFirstInTheLogCompletesItAndTheSecondIsRejected(bool aFirst)
    {
        MapUnitObject engineerA = null!, engineerB = null!;
        Unciv.Logic.City.City extraA = null!, extraB = null!;
        WonderRace race = null!;
        Arena Build()
        {
            race = BuildWonderRace(cost: 100, progress: 0);
            foreach (var city in new[] { race.CityA, race.CityB }) city.CityConstructions.SetCurrentConstruction(race.Wonder);
            var engineer = race.Arena.Game.CreateBaseUnit("Civilian", "Can speed up the construction of a wonder");
            engineerA = race.Arena.Unit(engineer.Name, race.Arena.A, -3, 0);
            engineerB = race.Arena.Unit(engineer.Name, race.Arena.B, 3, 0);
            extraA = race.Arena.Game.AddCity(race.Arena.A, race.Arena.Tile(-5, 0));
            extraB = race.Arena.Game.AddCity(race.Arena.B, race.Arena.Tile(5, 0));
            return race.Arena;
        }
        var arena = Build();
        var hurry = new Dictionary<CivilizationObject, GameCommand>
        {
            [arena.A] = new ExecuteUnitAction { UnitId = engineerA.Id, Descriptor = Hurry(engineerA) },
            [arena.B] = new ExecuteUnitAction { UnitId = engineerB.Id, Descriptor = Hurry(engineerB) },
        };
        var (first, second) = (Order(arena, aFirst)[0], Order(arena, aFirst)[1]);
        var (firstCity, secondCity, secondEngineer) = first == arena.A ? (race.CityA, race.CityB, engineerB) : (race.CityB, race.CityA, engineerA);
        Assert.Equal(race.Wonder, secondCity.CityConstructions.CurrentConstructionName()); // the race is on: both cities build it
        arena.Ok(first, hurry[first]);
        Assert.True(firstCity.CityConstructions.IsBuilt(race.Wonder));
        var goldBefore = second.Gold;
        arena.Rejected(second, hurry[second], CommandErrors.NotAllowed);
        arena.Rejected(second, hurry[second], CommandErrors.NotAllowed);
        Assert.False(secondEngineer.IsDestroyed); // the Great Engineer is not wasted on a wonder that is gone
        Assert.False(secondCity.CityConstructions.IsBuilt(race.Wonder));
        Assert.Equal(goldBefore, second.Gold);
        // nobody can start the finished wonder anywhere any more
        var later = new QueueConstruction { CityId = (second == arena.A ? extraA : extraB).Id, Name = race.Wonder };
        arena.Rejected(second, later, CommandErrors.NotAllowed, CommandErrors.InvalidTarget);
        arena.Rejected(first, new QueueConstruction { CityId = (first == arena.A ? extraA : extraB).Id, Name = race.Wonder }, CommandErrors.NotAllowed, CommandErrors.InvalidTarget);
        AssertReplays(arena, Build);
    }

    // ----- T242, row 7: a natural wonder's first-discoverer reward and ancient ruins are claimed by whoever the log lets arrive first -----

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TwoHumansDiscoveringTheSameNaturalWonder_OnlyTheFirstInTheLogGetsTheFirstToDiscoverReward(bool aFirst)
    {
        MapUnitObject a = null!, b = null!;
        const string wonder = "El Dorado";
        Arena Build()
        {
            var arena = new Arena();
            var wonderTile = arena.Tile(0, 0);
            wonderTile.NaturalWonder = wonder;
            wonderTile.SetTerrainFeatures(wonderTile.TerrainFeatures); // rebuilds the terrain caches that know the wonder's uniques
            a = arena.Unit("Warrior", arena.A, -4, 0);
            b = arena.Unit("Warrior", arena.B, 4, 0);
            Assert.DoesNotContain(wonder, arena.A.NaturalWonders); // nobody knows it yet: the race is on
            Assert.DoesNotContain(wonder, arena.B.NaturalWonders);
            return arena;
        }
        var arena = Build();
        var moves = new Dictionary<CivilizationObject, GameCommand>
        {
            [arena.A] = new MoveUnit { UnitId = a.Id, To = new HexCoord(-2, 0) },
            [arena.B] = new MoveUnit { UnitId = b.Id, To = new HexCoord(2, 0) },
        };
        var (first, second) = (Order(arena, aFirst)[0], Order(arena, aFirst)[1]);
        var (firstGold, secondGold) = (first.Gold, second.Gold);
        arena.Ok(first, moves[first]);
        Assert.Contains(wonder, first.NaturalWonders);
        Assert.Equal(firstGold + 500, first.Gold); // the first discoverer is paid ...
        arena.Ok(second, moves[second]);
        Assert.Contains(wonder, second.NaturalWonders);
        Assert.Equal(secondGold, second.Gold); // ... the second one discovers it too, for nothing
        Assert.Single(first.Notifications, it => it.Text.Contains("500", StringComparison.Ordinal));
        Assert.DoesNotContain(second.Notifications, it => it.Text.Contains("for discovering", StringComparison.Ordinal));
        AssertReplays(arena, Build);
    }

    private static List<string> RuinRewards(CivilizationObject civ) =>
        JsonNode.Parse(Save((GameInfo)civ.GameInfo))!["civilizations"]!.AsArray()
            .First(it => it!["civName"]!.GetValue<string>() == civ.CivName)!["ruinsManager"]!["lastChosenRewards"]!.AsArray()
            .Select(it => it!.GetValue<string>()).ToList();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TwoHumansReachingTheSameAncientRuins_OnlyTheFirstGetsAReward(bool aFirst)
    {
        MapUnitObject a = null!, b = null!;
        Arena Build()
        {
            var arena = new Arena();
            var ruins = arena.Game.Ruleset.TileImprovements.Values.First(it => it.IsAncientRuinsEquivalent());
            arena.Tile(0, 0).SetImprovement(ruins.Name);
            a = arena.Unit("Warrior", arena.A, -1, 0);
            b = arena.Unit("Warrior", arena.B, 1, 0);
            return arena;
        }
        var arena = Build();
        var tile = arena.Tile(0, 0);
        Assert.NotNull(tile.TileImprovement);
        Assert.All(new[] { arena.A, arena.B }, civ => Assert.All(RuinRewards(civ), it => Assert.Equal("", it)));
        var enter = new Dictionary<CivilizationObject, GameCommand>
        {
            [arena.A] = new MoveUnit { UnitId = a.Id, To = new HexCoord(0, 0) },
            [arena.B] = new MoveUnit { UnitId = b.Id, To = new HexCoord(0, 0) },
        };
        var (first, second) = (Order(arena, aFirst)[0], Order(arena, aFirst)[1]);
        var (firstUnit, secondUnit) = first == arena.A ? (a, b) : (b, a);
        arena.Ok(first, enter[first]);
        Assert.Null(tile.TileImprovement); // consumed by the first unit entering
        Assert.Contains(RuinRewards(first), it => it != "");
        // the second cannot even enter the occupied tile ...
        arena.Rejected(second, enter[second], CommandErrors.InvalidTarget);
        Assert.All(RuinRewards(second), it => Assert.Equal("", it));
        // ... and when the first has walked on, the tile is an ordinary one: entering it gives nothing
        arena.Ok(first, new MoveUnit { UnitId = firstUnit.Id, To = first == arena.A ? new HexCoord(-1, 0) : new HexCoord(1, 0) });
        Assert.Null(tile.MilitaryUnit);
        var secondGold = second.Gold;
        var secondUnits = second.Units.GetCivUnits().Count();
        arena.Ok(second, enter[second]);
        Assert.Equal(tile.Position, secondUnit.CurrentTile.Position);
        Assert.All(RuinRewards(second), it => Assert.Equal("", it));
        Assert.Equal((secondGold, secondUnits), (second.Gold, second.Units.GetCivUnits().Count()));
        AssertReplays(arena, Build);
    }

    // ----- T242, row 8: pantheons, religion founding and beliefs are limited per game: the first applied takes them -----

    private sealed record ReligionRace(Arena Arena, Unciv.Logic.City.City CityA, Unciv.Logic.City.City CityB);

    // Both humans own a city and plenty of faith. <paramref name="limit"/> may thin out the ruleset's beliefs or icons before anything is created.
    private static ReligionRace BuildReligionRace(Action<Unciv.Models.Ruleset.Ruleset>? limit = null)
    {
        var arena = new Arena();
        limit?.Invoke(arena.Game.Ruleset);
        GiveResearch(arena);
        var cityA = arena.Game.AddCity(arena.A, arena.Tile(-3, 0));
        var cityB = arena.Game.AddCity(arena.B, arena.Tile(3, 0));
        arena.A.ReligionManager.StoredFaith = 1000;
        arena.B.ReligionManager.StoredFaith = 1000;
        foreach (var city in new[] { cityA, cityB }) city.CityConstructions.AddToQueue("Warrior"); // a city without production blocks the end of the turn
        return new ReligionRace(arena, cityA, cityB);
    }

    private static List<Belief> Beliefs(Arena arena, BeliefType type) =>
        arena.Game.Ruleset.Beliefs.Values.Where(it => it.Type == type && arena.A.ReligionManager.GetReligionWithBelief(it) == null).ToList();

    // One belief of each slot the great prophet's picker offers, taking the candidates from the offset on.
    private static List<string> FoundingBeliefs(Arena arena, CivilizationObject civ, int offset)
    {
        var picked = new List<string>();
        foreach (var slot in civ.ReligionManager.GetBeliefsToChooseAtFounding())
            for (var i = 0; i < slot.Value; i++)
                picked.Add(Beliefs(arena, slot.Key == BeliefType.Any ? BeliefType.Follower : slot.Key).Select(it => it.Name).Except(picked).Skip(offset).First());
        return picked;
    }

    // The state after a great prophet was used in the city and before the religion is named: what the pending "found a religion" decision waits for.
    private static void UseProphet(Arena arena, CivilizationObject civ, Unciv.Logic.City.City city)
    {
        var pantheon = Beliefs(arena, BeliefType.Pantheon).First(it => !civ.GameInfo.Civilizations.Any(c => c.ReligionManager.GetReligionWithBelief(it) != null));
        civ.ReligionManager.ChooseBeliefs([pantheon], useFreeBeliefs: true);
        civ.ReligionManager.FoundReligion(arena.Game.AddUnit("Great Prophet", civ, city.GetCenterTile()));
        Assert.Equal(ReligionState.FoundingReligion, civ.ReligionManager.ReligionState);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TwoHumansFoundingTheSamePantheon_TheFirstTakesTheBeliefAndTheSecondMustTakeAnother(bool aFirst)
    {
        ReligionRace Build() => BuildReligionRace();
        var race = Build();
        var arena = race.Arena;
        var pantheons = Beliefs(arena, BeliefType.Pantheon);
        Assert.True(pantheons.Count >= 2);
        var found = new Dictionary<CivilizationObject, GameCommand>
        {
            [arena.A] = new FoundPantheon { Belief = pantheons[0].Name },
            [arena.B] = new FoundPantheon { Belief = pantheons[0].Name },
        };
        Assert.All(new[] { arena.A, arena.B }, civ => Assert.True(civ.ReligionManager.CanFoundOrExpandPantheon())); // both can: it is a race for the belief
        var (first, second) = (Order(arena, aFirst)[0], Order(arena, aFirst)[1]);
        var faith = (First: first.ReligionManager.StoredFaith, Second: second.ReligionManager.StoredFaith);
        arena.Ok(first, found[first]);
        Assert.True(first.ReligionManager.Religion!.HasBelief(pantheons[0].Name));
        Assert.Equal(faith.First - first.ReligionManager.FaithForPantheon(additionalCivs: -1), first.ReligionManager.StoredFaith);

        arena.Rejected(second, found[second], CommandErrors.NotAllowed); // taken
        arena.Rejected(second, found[second], CommandErrors.NotAllowed);
        Assert.Equal((ReligionState.None, faith.Second), (second.ReligionManager.ReligionState, second.ReligionManager.StoredFaith));
        Assert.Null(second.ReligionManager.Religion);

        var cost = second.ReligionManager.FaithForPantheon(); // the second pantheon of the game costs more
        arena.Ok(second, new FoundPantheon { Belief = pantheons[1].Name });
        Assert.Equal(faith.Second - cost, second.ReligionManager.StoredFaith);
        Assert.True(second.ReligionManager.Religion!.HasBelief(pantheons[1].Name));
        Assert.False(second.ReligionManager.Religion.HasBelief(pantheons[0].Name));
        AssertReplays(arena, () => Build().Arena);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TwoHumansRacingForTheLastPantheonBelief_TheSecondHasNothingLeftToFoundOrExpand(bool aFirst)
    {
        ReligionRace Build() => BuildReligionRace(rules =>
        {
            var last = rules.Beliefs.Values.First(it => it.Type == BeliefType.Pantheon).Name;
            foreach (var name in rules.Beliefs.Values.Where(it => it.Type == BeliefType.Pantheon && it.Name != last).Select(it => it.Name).ToList()) rules.Beliefs.Remove(name);
        });
        var race = Build();
        var arena = race.Arena;
        var last = Beliefs(arena, BeliefType.Pantheon).Single();
        var found = new FoundPantheon { Belief = last.Name };
        Assert.All(new[] { arena.A, arena.B }, civ => Assert.Contains(PendingDecisions.BlockingFor(civ), it => it.Kind == DecisionKind.Pantheon));
        var (first, second) = (Order(arena, aFirst)[0], Order(arena, aFirst)[1]);
        arena.Ok(first, found);
        Assert.False(second.ReligionManager.CanFoundOrExpandPantheon()); // not only the belief: the whole option is gone
        Assert.DoesNotContain(PendingDecisions.BlockingFor(second), it => it.Kind == DecisionKind.Pantheon); // so the second human is not asked to pick one any more
        arena.Rejected(second, found, CommandErrors.NotAllowed);
        arena.Rejected(second, new FoundPantheon { Belief = "Nonsense" }, CommandErrors.NotAllowed); // validated for availability before the name
        Assert.Equal(1000, second.ReligionManager.StoredFaith);
        arena.Ok(second, new EndTurn()); // and it can end its turn without picking
        AssertReplays(arena, () => Build().Arena);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TwoProphetsFoundingReligionsAtOnce_TheFirstTakesTheIconNameAndBeliefsAndTheSecondChoosesOthers(bool aFirst)
    {
        ReligionRace Build()
        {
            var built = BuildReligionRace();
            UseProphet(built.Arena, built.Arena.A, built.CityA);
            UseProphet(built.Arena, built.Arena.B, built.CityB);
            return built;
        }
        var race = Build();
        var arena = race.Arena;
        var (first, second) = (Order(arena, aFirst)[0], Order(arena, aFirst)[1]);
        var good = FoundingBeliefs(arena, first, 0);
        var other = FoundingBeliefs(arena, first, 1);
        Assert.Empty(good.Intersect(other));
        var religions = arena.Game.Ruleset.Religions;
        Assert.True(religions.Count >= 3);
        Assert.All(new[] { arena.A, arena.B }, civ => Assert.Contains(PendingDecisions.BlockingFor(civ), it => it.Kind == DecisionKind.FoundReligion));
        Assert.Equal(good, FoundingBeliefs(arena, second, 0)); // the very same offer

        arena.Ok(first, new FoundReligion { ReligionName = religions[0], DisplayName = "Faith One", Beliefs = good });
        Assert.Equal(first.CivId, arena.Game.GameInfo.Religions[religions[0]].FoundingCivName);
        Assert.Equal(ReligionState.FoundingReligion, second.ReligionManager.ReligionState);
        // the icon, then the beliefs, then the display name are each taken now
        arena.Rejected(second, new FoundReligion { ReligionName = religions[0], DisplayName = religions[0], Beliefs = other }, CommandErrors.InvalidTarget);
        arena.Rejected(second, new FoundReligion { ReligionName = religions[1], DisplayName = religions[1], Beliefs = good }, CommandErrors.NotAllowed, CommandErrors.InvalidTarget);
        arena.Rejected(second, new FoundReligion { ReligionName = religions[1], DisplayName = "Faith One", Beliefs = other }, CommandErrors.InvalidTarget);
        arena.Rejected(second, new FoundReligion { ReligionName = religions[1], DisplayName = religions[0], Beliefs = other }, CommandErrors.InvalidTarget);
        arena.Rejected(second, new ChooseBeliefs { Beliefs = other }, CommandErrors.NotAllowed); // still the founding, not a reform
        Assert.Equal(ReligionState.FoundingReligion, second.ReligionManager.ReligionState);
        Assert.DoesNotContain(religions[1], arena.Game.GameInfo.Religions.Keys);

        arena.Ok(second, new FoundReligion { ReligionName = religions[1], DisplayName = "Faith Two", Beliefs = other });
        Assert.Equal(new[] { religions[0], religions[1] }, arena.Game.GameInfo.Religions.Keys.Where(religions.Contains).ToArray()); // the pantheons are religions of their own, named after a belief
        Assert.All(other, name => Assert.True(second.ReligionManager.Religion!.HasBelief(name)));
        Assert.All(good, name => Assert.False(second.ReligionManager.Religion!.HasBelief(name)));
        AssertReplays(arena, () => Build().Arena);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TwoProphetsFoundingReligionsWithOneIconLeft_TheSecondCanNoLongerFoundAndIsNotBlockedForever(bool aFirst)
    {
        ReligionRace Build()
        {
            var built = BuildReligionRace(rules => rules.Religions.RemoveAll(it => it != rules.Religions[0])); // a game with room for a single religion
            UseProphet(built.Arena, built.Arena.A, built.CityA);
            UseProphet(built.Arena, built.Arena.B, built.CityB);
            return built;
        }
        var race = Build();
        var arena = race.Arena;
        var (first, second) = (Order(arena, aFirst)[0], Order(arena, aFirst)[1]);
        var icon = arena.Game.Ruleset.Religions.Single();
        var good = FoundingBeliefs(arena, first, 0);
        var found = new FoundReligion { ReligionName = icon, DisplayName = icon, Beliefs = good };
        arena.Ok(first, found);
        Assert.Equal(ReligionState.Religion, first.ReligionManager.ReligionState);

        arena.Rejected(second, found, CommandErrors.InvalidTarget); // the only icon is gone
        arena.Rejected(second, new FoundReligion { ReligionName = "Judaism", DisplayName = "Judaism", Beliefs = good }, CommandErrors.InvalidTarget); // and there is no other
        // what the second human is left with: a prophet that was used and nothing to found. It is told by the rejection, and the server
        // can still end its turn (T250 tracks giving the prophet back or ending this state in the engine).
        Assert.Equal(ReligionState.FoundingReligion, second.ReligionManager.ReligionState);
        arena.Rejected(second, new EndTurn(), CommandErrors.PendingDecisions);
        Assert.True(arena.Session.SubmitAsServer(new ForceEndTurn { CivId = second.CivId }).Accepted);
        Assert.True(arena.Game.GameInfo.HasEndedTurn(second));
        AssertReplays(arena, () => Build().Arena);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TwoHumansReformingTheirReligionWithTheSameFollowerBelief_TheFirstTakesItAndTheSecondChoosesAnother(bool aFirst)
    {
        ReligionRace Build()
        {
            var built = BuildReligionRace();
            var arena = built.Arena;
            var icons = arena.Game.Ruleset.Religions;
            UseProphet(arena, arena.A, built.CityA);
            arena.A.ReligionManager.FoundReligion(icons[0], icons[0]);
            arena.A.ReligionManager.ChooseBeliefs(FoundingBeliefs(arena, arena.A, 0).Select(it => arena.Game.Ruleset.Beliefs[it]).ToList());
            UseProphet(arena, arena.B, built.CityB);
            arena.B.ReligionManager.FoundReligion(icons[1], icons[1]);
            arena.B.ReligionManager.ChooseBeliefs(FoundingBeliefs(arena, arena.B, 0).Select(it => arena.Game.Ruleset.Beliefs[it]).ToList());
            foreach (var civ in new[] { arena.A, arena.B }) civ.ReligionManager.FreeBeliefs["Follower"] = 1; // a belief to add without a prophet (a unique's reward)
            return built;
        }
        var race = Build();
        var arena = race.Arena;
        Assert.All(new[] { arena.A, arena.B }, civ => Assert.Equal(ReligionState.Religion, civ.ReligionManager.ReligionState));
        var followers = Beliefs(arena, BeliefType.Follower);
        Assert.True(followers.Count >= 2);
        var choose = new ChooseBeliefs { Beliefs = [followers[0].Name] };
        var (first, second) = (Order(arena, aFirst)[0], Order(arena, aFirst)[1]);
        Assert.All(new[] { arena.A, arena.B }, civ => Assert.Contains(PendingDecisions.BlockingFor(civ), it => it.Kind == DecisionKind.ReformReligion));
        arena.Ok(first, choose);
        Assert.True(first.ReligionManager.Religion!.HasBelief(followers[0].Name));
        Assert.False(first.ReligionManager.HasFreeBeliefs());
        arena.Rejected(second, choose, CommandErrors.NotAllowed); // taken by the other religion
        arena.Rejected(second, choose, CommandErrors.NotAllowed);
        Assert.True(second.ReligionManager.HasFreeBeliefs()); // the free belief is not spent by a rejection
        Assert.False(second.ReligionManager.Religion!.HasBelief(followers[0].Name));
        arena.Ok(second, new ChooseBeliefs { Beliefs = [followers[1].Name] });
        Assert.True(second.ReligionManager.Religion.HasBelief(followers[1].Name));
        arena.Rejected(first, new ChooseBeliefs { Beliefs = [followers[1].Name] }, CommandErrors.NotAllowed); // nothing left to choose: not allowed
        AssertReplays(arena, () => Build().Arena);
    }
}
