using System.Collections.Frozen;
using Unciv.Logic.City;
using Unciv.Logic.WorldCongress;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.UI.Components;
using Unciv.Utils;

namespace Unciv.Models.Ruleset;

// core/src/com/unciv/models/ruleset/PerpetualConstruction.kt
public abstract class PerpetualConstruction(string name, string description) : IConstruction
{
    public string Name { get; set; } = name;
    public string Description { get; } = description;

    public abstract string GetProductionTooltip(City city, bool withIcon = false);

    public abstract bool IsBuildable(CityConstructions cityConstructions);

    public bool ShouldBeDisplayed(CityConstructions cityConstructions) => IsBuildable(cityConstructions);

    public Counter<string> GetStockpiledResourceRequirements(GameContext state) => Counter.ZERO;

    public Counter<string> GetResourceRequirementsPerTurn(GameContext? state = null) => Counter.ZERO;

    public IReadOnlySet<string> RequiredResources(GameContext? state = null) => EmptySet;

    private static readonly FrozenSet<string> EmptySet = FrozenSet<string>.Empty;

    public abstract class StatConversion(Stat stat)
        : PerpetualConstruction(stat.ToString(), $"Convert production to [{stat}] at a rate of [rate] to 1")
    {
        public Stat Stat { get; } = stat;

        public override string GetProductionTooltip(City city, bool withIcon = false) =>
            $"\r\n{KotlinMath.RoundToInt(city.CityStats.CurrentCityStats.Production / GetConversionRate(city))}{(withIcon ? Stat.Character() : "")}/{Fonts.Turn}";

        public int GetConversionRate(City city) => KotlinMath.RoundToInt(1 / city.CityStats.GetStatConversionRate(Stat));

        public override bool IsBuildable(CityConstructions cityConstructions)
        {
            var city = cityConstructions.City;
            if (Stat == Stat.Faith && !city.Civ.GameInfo.IsReligionEnabled())
                return false;

            var context = city.State;
            return city.Civ.GetMatchingUniques(UniqueType.EnablesStatProduction, context)
                .Any(it => it.Params[0] == Stat.ToString());
        }
    }

    public sealed class ScienceConstruction() : StatConversion(Stat.Science);
    public sealed class GoldConstruction() : StatConversion(Stat.Gold);
    public sealed class CultureConstruction() : StatConversion(Stat.Culture);
    public sealed class FaithConstruction() : StatConversion(Stat.Faith);
    public sealed class FoodConstruction() : StatConversion(Stat.Food);
    public sealed class IdleConstruction() : PerpetualConstruction("Nothing", "The city will not produce anything.")
    {
        public override string GetProductionTooltip(City city, bool withIcon = false) => "";
        public override bool IsBuildable(CityConstructions cityConstructions) => true;
    }

    /// <summary>
    /// Brave New World (B017, spec 4.7.7): the process a city works on to contribute its production to a World Congress project. It is named
    /// <c>"{project} Project"</c> (the International Space Station building has the project's name). It is only buildable while the project is enabled.
    /// </summary>
    public sealed class LeagueProcess(string project) : PerpetualConstruction(project + ProjectSuffix, $"Contribute production to the [{project}] project of the World Congress")
    {
        public string Project { get; } = project;

        public override string GetProductionTooltip(City city, bool withIcon = false) => "";

        /// <summary>Puts the production of a turn of <paramref name="city"/> into the project.</summary>
        public void Contribute(City city, int production) => LeagueProjects.Contribute(city, Project, production);

        public override bool IsBuildable(CityConstructions cityConstructions) => LeagueProjects.CanContribute(cityConstructions.City, Project);
    }

    public const string ProjectSuffix = " Project";

    public static readonly StatConversion Science = new ScienceConstruction();
    public static readonly StatConversion Gold = new GoldConstruction();
    public static readonly StatConversion Culture = new CultureConstruction();
    public static readonly StatConversion Faith = new FaithConstruction();
    public static readonly StatConversion Food = new FoodConstruction();
    public static readonly PerpetualConstruction Idle = new IdleConstruction();

    /// <summary>Map of names to instances, in declaration order (Kotlin <c>mapOf</c> is insertion-ordered).</summary>
    public static IReadOnlyDictionary<string, PerpetualConstruction> PerpetualConstructionsMap { get; } =
        new List<PerpetualConstruction> { Science, Gold, Culture, Faith, Food, Idle }.ToDictionary(it => it.Name);

    /// <summary>The World Congress project processes (B017), by name; they are not in <see cref="PerpetualConstructionsMap"/> because only a ruleset with the World Congress offers them.</summary>
    public static IReadOnlyDictionary<string, LeagueProcess> LeagueProcesses { get; } =
        LeagueData.Projects.Select(it => new LeagueProcess(it.Name)).ToDictionary(it => it.Name);

    /// <summary>Any perpetual construction by name: a stat conversion, Nothing or a World Congress project process.</summary>
    public static bool TryGetPerpetual(string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out PerpetualConstruction? perpetual)
    {
        if (PerpetualConstructionsMap.TryGetValue(name, out perpetual)) return true;
        if (LeagueProcesses.TryGetValue(name, out var process)) { perpetual = process; return true; }
        perpetual = null;
        return false;
    }

    /// <returns>whether <paramref name="name"/> represents a PerpetualConstruction - note "" is translated to Nothing in the queue so <c>IsNamePerpetual("")==true</c></returns>
    public static bool IsNamePerpetual(string name) => name.Length == 0 || PerpetualConstructionsMap.ContainsKey(name) || LeagueProcesses.ContainsKey(name);
}
