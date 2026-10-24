using System.Text.Json.Serialization;
using Unciv.Models.Ruleset.Unique;
using Unciv.UI.Components.Extensions;
using Unciv.Utils;

namespace Unciv.Models.Ruleset.Nation;

// core/src/com/unciv/models/ruleset/nation/CityStateType.kt
// Not ported yet: makeLink (Civilopedia).
public class CityStateType : RulesetObject
{
    public override string MakeLink() => $"CityStateType/{Name}";

    private readonly KotlinLazy<UniqueMap> friendBonusUniqueMap;
    private readonly KotlinLazy<UniqueMap> allyBonusUniqueMap;
    private readonly KotlinLazy<EngineColor> colorObject;

    public CityStateType()
    {
        var self = (IHasUniques)this;
        friendBonusUniqueMap = new KotlinLazy<UniqueMap>(() => self.UniqueMapProvider(self.UniqueObjectsProvider(FriendBonusUniques)));
        allyBonusUniqueMap = new KotlinLazy<UniqueMap>(() => self.UniqueMapProvider(self.UniqueObjectsProvider(AllyBonusUniques)));
        colorObject = new KotlinLazy<EngineColor>(() => ColorExtensions.ColorFromRGB(Color));
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
