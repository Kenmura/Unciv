using Unciv.Models.StatTypes;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Models.Ruleset;

/// <summary>
/// Specifies filenames to load for a Ruleset, and how to extract shared views over their contents.
/// The Kotlin enum class: the instances are <see cref="Values"/>, in declaration order (which is what <c>RulesetFile.entries</c> iterates).
///
/// <see cref="GetRulesetObjects"/> and <see cref="GetUniques"/> are used by ruleset-wide validation and unique parsing.
/// <see cref="GetINamed"/> backs <see cref="GetNames"/> for files whose entries have names without being <see cref="IRulesetObject"/>s.
/// Pass <c>getNames</c> directly when a file contains names outside its main objects, such as nation city names or spy names.
/// </summary>
public sealed class RulesetFile
{
    public string Filename { get; }
    public Func<Ruleset, IEnumerable<IRulesetObject>> GetRulesetObjects { get; }
    public Func<Ruleset, IEnumerable<UniqueObject>> GetUniques { get; }
    public Func<Ruleset, IEnumerable<INamed>> GetINamed { get; }
    public Func<Ruleset, IEnumerable<RulesetName>> GetNames { get; }

    private RulesetFile(
        string filename,
        Func<Ruleset, IEnumerable<IRulesetObject>>? getRulesetObjects = null,
        Func<Ruleset, IEnumerable<UniqueObject>>? getUniques = null,
        Func<Ruleset, IEnumerable<INamed>>? getINamed = null,
        Func<Ruleset, IEnumerable<RulesetName>>? getNames = null)
    {
        Filename = filename;
        var objects = getRulesetObjects ?? (_ => []);
        GetRulesetObjects = objects;
        GetUniques = getUniques ?? (r => objects(r).SelectMany(it => it.UniqueObjects));
        var named = getINamed ?? (r => objects(r));
        GetINamed = named;
        GetNames = getNames ?? (r => named(r).Select(it => it.ToRulesetName(r, named)));
    }

    public static readonly RulesetFile Beliefs = new("Beliefs.json", r => r.Beliefs.Values);
    public static readonly RulesetFile Buildings = new("Buildings.json", r => r.Buildings.Values);
    public static readonly RulesetFile Eras = new("Eras.json", r => r.Eras.Values);
    public static readonly RulesetFile Religions = new("Religions.json", getNames: r => r.Religions.Select(it => r.RulesetName(it, "Religion")));
    public static readonly RulesetFile Nations = new("Nations.json", r => r.Nations.Values, getNames: r => r.Nations.Values.SelectMany(nation =>
    {
        var origin = nation.OriginRuleset.Length != 0 ? nation.OriginRuleset : r.Name;
        return new[] { nation.ToRulesetName(r), r.RulesetName(nation.LeaderName, "Nation.leaderName", origin) }
            .Concat(nation.Cities.Select(it => r.RulesetName(it, "Nation.cities", origin)))
            .Concat(nation.SpyNames.Select(it => r.RulesetName(it, "Nation.spyNames", origin)));
    }));
    public static readonly RulesetFile Policies = new("Policies.json", r => r.Policies.Values);
    public static readonly RulesetFile Techs = new("Techs.json", r => r.Technologies.Values);
    public static readonly RulesetFile Terrains = new("Terrains.json", r => r.Terrains.Values);
    public static readonly RulesetFile Tutorials = new("Tutorials.json", r => r.Tutorials.Values);
    public static readonly RulesetFile TileImprovements = new("TileImprovements.json", r => r.TileImprovements.Values);
    public static readonly RulesetFile TileResources = new("TileResources.json", r => r.TileResources.Values);
    public static readonly RulesetFile Specialists = new("Specialists.json", getINamed: r => r.Specialists.Values);
    public static readonly RulesetFile Units = new("Units.json", r => r.Units.Values);
    public static readonly RulesetFile UnitPromotions = new("UnitPromotions.json", r => r.UnitPromotions.Values);
    public static readonly RulesetFile UnitNameGroup = new("UnitNameGroups.json", r => r.UnitNameGroups.Values, getNames: r => r.UnitNameGroups.Values.SelectMany(group =>
    {
        var origin = group.OriginRuleset.Length != 0 ? group.OriginRuleset : r.Name;
        return new[] { group.ToRulesetName(r) }
            .Concat(group.UnitNames.Select(it => r.RulesetName(it, "UnitNameGroup.unitNames", origin)));
    }));
    public static readonly RulesetFile UnitTypes = new("UnitTypes.json", r => r.UnitTypes.Values);
    public static readonly RulesetFile VictoryTypes = new("VictoryTypes.json", r => r.Victories.Values);
    public static readonly RulesetFile CityStateTypes = new("CityStateTypes.json",
        getUniques: r => r.CityStateTypes.Values.SelectMany(it =>
            it.AllyBonusUniqueMap.GetAllUniques().Concat(it.FriendBonusUniqueMap.GetAllUniques()).Concat(it.UniqueObjects)),
        getINamed: r => r.CityStateTypes.Values);
    public static readonly RulesetFile Personalities = new("Personalities.json", r => r.Personalities.Values);
    public static readonly RulesetFile Events = new("Events.json", r => r.Events.Values.Concat<IRulesetObject>(r.Events.Values.SelectMany(it => it.Choices)));
    public static readonly RulesetFile GlobalUniques = new("GlobalUniques.json", r => [r.GlobalUniques]);
    public static readonly RulesetFile ModOptions = new("ModOptions.json", getUniques: r => r.ModOptions.UniqueObjects);
    public static readonly RulesetFile Speeds = new("Speeds.json", r => r.Speeds.Values);
    public static readonly RulesetFile Difficulties = new("Difficulties.json", getINamed: r => r.Difficulties.Values);
    public static readonly RulesetFile Quests = new("Quests.json", getINamed: r => r.Quests.Values);
    public static readonly RulesetFile GreatWorks = new("GreatWorks.json", getINamed: r => r.GreatWorks.Values);
    public static readonly RulesetFile Ruins = new("Ruins.json", r => r.RuinRewards.Values);

    /// <summary>Kotlin <c>RulesetFile.entries</c>.</summary>
    public static readonly IReadOnlyList<RulesetFile> Values =
    [
        Beliefs, Buildings, Eras, Religions, Nations, Policies, Techs, Terrains, Tutorials, TileImprovements, TileResources, Specialists,
        Units, UnitPromotions, UnitNameGroup, UnitTypes, VictoryTypes, CityStateTypes, Personalities, Events, GlobalUniques, ModOptions,
        Speeds, Difficulties, Quests, Ruins, GreatWorks,
    ];
}
