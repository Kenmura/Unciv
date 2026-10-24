using System.Text.Json.Serialization;
using Unciv.Models.Ruleset.Unique;
using Unciv.UI.Components.Extensions;

namespace Unciv.Models.Ruleset.Nation;

// core/src/com/unciv/models/ruleset/nation/CityStateType.kt
// Not ported yet: makeLink (Civilopedia).
public class CityStateType : RulesetObject
{
    private readonly Lazy<UniqueMap> friendBonusUniqueMap;
    private readonly Lazy<UniqueMap> allyBonusUniqueMap;
    private readonly Lazy<EngineColor> colorObject;

    public CityStateType()
    {
        var self = (IHasUniques)this;
        friendBonusUniqueMap = new Lazy<UniqueMap>(() => self.UniqueMapProvider(self.UniqueObjectsProvider(FriendBonusUniques)));
        allyBonusUniqueMap = new Lazy<UniqueMap>(() => self.UniqueMapProvider(self.UniqueObjectsProvider(AllyBonusUniques)));
        colorObject = new Lazy<EngineColor>(() => ColorExtensions.ColorFromRGB(Color));
    }

    public override UniqueTarget GetUniqueTarget() => UniqueTarget.CityState;

    public List<string> FriendBonusUniques { get; set; } = [];

    [JsonIgnore]
    public UniqueMap FriendBonusUniqueMap => friendBonusUniqueMap.Value;

    public List<string> AllyBonusUniques { get; set; } = [];

    [JsonIgnore]
    public UniqueMap AllyBonusUniqueMap => allyBonusUniqueMap.Value;

    public List<int> Color { get; set; } = [255, 255, 255];

    public EngineColor GetColor() => colorObject.Value;
}
