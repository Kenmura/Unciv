using Unciv.Utils;
using System.Text.Json.Serialization;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.UI.Screens.Civilopedia;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Models.Ruleset;

// core/src/com/unciv/models/ruleset/RulesetObject.kt
// IRulesetObject is an ICivilopediaText (T095). The per-type MakeLink/getCivilopediaTextLines/getSortGroup/getSubCategory members are ported by T096/T097.

public interface IRulesetObject : IHasUniques, ICivilopediaText
{
    string OriginRuleset { get; set; }
}

public abstract class RulesetObject : IRulesetObject
{
    private readonly KotlinLazy<IReadOnlyList<UniqueObject>> uniqueObjects;
    private readonly KotlinLazy<UniqueMap> uniqueMap;

    protected RulesetObject()
    {
        uniqueObjects = new KotlinLazy<IReadOnlyList<UniqueObject>>(() => ((IHasUniques)this).UniqueObjectsProvider());
        uniqueMap = new KotlinLazy<UniqueMap>(() => ((IHasUniques)this).UniqueMapProvider());
    }

    public virtual string Name { get; set; } = "";
    public string OriginRuleset { get; set; } = "";

    public List<FormattedLine> CivilopediaText { get; set; } = [];

    /// <summary>Can not be a hashset as that would remove doubles</summary>
    public List<string> Uniques { get; set; } = [];

    [JsonIgnore]
    public IReadOnlyList<UniqueObject> UniqueObjects => uniqueObjects.Value;

    [JsonIgnore]
    public UniqueMap UniqueMap => uniqueMap.Value;

    public abstract UniqueTarget GetUniqueTarget();

    public abstract string MakeLink();

    /// <summary>Virtual so subclasses can extend the default (C# cannot call `super` on a default interface method, see <see cref="IHasUniques.IsUnavailableBySettingsDefault"/>)</summary>
    public virtual bool IsUnavailableBySettings(IGameContextGameInfo gameInfo) => IHasUniques.IsUnavailableBySettingsDefault(this, gameInfo);

    public override string ToString() => Name;
}

/// <summary>Same, but inherits from NamedStats - I couldn't find a way to unify the declarations but this is fine</summary>
public abstract class RulesetStatsObject : NamedStats, IRulesetObject
{
    private readonly KotlinLazy<IReadOnlyList<UniqueObject>> uniqueObjects;
    private readonly KotlinLazy<UniqueMap> uniqueMap;

    protected RulesetStatsObject()
    {
        uniqueObjects = new KotlinLazy<IReadOnlyList<UniqueObject>>(() => ((IHasUniques)this).UniqueObjectsProvider());
        uniqueMap = new KotlinLazy<UniqueMap>(() => ((IHasUniques)this).UniqueMapProvider());
    }

    public string OriginRuleset { get; set; } = "";

    public List<FormattedLine> CivilopediaText { get; set; } = [];

    /// <summary>Can not be a hashset as that would remove doubles</summary>
    public List<string> Uniques { get; set; } = [];

    [JsonIgnore]
    public IReadOnlyList<UniqueObject> UniqueObjects => uniqueObjects.Value;

    [JsonIgnore]
    public UniqueMap UniqueMap => uniqueMap.Value;

    public abstract UniqueTarget GetUniqueTarget();

    public abstract string MakeLink();

    /// <summary>Virtual so subclasses can extend the default (C# cannot call `super` on a default interface method, see <see cref="IHasUniques.IsUnavailableBySettingsDefault"/>)</summary>
    public virtual bool IsUnavailableBySettings(IGameContextGameInfo gameInfo) => IHasUniques.IsUnavailableBySettingsDefault(this, gameInfo);
}
