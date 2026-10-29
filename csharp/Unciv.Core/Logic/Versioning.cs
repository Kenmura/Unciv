using System.Text.Json.Serialization;
using Unciv.Json;
using Unciv.Models.Translations;

namespace Unciv.Logic;

// core/src/com/unciv/logic/Versioning.kt

/// <summary>
/// Wrapper for a release version. <c>Text</c> corresponds to BuildConfig.appVersion, <c>Number</c> to appCodeNumber.
/// </summary>
public sealed record Version(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("number")] int Number) : IIsPartOfGameInfoSerialization
{
    // Stands in for UncivGame.VERSION (written by a workflow in the Kotlin app); keep in step with the ported revision
    public static Version Current { get; } = new("4.22.13", 1275);

    public string ToSerializeString() => $"{Text} (Build {Number})";

    public string ToNiceString() => $"[{Text}] (Build [{Number}])".Tr();
}

public interface IHasGameInfoSerializationVersion
{
    CompatibilityVersion Version { get; }
}

/// <param name="Number">
/// The serialization version of GameInfo: when it differs from <see cref="CompatibilityVersion.CurrentCompatibilityNumber"/>
/// the instance was loaded from a save made with another version of the game.
/// </param>
/// <param name="CreatedWith">The version that saved the game, not the one that did the "new game".</param>
public sealed record CompatibilityVersion(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("createdWith")] Version CreatedWith) : IIsPartOfGameInfoSerialization, IComparable<CompatibilityVersion>
{
    /// <summary>Incremented whenever changes to the save file structure guarantee that previous versions cannot load or play it.</summary>
    public const int CurrentCompatibilityNumber = 5; // 5: Brave New World (B001) state; G&K saves still load

    public static CompatibilityVersion CurrentCompatibilityVersion { get; } = new(CurrentCompatibilityNumber, Version.Current);

    /// <summary>The version just before this field was introduced, i.e. all saves without any version are from this version.</summary>
    public static CompatibilityVersion FirstWithout { get; } = new(1, new Version("4.1.14", 731));

    // ordering compares the number only, while equality (record) compares everything
    public int CompareTo(CompatibilityVersion? other) => other == null ? 1 : Number.CompareTo(other.Number);
}

/// <summary>Class to use when parsing a saved game json if you only want the serialization version.</summary>
public class GameInfoSerializationVersion : IHasGameInfoSerializationVersion
{
    [JsonPropertyName("version")]
    public CompatibilityVersion Version { get; set; } = CompatibilityVersion.FirstWithout;
}
