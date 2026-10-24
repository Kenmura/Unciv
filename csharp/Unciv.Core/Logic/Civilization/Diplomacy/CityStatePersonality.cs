namespace Unciv.Logic.Civilization.Diplomacy;

// core/src/com/unciv/logic/civilization/diplomacy/CityStatePersonality.kt
// Ported with T055 because Civilization.cityStatePersonality needs the type.
public enum CityStatePersonality
{
    Friendly,
    Neutral,
    Hostile,
    Irrational,
}

public static class CityStatePersonalityExtensions
{
    /// <summary>Kotlin <c>CityStatePersonality.safeValueOf</c>: matches on the enum name.</summary>
    public static CityStatePersonality? SafeValueOf(string? name) =>
        Enum.GetValues<CityStatePersonality>().Cast<CityStatePersonality?>().FirstOrDefault(it => it.ToString() == name);
}
