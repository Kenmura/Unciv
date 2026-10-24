using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unit;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Models.Ruleset.Validation;

/// <summary>
/// The part of Kotlin's <c>TestGame</c> (tests/src/com/unciv/testing/TestGame.kt, T035) the validator tests use: a clone of the shipped G&amp;K ruleset
/// and the <c>createXxx</c> helpers that add a uniquely named ruleset object. T035 replaces this with the real TestGame.
/// </summary>
internal sealed class ValidatorTestRuleset
{
    private int objectsCreated;

    public RulesetClass Ruleset { get; }

    public ValidatorTestRuleset()
    {
        RulesetCache.Clear();
        string jsons = JsonsPath();
        var errors = RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")));
        Assert.Empty(errors);
        Ruleset = RulesetCache.Get(BaseRuleset.Civ_V_GnK.FullName())!.Clone();
    }

    public static string JsonsPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "android", "assets", "jsons");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("android/assets/jsons not found above " + AppContext.BaseDirectory);
    }

    private T Create<T>(Unciv.Utils.LinkedHashMap<string, T> collection, T obj, string[] uniques) where T : IRulesetObject
    {
        // Kotlin: "${obj::class.simpleName}-${objectsCreated++}"
        string name = $"{typeof(T).Name}-{objectsCreated++}";
        obj.Name = name;
        obj.Uniques.AddRange(uniques);
        collection[name] = obj;
        return obj;
    }

    public Building CreateBuilding(params string[] uniques) => Create(Ruleset.Buildings, new Building(), uniques);

    public BaseUnit CreateBaseUnit(string unitType, params string[] uniques)
    {
        var unit = new BaseUnit { UnitType = unitType };
        return Create(Ruleset.Units, unit, uniques);
    }

    public BaseUnit CreateBaseUnit(params string[] uniques) => CreateBaseUnit(CreateUnitType().Name, uniques);

    public TileImprovement CreateTileImprovement(params string[] uniques) => Create(Ruleset.TileImprovements, new TileImprovement(), uniques);

    public TileResource CreateResource(params string[] uniques) => Create(Ruleset.TileResources, new TileResource(), uniques);

    public UnitType CreateUnitType(params string[] uniques) => Create(Ruleset.UnitTypes, new UnitType(), uniques);

    public Promotion CreateUnitPromotion(params string[] uniques) => Create(Ruleset.UnitPromotions, new Promotion(), uniques);
}

/// <summary>In-memory ruleset folder: nested dictionaries are folders, strings are files.</summary>
internal sealed class TestFolder(string name, Dictionary<string, object> content) : IRulesetFolder
{
    public string Name => name;
    public bool Exists => true;
    public bool IsDirectory => true;
    public IRulesetFolder Child(string childName) => content.TryGetValue(childName, out var child)
        ? child is string text ? new TestFile(childName, text) : new TestFolder(childName, (Dictionary<string, object>)child)
        : new TestMissing(childName);
    public IReadOnlyList<IRulesetFolder> List() => content.Keys.Order(StringComparer.Ordinal).Select(Child).ToList();
    public string ReadString() => throw new InvalidOperationException("a folder");
}

internal sealed class TestFile(string name, string text) : IRulesetFolder
{
    public string Name => name;
    public bool Exists => true;
    public bool IsDirectory => false;
    public IRulesetFolder Child(string childName) => new TestMissing(childName);
    public IReadOnlyList<IRulesetFolder> List() => [];
    public string ReadString() => text;
}

internal sealed class TestMissing(string name) : IRulesetFolder
{
    public string Name => name;
    public bool Exists => false;
    public bool IsDirectory => false;
    public IRulesetFolder Child(string childName) => new TestMissing(childName);
    public IReadOnlyList<IRulesetFolder> List() => [];
    public string ReadString() => throw new FileNotFoundException(name);
}
