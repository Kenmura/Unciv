using Unciv.Json;
using Unciv.Models.Ruleset.Unique;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Core.Tests.Models.Ruleset.Unique;

/// <summary>
/// Port of <c>tests/src/com/unciv/uniques/TimedUniquesTests.kt</c> (the state-independent part) plus tests for <c>TemporaryUnique</c>.
/// Deferred to T035/T046 (they need TestGame, Civilization.policies.adopt, BattleDamage and TurnManager):
/// <c>testConditionalTimedUniqueStrength</c> and <c>testConditionalTimedUniqueExpires</c>.
/// </summary>
public class TimedUniqueTests
{
    // game.createPolicy(...).uniqueObjects: Unique(text, Policy, policy name)
    private const string PolicyUnique = "[+42]% Strength <when attacking> <for [Military] units> <for [1] turns>";

    [Fact]
    public void TestConditionalTimedUniqueIsTriggerable()
    {
        var unique = new[] { new UniqueObject(PolicyUnique, UniqueTarget.Policy, "Test policy") }.First(it => it.Type == UniqueType.Strength);
        Assert.True(unique.IsTriggerable, "Unique with timed conditional must be triggerable");
    }

    [Fact]
    public void ConstructorStripsTheTimedModifierAndTrims()
    {
        var source = new UniqueObject(PolicyUnique, UniqueTarget.Policy, "Test policy");
        var temporary = new TemporaryUnique(source, 3);
        Assert.Equal("[+42]% Strength <when attacking> <for [Military] units>", temporary.UniqueText);
        Assert.Equal(3, temporary.TurnsLeft);
        Assert.Equal(UniqueTarget.Policy, temporary.SourceObjectType);
        Assert.Equal("Test policy", temporary.SourceObjectName);

        // only the first occurrence is removed, and trimming removes the space left at the edges
        var leading = new TemporaryUnique(new UniqueObject("<for [2] turns> [+1 Gold] from every [Shrine]"), 2);
        Assert.Equal("[+1 Gold] from every [Shrine]", leading.UniqueText);
    }

    [Fact]
    public void UniqueObjectIsParsedFromTheStoredStateOnceAndIsNotTimedAnymore()
    {
        var temporary = new TemporaryUnique(new UniqueObject(PolicyUnique, UniqueTarget.Policy, "Test policy"), 1);
        var uniqueObject = temporary.UniqueObject;
        Assert.Same(uniqueObject, temporary.UniqueObject);
        Assert.Equal(UniqueType.Strength, uniqueObject.Type);
        Assert.False(uniqueObject.IsTimedTriggerable);
        Assert.Equal(UniqueTarget.Policy, uniqueObject.SourceObjectType);
        Assert.Equal("Test policy", uniqueObject.SourceObjectName);
    }

    [Fact]
    public void CloneCopiesTheSerializedStateButNotTheParsedObject()
    {
        var temporary = new TemporaryUnique(new UniqueObject(PolicyUnique, UniqueTarget.Policy, "Test policy"), 4);
        var original = temporary.UniqueObject;
        var clone = temporary.Clone();
        Assert.Equal(temporary.UniqueText, clone.UniqueText);
        Assert.Equal(temporary.TurnsLeft, clone.TurnsLeft);
        Assert.Equal(temporary.SourceObjectType, clone.SourceObjectType);
        Assert.Equal(temporary.SourceObjectName, clone.SourceObjectName);
        clone.TurnsLeft = 1;
        Assert.Equal(4, temporary.TurnsLeft);
        Assert.NotSame(original, clone.UniqueObject);
    }

    [Fact]
    public void EndTurnCountsDownAndRemovesExpiredOnes()
    {
        var uniques = new List<TemporaryUnique>
        {
            new() { UniqueText = "a [b]", TurnsLeft = 2 },
            new() { UniqueText = "c [d]", TurnsLeft = 1 },
            new() { UniqueText = "e [f]", TurnsLeft = -1 }, // negative: never counted down, never removed
            // Kotlin quirk kept: 0 is >= 0, so it is counted down to -1, which means "forever" and is never removed again
            new() { UniqueText = "g [h]", TurnsLeft = 0 },
        };
        uniques.EndTurn();
        Assert.Equal(new[] { "a [b]", "e [f]", "g [h]" }, uniques.Select(it => it.UniqueText).ToArray());
        Assert.Equal(new[] { 1, -1, -1 }, uniques.Select(it => it.TurnsLeft).ToArray());
        uniques.EndTurn();
        Assert.Equal(new[] { "e [f]", "g [h]" }, uniques.Select(it => it.UniqueText).ToArray());
    }

    [Fact]
    public void MatchingFiltersByTypeAndAppliesConditionalsAndMultiplication()
    {
        var uniques = new List<TemporaryUnique>
        {
            new() { UniqueText = "[+10]% Strength", TurnsLeft = 2 },
            new() { UniqueText = "[+20]% Strength", TurnsLeft = 2 },
            new() { UniqueText = "[+1 Gold] from every [Shrine]", TurnsLeft = 2 },
            new() { UniqueText = "no such thing", TurnsLeft = 2 },
        };
        var context = GameContext.EmptyState;
        Assert.Equal(new[] { "[+10]% Strength", "[+20]% Strength" }, uniques.GetMatchingTagUniques(UniqueType.Strength, context).Select(it => it.Text).ToArray());
        var visited = new List<string>();
        uniques.ForEachMatchingUnique(UniqueType.StatsFromObject, context, it => visited.Add(it.Text));
        Assert.Equal(new[] { "[+1 Gold] from every [Shrine]" }, visited.ToArray());
        Assert.Empty(uniques.GetMatchingTagUniques(UniqueType.Stats, context));
    }

    [Fact]
    public void SerializesWithTheKotlinFieldNames()
    {
        var temporary = new TemporaryUnique(new UniqueObject(PolicyUnique, UniqueTarget.Policy, "Test policy"), 3);
        string json = UncivJson.Serialize(temporary);
        using (var document = System.Text.Json.JsonDocument.Parse(json))
        {
            var root = document.RootElement;
            Assert.Equal(new[] { "unique", "sourceObjectType", "sourceObjectName", "turnsLeft" }, root.EnumerateObject().Select(it => it.Name).ToArray());
            Assert.Equal("[+42]% Strength <when attacking> <for [Military] units>", root.GetProperty("unique").GetString());
            Assert.Equal("Policy", root.GetProperty("sourceObjectType").GetString());
            Assert.Equal(3, root.GetProperty("turnsLeft").GetInt32());
        }
        var back = UncivJson.Deserialize<TemporaryUnique>(json)!;
        Assert.Equal(temporary.UniqueText, back.UniqueText);
        Assert.Equal(UniqueTarget.Policy, back.SourceObjectType);
        Assert.Equal("Test policy", back.SourceObjectName);
        Assert.Equal(3, back.TurnsLeft);
        Assert.Equal(UniqueType.Strength, back.UniqueObject.Type);
    }
}
