namespace Unciv.Models.StatTypes;

// Stripped: `icon` (NotificationIcon string constants) - restore with the NotificationIcon ticket.
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

    /// <summary>Kotlin `SubStat.safeValueOf`: matches on <see cref="Text"/>, not on the enum name.</summary>
    public static SubStat? SafeValueOf(string name)
    {
        foreach (var value in Enum.GetValues<SubStat>())
            if (value.Text() == name) return value;
        return null;
    }
}
