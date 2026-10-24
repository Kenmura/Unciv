using Unciv.Models.Ruleset.Unique;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Core.Tests.Models.Ruleset.Unique;

/// <summary>
/// <c>LocalUniqueCache</c> has no Kotlin test class. The behaviour under test is what its comments promise: the unfiltered, unmultiplied
/// uniques are cached per civ/city and type, conditionals and multiplication are applied on every retrieval.
/// </summary>
public class LocalUniqueCacheUniqueTests
{
    private static (FakeCiv Civ, FakeCity City, UniqueObject CivUnique, UniqueObject CityUnique) Setup()
    {
        var civ = new FakeCiv("Rome");
        var cityTile = new FakeTile(new Unciv.Logic.Map.HexCoord(0, 0));
        var city = new FakeCity("city-1", civ, cityTile);
        var civUnique = new UniqueObject("[+10]% Strength");
        var cityUnique = new UniqueObject("[+20]% Strength");
        civ.CivUniques.Add(civUnique);
        city.LocalUniques.Add(cityUnique);
        return (civ, city, civUnique, cityUnique);
    }

    [Fact]
    public void CityUniquesComeBeforeCivUniquesAndAreCachedPerKey()
    {
        var (civ, city, civUnique, cityUnique) = Setup();
        var cache = new LocalUniqueCache();
        var first = cache.ForCityGetMatchingUniques(city, UniqueType.Strength).ToList();
        Assert.Equal(new[] { cityUnique, civUnique }, first.ToArray());
        var second = cache.ForCityGetMatchingUniques(city, UniqueType.Strength).ToList();
        Assert.Equal(first, second);
        Assert.Equal(1, city.GetLocalMatchingUniquesCalls);
        Assert.Equal(1, civ.GetMatchingUniquesCalls);

        // another type is another key
        Assert.Empty(cache.ForCityGetMatchingUniques(city, UniqueType.StatsFromObject));
        Assert.Equal(2, city.GetLocalMatchingUniquesCalls);
    }

    [Fact]
    public void CivUniquesAreSharedBetweenCities()
    {
        var (civ, city, civUnique, _) = Setup();
        var otherCity = new FakeCity("city-2", civ, new FakeTile(new Unciv.Logic.Map.HexCoord(1, 0)));
        var cache = new LocalUniqueCache();
        cache.ForCityGetMatchingUniques(city, UniqueType.Strength).ToList();
        var other = cache.ForCityGetMatchingUniques(otherCity, UniqueType.Strength).ToList();
        Assert.Equal(new[] { civUnique }, other.ToArray());
        Assert.Equal(1, civ.GetMatchingUniquesCalls);
    }

    [Fact]
    public void ACacheThatIsSwitchedOffAsksEveryTime()
    {
        var (civ, city, _, _) = Setup();
        var cache = new LocalUniqueCache(cache: false);
        cache.ForCivGetMatchingUniques(civ, UniqueType.Strength).ToList();
        cache.ForCivGetMatchingUniques(civ, UniqueType.Strength).ToList();
        Assert.Equal(2, civ.GetMatchingUniquesCalls);
        cache.ForCityGetMatchingUniques(city, UniqueType.Strength).ToList();
        cache.ForCityGetMatchingUniques(city, UniqueType.Strength).ToList();
        Assert.Equal(2, city.GetLocalMatchingUniquesCalls);
    }

    [Fact]
    public void ExplicitGameContextIsUsedForConditionalsAndTheDefaultIsTheOwnersState()
    {
        var (civ, _, civUnique, _) = Setup();
        var conditional = new UniqueObject("[+30]% Strength <when attacking>");
        civ.CivUniques.Add(conditional);
        var cache = new LocalUniqueCache();
        // IgnoreConditionals lets the conditional unique through without evaluating it
        Assert.Equal(new[] { civUnique, conditional }, cache.ForCivGetMatchingUniques(civ, UniqueType.Strength, GameContext.IgnoreConditionalsState).ToArray());
        // The cached list is the unfiltered one: a second call with the default state still has both uniques to evaluate
        // (and fails here only because Conditionals are T028's)
        Assert.Throws<NotImplementedException>(() => cache.ForCivGetMatchingUniques(civ, UniqueType.Strength).ToList());
        Assert.Equal(1, civ.GetMatchingUniquesCalls);
    }
}
