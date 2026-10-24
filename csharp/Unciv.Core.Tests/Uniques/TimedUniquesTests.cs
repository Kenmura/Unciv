using Unciv.Core.Tests.Support;
using Unciv.Logic.Battle;
using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Map;
using Unciv.Models.Ruleset.Unique;

namespace Unciv.Core.Tests.Uniques;

// T143: port of tests/src/com/unciv/uniques/TimedUniquesTests.kt over the real TestGame.
// (testConditionalTimedUniqueIsTriggerable is also covered game-free in Models/Ruleset/Unique/TimedUniqueTests.cs.)
[Collection("RulesetCache")]
public sealed class TimedUniquesTests
{
    private readonly TestGame game;
    private readonly Unciv.Logic.Civilization.Civilization civInfo;
    private readonly Unciv.Models.Ruleset.Policy policy;
    private readonly MapUnitCombatant attacker;
    private readonly MapUnitCombatant defender;

    public TimedUniquesTests()
    {
        game = new TestGame();
        game.MakeHexagonalMap(2);
        civInfo = game.AddCiv();
        policy = game.CreatePolicy("[+42]% Strength <when attacking> <for [Military] units> <for [1] turns>");
        var enemy = game.AddCiv();
        attacker = new MapUnitCombatant(game.AddUnit("Warrior", civInfo, game.GetTile(HexCoord.Zero)));
        defender = new MapUnitCombatant(game.AddUnit("Warrior", enemy, game.GetTile(1, 0)));
    }

    [Fact]
    public void TestConditionalTimedUniqueIsTriggerable()
    {
        var unique = policy.UniqueObjects.First(it => it.Type == UniqueType.Strength);
        Assert.True(unique.IsTriggerable, "Unique with timed conditional must be triggerable");
    }

    [Fact]
    public void TestConditionalTimedUniqueStrength()
    {
        civInfo.Policies.Adopt(policy, true);
        var modifiers = BattleDamage.GetAttackModifiers(attacker, defender, attacker.GetTile());
        Assert.True(modifiers.SumValues() == 42, "Timed Strength should work right after triggering");
    }

    [Fact]
    public void TestConditionalTimedUniqueExpires()
    {
        civInfo.Policies.Adopt(policy, true);
        // For endTurn to do the part we need, the civ must be alive - have a city or unit,
        // and right now that attacker is not in the civ's unit list
        civInfo.Units.AddUnit(attacker.Unit, false);
        new TurnManager(civInfo).EndTurn();
        var modifiers = BattleDamage.GetAttackModifiers(attacker, defender, attacker.GetTile());
        Assert.True(modifiers.SumValues() == 0, "Timed Strength should no longer work after endTurn");
    }
}
