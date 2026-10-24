using Unciv.Logic.Map;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Core.Tests.Models.Ruleset.Unique;

/// <summary>
/// <c>Unique</c> has no Kotlin test class of its own (only the TimedUniquesTests fixture, ported as <c>TimedUniqueTests</c>), so the parsing
/// results are compared with the real compiled Kotlin class: <c>Golden/Unique.golden.tsv</c> (Unciv 4.22.13 jar, <c>new Unique(text)</c> on the JVM)
/// holds 2009 uniques: the uniques of the bundled rulesets that load as plain JSON, every <c>UniqueType</c> text, and those texts with one or
/// two modifiers appended. Uniques with a deprecated type are left out (T029). Columns: text, placeholderText, params, type name,
/// modifier texts, modifier type names, isTimedTriggerable, isTriggerable, allParams, isLocalEffect, isOtherModifierType,
/// hasFlag for each UniqueFlag as 0/1, isHiddenToUsers, hasTriggerConditional, isModifiedByGameSpeed, isModifiedByGameProgress,
/// getDisplayText, toString, the bits of each Stat in stats. Lists are joined with U+001F.
/// </summary>
public class UniqueTests
{
    private const char Us = '\u001f';

    private static string Join(IEnumerable<string> items) => string.Join(Us, items);

    private static IReadOnlyList<string[]> LoadGolden() =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Models", "Ruleset", "Unique", "Golden", "Unique.golden.tsv"))
            .Select(it => it.Split('\t')).ToList();

    [Fact]
    public void ParsingMatchesKotlin()
    {
        var golden = LoadGolden();
        Assert.Equal(2009, golden.Count);
        foreach (var c in golden)
        {
            Assert.Equal(19, c.Length);
            var unique = new UniqueObject(c[0]);
            string text = c[0];
            Assert.True(c[1] == unique.PlaceholderText, $"placeholderText of {text}");
            Assert.True(c[2] == Join(unique.Params), $"params of {text}");
            Assert.True(c[3] == (unique.Type?.ToString() ?? "<null>"), $"type of {text}");
            Assert.True(c[4] == Join(unique.Modifiers.Select(it => it.Text)), $"modifiers of {text}");
            Assert.True(c[5] == Join(unique.Modifiers.Select(it => it.Type?.ToString() ?? "<null>")), $"modifier types of {text}");
            Assert.True(bool.Parse(c[6]) == unique.IsTimedTriggerable, $"isTimedTriggerable of {text}");
            Assert.True(bool.Parse(c[7]) == unique.IsTriggerable, $"isTriggerable of {text}");
            Assert.True(c[8] == Join(unique.AllParams), $"allParams of {text}");
            Assert.True(bool.Parse(c[9]) == unique.IsLocalEffect, $"isLocalEffect of {text}");
            Assert.True(bool.Parse(c[10]) == unique.IsOtherModifierType, $"isOtherModifierType of {text}");
            Assert.True(c[11] == new string(Enum.GetValues<UniqueFlag>().Select(f => unique.HasFlag(f) ? '1' : '0').ToArray()), $"flags of {text}");
            Assert.True(bool.Parse(c[12]) == unique.IsHiddenToUsers(), $"isHiddenToUsers of {text}");
            Assert.True(bool.Parse(c[13]) == unique.HasTriggerConditional(), $"hasTriggerConditional of {text}");
            Assert.True(bool.Parse(c[14]) == unique.IsModifiedByGameSpeed(), $"isModifiedByGameSpeed of {text}");
            Assert.True(bool.Parse(c[15]) == unique.IsModifiedByGameProgress(), $"isModifiedByGameProgress of {text}");
            Assert.True(c[16] == unique.GetDisplayText(), $"displayText of {text}");
            Assert.True(c[17] == unique.ToString(), $"toString of {text}");
            Assert.True(c[18] == string.Join(',', Enum.GetValues<Stat>().Select(s => BitConverter.SingleToInt32Bits(unique.Stats[s]))), $"stats of {text}");
        }
    }

    [Fact]
    public void GoldenCoversTheInterestingBranches()
    {
        var golden = LoadGolden();
        Assert.Contains(golden, c => bool.Parse(c[6])); // timed
        Assert.Contains(golden, c => bool.Parse(c[7]) && !bool.Parse(c[6])); // triggerable
        Assert.Contains(golden, c => bool.Parse(c[9])); // local effect
        Assert.Contains(golden, c => bool.Parse(c[10])); // other modifier type
        Assert.Contains(golden, c => c[11].Contains('1')); // flags
        Assert.Contains(golden, c => bool.Parse(c[12])); // hidden
        Assert.Contains(golden, c => bool.Parse(c[13])); // trigger conditional
        Assert.Contains(golden, c => c[3] == "<null>"); // unknown type
        Assert.Contains(golden, c => c[16] != c[0]); // display text differs
        Assert.Contains(golden, c => c[18].Split(',').Any(it => it != "0")); // stats
    }

    [Fact]
    public void ConstructorKeepsSourceInformation()
    {
        var unique = new UniqueObject("[+42]% Strength <when attacking>", UniqueTarget.Policy, "Honor");
        Assert.Equal(UniqueTarget.Policy, unique.SourceObjectType);
        Assert.Equal("Honor", unique.SourceObjectName);
        Assert.Equal("Policies", unique.GetSourceNameForUser());
        var bare = new UniqueObject("[+42]% Strength");
        Assert.Null(bare.SourceObjectType);
        Assert.Null(bare.SourceObjectName);
        Assert.Equal("", bare.GetSourceNameForUser());
    }

    [Fact]
    public void SourceNameForUserMatchesKotlinWhen()
    {
        Assert.Equal("Wonders", new UniqueObject("x", UniqueTarget.Wonder).GetSourceNameForUser());
        Assert.Equal("Buildings", new UniqueObject("x", UniqueTarget.Building).GetSourceNameForUser());
        Assert.Equal("Policies", new UniqueObject("x", UniqueTarget.Policy).GetSourceNameForUser());
        Assert.Equal("City-States", new UniqueObject("x", UniqueTarget.CityState).GetSourceNameForUser());
        Assert.Equal("Tech", new UniqueObject("x", UniqueTarget.Tech).GetSourceNameForUser());
        Assert.Equal("Global Effect", new UniqueObject("x", UniqueTarget.Global).GetSourceNameForUser());
        Assert.Equal("Global Effect", new UniqueObject("[+1 Gold] <when attacking>", UniqueTarget.Global).GetSourceNameForUser());
        Assert.Equal("Golden Age", new UniqueObject("[+1 Gold] <during a Golden Age>", UniqueTarget.Global).GetSourceNameForUser());
        Assert.Equal("Happiness", new UniqueObject("[+1 Gold] <while the empire is happy>", UniqueTarget.Global).GetSourceNameForUser());
        Assert.Equal("We Love The King Day", new UniqueObject("[+1 Gold] <during We Love The King Day>", UniqueTarget.Global).GetSourceNameForUser());
    }

    [Fact]
    public void StatsAreTheFirstStatsParameterAndBadlyDefinedStatsAreEmpty()
    {
        var unique = new UniqueObject("[+1 Gold, +2 Culture] from every [Shrine]");
        Assert.Equal(1f, unique.Stats.Gold);
        Assert.Equal(2f, unique.Stats.Culture);
        Assert.Same(unique.Stats, unique.Stats); // lazy: parsed once

        var bad = new UniqueObject("[lots of Gold] from every [Shrine]");
        Assert.True(bad.Stats.IsEmpty());
    }

    [Fact]
    public void ModifiersAreGroupedByTypeAndKeepOrder()
    {
        var unique = new UniqueObject("[+42]% Strength <when attacking> <for [Military] units> <for [1] turns> <for [Civilian] units> <no such conditional>");
        Assert.Equal(5, unique.Modifiers.Count);
        Assert.Equal("when attacking", unique.GetModifiers(UniqueType.ConditionalAttacking).Single().Text);
        Assert.Empty(unique.GetModifiers(UniqueType.ConditionalInThisCity));
        Assert.True(unique.HasModifier(UniqueType.ConditionalAttacking));
        Assert.True(unique.HasModifier(UniqueType.ConditionalTimedUnique));
        Assert.True(unique.IsTimedTriggerable);
        Assert.False(unique.HasModifier(UniqueType.ConditionalInThisCity));
        // allParams: unique parameters first, then each modifier's
        Assert.Equal(new[] { "+42" }, unique.Params);
        Assert.Equal(new[] { "+42", "Military", "1", "Civilian" }, unique.AllParams.Where(it => it != "").ToArray());
    }

    [Fact]
    public void ConditionalsApplyShortCircuits()
    {
        // No modifiers, ignoreConditionals and timed uniques never reach Conditionals (T028)
        Assert.True(new UniqueObject("[+1 Gold] from every [Shrine]").ConditionalsApply(GameContext.EmptyState));
        Assert.True(new UniqueObject("[+1 Gold] from every [Shrine] <when attacking>").ConditionalsApply(GameContext.IgnoreConditionalsState));
        Assert.True(new UniqueObject("[+25]% Strength <when attacking> <for [1] turns>").ConditionalsApply(GameContext.EmptyState));
        // TODO(T028): with Conditionals ported, a unique with a real conditional is evaluated by it
        Assert.Throws<NotImplementedException>(() => new UniqueObject("[+1 Gold] <when attacking>").ConditionalsApply(GameContext.EmptyState));
    }

    [Fact]
    public void IsTriggerableCoversTriggerableTargetsAndTimedModifiers()
    {
        Assert.True(new UniqueObject("Free Great Person").IsTriggerable);
        Assert.False(new UniqueObject("no such unique").IsTriggerable);
        Assert.True(new UniqueObject(PolicyUniqueForTimed).IsTriggerable);
    }

    private const string PolicyUniqueForTimed = "[+42]% Strength <when attacking> <for [Military] units> <for [1] turns>";

    [Fact]
    public void HiddenToUsersFollowsFlagAndModifier()
    {
        Assert.True(new UniqueObject("[+1 Gold] from every [Shrine] <hidden from users>").IsHiddenToUsers());
        Assert.False(new UniqueObject("[+1 Gold] from every [Shrine]").IsHiddenToUsers());
        // From the Kotlin run: a modifier that is itself hidden (a Suppress warning) is dropped from the display text
        var withSuppression = new UniqueObject(
            "Enemy [Land] units must spend [1] extra movement points when inside your territory <before discovering [Dynamite]> <Suppress warning [*contains a conditional on a unit movement unique*]>");
        Assert.Equal(
            "Enemy [Land] units must spend [1] extra movement points when inside your territory <before discovering [Dynamite]>",
            withSuppression.GetDisplayText());
        var onlyHidden = new UniqueObject("Free Great Person <Suppress warning [*x*]>");
        Assert.Equal("Free Great Person", onlyHidden.GetDisplayText());
        Assert.Equal("Free Great Person <when attacking>", new UniqueObject("Free Great Person <when attacking>").GetDisplayText());
    }

    [Fact]
    public void GameProgressModifierStacksMultiplicativelyUsingTheLargerProgress()
    {
        var one = new UniqueObject("[+10]% Strength <(modified by game progress up to [50]%)>");
        var two = new UniqueObject("[+10]% Strength <(modified by game progress up to [50]%)> <(modified by game progress up to [200]%)>");
        Assert.True(one.IsModifiedByGameProgress());
        // 1 tech of 10 -> 0.1, 2 policies of 4 -> 0.5; the larger one counts: 1 + (50/100 - 1) * 0.5 = 0.75, then 1 + (200/100 - 1) * 0.5 = 1.5
        var civ = new FakeProgress(1, 10, 2, 4);
        Assert.Equal(0.75f, one.GetGameProgressModifier(civ));
        Assert.Equal(0.75f * 1.5f, two.GetGameProgressModifier(civ));
        // technologies ahead of policies
        Assert.Equal(1 + (0.5f - 1) * 0.8f, one.GetGameProgressModifier(new FakeProgress(8, 10, 1, 4)));
        // empty rulesets count as no progress; a unique without the modifier is unaffected
        Assert.Equal(1f, one.GetGameProgressModifier(new FakeProgress(0, 0, 0, 0)));
        Assert.Equal(1f, new UniqueObject("[+10]% Strength").GetGameProgressModifier(civ));
    }

    private sealed class FakeProgress(int techs, int rulesetTechs, int policies, int rulesetPolicies) : IGameProgressCivilization
    {
        public int ResearchedTechnologiesCount { get; } = techs;
        public int RulesetTechnologiesCount { get; } = rulesetTechs;
        public int AdoptedPoliciesCount { get; } = policies;
        public int RulesetPoliciesCount { get; } = rulesetPolicies;
    }

    [Fact]
    public void UniqueMultiplierIsOneWithoutCountableModifiers()
    {
        var unique = new UniqueObject("[+1 Gold] from every [Shrine]");
        Assert.Equal(1, unique.GetUniqueMultiplier(GameContext.EmptyState));
        Assert.Equal(1, unique.GetUniqueMultiplier(GameContext.IgnoreMultiplicationForCaching));
        // equal to IgnoreConditionals in Kotlin, because GameContext is a data class
        Assert.Equal(1, unique.GetUniqueMultiplier(GameContext.IgnoreConditionalsState));
        Assert.Single(unique.GetMultiplied(GameContext.EmptyState));
        var visits = 0;
        unique.ForEachMultiplied(GameContext.EmptyState, _ => visits++);
        Assert.Equal(1, visits);
    }

    [Fact]
    public void UniqueMultiplierCountsMatchingNeighborsForEveryAdjacentTile()
    {
        var unique = new UniqueObject("[+1 Gold] from every [Shrine] <for every adjacent [Forest]>");
        var tile = new FakeTile(new HexCoord(0, 0));
        foreach (var (x, y, matches) in new[] { (1, 0, true), (0, 1, false), (-1, 0, true), (0, -1, true) })
        {
            var neighbor = new FakeTile(new HexCoord(x, y));
            if (matches) neighbor.Matching.Add("Forest");
            tile.NeighborTiles.Add(neighbor);
        }
        var context = new GameContext(tile: tile);
        Assert.Equal(3, unique.GetUniqueMultiplier(context));
        Assert.Equal(3, unique.GetMultiplied(context).Count());
        Assert.All(unique.GetMultiplied(context), it => Assert.Same(unique, it));
        var visits = 0;
        unique.ForEachMultiplied(context, _ => visits++);
        Assert.Equal(3, visits);
        // without a relevant tile the modifier is not evaluated
        Assert.Equal(1, unique.GetUniqueMultiplier(GameContext.EmptyState));
        // caching state always 1:1
        Assert.Equal(1, unique.GetUniqueMultiplier(GameContext.IgnoreMultiplicationForCaching));

        // no matching neighbor: multiplier 0 removes the unique
        var none = new FakeTile(new HexCoord(5, 5));
        none.NeighborTiles.Add(new FakeTile(new HexCoord(5, 6)));
        Assert.Equal(0, unique.GetUniqueMultiplier(new GameContext(tile: none)));
        Assert.Empty(unique.GetMultiplied(new GameContext(tile: none)));
    }

    [Fact]
    public void ToStringMatchesKotlin()
    {
        Assert.Equal("\"no such unique\"", new UniqueObject("no such unique").ToString());
        Assert.Equal("[stats] from every [tileFilter/specialist/buildingFilter] (\"[+1 Culture] from every [Shrine]\")",
            new UniqueObject("[+1 Culture] from every [Shrine]").ToString());
    }

    [Fact]
    public void ReplacementTextIsEmptyForUniquesWithoutDeprecationAnnotation()
    {
        // No UniqueType entry is deprecated; the DeprecatedUniqueType path (the whole sign/parameter shuffling) is covered by T029.
        Assert.Equal("", new UniqueObject("[+1 Culture] from every [Shrine]").GetReplacementText(_ => true));
        Assert.Null(new UniqueObject("[+1 Culture] from every [Shrine]").GetDeprecationAnnotation());
    }

    [Fact]
    public void GetModifiersExtensionParsesNestedAndMultipleModifiers()
    {
        var modifiers = "[+1 Gold] <when attacking> <for [Military] units>".GetModifiers();
        Assert.Equal(new[] { "when attacking", "for [Military] units" }, modifiers.Select(it => it.Text).ToArray());
        Assert.Empty("no modifiers here".GetModifiers());
    }
}
