using Unciv.Logic.Civilization;

namespace Unciv.Models.StatTypes;

// `icon` (NotificationIcon string constants) is the Icon() extension (T083).
public enum SubStat
{
    GoldenAgePoints,
    StoredFood, // city-scoped
}

public static class SubStatExtensions
{
    /// <summary>Kotlin constructor parameter `text`.</summary>
    public static string Text(this SubStat subStat) => subStat switch
    {
        SubStat.GoldenAgePoints => "Golden Age points",
        SubStat.StoredFood => "Stored Food",
        _ => throw new ArgumentOutOfRangeException(nameof(subStat)),
    };

    /// <summary>Kotlin constructor parameter `icon` (T083: Battle.plunderFromDamage reads it).</summary>
    public static string Icon(this SubStat subStat) => subStat switch
    {
        SubStat.GoldenAgePoints => NotificationIcon.Happiness,
        SubStat.StoredFood => NotificationIcon.Food,
        _ => throw new ArgumentOutOfRangeException(nameof(subStat)),
    };

    /// <summary>Kotlin `SubStat.safeValueOf`: matches on <see cref="Text"/>, not on the enum name.</summary>
    public static SubStat? SafeValueOf(string name)
    {
        foreach (var value in Enum.GetValues<SubStat>())
            if (value.Text() == name) return value;
        return null;
    }
}
