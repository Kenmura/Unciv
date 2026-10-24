using Unciv.Models.Ruleset.Unique;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Core.Tests.Models.Ruleset.Unique;

/// <summary>
/// <c>IHasUniques</c> has no Kotlin test class of its own (the ruleset object classes that implement it are tested through their own tickets).
/// </summary>
public class IHasUniquesUniqueTests
{
    private sealed class TestObject : IHasUniques
    {
        private readonly Lazy<IReadOnlyList<UniqueObject>> uniqueObjects;
        private readonly Lazy<UniqueMap> uniqueMap;

        public TestObject(string name, params string[] uniques)
        {
            Name = name;
            Uniques = uniques.ToList();
            uniqueObjects = new Lazy<IReadOnlyList<UniqueObject>>(() => ((IHasUniques)this).UniqueObjectsProvider());
            uniqueMap = new Lazy<UniqueMap>(() => ((IHasUniques)this).UniqueMapProvider());
        }

        public string Name { get; set; }
        public List<string> Uniques { get; set; }
        public IReadOnlyList<UniqueObject> UniqueObjects => uniqueObjects.Value;
        public UniqueMap UniqueMap => uniqueMap.Value;
        public UniqueTarget GetUniqueTarget() => UniqueTarget.Building;
    }

    [Fact]
    public void ProvidersBuildUniquesWithTheirSourceAndKeepDuplicates()
    {
        IHasUniques obj = new TestObject("Granary", "[+1 Food] from every [Farm]", "[+1 Food] from every [Farm]", "unknown [x]");
        Assert.Equal(3, obj.UniqueObjects.Count);
        Assert.All(obj.UniqueObjects, it => Assert.Equal(UniqueTarget.Building, it.SourceObjectType));
        Assert.All(obj.UniqueObjects, it => Assert.Equal("Granary", it.SourceObjectName));
        Assert.Equal(2, obj.UniqueMap.GetUniques(UniqueType.StatsFromObject).Count());
        Assert.Empty(new TestObject("Empty").UniqueObjects);
        Assert.True(new TestObject("Empty").UniqueMap.IsEmpty());
    }

    [Fact]
    public void QueriesDelegateToTheUniqueMap()
    {
        IHasUniques obj = new TestObject("Granary", "[+1 Food] from every [Farm]", "unknown [x]");
        var empty = GameContext.EmptyState;
        Assert.True(obj.HasUnique(UniqueType.StatsFromObject));
        Assert.True(obj.HasUnique(UniqueType.StatsFromObject, empty));
        Assert.False(obj.HasUnique(UniqueType.Strength));
        Assert.True(obj.HasTagUnique("unknown []"));
        Assert.True(obj.HasTagUnique("unknown []", null));
        Assert.True(obj.HasTagUnique("unknown []", empty));
        Assert.False(obj.HasTagUnique("nothing"));
        Assert.Single(obj.GetMatchingUniques(UniqueType.StatsFromObject));
        Assert.Single(obj.GetMatchingTagUniques("unknown []"));
        var visited = new List<string>();
        obj.ForEachMatchingUnique(UniqueType.StatsFromObject, empty, it => visited.Add(it.Text));
        obj.ForEachMatchingUnique(UniqueType.StatsFromObject, empty, _ => true, it => visited.Add(it.Text));
        Assert.Equal(2, visited.Count);
    }

    [Fact]
    public void ParameterViewUsesTheUniqueMapNotTextMatching()
    {
        IRulesetObjectParameterView view = new TestObject("Granary", "[+1 Food] from every [Farm]", "unknown [x] <when attacking>");
        Assert.True(view.HasTagUnique("unknown []")); // tag exists, conditional not evaluated
        Assert.False(view.HasTagUnique("nothing"));
        Assert.True(view.HasTagUniqueIgnoringConditionals("unknown []"));
        Assert.False(view.HasTagUniqueIgnoringConditionals("nothing"));
        Assert.Equal(2, view.Uniques.Count);
        Assert.Equal("Granary", view.Name);
    }

    [Fact]
    public void MentioningAndRequiredTechs()
    {
        IHasUniques obj = new TestObject("Granary",
            "[+1 Food] from every [Farm]",
            "Only available <after discovering [Pottery]> <after discovering [Writing]>",
            "Can only be built <after discovering [Masonry]>");
        Assert.True(obj.HasUniquesMentioning("Farm"));
        Assert.False(obj.HasUniquesMentioning("Mine"));
        Assert.Equal(new[] { "Pottery", "Writing", "Masonry" }, obj.RequiredTechs().ToArray());
        Assert.Empty(((IHasUniques)new TestObject("Plain", "[+1 Food] from every [Farm]")).RequiredTechs());
    }

    [Fact]
    public void GameBasedConditionalsAreTheTenGameSettingConditionals()
    {
        Assert.Equal(10, IHasUniques.GameBasedConditionals.Count);
        Assert.Contains(UniqueType.ConditionalSpeed, IHasUniques.GameBasedConditionals);
        Assert.DoesNotContain(UniqueType.ConditionalTech, IHasUniques.GameBasedConditionals);
    }

    [Fact]
    public void IsAvailableWithoutAvailabilityUniques()
    {
        Assert.True(((IHasUniques)new TestObject("Plain", "[+1 Food] from every [Farm]")).IsAvailable(GameContext.EmptyState));
    }

    [Fact]
    public void HiddenFromCivilopediaGuessesFromTheRulesetWhenNoGameIsLoaded()
    {
        IHasUniques hidden = new TestObject("Hidden", "Will not be displayed in Civilopedia");
        Assert.True(hidden.IsHiddenFromCivilopedia(null));
        IHasUniques plain = new TestObject("Plain", "[+1 Food] from every [Farm]");
        Assert.False(plain.IsHiddenFromCivilopedia(null));
        Assert.False(plain.IsHiddenFromCivilopedia(null, new FakeFeatures(true, true)));

        // Only available when religion is enabled: hidden by a ruleset without beliefs, shown by one with beliefs
        IHasUniques needsReligion = new TestObject("Temple", "Only available <when religion is enabled>");
        Assert.True(needsReligion.IsHiddenFromCivilopedia(null, new FakeFeatures(false, true)));
        Assert.False(needsReligion.IsHiddenFromCivilopedia(null, new FakeFeatures(true, false)));
        IHasUniques noReligion = new TestObject("Pagan", "Only available <when religion is disabled>");
        Assert.False(noReligion.IsHiddenFromCivilopedia(null, new FakeFeatures(false, true)));
        Assert.True(noReligion.IsHiddenFromCivilopedia(null, new FakeFeatures(true, true)));
        IHasUniques needsSpies = new TestObject("Spy", "Only available <when espionage is enabled>");
        Assert.True(needsSpies.IsHiddenFromCivilopedia(null, new FakeFeatures(true, false)));
        Assert.False(needsSpies.IsHiddenFromCivilopedia(null, new FakeFeatures(false, true)));
        IHasUniques unavailableWithoutSpies = new TestObject("NoSpy", "Unavailable <when espionage is disabled>");
        Assert.True(unavailableWithoutSpies.IsHiddenFromCivilopedia(null, new FakeFeatures(false, false)));
        Assert.False(unavailableWithoutSpies.IsHiddenFromCivilopedia(null, new FakeFeatures(false, true)));
    }

    private sealed class FakeFeatures(bool hasBeliefs, bool hasSpyNames) : IRulesetFeatureView
    {
        public bool HasBeliefs { get; } = hasBeliefs;
        public bool HasSpyNames { get; } = hasSpyNames;
    }

    [Fact]
    public void WithAKnownGameTheAvailabilityCheckRunsInsteadOfTheRulesetGuess()
    {
        var game = new FakeGameInfo { Turns = 3, GameId = "g" };
        IHasUniques plain = new TestObject("Plain", "[+1 Food] from every [Farm]");
        Assert.False(plain.IsUnavailableBySettings(game));
        Assert.False(plain.IsHiddenFromCivilopedia(game));
        // without game-based conditionals the Unavailable unique never consults Conditionals (T028 adds the cases with real conditionals)
        IHasUniques unavailable = new TestObject("Unavailable", "Unavailable");
        Assert.False(unavailable.IsUnavailableBySettings(game));
        IHasUniques hidden = new TestObject("Hidden", "Will not be displayed in Civilopedia");
        Assert.True(hidden.IsHiddenFromCivilopedia(game));
        // the ruleset is ignored once the game is known
        Assert.False(plain.IsHiddenFromCivilopedia(game, new FakeFeatures(false, false)));
    }
}
