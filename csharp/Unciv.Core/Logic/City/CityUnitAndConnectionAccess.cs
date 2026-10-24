using System.Text.Json.Serialization;
using Unciv.Logic.Civilization.Transients;
using Unciv.Logic.Map.MapUnits;
using Unciv.Utils;

namespace Unciv.Logic.City;

// T128: the City members of Kotlin City.kt that name MapUnit / UnitPromotions / CapitalConnectionMedium, split out of City.cs so that file
// stays within the coupling ratchet. Same class, same behaviour.
public partial class City
{
    [JsonPropertyName("unitToPromotions")]
    public JavaHashMap<string, UnitPromotions> UnitToPromotions { get; set; } = new();

    public bool IsConnectedToCapital(Func<HashSet<CapitalConnectionMedium>, bool> connectionTypePredicate)
    {
        if (!Civ.Cache.CitiesConnectedToCapitalToMediums.TryGetValue(this, out var mediumTypes)) return false;
        return connectionTypePredicate(mediumTypes);
    }

    public MapUnit? GetGarrison()
    {
        var unit = GetCenterTile().MilitaryUnit;
        return unit != null && ReferenceEquals(unit.Civ, Civ) && unit.CanGarrison() ? unit : null;
    }
}
