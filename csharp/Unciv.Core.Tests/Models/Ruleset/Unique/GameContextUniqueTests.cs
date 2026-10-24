using Unciv.Logic.Battle;
using Unciv.Logic.Map;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;

namespace Unciv.Core.Tests.Models.Ruleset.Unique;

/// <summary>
/// <c>GameContext</c> has no Kotlin test class. <c>hashCode()</c> and <c>stateBasedRandom</c> are compared with the real compiled Kotlin
/// (<c>Golden/GameContext.golden.tsv</c>: Unciv 4.22.13 jar, objects built through the real Civilization/Tile/MapUnit/MapUnitCombatant/GameInfo
/// classes). Columns: scenario, hashCode, first 3 ints of <c>stateBasedRandom("caller", 31)</c>, first 2 of <c>stateBasedRandom("Chance", 7)</c>.
/// </summary>
public class GameContextUniqueTests
{
    private static readonly FakeGameInfo G1 = new() { Turns = 5, GameId = "g1" };
    private static readonly FakeCiv C1 = new("civ1");
    private static readonly FakeCiv C2 = new("Rome");
    private static readonly FakeUnit UNo = new("Warrior", 2f, C1);
    private static readonly FakeUnit UAt = new("Warrior", 1.5f, C1, new FakeTile(new HexCoord(4, 5)));
    private static readonly FakeUnit UNaN = new("Archer", float.NaN, C1, new FakeTile(new HexCoord(-1, 0)));
    private static readonly FakeUnit Horseman = new("Horseman", 3f, C2, new FakeTile(new HexCoord(5, 5)));
    private static readonly FakeCombatant Our = new(C1, (FakeTile)UAt.GetTile(), "Warrior", unit: UAt);
    private static readonly FakeCombatant Their = new(C2, (FakeTile)Horseman.GetTile(), "Horseman", unit: Horseman);

    private static GameContext Scenario(string id) => id switch
    {
        "empty" => GameContext.EmptyState,
        "ignore" => GameContext.IgnoreConditionalsState,
        "caching" => GameContext.IgnoreMultiplicationForCaching,
        "gameinfo" => new GameContext(gameInfo: G1),
        "gameinfo_negturn_emptyid" => new GameContext(gameInfo: new FakeGameInfo { Turns = -7, GameId = "" }),
        "civ" => new GameContext(civInfo: C1),
        "civ_other" => new GameContext(civInfo: C1, otherCiv: C2),
        "civ_other_gameinfo" => new GameContext(civInfo: C1, otherCiv: C2, gameInfo: G1),
        "tile" => new GameContext(tile: new FakeTile(new HexCoord(3, -2))),
        "tile_attacked" => new GameContext(tile: new FakeTile(new HexCoord(2, 2)), attackedTile: new FakeTile(new HexCoord(1, 1))),
        "unit_notile" => new GameContext(unit: UNo),
        "unit_tile" => new GameContext(unit: UAt),
        "unit_nan" => new GameContext(unit: UNaN),
        "civ_unit_tile" => new GameContext(civInfo: C1, unit: UAt, tile: new FakeTile(new HexCoord(9, 9)), gameInfo: G1),
        "combatants" => new GameContext(civInfo: C1, ourCombatant: Our, theirCombatant: Their),
        "combatants_action" => new GameContext(civInfo: C1, ourCombatant: Our, combatAction: CombatAction.Defend),
        "action_attack" => new GameContext(combatAction: CombatAction.Attack),
        "action_intercept" => new GameContext(combatAction: CombatAction.Intercept),
        "everything" => new GameContext(civInfo: C1, unit: UAt, tile: new FakeTile(new HexCoord(1, 2)), ourCombatant: Our, theirCombatant: Their,
            attackedTile: new FakeTile(new HexCoord(7, -3)), combatAction: CombatAction.Attack, otherCiv: C2, gameInfo: G1, ignoreConditionals: true),
        _ => throw new ArgumentException(id),
    };

    private static string[][] GoldenRows() =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Models", "Ruleset", "Unique", "Golden", "GameContext.golden.tsv"))
            .Select(it => it.Split('\t')).ToArray();

    private static int[] Ints(string csv) => csv.Split(',').Select(it => int.Parse(it, System.Globalization.CultureInfo.InvariantCulture)).ToArray();

    [Fact]
    public void HashCodeAndStateBasedRandomMatchKotlin()
    {
        var rows = GoldenRows().Where(it => it[0] != "EQ").ToList();
        Assert.Equal(19, rows.Count);
        foreach (var row in rows)
        {
            var context = Scenario(row[0]);
            Assert.True(int.Parse(row[1], System.Globalization.CultureInfo.InvariantCulture) == context.GetHashCode(), $"hashCode of {row[0]}");

            var random = context.StateBasedRandom("caller");
            Assert.Equal(Ints(row[2]), new[] { random.NextInt(), random.NextInt(), random.NextInt() });
            var random2 = context.StateBasedRandom("Chance", 7);
            Assert.Equal(Ints(row[3]), new[] { random2.NextInt(), random2.NextInt() });
        }
    }

    [Fact]
    public void DataClassEqualityMatchesKotlin()
    {
        var eq = GoldenRows().Single(it => it[0] == "EQ");
        // The three companion states are structurally equal in Kotlin (data class), which getUniqueMultiplier relies on.
        Assert.Equal(bool.Parse(eq[1]), GameContext.IgnoreMultiplicationForCaching.Equals(GameContext.IgnoreConditionalsState));
        Assert.Equal(bool.Parse(eq[2]), GameContext.EmptyState.Equals(new GameContext()));
        Assert.True(GameContext.IgnoreMultiplicationForCaching.Equals(GameContext.IgnoreConditionalsState));
        Assert.False(GameContext.EmptyState.Equals(GameContext.IgnoreConditionalsState));
        Assert.False(new GameContext(civInfo: C1).Equals(new GameContext(civInfo: C2)));
        Assert.True(new GameContext(civInfo: C1, otherCiv: C2).Equals(new GameContext(civInfo: C1, otherCiv: C2)));
        Assert.False(GameContext.EmptyState.Equals(null));
    }

    [Fact]
    public void GameInfoIsDerivedFromTheOtherMembersLikeKotlinsDefaultArgument()
    {
        var civ = new FakeCiv("c", G1);
        Assert.Same(G1, new GameContext(civInfo: civ).GameInfo);
        Assert.Same(G1, new GameContext(otherCiv: civ).GameInfo);
        Assert.Same(G1, new GameContext(unit: new FakeUnit("u", 1f, civ)).GameInfo);
        Assert.Same(G1, new GameContext(city: new FakeCity("city", civ)).GameInfo);
        Assert.Same(G1, new GameContext(ourCombatant: new FakeCombatant(civ, new FakeTile(HexCoord.Zero), "n")).GameInfo);
        Assert.Same(G1, new GameContext(theirCombatant: new FakeCombatant(civ, new FakeTile(HexCoord.Zero), "n")).GameInfo);
        Assert.Same(G1, new GameContext(attackedTile: new FakeTile(HexCoord.Zero) { GameInfo = G1 }).GameInfo);
        // tile and region do not deduce gameInfo
        Assert.Null(new GameContext(tile: new FakeTile(HexCoord.Zero) { GameInfo = G1 }).GameInfo);
        var other = new FakeGameInfo { Turns = 1, GameId = "x" };
        Assert.Same(other, new GameContext(civInfo: civ, gameInfo: other).GameInfo);
    }

    [Fact]
    public void FactoriesMirrorTheSecondaryConstructors()
    {
        var civ = new FakeCiv("c", G1);
        var centerTile = new FakeTile(new HexCoord(1, 1));
        var city = new FakeCity("city1", civ, centerTile);
        var forCity = GameContext.ForCity(city);
        Assert.Same(civ, forCity.CivInfo);
        Assert.Same(city, forCity.City);
        Assert.Same(centerTile, forCity.Tile);
        Assert.Same(G1, forCity.GameInfo);

        var placed = new FakeUnit("u", 2f, civ, new FakeTile(new HexCoord(2, 2)));
        Assert.Same(placed.GetTile(), GameContext.ForUnit(placed).Tile);
        Assert.Same(placed, GameContext.ForUnit(placed).Unit);
        Assert.Null(GameContext.ForUnit(new FakeUnit("u", 2f, civ)).Tile);

        var civ2 = new FakeCiv("d");
        var forCiv = GameContext.ForCiv(civ, civ2);
        Assert.Same(civ, forCiv.CivInfo);
        Assert.Same(civ2, forCiv.OtherCiv);
        Assert.Null(GameContext.ForCiv(civ).OtherCiv);

        var attackedTile = new FakeTile(new HexCoord(8, 8));
        var ourUnit = new FakeUnit("Warrior", 1f, civ, new FakeTile(new HexCoord(3, 3)));
        var our = new FakeCombatant(civ, (FakeTile)ourUnit.GetTile(), "Warrior", unit: ourUnit);
        var their = new FakeCombatant(civ2, new FakeTile(new HexCoord(4, 4)), "Enemy");
        var combat = GameContext.ForCombatants(our, their, attackedTile, CombatAction.Attack);
        Assert.Same(civ, combat.CivInfo);
        Assert.Same(ourUnit, combat.Unit);
        Assert.Null(combat.City);
        Assert.Same(our.GetTile(), combat.Tile);
        Assert.Same(our, combat.OurCombatant);
        Assert.Same(their, combat.TheirCombatant);
        Assert.Same(civ2, combat.OtherCiv);
        Assert.Same(attackedTile, combat.AttackedTile);
        Assert.Equal(CombatAction.Attack, combat.CombatAction);
        Assert.Same(G1, combat.GameInfo);

        var cityCombatant = new FakeCombatant(civ, centerTile, "City", city: city);
        Assert.Same(city, GameContext.ForCombatants(cityCombatant).City);
    }

    [Fact]
    public void RelevantMembersFollowTheKotlinPrecedence()
    {
        var civ = new FakeCiv("c");
        var other = new FakeCiv("o");
        var unitTile = new FakeTile(new HexCoord(2, 2));
        var unit = new FakeUnit("u", 1f, civ, unitTile);
        var tile = new FakeTile(new HexCoord(1, 1));
        var attacked = new FakeTile(new HexCoord(3, 3));
        var centerTile = new FakeTile(new HexCoord(4, 4));
        var city = new FakeCity("city", civ, centerTile);

        Assert.Same(attacked, new GameContext(tile: tile, attackedTile: attacked).RelevantTile);
        Assert.Same(tile, new GameContext(unit: unit, tile: tile).RelevantTile);
        Assert.Same(unitTile, new GameContext(unit: unit).RelevantTile);
        Assert.Same(centerTile, new GameContext(city: city).RelevantTile);
        Assert.Null(new GameContext(unit: new FakeUnit("u", 1f, civ)).RelevantTile); // pre-placement unit: no tile, no exception
        Assert.Null(GameContext.EmptyState.RelevantTile);

        var mapUnitCombatant = new FakeCombatant(civ, unitTile, "u", unit: unit);
        var cityCombatant = new FakeCombatant(civ, centerTile, "c", city: city);
        Assert.Same(unit, new GameContext(ourCombatant: mapUnitCombatant).RelevantUnit);
        Assert.Same(unit, new GameContext(ourCombatant: mapUnitCombatant, unit: new FakeUnit("x", 1f, civ)).RelevantUnit);
        Assert.Null(new GameContext(ourCombatant: cityCombatant).RelevantUnit);
        Assert.Same(unit, new GameContext(ourCombatant: cityCombatant, unit: unit).RelevantUnit);

        Assert.Same(civ, new GameContext(civInfo: civ, otherCiv: other).RelevantCiv);
        Assert.Same(civ, new GameContext(city: city).RelevantCiv);
        Assert.Same(civ, new GameContext(unit: unit).RelevantCiv);
        Assert.Null(GameContext.EmptyState.RelevantCiv);
    }

    [Fact]
    public void RelevantCityIsTheCityOnTheOwnTileOfTheOwnCivOnly()
    {
        var civ = new FakeCiv("c");
        var enemy = new FakeCiv("e");
        var ownCity = new FakeCity("own", civ);
        var enemyCity = new FakeCity("enemy", enemy);
        var ownTile = new FakeTile(new HexCoord(0, 0)) { City = ownCity };
        var enemyTile = new FakeTile(new HexCoord(1, 0)) { City = enemyCity };

        Assert.Same(ownCity, new GameContext(civInfo: civ, tile: ownTile).RelevantCity);
        Assert.Null(new GameContext(civInfo: civ, tile: enemyTile).RelevantCity);
        // Attacking a city must not make it the relevant city: only `tile`, not `attackedTile`, is consulted
        Assert.Null(new GameContext(civInfo: civ, tile: new FakeTile(new HexCoord(5, 5)), attackedTile: enemyTile).RelevantCity);
        Assert.Same(ownCity, new GameContext(unit: new FakeUnit("u", 1f, civ, ownTile)).RelevantCity);
        // An explicit city wins
        Assert.Same(enemyCity, new GameContext(civInfo: civ, city: enemyCity, tile: ownTile).RelevantCity);
        // No civ at all: the tile city is only accepted when it belongs to the unit's civ
        Assert.Null(new GameContext(tile: ownTile).RelevantCity);
    }

    [Fact]
    public void ResourceAndStatAmountsPreferTheCityThenTheCiv()
    {
        var civ = new FakeCiv("c");
        civ.Resources["Iron"] = 3;
        civ.StatReserves[Stat.Gold] = 100;
        civ.StatReserves[Stat.Food] = 7;
        var city = new FakeCity("city", civ);
        city.Resources["Iron"] = 1;
        city.StatReserves[Stat.Gold] = 5;
        city.StatReserves[Stat.Food] = 9;

        var cityContext = new GameContext(civInfo: civ, city: city);
        Assert.Equal(1, cityContext.GetResourceAmount("Iron"));
        Assert.Equal(5, cityContext.GetStatAmount(Stat.Gold));
        Assert.Equal(9, cityContext.GetStatAmount(Stat.Food));

        var civContext = new GameContext(civInfo: civ);
        Assert.Equal(3, civContext.GetResourceAmount("Iron"));
        Assert.Equal(100, civContext.GetStatAmount(Stat.Gold));
        Assert.Equal(0, civContext.GetStatAmount(Stat.Food)); // Food has no civ-wide field

        Assert.Equal(0, GameContext.EmptyState.GetResourceAmount("Iron"));
        Assert.Equal(0, GameContext.EmptyState.GetStatAmount(Stat.Gold));
    }

    private sealed class FakeRegion(int rectHashCode) : IGameContextRegion
    {
        public int RectHashCode { get; } = rectHashCode;
    }

    [Fact]
    public void CopyKeepsTheStoredGameInfoInsteadOfDerivingItAgain()
    {
        var withoutGameInfo = new GameContext(tile: new FakeTile(new HexCoord(1, 1)));
        Assert.Null(withoutGameInfo.GameInfo);
        // Kotlin `copy(otherCiv = civ)` keeps gameInfo = null although civ has one (Nuke.kt / QuestManager.kt rely on this for stateBasedRandom)
        var copy = withoutGameInfo.Copy(otherCiv: new FakeCiv("civ", G1));
        Assert.Null(copy.GameInfo);
        Assert.Same(withoutGameInfo.Tile, copy.Tile);
        Assert.NotNull(copy.OtherCiv);
        Assert.NotEqual(withoutGameInfo.GetHashCode(), copy.GetHashCode());
        // an explicit gameInfo wins, and so does an explicit null
        Assert.Same(G1, withoutGameInfo.Copy(gameInfo: G1).GameInfo);
        Assert.Null(new GameContext(civInfo: C1).Copy(gameInfo: new GameContext.CopyArg<IGameContextGameInfo>(null)).GameInfo);
    }

    [Fact]
    public void CopyReplacesOnlyTheGivenMembersAndCanSetThemToNull()
    {
        var tile = new FakeTile(new HexCoord(2, 3));
        var original = new GameContext(civInfo: C1, tile: tile, combatAction: CombatAction.Attack, gameInfo: G1);
        var moved = new FakeTile(new HexCoord(9, 9));
        var copy = original.Copy(tile: moved, ignoreConditionals: true);
        Assert.Same(C1, copy.CivInfo);
        Assert.Same(moved, copy.Tile);
        Assert.Equal(CombatAction.Attack, copy.CombatAction);
        Assert.True(copy.IgnoreConditionals);
        Assert.Same(G1, copy.GameInfo);
        Assert.Null(original.Copy(tile: new GameContext.CopyArg<IGameContextTile>(null)).Tile);
        Assert.Equal(original, original.Copy());
    }

    [Fact]
    public void AnExplicitlyNullGameInfoOfTheFactoriesStaysNull()
    {
        var civWithoutGameInfo = new FakeCiv("noGame");
        var civWithGameInfo = new FakeCiv("game", G1);
        // Kotlin passes `gameInfo = civ.gameInfo` explicitly, so the second civ's gameInfo is not picked up
        Assert.Null(GameContext.ForCiv(civWithoutGameInfo, civWithGameInfo).GameInfo);
        Assert.Same(G1, GameContext.ForCiv(civWithGameInfo, civWithoutGameInfo).GameInfo);
        var ours = new FakeCombatant(civWithoutGameInfo, new FakeTile(HexCoord.Zero), "n");
        var theirs = new FakeCombatant(civWithGameInfo, new FakeTile(HexCoord.Zero), "m");
        Assert.Null(GameContext.ForCombatants(ours, theirs).GameInfo);
    }

    [Fact]
    public void RegionAndCityCombatantContributeToTheHash()
    {
        Assert.NotEqual(new GameContext().GetHashCode(), new GameContext(region: new FakeRegion(12345)).GetHashCode());
        Assert.NotEqual(new GameContext(region: new FakeRegion(1)).GetHashCode(), new GameContext(region: new FakeRegion(2)).GetHashCode());
        var city = new FakeCity("Roma", C1);
        var cityCombatant = new FakeCombatant(C1, new FakeTile(new HexCoord(3, 3)), "Roma", city: city);
        var otherCityCombatant = new FakeCombatant(C1, new FakeTile(new HexCoord(3, 4)), "Roma", city: city);
        Assert.NotEqual(new GameContext(ourCombatant: cityCombatant).GetHashCode(), new GameContext(ourCombatant: otherCityCombatant).GetHashCode());
    }

    [Fact]
    public void LazyRelevantMembersAreStableAcrossThreads()
    {
        var civ = new FakeCiv("c", G1);
        var unit = new FakeUnit("u", 1f, civ, new FakeTile(new HexCoord(1, 1)));
        var context = new GameContext(unit: unit);
        var seen = new IGameContextTile?[64];
        Parallel.For(0, seen.Length, i => seen[i] = context.RelevantTile);
        Assert.All(seen, it => Assert.Same(unit.GetTile(), it));
    }
}
