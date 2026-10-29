using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.Utils;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Models.Ruleset;

/// <summary>
/// T034: Ruleset and RulesetCache. Kotlin has no unit test for the loader itself (ResourceTests only uses it through TestGame, T035), so the expectations are
/// derived by hand from Ruleset.kt / RulesetCache.kt, plus a smoke load of the shipped rulesets.
/// </summary>
[Collection("RulesetCache")]
public sealed class RulesetLoadTests : IDisposable
{
    public RulesetLoadTests() => RulesetCache.Clear();

    public void Dispose()
    {
        RulesetCache.Clear();
        RulesetClass.IsUniqueValid = (_, _) => true;
    }

    // region in-memory files

    private sealed class MemoryFolder(string name, Dictionary<string, object> content) : IRulesetFolder
    {
        public string Name => name;
        public bool Exists => true;
        public bool IsDirectory => true;
        public IRulesetFolder Child(string childName) => content.TryGetValue(childName, out var child)
            ? child is string text ? new MemoryFile(childName, text) : new MemoryFolder(childName, (Dictionary<string, object>)child)
            : new MissingFolder(childName);
        public IReadOnlyList<IRulesetFolder> List() => content.Keys.Order(StringComparer.Ordinal).Select(Child).ToList();
        public string ReadString() => throw new InvalidOperationException("a folder");
    }

    private sealed class MemoryFile(string name, string text) : IRulesetFolder
    {
        public string Name => name;
        public bool Exists => true;
        public bool IsDirectory => false;
        public IRulesetFolder Child(string childName) => new MissingFolder(childName);
        public IReadOnlyList<IRulesetFolder> List() => [];
        public string ReadString() => text;
    }

    private sealed class MissingFolder(string name) : IRulesetFolder
    {
        public string Name => name;
        public bool Exists => false;
        public bool IsDirectory => false;
        public IRulesetFolder Child(string childName) => new MissingFolder(childName);
        public IReadOnlyList<IRulesetFolder> List() => [];
        public string ReadString() => throw new FileNotFoundException(name);
    }

    private sealed class MemorySource(Dictionary<string, object> builtins, Dictionary<string, object> mods, Dictionary<string, object>? globalJsons = null) : IRulesetFileSource
    {
        public IRulesetFolder GetBuiltinRulesetFolder(string fullName) => new MemoryFolder("jsons", builtins).Child(fullName);
        public IRulesetFolder? GlobalJsonsFolder => globalJsons == null ? null : new MemoryFolder("jsons", globalJsons);
        public IRulesetFolder ModsFolder => new MemoryFolder("mods", mods);
    }

    private static MemoryFolder Folder(Dictionary<string, object> files) => new("jsons", files);

    private static RulesetClass Load(Dictionary<string, object> files, string name = "Test", IRulesetFolder? global = null)
    {
        var ruleset = new RulesetClass { Name = name };
        ruleset.Load(Folder(files), global);
        return ruleset;
    }

    private const string VanillaVictories = """[ { "name": "Neutral" }, { "name": "Cultural" } ]""";

    /// <summary>Puts a small vanilla stand-in in the cache for the base ruleset fallbacks.</summary>
    private static Dictionary<string, object> VanillaFiles() => new()
    {
        ["ModOptions.json"] = """{ "isBaseRuleset": true }""",
        ["VictoryTypes.json"] = VanillaVictories,
        ["Speeds.json"] = """[ { "name": "Quick" } ]""",
        ["Difficulties.json"] = """[ { "name": "Prince" } ]""",
        ["UnitTypes.json"] = """[ { "name": "Melee" }, { "name": "Ranged" } ]""",
        ["Ruins.json"] = """[ { "name": "Free Gold" } ]""",
        ["GlobalUniques.json"] = """{ "uniques": ["[+1 Food] [in all cities]"], "unitUniques": ["[+1] Sight"] }""",
        ["CityStateTypes.json"] = """[ { "name": "Cultured", "color": [1, 2, 3], "friendBonusUniques": ["[+3 Culture] [in this city]"], "allyBonusUniques": ["[+5 Culture] [in this city]"] } ]""",
    };

    private static RulesetClass PutVanilla()
    {
        var vanilla = Load(VanillaFiles(), BaseRuleset.Civ_V_Vanilla.FullName());
        RulesetCache.Put(vanilla.Name, vanilla);
        return vanilla;
    }

    // endregion

    // region LinkedHashMap

    [Fact]
    public void LinkedHashMapKeepsInsertionOrderAcrossReplaceAndRemove()
    {
        var map = new LinkedHashMap<string, int>();
        map["c"] = 1;
        map["a"] = 2;
        map["b"] = 3;
        map["c"] = 4; // replaced in place
        Assert.Equal(["c", "a", "b"], map.Keys);
        Assert.Equal([4, 2, 3], map.Values);
        Assert.True(map.Remove("a"));
        Assert.False(map.Remove("a"));
        map["d"] = 5;
        map["a"] = 6; // removed and re-added: last
        Assert.Equal(["c", "b", "d", "a"], map.Keys);
        Assert.Equal(4, map.Count);
        Assert.Throws<KeyNotFoundException>(() => map["zzz"]);
        map.Clear();
        Assert.Empty(map);
    }

    // endregion

    // region Load

    [Fact]
    public void LoadOnEmptyFolderLeavesEverythingEmpty()
    {
        var ruleset = Load([]);
        Assert.Empty(ruleset.Buildings);
        Assert.Empty(ruleset.Technologies);
        Assert.False(ruleset.ModOptions.IsBaseRuleset);
        Assert.Equal("Test", ruleset.ToString());
    }

    [Fact]
    public void LoadSetsOriginRulesetAndKeepsFileOrderAndLastDuplicateWins()
    {
        var ruleset = Load(new()
        {
            ["Buildings.json"] = """[ { "name": "Zoo" }, { "name": "Aqueduct", "cost": 5 }, { "name": "Zoo", "cost": 7 } ]""",
            ["Terrains.json"] = """[ { "name": "Grassland", "type": "Land" } ]""",
        });
        Assert.Equal(["Zoo", "Aqueduct"], ruleset.Buildings.Keys);
        Assert.Equal(7, ruleset.Buildings["Zoo"].Cost);
        Assert.All(ruleset.Buildings.Values, it => Assert.Equal("Test", it.OriginRuleset));
        Assert.Equal("Test", ruleset.Terrains["Grassland"].OriginRuleset);
    }

    [Fact]
    public void TechColumnsFillCostColumnAndOrigin()
    {
        var ruleset = Load(new()
        {
            ["Techs.json"] = """
                [
                  { "columnNumber": 0, "era": "Ancient era", "techCost": 20, "buildingCost": 30, "wonderCost": 40,
                    "techs": [ { "name": "Agriculture" }, { "name": "Pottery", "cost": 99, "prerequisites": ["Agriculture"] } ] },
                  { "columnNumber": 1, "era": "Classical era", "techCost": 50,
                    "techs": [ { "name": "Writing" } ] }
                ]
                """,
        });
        Assert.Equal(["Agriculture", "Pottery", "Writing"], ruleset.Technologies.Keys);
        Assert.Equal(20, ruleset.Technologies["Agriculture"].Cost); // cost 0 takes the column's
        Assert.Equal(99, ruleset.Technologies["Pottery"].Cost);
        Assert.Same(ruleset.TechColumns[0], ruleset.Technologies["Pottery"].Column);
        Assert.Equal("Classical era", ruleset.Technologies["Writing"].Era());
        Assert.Equal("Test", ruleset.Technologies["Writing"].OriginRuleset);
        Assert.Equal(2, ruleset.TechColumns.Count);
    }

    [Fact]
    public void ErasAreNumberedByPosition()
    {
        var ruleset = Load(new() { ["Eras.json"] = """[ { "name": "Ancient era" }, { "name": "Classical era" }, { "name": "Medieval era" } ]""" });
        Assert.Equal([0, 1, 2], ruleset.Eras.Values.Select(it => it.EraNumber));
        Assert.Equal(1, ((IGameContextRuleset)ruleset).GetEraNumber("Classical era"));
        Assert.Null(((IGameContextRuleset)ruleset).GetEraNumber("Future era"));
    }

    [Fact]
    public void PoliciesSetUpBranchesFinishersAndConflictingLocations()
    {
        var ruleset = Load(new()
        {
            ["VictoryTypes.json"] = VanillaVictories,
            ["Policies.json"] = """
                [
                  { "name": "Tradition", "priorities": { "Cultural": 3 },
                    "policies": [
                      { "name": "Aristocracy", "row": 1, "column": 1 },
                      { "name": "Legalism", "row": 1, "column": 1, "requires": ["Aristocracy"] },
                      { "name": "Tradition Complete" }
                    ] }
                ]
                """,
        });
        var branch = ruleset.PolicyBranches["Tradition"];
        Assert.Empty(branch.Requires!);
        Assert.Same(branch, branch.Branch);
        Assert.Equal(3, branch.Priorities["Cultural"]);
        Assert.Equal(["Cultural"], branch.Priorities.Keys); // VictoryTypes.json is loaded after Policies.json, see the next test
        Assert.Equal("Tradition Complete", branch.Policies[^1].Name);
        // Aristocracy was replaced by Legalism at the same location (a policy is only compared with the ones loaded before it)
        Assert.Equal(["Tradition", "Legalism", "Tradition Complete"], ruleset.Policies.Keys);
        Assert.Equal(["Tradition"], ruleset.Policies["Tradition Complete"].Requires!);
        Assert.Equal(["Aristocracy"], ruleset.Policies["Legalism"].Requires!);
        Assert.Equal("Test", ruleset.Policies["Legalism"].OriginRuleset);
    }

    [Fact]
    public void PoliciesLoadedBeforeVictoriesGetNoPriorities()
    {
        // Kotlin loads Policies.json before VictoryTypes.json, so the priorities only know the victories already present
        var ruleset = Load(new()
        {
            ["Policies.json"] = """[ { "name": "Piety", "policies": [ { "name": "Organized Religion" } ] } ]""",
            ["VictoryTypes.json"] = VanillaVictories,
        });
        Assert.Empty(ruleset.PolicyBranches["Piety"].Priorities);
    }

    [Fact]
    public void BrokenModOptionsAreLoggedAndIgnored()
    {
        var ruleset = Load(new() { ["ModOptions.json"] = "{ this is not json", ["Buildings.json"] = """[ { "name": "Zoo" } ]""" });
        Assert.False(ruleset.ModOptions.IsBaseRuleset);
        Assert.Single(ruleset.Buildings);
    }

    [Fact]
    public void BrokenOtherFileThrows()
    {
        Assert.ThrowsAny<Exception>(() => Load(new() { ["Buildings.json"] = "[ { \"name\": " }));
    }

    [Fact]
    public void ReligionsAreReadAsPlainStrings()
    {
        var ruleset = Load(new() { ["Religions.json"] = """["Buddhism", "Islam"]""", ["Nations.json"] = """[ { "name": "Rome", "outerColor": [0, 0, 0], "favoredReligion": "Islam" } ]""" });
        Assert.Equal(["Buddhism", "Islam"], ruleset.Religions);
        Assert.Equal("Rome", ruleset.Nations["Rome"].Name);
    }

    [Fact]
    public void UnquotedKeysOfTheGlobalTutorialsFileAreRead()
    {
        Assert.Equal(
            "{ \"text\": \"a: b, c: d\", \"x\" : 1 }",
            Unciv.Json.LenientJson.QuoteBareKeys("{ text: \"a: b, c: d\", x : 1 }"));
        Assert.Equal("[ { \"a\": [1, 2], /* c: */ \"b\": 3 // d: 4\n } ]", Unciv.Json.LenientJson.QuoteBareKeys("[ { a: [1, 2], /* c: */ b: 3 // d: 4\n } ]"));
        Assert.Equal("{ \"k\": true, \"j\": null }", Unciv.Json.LenientJson.QuoteBareKeys("{ k: true, j: null }"));
    }

    [Fact]
    public void GlobalTutorialsAreLoadedOnlyForBaseRulesetsAndOutsideConsoleMode()
    {
        PutVanilla();
        var global = Folder(new() { ["Tutorials.json"] = """[ { "name": "Global", "category": "Basics" } ]""" });
        var baseFiles = () => new Dictionary<string, object>
        {
            ["ModOptions.json"] = """{ "isBaseRuleset": true }""",
            ["Tutorials.json"] = """[ { "name": "Local" } ]""",
        };

        Assert.Equal(["Global", "Local"], Load(baseFiles(), global: global).Tutorials.Keys);
        Assert.Equal(["Local"], Load(baseFiles()).Tutorials.Keys); // console mode
        Assert.Equal(["Local"], Load(new() { ["Tutorials.json"] = """[ { "name": "Local" } ]""" }, global: global).Tutorials.Keys); // not a base ruleset
    }

    [Fact]
    public void BaseRulesetFallsBackToVanillaForMissingParts()
    {
        var vanilla = PutVanilla();
        var ruleset = Load(new() { ["ModOptions.json"] = """{ "isBaseRuleset": true }""" }, "MyBase");

        Assert.Equal(["Melee", "Ranged"], ruleset.UnitTypes.Keys);
        Assert.Equal(["Free Gold"], ruleset.RuinRewards.Keys);
        Assert.Equal(["Neutral", "Cultural"], ruleset.Victories.Keys);
        Assert.Equal(["Quick"], ruleset.Speeds.Keys);
        Assert.Equal(["Prince"], ruleset.Difficulties.Keys);
        Assert.Equal(["[+1 Food] [in all cities]"], ruleset.GlobalUniques.Uniques);
        Assert.Equal(["[+1] Sight"], ruleset.GlobalUniques.UnitUniques);
        // The vanilla used is a clone: changing the new ruleset does not change the cached one
        ruleset.Speeds.Clear();
        Assert.Single(vanilla.Speeds);
    }

    [Fact]
    public void FallbackCityStateTypesKeepOnlyUniquesThePolicyAccepts()
    {
        PutVanilla();
        RulesetClass.IsUniqueValid = (_, unique) => unique.Contains("[+5 Culture]", StringComparison.Ordinal);
        var ruleset = Load(new() { ["ModOptions.json"] = """{ "isBaseRuleset": true }""" }, "MyBase");

        var cultured = ruleset.CityStateTypes["Cultured"];
        Assert.Equal([1, 2, 3], cultured.Color);
        Assert.Empty(cultured.FriendBonusUniques);
        Assert.Equal(["[+5 Culture] [in this city]"], cultured.AllyBonusUniques);
    }

    [Fact]
    public void BaseRulesetWithOwnPartsDoesNotTakeTheFallbacks()
    {
        PutVanilla();
        var ruleset = Load(new()
        {
            ["ModOptions.json"] = """{ "isBaseRuleset": true }""",
            ["VictoryTypes.json"] = """[ { "name": "Mine" } ]""",
            ["Ruins.json"] = "[]",
            ["GlobalUniques.json"] = """{ "uniques": [] }""",
        }, "MyBase");
        Assert.Equal(["Mine"], ruleset.Victories.Keys);
        Assert.Empty(ruleset.RuinRewards); // the file exists, even though it is empty
        Assert.Empty(ruleset.GlobalUniques.Uniques);
        Assert.Equal("MyBase", ruleset.GlobalUniques.OriginRuleset);
    }

    // endregion

    // region Add / Clone / Clear

    private static RulesetClass Base()
    {
        PutVanilla(); // a base ruleset without parts takes them from the cached vanilla
        return LoadBase();
    }

    private static RulesetClass LoadBase() => Load(new()
    {
        ["ModOptions.json"] = """{ "isBaseRuleset": true, "uniques": ["[+1 Gold]"] }""",
        ["Buildings.json"] = """[ { "name": "Monument" }, { "name": "Temple" }, { "name": "Temple of Artemis", "isWonder": true } ]""",
        ["Religions.json"] = """["Buddhism", "Islam"]""",
        ["Nations.json"] = """[ { "name": "Rome", "outerColor": [0, 0, 0], "favoredReligion": "Islam" }, { "name": "Greece", "outerColor": [0, 0, 0], "favoredReligion": "Buddhism" }, { "name": "Persia", "outerColor": [0, 0, 0] } ]""",
        ["Techs.json"] = """[ { "columnNumber": 0, "era": "Ancient era", "techCost": 10, "buildingCost": 11, "wonderCost": 12, "techs": [ { "name": "Masonry" }, { "name": "Writing" } ] } ]""",
        ["Policies.json"] = """
            [ { "name": "Tradition", "policies": [ { "name": "Aristocracy", "row": 1 }, { "name": "Oligarchy", "row": 2 }, { "name": "Tradition Complete", "row": 3 } ] },
              { "name": "Liberty", "policies": [ { "name": "Citizenship", "row": 1 }, { "name": "Liberty Complete", "row": 2 } ] } ]
            """,
        ["Units.json"] = """[ { "name": "Warrior", "unitType": "Melee" }, { "name": "Archer", "unitType": "Ranged" } ]""",
        ["Eras.json"] = """[ { "name": "Ancient era" } ]""",
    }, "Base");

    [Fact]
    public void AddWithoutRemovalsPutsAllInOrderAndReplacesInPlace()
    {
        var ruleset = new RulesetClass();
        ruleset.Add(Base());
        var mod = Load(new()
        {
            ["Buildings.json"] = """[ { "name": "Temple", "cost": 3 }, { "name": "Harbor" } ]""",
            ["Techs.json"] = """[ { "columnNumber": 1, "era": "Classical era", "techCost": 20, "techs": [ { "name": "Currency" } ] } ]""",
            ["ModOptions.json"] = """{ "uniques": ["[+2 Gold]"] }""",
        }, "Mod");
        ruleset.Add(mod);

        Assert.Equal(["Monument", "Temple", "Temple of Artemis", "Harbor"], ruleset.Buildings.Keys);
        Assert.Equal(3, ruleset.Buildings["Temple"].Cost);
        Assert.Equal(["Masonry", "Writing", "Currency"], ruleset.Technologies.Keys);
        Assert.Equal(2, ruleset.TechColumns.Count);
        Assert.Equal(["[+1 Gold]", "[+2 Gold]"], ruleset.ModOptions.Uniques);
        Assert.Equal(["Buddhism", "Islam"], ruleset.Religions);
    }

    [Fact]
    public void AddRemovesByNameFilterAndAll()
    {
        var ruleset = new RulesetClass();
        ruleset.Add(Base());
        ruleset.Add(Load(new() { ["ModOptions.json"] = """{ "buildingsToRemove": ["Monument", "Wonder"], "unitsToRemove": ["Warrior"], "nationsToRemove": ["Persia"] }""" }));

        Assert.Equal(["Temple"], ruleset.Buildings.Keys);
        Assert.Equal(["Archer"], ruleset.Units.Keys);
        Assert.Equal(["Rome", "Greece"], ruleset.Nations.Keys);

        ruleset.Add(Load(new() { ["ModOptions.json"] = """{ "buildingsToRemove": ["All"], "techsToRemove": ["Masonry"], "erasToRemove": ["x"] }""" }));
        Assert.Empty(ruleset.Buildings);
        Assert.Equal(["Writing"], ruleset.Technologies.Keys);
    }

    [Fact]
    public void AddRemovingReligionsClearsFavoriteOfNations()
    {
        var ruleset = new RulesetClass();
        ruleset.Add(Base());
        ruleset.Add(Load(new() { ["ModOptions.json"] = """{ "religionsToRemove": ["Islam"] }""", ["Religions.json"] = """["Taoism"]""" }));

        Assert.Equal(["Buddhism", "Taoism"], ruleset.Religions);
        Assert.Null(ruleset.Nations["Rome"].FavoredReligion);
        Assert.Equal("Buddhism", ruleset.Nations["Greece"].FavoredReligion);
        Assert.Null(ruleset.Nations["Persia"].FavoredReligion);
    }

    [Fact]
    public void AddRemovingAPolicyBranchRemovesItsPolicies()
    {
        var ruleset = new RulesetClass();
        ruleset.Add(Base());
        ruleset.Add(Load(new() { ["ModOptions.json"] = """{ "policyBranchesToRemove": ["Tradition"] }""" }));

        Assert.Equal(["Liberty"], ruleset.PolicyBranches.Keys);
        // Upstream removes the branch's child policies, but not the branch's own entry in the policies map
        Assert.Equal(["Tradition", "Liberty", "Citizenship", "Liberty Complete"], ruleset.Policies.Keys);
    }

    [Fact]
    public void AddOverridingABranchReplacesItsPoliciesAndDropsRemovedOnesFromBranches()
    {
        var ruleset = new RulesetClass();
        ruleset.Add(Base());
        ruleset.Add(Load(new()
        {
            ["ModOptions.json"] = """{ "policiesToRemove": ["Citizenship"] }""",
            ["Policies.json"] = """[ { "name": "Tradition", "policies": [ { "name": "Monarchy", "row": 1 }, { "name": "Tradition Complete", "row": 2 } ] } ]""",
        }, "Mod"));

        Assert.Equal(["Liberty", "Tradition"], ruleset.PolicyBranches.Keys); // Tradition was removed because it is overridden, then put again
        Assert.DoesNotContain("Aristocracy", ruleset.Policies.Keys);
        Assert.Contains("Monarchy", ruleset.Policies.Keys);
        Assert.DoesNotContain("Citizenship", ruleset.Policies.Keys);
        Assert.DoesNotContain(ruleset.PolicyBranches["Liberty"].Policies, it => it.Name == "Citizenship");
    }

    [Fact]
    public void AddWithNothingToMergeSkipsPolicies()
    {
        var ruleset = new RulesetClass();
        ruleset.Add(Base());
        var before = ruleset.Policies.Keys.ToList();
        ruleset.Add(Load([]));
        Assert.Equal(before, ruleset.Policies.Keys);
    }

    [Fact]
    public void AddCollectsModsAndConstants()
    {
        var ruleset = new RulesetClass();
        var a = Load([], "A");
        a.Mods.Add("A");
        var b = Load(new() { ["ModOptions.json"] = """{ "constants": { "maxXPfromBarbarians": 99 } }""" }, "B");
        b.Mods.Add("B");
        ruleset.Add(a);
        ruleset.Add(b);
        Assert.Equal(["A", "B"], ruleset.Mods);
        Assert.Equal(99, ruleset.ModOptions.Constants.MaxXPfromBarbarians);
    }

    [Fact]
    public void CloneCopiesNameAndBaseFlagAndIsIndependent()
    {
        var original = Base();
        var clone = original.Clone();
        Assert.Equal("Base", clone.Name);
        Assert.True(clone.ModOptions.IsBaseRuleset);
        Assert.Equal(original.Buildings.Keys, clone.Buildings.Keys);
        clone.Buildings.Clear();
        Assert.Equal(3, original.Buildings.Count);
    }

    [Fact]
    public void ClearEmptiesEverything()
    {
        var ruleset = Base();
        ruleset.Mods.Add("x");
        ruleset.Clear();
        Assert.Empty(ruleset.Buildings);
        Assert.Empty(ruleset.Technologies);
        Assert.Empty(ruleset.TechColumns);
        Assert.Empty(ruleset.Religions);
        Assert.Empty(ruleset.Policies);
        Assert.Empty(ruleset.Mods);
        Assert.Empty(ruleset.GlobalUniques.Uniques);
    }

    // endregion

    // region Era and Technology filters

    [Fact]
    public void EraFilterWithoutAGameKnowsOnlyAnyEraAndItsName()
    {
        var era = new Unciv.Models.Ruleset.Tech.Era { Name = "Classical era", EraNumber = 1 };
        Assert.True(era.MatchesFilter("any era"));
        Assert.True(era.MatchesFilter("Classical era"));
        Assert.False(era.MatchesFilter("Starting Era"));
        Assert.False(era.MatchesFilter("pre-[Medieval era]"));
        Assert.True(era.MatchesFilter("{any era} {Classical era}"));
        Assert.False(era.MatchesFilter("{any era} {Medieval era}"));
        Assert.True(era.MatchesFilter("non-[Medieval era]"));
        Assert.False(era.MatchesFilter("non-[Classical era]", multiFilter: false)); // without multiFilter the term is just an unknown name
    }

    [Fact]
    public void EraFilterReadsStartingEraAndEraNumbersFromTheState()
    {
        var gameInfo = new Unciv.Core.Tests.Models.Ruleset.Unique.FakeGameInfo();
        gameInfo.GameParameters.StartingEra = "Classical era";
        gameInfo.FakeRulesetData.EraNumbers["Medieval era"] = 2;
        gameInfo.FakeRulesetData.EraNumbers["Ancient era"] = 0;
        var state = new GameContext(gameInfo: gameInfo);
        var era = new Unciv.Models.Ruleset.Tech.Era { Name = "Classical era", EraNumber = 1 };

        Assert.True(era.MatchesFilter("Starting Era", state));
        Assert.True(era.MatchesFilter("pre-[Medieval era]", state));
        Assert.False(era.MatchesFilter("pre-[Ancient era]", state));
        Assert.True(era.MatchesFilter("post-[Ancient era]", state));
        Assert.False(era.MatchesFilter("post-[Medieval era]", state));
        Assert.False(era.MatchesFilter("pre-[Unknown era]", state)); // no such era
        Assert.False(era.MatchesFilter("nonsense", state));
        Assert.False(new Unciv.Models.Ruleset.Tech.Era { Name = "Medieval era" }.MatchesFilter("Starting Era", state));
    }

    [Fact]
    public void TechnologyFilterMatchesAllNameEraAndUniques()
    {
        var ruleset = Load(new()
        {
            ["Techs.json"] = """[ { "columnNumber": 0, "era": "Ancient era", "techCost": 10, "techs": [ { "name": "Masonry", "uniques": ["Tagged"] } ] } ]""",
        });
        var tech = ruleset.Technologies["Masonry"];
        Assert.True(tech.MatchesFilter("All"));
        Assert.True(tech.MatchesFilter("all"));
        Assert.True(tech.MatchesFilter("Masonry"));
        Assert.True(tech.MatchesFilter("Ancient era"));
        Assert.True(tech.MatchesFilter("Tagged"));
        Assert.False(tech.MatchesFilter("Other"));
        Assert.True(tech.MatchesFilter("{Masonry} {Tagged}"));
        Assert.False(tech.MatchesFilter("{Masonry} {Other}"));
        Assert.False(tech.MatchesFilter("{Masonry} {Tagged}", multiFilter: false));
    }

    // endregion

    // region views and caches

    [Fact]
    public void ParameterViewKeepsOrderAndLooksUpByName()
    {
        var view = (IRulesetParameterView)Base();
        Assert.Equal(["Monument", "Temple", "Temple of Artemis"], view.Buildings.Keys);
        Assert.True(view.Buildings.ContainsKey("Temple"));
        Assert.Equal("Temple", view.Buildings["Temple"]!.Name);
        Assert.Null(view.Buildings["Nope"]);
        Assert.Equal(["Warrior", "Archer"], view.Units.Values.Select(it => it.Name));
        Assert.Equal(["Buddhism", "Islam"], view.Religions);
        Assert.Equal(["Ancient era"], view.Eras.Keys);
    }

    [Fact]
    public void TechViewConstructionViewAndFeatureView()
    {
        var ruleset = Base();
        Assert.Equal("Masonry", ruleset.GetTechnology("Masonry")!.Name);
        Assert.Null(ruleset.GetTechnology("Nope"));
        Assert.Equal("Ancient era", ruleset.GetEra("Ancient era")!.Name);
        Assert.Null(ruleset.GetEra(null));
        var construction = (IRulesetConstructionView)ruleset;
        Assert.Equal("Warrior", construction.GetUnit("Warrior")!.Name);
        Assert.Equal(["Warrior", "Archer"], construction.GetAllUnits().Select(it => it.Name));
        Assert.Equal("Melee", construction.GetUnitType("Melee")!.Name);
        Assert.Null(construction.GetUnitType("Nope"));
        Assert.Null(construction.GetPromotion("x"));
        Assert.Null(construction.GetTileImprovement("x"));
        Assert.True(construction.TechnologyMatchesFilter("Masonry", "Masonry", null, true));
        Assert.False(construction.TechnologyMatchesFilter("Masonry", "Writing", null, true));
        Assert.Null(construction.TechnologyMatchesFilter("Nope", "Masonry", null, true));
        var features = (IRulesetFeatureView)ruleset;
        Assert.False(features.HasBeliefs);
        Assert.False(features.HasSpyNames);
        Assert.Equal(["Monument"], ((IGameContextRuleset)ruleset).GetBuildingNamesMatchingFilter("Monument"));
        Assert.Equal(["Temple of Artemis"], ((IGameContextRuleset)ruleset).GetBuildingNamesMatchingFilter("Wonder"));
    }

    [Fact]
    public void GetGameResourceFindsStatSubStatOrTileResource()
    {
        var ruleset = Load(new() { ["TileResources.json"] = """[ { "name": "Coal", "resourceType": "Strategic" } ]""" });
        Assert.Equal(Stat.Gold, ruleset.GetGameResource("Gold"));
        Assert.Equal(SubStat.GoldenAgePoints, ruleset.GetGameResource("Golden Age points"));
        Assert.Same(ruleset.TileResources["Coal"], ruleset.GetGameResource("Coal"));
        Assert.Null(ruleset.GetGameResource("Unobtainium"));
    }

    [Fact]
    public void SelectableVictoriesExcludeTheWillNotBeChosenOnes()
    {
        var ruleset = Load(new() { ["VictoryTypes.json"] = """[ { "name": "Neutral" }, { "name": "Hidden", "uniques": ["Will not be chosen for new games"] } ]""" });
        Assert.Equal(["Neutral"], ruleset.SelectableVictories().Select(it => it.Name));
    }

    [Fact]
    public void ResourceImprovementsAreCachedFromTheRuleset()
    {
        var ruleset = Load(new()
        {
            ["TileResources.json"] = """[ { "name": "Iron", "improvement": "Mine", "improvedBy": ["Quarry"] }, { "name": "Wheat", "improvedBy": ["Farm"] } ]""",
            ["TileImprovements.json"] = """
                [ { "name": "Mine" }, { "name": "Quarry" }, { "name": "Farm" },
                  { "name": "Super Mine", "replaces": "Mine" },
                  { "name": "Plantation", "uniques": ["Improves [Iron] resource in this tile"] } ]
                """,
        });
        ruleset.UpdateResourceTransients();
        var iron = ruleset.TileResources["Iron"];
        Assert.Equal(["Mine", "Quarry", "Super Mine", "Plantation"], iron.GetImprovements());
        Assert.True(iron.IsImprovedBy("Plantation"));
        Assert.True(iron.IsImprovedBy(ruleset.TileImprovements["Mine"]));
        Assert.False(iron.IsImprovedBy("Farm"));
        Assert.Equal(["Farm"], ruleset.TileResources["Wheat"].GetImprovements());
    }

    [Fact]
    public void ResourceImprovementsNeedARuleset()
    {
        Assert.Throws<InvalidOperationException>(() => new Unciv.Models.Ruleset.Tile.TileResource().GetImprovements());
    }

    [Fact]
    public void RoadAndRemovalCaches()
    {
        var ruleset = Load(new()
        {
            ["TileImprovements.json"] = """[ { "name": "Road" }, { "name": "Railroad" }, { "name": "Remove Road" }, { "name": "Remove Forest" }, { "name": "Remove Railroad" }, { "name": "Mine" } ]""",
        });
        Assert.Equal("Road", ruleset.RoadImprovement!.Name);
        Assert.Equal("Railroad", ruleset.RailroadImprovement!.Name);
        Assert.Equal(["Remove Road", "Remove Forest", "Remove Railroad"], ruleset.TileRemovals.Select(it => it.Name));
        Assert.Equal(["Remove Forest"], ruleset.NonRoadTileRemovals.Select(it => it.Name));
        Assert.Null(Load([]).RoadImprovement);
    }

    [Fact]
    public void GreatGeneralUnitsAndHappinessLevels()
    {
        var ruleset = Load(new()
        {
            ["Units.json"] = """[ { "name": "Great General", "unitType": "Civilian", "uniques": ["Great Person - [War]", "Bonus as Great General for [15]% [Strength] when stacked with [Military] units"] }, { "name": "Warrior", "unitType": "Melee" } ]""",
            ["Buildings.json"] = """
                [ { "name": "A", "uniques": ["[+1 Culture] <when above [5] [Happiness]>", "[+1 Culture] <when below [3] [Happiness]>", "[+1 Culture] <when between [7] and [9] [Happiness]>", "[+1 Culture] <when above [5] [Gold]>"] },
                  { "name": "B", "uniques": ["[+1 Culture] <while the empire is happy>"] } ]
                """,
        });
        Assert.Equal([5, 3, 7, 10, 0], ruleset.AllHappinessLevelsThatAffectUniques);
        Assert.Empty(ruleset.GreatGeneralUnits); // needs the unique GreatPersonFromCombat, which these are not
    }

    [Fact]
    public void ToStringAndSummary()
    {
        PutVanilla();
        var combined = RulesetCache.GetComplexRuleset(new LinkedHashSet<string>());
        Assert.Equal("", combined.Name);
        Assert.Equal(BaseRuleset.Civ_V_Vanilla.FullName(), combined.ToString()); // one base ruleset
        var two = new RulesetClass();
        two.Mods.Add(BaseRuleset.Civ_V_Vanilla.FullName());
        two.Mods.Add("Mod");
        Assert.Equal("Combined RuleSet ([Civ V - Vanilla, Mod])", two.ToString());

        var ruleset = Base();
        Assert.Equal("Base Ruleset, 2 Techs, 3 Nations, 2 Units, 3 Buildings, 2 Religions", ruleset.GetSummary());
    }

    [Fact]
    public void RulesetFilesCoverUniquesAndNamesInDeclarationOrder()
    {
        Assert.Equal("Beliefs.json", RulesetFile.Values[0].Filename);
        Assert.Equal("Ruins.json", RulesetFile.Values[^1].Filename);
        Assert.Equal(26, RulesetFile.Values.Count);

        var ruleset = Load(new()
        {
            ["Buildings.json"] = """[ { "name": "Zoo", "uniques": ["[+1 Culture]"] } ]""",
            ["Religions.json"] = """["Buddhism"]""",
            ["Nations.json"] = """[ { "name": "Rome", "outerColor": [0, 0, 0], "leaderName": "Caesar", "cities": ["Roma"], "spyNames": ["Spy 1"], "uniques": ["[+2 Culture]"] } ]""",
            ["ModOptions.json"] = """{ "uniques": ["[+3 Culture]"] }""",
        });
        Assert.Equal(["[+1 Culture]", "[+2 Culture]", "[+3 Culture]"], ruleset.AllUniques().Select(it => it.Text));
        Assert.Equal(["Zoo", "Rome", "Global Uniques"], ruleset.AllRulesetObjects().Select(it => it.Name));
        Assert.Equal(
            [("Zoo", "Building", "Test"), ("Buddhism", "Religion", "Test"), ("Rome", "Nation", "Test"), ("Caesar", "Nation.leaderName", "Test"),
             ("Roma", "Nation.cities", "Test"), ("Spy 1", "Nation.spyNames", "Test"), ("Global Uniques", "GlobalUniques", "Test")],
            ruleset.AllNames().Select(it => (it.Name, it.Source, it.OriginRuleset)));
    }

    // endregion

    // region RulesetCache

    private static Dictionary<string, object> BaseFiles(string unitName)
    {
        var files = VanillaFiles(); // complete, so that loading it needs no cached vanilla
        files["Units.json"] = $$"""[ { "name": "{{unitName}}", "unitType": "Melee" } ]""";
        return files;
    }

    [Fact]
    public void LoadRulesetsLoadsBuiltinsAndModsAndReportsBrokenOnes()
    {
        var builtins = new Dictionary<string, object>
        {
            [BaseRuleset.Civ_V_Vanilla.FullName()] = BaseFiles("Vanilla Unit"),
            [BaseRuleset.Civ_V_GnK.FullName()] = BaseFiles("GnK Unit"),
            [BaseRuleset.Civ_V_BNW.FullName()] = BaseFiles("BNW Unit"),
        };
        var mods = new Dictionary<string, object>
        {
            ["Zeta"] = new Dictionary<string, object> { ["jsons"] = new Dictionary<string, object> { ["Buildings.json"] = """[ { "name": "Z" } ]""" } },
            ["Alpha"] = new Dictionary<string, object> { ["jsons"] = new Dictionary<string, object> { ["Buildings.json"] = """[ { "name": "A" } ]""" } },
            [".hidden"] = new Dictionary<string, object> { ["jsons"] = new Dictionary<string, object>() },
            ["Broken"] = new Dictionary<string, object> { ["jsons"] = new Dictionary<string, object> { ["Buildings.json"] = "[ { \"name\": " } },
            ["readme.txt"] = "not a folder",
            ["OtherBase"] = new Dictionary<string, object> { ["jsons"] = BaseFiles("Other Unit") },
        };
        RulesetCache.Put("Stale", new RulesetClass { Name = "Stale" });

        var errors = RulesetCache.LoadRulesets(new MemorySource(builtins, mods));

        Assert.Equal("Exception loading mod 'Broken':", errors[0]);
        Assert.StartsWith("  ", errors[1]);
        Assert.True(errors.Count >= 2); // the cause is added when its message mentions a line, even when it is the exception itself (as upstream)
        Assert.False(RulesetCache.ContainsKey("Stale")); // replaced as a whole
        Assert.False(RulesetCache.ContainsKey("Broken"));
        Assert.False(RulesetCache.ContainsKey(".hidden"));
        Assert.False(RulesetCache.ContainsKey("readme.txt"));
        Assert.Equal(6, RulesetCache.Count);
        Assert.Equal(["Vanilla Unit"], RulesetCache.Get(BaseRuleset.Civ_V_Vanilla.FullName())!.Units.Keys);
        var alpha = RulesetCache.Get("Alpha")!;
        Assert.Equal("Alpha", alpha.Name);
        Assert.NotNull(alpha.FolderLocation);
        Assert.Null(RulesetCache.Get(BaseRuleset.Civ_V_Vanilla.FullName())!.FolderLocation);
        Assert.Equal(["Civ V - Vanilla", "Civ V - Gods & Kings", "Civ V - Brave New World", "OtherBase"], RulesetCache.GetSortedBaseRulesets());
    }

    [Fact]
    public void LoadRulesetsLoadsBnwWhenItsFolderExistsAndSortsItAfterGnK()
    {
        var builtins = new Dictionary<string, object>
        {
            [BaseRuleset.Civ_V_Vanilla.FullName()] = BaseFiles("V"),
            [BaseRuleset.Civ_V_GnK.FullName()] = BaseFiles("G"),
            [BaseRuleset.Civ_V_BNW.FullName()] = BaseFiles("B"),
        };
        Assert.Empty(RulesetCache.LoadRulesets(new MemorySource(builtins, []), noMods: true));
        Assert.Equal(3, RulesetCache.Count);
        Assert.Equal(["B"], RulesetCache.Get(BaseRuleset.Civ_V_BNW.FullName())!.Units.Keys);
        Assert.Equal(["Civ V - Vanilla", "Civ V - Gods & Kings", "Civ V - Brave New World"], RulesetCache.GetSortedBaseRulesets());
    }

    [Fact]
    public void LoadRulesetsNoModsIgnoresTheModsFolder()
    {
        var builtins = new Dictionary<string, object>
        {
            [BaseRuleset.Civ_V_Vanilla.FullName()] = BaseFiles("V"),
            [BaseRuleset.Civ_V_GnK.FullName()] = BaseFiles("G"),
            [BaseRuleset.Civ_V_BNW.FullName()] = BaseFiles("B"),
        };
        var mods = new Dictionary<string, object> { ["Alpha"] = new Dictionary<string, object> { ["jsons"] = new Dictionary<string, object>() } };
        Assert.Empty(RulesetCache.LoadRulesets(new MemorySource(builtins, mods), noMods: true));
        Assert.Equal(3, RulesetCache.Count);
    }

    [Fact]
    public void ReloadSingleRulesetReplacesOrReports()
    {
        var mods = new Dictionary<string, object>
        {
            ["Alpha"] = new Dictionary<string, object> { ["jsons"] = new Dictionary<string, object> { ["Buildings.json"] = """[ { "name": "A2" } ]""" } },
            ["Bad"] = new Dictionary<string, object> { ["jsons"] = new Dictionary<string, object> { ["Buildings.json"] = "nonsense" } },
            ["file"] = "x",
        };
        var source = new MemorySource([], mods);
        RulesetCache.Put("Alpha", new RulesetClass { Name = "Alpha" });

        Assert.Equal(["No such mod"], RulesetCache.ReloadSingleRuleset(source, "Missing"));
        Assert.Equal(["No such mod"], RulesetCache.ReloadSingleRuleset(source, "file"));
        Assert.Empty(RulesetCache.ReloadSingleRuleset(source, "Alpha"));
        Assert.Equal(["A2"], RulesetCache.Get("Alpha")!.Buildings.Keys);
        Assert.NotEmpty(RulesetCache.ReloadSingleRuleset(source, "Bad"));
        Assert.False(RulesetCache.ContainsKey("Bad"));
    }

    [Fact]
    public void SortedBaseRulesetsPutBuiltinsFirstThenAlphabetical()
    {
        Assert.Empty(RulesetCache.GetSortedBaseRulesets());
        void Add(string name, bool isBase) =>
            RulesetCache.Put(name, new RulesetClass { Name = name, ModOptions = { IsBaseRuleset = isBase } });
        Add("Zed", true);
        Add("Civ V - Gods & Kings", true);
        Add("Mod", false);
        Add("Abc", true);
        Add("Civ V - Vanilla", true);
        Assert.Equal(["Civ V - Vanilla", "Civ V - Gods & Kings", "Abc", "Zed"], RulesetCache.GetSortedBaseRulesets());
    }

    [Fact]
    public void ComplexRulesetCombinesBaseThenModsInGivenOrderAndUpdatesBuildingCosts()
    {
        var vanilla = Base();
        vanilla.Name = BaseRuleset.Civ_V_Vanilla.FullName();
        RulesetCache.Put(vanilla.Name, vanilla);
        var modA = Load(new()
        {
            ["Buildings.json"] = """[ { "name": "Library", "requiredTech": "Writing" }, { "name": "Colossus", "isWonder": true, "requiredTech": "Masonry" }, { "name": "Fixed", "cost": 77, "requiredTech": "Masonry" }, { "name": "Free" } ]""",
            ["ModOptions.json"] = """{ "uniques": ["[+9 Gold]"], "tileset": "ModTiles" }""",
        }, "ModA");
        var modB = Load(new() { ["Buildings.json"] = """[ { "name": "Library", "cost": 5 } ]""" }, "ModB");
        var otherBase = Load(new() { ["ModOptions.json"] = """{ "isBaseRuleset": true }""" }, "OtherBase");
        RulesetCache.Put("ModA", modA);
        RulesetCache.Put("ModB", modB);
        RulesetCache.Put("OtherBase", otherBase);

        var combined = RulesetCache.GetComplexRuleset(new LinkedHashSet<string>(["ModA", "Unloaded", "OtherBase", "ModB"]), "Nonexistent base");

        Assert.Equal("", combined.Name);
        Assert.Equal([BaseRuleset.Civ_V_Vanilla.FullName(), "ModA", "ModB"], combined.Mods); // the fallback base, then the mods; OtherBase and Unloaded are dropped
        Assert.True(combined.ModOptions.IsBaseRuleset);
        // Building costs: -1 takes the cost of the column of the required tech; wonders use wonderCost; Unbuildable/techless ones stay
        Assert.Equal(5, combined.Buildings["Library"].Cost); // overwritten by ModB, with an explicit cost
        Assert.Equal(12, combined.Buildings["Colossus"].Cost);
        Assert.Equal(77, combined.Buildings["Fixed"].Cost);
        Assert.Equal(-1, combined.Buildings["Free"].Cost);
        // the base ruleset's uniques are not shared by reference with the combined one
        Assert.Equal(["[+1 Gold]", "[+9 Gold]"], combined.ModOptions.Uniques);
        Assert.Equal(["[+1 Gold]"], vanilla.ModOptions.Uniques);
    }

    [Fact]
    public void ComplexRulesetUsesTheNamedBaseRulesetAndGameParameters()
    {
        PutVanilla();
        var gnk = Load(new() { ["ModOptions.json"] = """{ "isBaseRuleset": true, "tileset": "GnkTiles" }""", ["Buildings.json"] = """[ { "name": "Shrine" } ]""" }, BaseRuleset.Civ_V_GnK.FullName());
        RulesetCache.Put(gnk.Name, gnk);
        var mod = Load(new() { ["Buildings.json"] = """[ { "name": "Modded" } ]""" }, "Mod");
        RulesetCache.Put("Mod", mod);

        var parameters = new GameParameters { BaseRuleset = gnk.Name, Mods = new LinkedHashSet<string>(["Mod"]) };
        var combined = RulesetCache.GetComplexRuleset(parameters);
        Assert.Equal(["Shrine", "Modded"], combined.Buildings.Keys);
        Assert.Equal("GnkTiles", combined.ModOptions.Tileset);
        Assert.Equal([gnk.Name, "Mod"], combined.Mods);

        // A "mod" that is a base ruleset is not a valid base either: falls back to vanilla
        var fallback = RulesetCache.GetComplexRuleset(new LinkedHashSet<string>(), "Mod");
        Assert.Equal([BaseRuleset.Civ_V_Vanilla.FullName()], fallback.Mods);
    }

    [Fact]
    public void GetVanillaRulesetIsAClone()
    {
        var vanilla = PutVanilla();
        var clone = RulesetCache.GetVanillaRuleset();
        Assert.NotSame(vanilla, clone);
        Assert.Equal(vanilla.Name, clone.Name);
        Assert.True(clone.ModOptions.IsBaseRuleset);
    }

    [Fact]
    public void ThresholdDefault()
    {
        Assert.Equal(0.15, RulesetCache.UniqueMisspellingThreshold);
    }

    // endregion

    // region shipped rulesets

    private static string JsonsPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "android", "assets", "jsons");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("android/assets/jsons not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void ShippedRulesetsLoadAndCombine()
    {
        var errors = RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(JsonsPath(), Path.Combine(JsonsPath(), "no mods here")));
        Assert.Empty(errors);

        var vanilla = RulesetCache.Get(BaseRuleset.Civ_V_Vanilla.FullName())!;
        var gnk = RulesetCache.Get(BaseRuleset.Civ_V_GnK.FullName())!;
        Assert.Equal(74, vanilla.Technologies.Count);
        Assert.Equal(["Ancient era", "Classical era", "Medieval era", "Renaissance era", "Industrial era", "Modern era", "Atomic era", "Information era", "Future era"], vanilla.Eras.Keys);
        Assert.Equal(Enumerable.Range(0, vanilla.Eras.Count), vanilla.Eras.Values.Select(it => it.EraNumber));
        Assert.Equal(11, gnk.Religions.Count);
        Assert.Equal(62, vanilla.Nations.Count);
        Assert.True(vanilla.Tutorials.Count > 0); // the global Tutorials.json, with its unquoted keys
        Assert.True(vanilla.Buildings.Values.All(it => it.OriginRuleset == vanilla.Name));
        Assert.Equal(vanilla.Name, vanilla.TileResources.Values.First().OriginRuleset);
        Assert.True(vanilla.Policies.ContainsKey("Tradition Complete"));
        Assert.NotNull(vanilla.RoadImprovement);
        Assert.NotNull(vanilla.RailroadImprovement);
        Assert.Equal(["Civ V - Vanilla", "Civ V - Gods & Kings", "Civ V - Brave New World"], RulesetCache.GetSortedBaseRulesets());
        Assert.NotNull(vanilla.AllHappinessLevelsThatAffectUniques);

        var combined = RulesetCache.GetComplexRuleset(new GameParameters());
        Assert.Equal(gnk.Name, combined.Mods.Single());
        Assert.Equal(gnk.Buildings.Keys, combined.Buildings.Keys);
        // building costs are filled from the tech columns
        Assert.DoesNotContain(combined.Buildings.Values, it => it.Cost == -1 && it.Name == "Monument");
        Assert.Equal(gnk.Name, gnk.ToString());
        Assert.Contains("Base Ruleset", gnk.GetSummary());
        Assert.NotEmpty(vanilla.AllNames().ToList());
    }

    // endregion
}
