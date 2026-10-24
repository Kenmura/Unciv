using System.Globalization;
using Unciv.Mathematics;
using Unciv.Models.Translations;
using Unciv.Utils;

namespace Unciv.UI.Components.Extensions;

// core/src/com/unciv/ui/components/extensions/FormattingExtensions.kt and TimeExtensions.kt
// Duration -> TimeSpan, Instant -> DateTimeOffset, java.util.Date -> DateTime (wall time, no zone conversion).
public static class FormattingExtensions
{
    /// <summary>Fonts.turn (U+23F3 hourglass), inlined because Fonts is a UI class.</summary>
    private const char FontsTurn = '⏳';

    /// <summary>Translate a percentage number - e.g. 25 - to the multiplication value - e.g. 1.25f.</summary>
    public static float ToPercent(this string text) => float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture).ToPercent();

    public static float ToPercent(this int value) => ((float)value).ToPercent();

    public static float ToPercent(this float value) => 1 + (value / 100);

    /// <summary>Convert a resource name into "Consumes [amount] resource" string (untranslated).</summary>
    public static string GetConsumesAmountString(this string resource, int amount, bool isStockpiled, int available = -1)
    {
        string uniqueString = $"{{Consumes [{amount.ToString(CultureInfo.InvariantCulture)}] [{resource}]}}";
        string perTurnString = isStockpiled ? $" /{FontsTurn}" : "";
        string availableString = available >= 0 ? $" ({{[{available.ToString(CultureInfo.InvariantCulture)}] available}})" : "";
        return uniqueString + perTurnString + availableString;
    }

    /// <summary>Convert a resource name into "Costs [amount] resource" string (untranslated).</summary>
    public static string GetCostsAmountString(this string resource, int amount, int available = -1)
    {
        string uniqueString = $"{{Costs [{amount.ToString(CultureInfo.InvariantCulture)}] [{resource}]}}";
        string availableString = available >= 0 ? $" ({{[{available.ToString(CultureInfo.InvariantCulture)}] available}})" : "";
        return uniqueString + availableString;
    }

    public static string GetNeedMoreAmountString(this string resource, int amount) =>
        $"Need [{amount.ToString(CultureInfo.InvariantCulture)}] more [{resource}]";

    public static string ToStringSigned(this int value) => value > 0 ? $"+{value.Tr()}" : value.Tr();

    /// <summary>Formats the duration into a translated string.</summary>
    public static string Format(this TimeSpan duration)
    {
        var sb = new System.Text.StringBuilder();
        bool firstPartAlreadyAdded = false;
        foreach (var (unit, part) in ToParts(duration))
        {
            if (part == 0L) continue;
            if (firstPartAlreadyAdded) sb.Append(", ");
            sb.Append($"[{part.Tr()}] {unit}");
            firstPartAlreadyAdded = true;
        }

        return sb.ToString();
    }

    /// <summary>Formats the duration into a translated string, but only showing the most significant time unit.</summary>
    public static string FormatShort(this TimeSpan duration)
    {
        var parts = ToParts(duration);
        foreach (var (unit, part) in parts)
        {
            if (part > 0) return $"[{part.ToString(CultureInfo.InvariantCulture)}] {unit}".Tr();
        }

        return $"[{parts[3].Part.ToString(CultureInfo.InvariantCulture)}] Seconds".Tr();
    }

    // Descending by unit like Kotlin's toSortedMap(compareByDescending): Days, Hours, Minutes, Seconds.
    // java.time.Duration.getSeconds() is the floor of the total seconds; toMinutes/Hours/Days truncate that.
    private static (string Unit, long Part)[] ToParts(TimeSpan duration)
    {
        long seconds = (long)Math.Floor((double)duration.Ticks / TimeSpan.TicksPerSecond);
        return
        [
            ("Days", seconds / 86400),
            ("Hours", (seconds / 3600) % 24),
            ("Minutes", (seconds / 60) % 60),
            ("Seconds", seconds % 60),
        ];
    }

    public static bool IsLargerThan(this TimeSpan duration, TimeSpan other) => duration.CompareTo(other) > 0;

    public static bool IsLargerThan(this DateTimeOffset instant, DateTimeOffset other) => instant.CompareTo(other) > 0;

    /// <summary>Format a Vector2 like (0,0) instead of (0.0,0.0).</summary>
    public static string ToPrettyString(this Vector2 vector) =>
        $"({KotlinMath.FloatToInt(vector.x).ToString(CultureInfo.InvariantCulture)},{KotlinMath.FloatToInt(vector.y).ToString(CultureInfo.InvariantCulture)})";
}

/// <summary>Standardize date formatting so dates are presented in a consistent style.</summary>
public static class UncivDateFormat
{
    /// <summary>Format a date to ISO format with minutes (wall time of the value as given).</summary>
    public static string FormatDate(this DateTime date) => date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// Parse an UTC date as passed by online API's, e.g. "2021-04-11T14:43:33Z". Like Kotlin's SimpleDateFormat with a
    /// quoted 'Z' literal, the zone is ignored: the result is the wall time as written, <see cref="DateTimeKind.Unspecified"/>.
    /// </summary>
    public static DateTime ParseDate(this string text) =>
        DateTime.ParseExact(text, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.None);
}
