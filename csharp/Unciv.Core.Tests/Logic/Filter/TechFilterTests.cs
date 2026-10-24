using Unciv.Core.Tests.Support;
using Unciv.Models.Ruleset.Unique;

namespace Unciv.Core.Tests.Logic.Filter;

// T103: port of tests/src/com/unciv/logic/filter/TechFilterTests.kt (Technology and Era filters).
[Collection("RulesetCache")]
public class TechFilterTests
{
    private readonly TestGame game = new();
    private readonly Unciv.Logic.Civilization.Civilization civ;

    public TechFilterTests()
    {
        game.MakeHexagonalMap(3);
        civ = game.AddCiv();
    }

    [Fact]
    public void TestEraMatchesFilter()
    {
        var eraTests = new (string Era, (string Filter, bool Expected)[] Tests)[]
        {
            ("Ancient era",
            [
                ("any era", true),
                ("Ancient era", true),
                ("Industrial era", false),
                ("Starting Era", true),
                ("pre-[Industrial era]", true),
                ("pre-[Ancient era]", false),
                ("post-[Ancient era]", false),
                ("Invalid Filter", false),
            ]),
            ("Industrial era",
            [
                ("any era", true),
                ("Ancient era", false),
                ("Industrial era", true),
                ("Starting Era", false),
                ("post-[Ancient era]", true),
                ("post-[Classical era]", true),
                ("post-[Industrial era]", false),
                ("pre-[Industrial era]", false),
                ("pre-[Modern era]", true),
                ("pre-[Classical era]", false),
                ("pre-[Invalid era]", false),
                ("post-[Invalid era]", false),
            ]),
        };

        var state = new GameContext(civ);
        var failures = new List<string>();
        foreach (var (eraName, tests) in eraTests)
        {
            var era = game.Ruleset.Eras[eraName];
            foreach (var (filter, expected) in tests)
            {
                var actual = era.MatchesFilter(filter, state);
                if (actual != expected) failures.Add($"Testing that `{era.Name}` matchesFilter `{filter}`: expected {expected} but was {actual}");
            }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void TestTechMatchesFilter()
    {
        var testTechs = new[] { "Agriculture", "Pottery", "Sailing", "Animal Husbandry", "Optics" };
        var techObjects = testTechs.Select(it => game.Ruleset.Technologies[it]).ToList();
        var filters = new (string Filter, string[] Names, int Count)[]
        {
            ("All", ["Agriculture", "Pottery", "Sailing", "Animal Husbandry", "Optics"], 5),
            ("Ancient era", ["Sailing", "Agriculture", "Pottery", "Animal Husbandry"], 4),
            ("Classical era", ["Optics"], 1),
            ("Modern era", [], 0),
            ("Starting tech", ["Agriculture"], 1),
            ("Pottery", ["Pottery"], 1),
            ("Archery", [], 0),
        };
        var failures = new List<string>();
        foreach (var (filter, names, count) in filters)
        {
            var filtered = techObjects.Where(it => it.MatchesFilter(filter)).ToList();
            var filteredNames = filtered.Select(it => it.Name).ToList();
            if (!names.All(filteredNames.Contains) || filtered.Count != count)
                failures.Add($"filter: {filter} got {string.Join(", ", filteredNames)}; test: {string.Join(", ", names)}");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}
