using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using Unciv.Utils;

namespace Unciv.Models;

// core/src/com/unciv/models/ModConstants.kt

/// <summary>
/// Used as a member of ModOptions for moddable "constants" - factors in formulae and such.
/// When combining mods, this is <see cref="Merge"/>d _per constant/field_, not as entire object like other RulesetObjects.
/// Merging happens on a very simple basis: If a Mod comes with a non-default value, it is copied, otherwise the parent value is left intact.
/// If several mods change the same field, the last one wins.
/// </summary>
/// <remarks>
/// <see cref="Merge"/>, <see cref="Equals(object?)"/>, <see cref="GetHashCode"/> and <see cref="ToString"/> go through reflection over the properties in
/// declaration order, like upstream does over the Java fields, so adding a property needs no change there. The semantics of Java's boxed
/// <c>equals</c>/<c>hashCode</c>/<c>toString</c> are kept: <c>Float</c>/<c>Double</c> compare by bits (<c>NaN</c> equals <c>NaN</c>, <c>0.0</c> differs from
/// <c>-0.0</c>), hash codes are the JVM's, and values print the way <c>Double.toString</c> does. All members must conform to the equality contract.
/// </remarks>
public sealed class ModConstants
{
    private static readonly ImmutableArray<PropertyInfo> Fields = [.. typeof(ModConstants)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .OrderBy(property => property.MetadataToken)];

    /// <summary>As merge will need a default instance repeatedly, store it as static.</summary>
    public static ModConstants Defaults { get; } = new();

    // Max amount of experience that can be gained from combat with barbarians
    [JsonPropertyName("maxXPfromBarbarians")]
    public int MaxXPfromBarbarians { get; set; } = 30;

    // Formula for city Strength:
    // Strength = baseStrength + strengthPerPop + strengthFromTiles +
    //            ((%techs * multiplier) ^ exponent) * fullMultiplier +
    //            (garrisonBonus * garrisonUnitStrength * garrisonUnitHealth/100) +
    //            defensiveBuildingStrength
    // where %techs is the percentage of techs in the tech tree that are complete
    // If no techs exist in this ruleset, %techs = 0.5 (=50%)
    [JsonPropertyName("cityStrengthBase")]
    public double CityStrengthBase { get; set; } = 8.0;

    [JsonPropertyName("cityStrengthPerPop")]
    public double CityStrengthPerPop { get; set; } = 0.4;

    [JsonPropertyName("cityStrengthFromTechsMultiplier")]
    public double CityStrengthFromTechsMultiplier { get; set; } = 5.5;

    [JsonPropertyName("cityStrengthFromTechsExponent")]
    public double CityStrengthFromTechsExponent { get; set; } = 2.8;

    [JsonPropertyName("cityStrengthFromTechsFullMultiplier")]
    public double CityStrengthFromTechsFullMultiplier { get; set; } = 1.0;

    [JsonPropertyName("cityStrengthFromGarrison")]
    public double CityStrengthFromGarrison { get; set; } = 0.2;

    // Formula for Unit Supply:
    // Supply = unitSupplyBase (difficulties.json)
    //          unitSupplyPerCity * amountOfCities + (difficulties.json)
    //          unitSupplyPerPopulation * amountOfPopulationInAllCities
    // unitSupplyBase and unitSupplyPerCity can be found in difficulties.json
    // unitSupplyBase, unitSupplyPerCity and unitSupplyPerPopulation can also be increased through uniques
    [JsonPropertyName("unitSupplyPerPopulation")]
    public double UnitSupplyPerPopulation { get; set; } = 0.5;

    // The minimal distance that must be between any two cities, not counting the tiles cities are on
    // The number is the amount of tiles between two cities, not counting the tiles the cities are on.
    // e.g. "C__C", where "C" is a tile with a city and "_" is a tile without a city, has a distance of 2.
    // First constant is for cities on the same landmass, the second is for cities on different continents.
    [JsonPropertyName("minimalCityDistance")]
    public int MinimalCityDistance { get; set; } = 3;

    [JsonPropertyName("minimalCityDistanceOnDifferentContinents")]
    public int MinimalCityDistanceOnDifferentContinents { get; set; } = 2;

    [JsonPropertyName("baseCityBombardRange")]
    public int BaseCityBombardRange { get; set; } = 2;

    [JsonPropertyName("cityWorkRange")]
    public int CityWorkRange { get; set; } = 3;

    [JsonPropertyName("cityExpandRange")]
    public int CityExpandRange { get; set; } = 5;

    // Number of air units that can be stationed in a city
    [JsonPropertyName("cityAirUnitCapacity")]
    public int CityAirUnitCapacity { get; set; } = 6;

    // Modifies how much the gold value of a one-sided trade is applied to the gifts diplomatic modifier.
    // Eg: One side offers a city, resource or gold for nothing in return.
    [JsonPropertyName("goldGiftMultiplier")]
    public float GoldGiftMultiplier { get; set; } = 1f;

    // Modifies how much the gold value of a regular trade is applied to the gifts diplomatic modifier.
    [JsonPropertyName("goldGiftTradeMultiplier")]
    public float GoldGiftTradeMultiplier { get; set; } = 0.8f;

    // Modifies how quickly the GaveUsGifts dimplomacy modifier runs out. A higher value makes it run out quicker.
    // Normally the gifts reduced by ~2.5% per turn depending on the diplomatic relations with the default value.
    [JsonPropertyName("goldGiftDegradationMultiplier")]
    public float GoldGiftDegradationMultiplier { get; set; } = 1f;

    // Constants used to calculate Unit Upgrade gold Cost (can only be modded all-or-nothing)
    // This is a data class for one reason only: The equality implementation enables Gdx Json to omit it when default (otherwise only the individual fields are omitted)
    [JsonPropertyName("unitUpgradeCost")]
    public UpgradeCostConstants UnitUpgradeCost { get; set; } = new();

    // NaturalWonderGenerator uses these to determine the number of Natural Wonders to spawn for a given map size.
    // With these values, radius * mul + add gives a 1-2-3-4-5 progression for Unciv predefined map sizes and a 2-3-4-5-6-7 progression for the original Civ5 map sizes.
    // 0.124 = (Civ5.Huge.getHexagonalRadiusForArea(w*h) - Civ5.Duel.getHexagonalRadiusForArea(w*h)) / 5 (if you do not round in the radius function)
    // The other constant is empiric to avoid an ugly jump in the progression.
    [JsonPropertyName("naturalWonderCountMultiplier")]
    public float NaturalWonderCountMultiplier { get; set; } = 0.124f;

    [JsonPropertyName("naturalWonderCountAddedConstant")]
    public float NaturalWonderCountAddedConstant { get; set; } = 0.1f;

    // MapGenerator.spreadAncientRuins: number of ruins = suitable tile count * this
    [JsonPropertyName("ancientRuinCountMultiplier")]
    public float AncientRuinCountMultiplier { get; set; } = 0.025f;

    // MapGenerator.spawnLakesAndCoasts: Water bodies up to this tile count become Lakes
    [JsonPropertyName("maxLakeSize")]
    public int MaxLakeSize { get; set; } = 10;

    // RiverGenerator: river frequency and length bounds
    [JsonPropertyName("riverCountMultiplier")]
    public float RiverCountMultiplier { get; set; } = 0.01f;

    [JsonPropertyName("minRiverLength")]
    public int MinRiverLength { get; set; } = 5;

    [JsonPropertyName("maxRiverLength")]
    public int MaxRiverLength { get; set; } = 666; // Do not set to less than the maximal map radius. Set to 0 to disable river generation.

    // Factors in formula for Maximum Number of foundable Religions
    [JsonPropertyName("religionLimitBase")]
    public int ReligionLimitBase { get; set; } = 1;

    [JsonPropertyName("religionLimitMultiplier")]
    public float ReligionLimitMultiplier { get; set; } = 0.5f;

    // Factors in formula for pantheon cost
    [JsonPropertyName("pantheonBase")]
    public int PantheonBase { get; set; } = 10;

    [JsonPropertyName("pantheonGrowth")]
    public int PantheonGrowth { get; set; } = 5;

    // AI behaviour
    [JsonPropertyName("workboatAutomationSearchMaxTiles")]
    public int WorkboatAutomationSearchMaxTiles { get; set; } = 37;

    [JsonPropertyName("minimumCityLocationTileValue")]
    public float MinimumCityLocationTileValue { get; set; } = 53f;

    // Civilization
    [JsonPropertyName("minimumWarDuration")]
    public int MinimumWarDuration { get; set; } = 10;

    [JsonPropertyName("baseTurnsUntilRevolt")]
    public int BaseTurnsUntilRevolt { get; set; } = 4;

    [JsonPropertyName("cityStateElectionTurns")]
    public int CityStateElectionTurns { get; set; } = 15;

    // Economy
    [JsonPropertyName("maxGoldTradeOffer")]
    public int MaxGoldTradeOffer { get; set; } = 10000;

    // City State Tribute: maximum points from Force ranking towards reaching Tribute willingness threshold
    [JsonPropertyName("tributeGlobalModifier")]
    public int TributeGlobalModifier { get; set; } = 100; // 75 in BNW

    [JsonPropertyName("tributeLocalModifier")]
    public int TributeLocalModifier { get; set; } = 100; // 125 in BNW

    // Espionage
    [JsonPropertyName("maxSpyRank")]
    public int MaxSpyRank { get; set; } = 3;

    // How much of a skill bonus each rank gives.
    // Rank 0 is 100, rank 1 is 130, and so on.
    // Half as much for a coup.
    [JsonPropertyName("spyRankSkillPercentBonus")]
    public int SpyRankSkillPercentBonus { get; set; } = 30;

    // Rank 2 is +25% tech steal rate, rank 3 is +50%, and so on
    [JsonPropertyName("spyRankStealPercentBonus")]
    public int SpyRankStealPercentBonus { get; set; } = 25;

    // Steal cost equal to 125% of the most expensive stealable tech
    [JsonPropertyName("spyTechStealCostModifier")]
    public float SpyTechStealCostModifier { get; set; } = 1.25f;

    // Score value of things
    [JsonPropertyName("scoreFromPopulation")]
    public int ScoreFromPopulation { get; set; } = 3; // 4 in BNW

    [JsonPropertyName("scoreFromWonders")]
    public int ScoreFromWonders { get; set; } = 40; // 25 in BNW

    // UI: If set >= 0, ImprovementPicker will silently skip improvements whose tech requirement is more advanced than your current Era + this
    [JsonPropertyName("maxImprovementTechErasForward")]
    public int MaxImprovementTechErasForward { get; set; } = -1;

    public void Merge(ModConstants other)
    {
        foreach (var field in Fields)
        {
            object? value = field.GetValue(other);
            if (JavaEquals(field.GetValue(Defaults), value))
            {
                continue;
            }

            field.SetValue(this, value);
        }
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        return obj is ModConstants other && Fields.All(field => JavaEquals(field.GetValue(this), field.GetValue(other)));
    }

    // Note: This is of course heavily dependent on iteration order, which is declaration order here as upstream
    public override int GetHashCode()
    {
        int result = 0;
        foreach (var field in Fields)
        {
            result = result.WithHash(JavaHashCode(field.GetValue(this)));
        }

        return result;
    }

    /// <summary>Debug only so far.</summary>
    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append('{');
        foreach (var field in Fields)
        {
            object? value = field.GetValue(this);
            if (JavaEquals(value, field.GetValue(Defaults)))
            {
                continue;
            }

            sb.Append(field.GetCustomAttribute<JsonPropertyNameAttribute>().NotNull().Name);
            sb.Append(':');
            sb.Append(JavaFormat.ToJavaString(value));
            sb.Append(',');
        }

        sb.Length--; // remove extra ',' (or the opening brace if nothing differs)
        sb.Append('}');
        string text = sb.ToString();
        return text == "}" ? "defaults" : text;
    }

    private static bool JavaEquals(object? a, object? b) => (a, b) switch
    {
        (float x, float y) => JavaHash.HashCode(x) == JavaHash.HashCode(y),
        (double x, double y) => JavaHash.HashCode(x) == JavaHash.HashCode(y),
        _ => Equals(a, b),
    };

    private static int JavaHashCode(object? value) => value switch
    {
        int i => JavaHash.HashCode(i),
        float f => JavaHash.HashCode(f),
        double d => JavaHash.HashCode(d),
        null => 0,
        _ => value.GetHashCode(),
    };

    /// <summary>
    /// Constants used to calculate Unit Upgrade gold Cost (can only be modded all-or-nothing).
    /// A data class upstream: equality, hash code (31-chained) and string form follow Kotlin's.
    /// </summary>
    public sealed class UpgradeCostConstants : IEquatable<UpgradeCostConstants>
    {
        [JsonPropertyName("base")]
        public float Base { get; init; } = 10f;

        [JsonPropertyName("perProduction")]
        public float PerProduction { get; init; } = 2f;

        [JsonPropertyName("eraMultiplier")]
        public float EraMultiplier { get; init; } // 0.3 in Civ5 cpp sources but 0 in xml

        [JsonPropertyName("exponent")]
        public float Exponent { get; init; } = 1f;

        [JsonPropertyName("roundTo")]
        public int RoundTo { get; init; } = 5;

        public bool Equals(UpgradeCostConstants? other) =>
            other != null
            && JavaHash.HashCode(Base) == JavaHash.HashCode(other.Base)
            && JavaHash.HashCode(PerProduction) == JavaHash.HashCode(other.PerProduction)
            && JavaHash.HashCode(EraMultiplier) == JavaHash.HashCode(other.EraMultiplier)
            && JavaHash.HashCode(Exponent) == JavaHash.HashCode(other.Exponent)
            && RoundTo == other.RoundTo;

        public override bool Equals(object? obj) => obj is UpgradeCostConstants other && Equals(other);

        public override int GetHashCode() =>
            JavaHash.HashCode(Base)
                .WithHash(JavaHash.HashCode(PerProduction))
                .WithHash(JavaHash.HashCode(EraMultiplier))
                .WithHash(JavaHash.HashCode(Exponent))
                .WithHash(RoundTo);

        public override string ToString() =>
            $"UnitUpgradeCost(base={JavaFormat.ToJavaString(Base)}, perProduction={JavaFormat.ToJavaString(PerProduction)}, eraMultiplier={JavaFormat.ToJavaString(EraMultiplier)}, exponent={JavaFormat.ToJavaString(Exponent)}, roundTo={RoundTo.ToString(CultureInfo.InvariantCulture)})";
    }
}
