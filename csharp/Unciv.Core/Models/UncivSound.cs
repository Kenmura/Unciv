using System.Text.Json.Serialization;

namespace Unciv.Models;

// core/src/com/unciv/models/UncivSound.kt

/// <summary>Represents an Unciv Sound, either from a predefined set or custom with a specified filename.</summary>
/// <param name="FileName">The base filename without extension.</param>
public sealed record UncivSound([property: JsonPropertyName("fileName")] string FileName)
{
    /// <summary>For deserialization.</summary>
    [JsonConstructor]
    public UncivSound()
        : this("")
    {
    }

    public static UncivSound Bombard { get; } = new("bombard");

    public static UncivSound Chimes { get; } = new("chimes");

    public static UncivSound Choir { get; } = new("choir");

    public static UncivSound Click { get; } = new("click");

    public static UncivSound Coin { get; } = new("coin");

    public static UncivSound Construction { get; } = new("construction");

    public static UncivSound Fire { get; } = new("fire");

    public static UncivSound Fortify { get; } = new("fortify");

    public static UncivSound Notification1 { get; } = new("notification1");

    public static UncivSound Notification2 { get; } = new("notification2");

    public static UncivSound Paper { get; } = new("paper");

    public static UncivSound Policy { get; } = new("policy");

    public static UncivSound Promote { get; } = new("promote");

    public static UncivSound Setup { get; } = new("setup");

    public static UncivSound Silent { get; } = new("");

    public static UncivSound Slider { get; } = new("slider");

    public static UncivSound Swap { get; } = new("swap");

    public static UncivSound Upgrade { get; } = new("upgrade");

    public static UncivSound Whoosh { get; } = new("whoosh");
}
