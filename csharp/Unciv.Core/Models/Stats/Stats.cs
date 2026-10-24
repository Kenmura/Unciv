using System.Collections;
using System.Text.RegularExpressions;

namespace Unciv.Models.StatTypes;

/// <summary>
/// A container for the seven basic "currencies" in Unciv, <b>Mutable</b>, allowing for easy merging of sources and applying bonuses.
///
/// Enumerating skips zero values automatically. Use <see cref="Sum"/>, <see cref="Min"/>, <see cref="Max"/> for fast aggregates.
/// </summary>
/// <remarks>
/// Like Kotlin, <c>==</c>/<c>Equals(object)</c> is reference identity; use the <see cref="Equals(Stats)"/> overload for value comparison.
/// Deliberately NOT IEquatable so dictionaries/sets keep Kotlin's identity semantics.
/// </remarks>
public class Stats : IEnumerable<Stats.StatValuePair>
{
    public float Production { get; set; }
    public float Food { get; set; }
    public float Gold { get; set; }
    public float Science { get; set; }
    public float Culture { get; set; }
    public float Happiness { get; set; }
    public float Faith { get; set; }

    public Stats(float production = 0f, float food = 0f, float gold = 0f, float science = 0f,
                 float culture = 0f, float happiness = 0f, float faith = 0f)
    {
        Production = production;
        Food = food;
        Gold = gold;
        Science = science;
        Culture = culture;
        Happiness = happiness;
        Faith = faith;
    }

    /// <summary>Indexed access for a given <see cref="Stat"/>, e.g. <c>this.Gold == this[Stat.Gold]</c></summary>
    public float this[Stat stat]
    {
        get => stat switch
        {
            Stat.Production => Production,
            Stat.Food => Food,
            Stat.Gold => Gold,
            Stat.Science => Science,
            Stat.Culture => Culture,
            Stat.Happiness => Happiness,
            Stat.Faith => Faith,
            _ => throw new ArgumentOutOfRangeException(nameof(stat)),
        };
        set
        {
            switch (stat)
            {
                case Stat.Production: Production = value; break;
                case Stat.Food: Food = value; break;
                case Stat.Gold: Gold = value; break;
                case Stat.Science: Science = value; break;
                case Stat.Culture: Culture = value; break;
                case Stat.Happiness: Happiness = value; break;
                case Stat.Faith: Faith = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(stat));
            }
        }
    }

    /// <summary>Compares two instances by value (plain float comparison, so NaN != NaN). Not callable via <c>==</c>.</summary>
    public bool Equals(Stats otherStats) =>
        Production == otherStats.Production
        && Food == otherStats.Food
        && Gold == otherStats.Gold
        && Science == otherStats.Science
        && Culture == otherStats.Culture
        && Happiness == otherStats.Happiness
        && Faith == otherStats.Faith;

    /// <summary>Non-mutating: a new instance containing the same values as this</summary>
    public Stats Clone() => new(Production, Food, Gold, Science, Culture, Happiness, Faith);

    public bool IsEmpty() =>
        Production == 0f && Food == 0f && Gold == 0f && Science == 0f
        && Culture == 0f && Happiness == 0f && Faith == 0f;

    /// <summary>Reset all values to zero (in place)</summary>
    public void Clear()
    {
        Production = 0f;
        Food = 0f;
        Gold = 0f;
        Science = 0f;
        Culture = 0f;
        Happiness = 0f;
        Faith = 0f;
    }

    /// <summary>Mutating (but not <paramref name="other"/>): adds each value in place. Returns this for chaining.</summary>
    public Stats Add(Stats other)
    {
        Production += other.Production;
        Food += other.Food;
        Gold += other.Gold;
        Science += other.Science;
        Culture += other.Culture;
        Happiness += other.Happiness;
        Faith += other.Faith;
        return this;
    }

    /// <summary>Mutating: adds <paramref name="value"/> to the value specified by <paramref name="stat"/>. Returns this.</summary>
    public Stats Add(Stat stat, float value)
    {
        this[stat] = value + this[stat];
        return this;
    }

    public static Stats operator +(Stats a, Stats b)
    {
        var clone = a.Clone();
        clone.Add(b);
        return clone;
    }

    public static Stats operator -(Stats a, Stats b)
    {
        var clone = a.Clone();
        clone.Add(b * -1);
        return clone;
    }

    public static Stats operator *(Stats stats, int number) => stats * (float)number;

    public static Stats operator *(Stats stats, float number) => new(
        stats.Production * number,
        stats.Food * number,
        stats.Gold * number,
        stats.Science * number,
        stats.Culture * number,
        stats.Happiness * number,
        stats.Faith * number);

    public static Stats operator /(Stats stats, float number) => stats * (1 / number);

    /// <summary>Mutating: multiplies each value of this instance by <paramref name="number"/> in place</summary>
    public void TimesInPlace(float number)
    {
        Production *= number;
        Food *= number;
        Gold *= number;
        Science *= number;
        Culture *= number;
        Happiness *= number;
        Faith *= number;
    }

    /// <summary>Mutating: apply weighting for Production Ranking</summary>
    public void ApplyRankingWeights()
    {
        Food *= 14;
        Production *= 12.01f; // tie break Production vs gold
        Gold *= 6; // 2 gold worth about 1 production
        Science *= 9.01f; // 4 Science better than 3 Production
        Culture *= 8;
        Happiness *= 10; // base
        Faith *= 7;
    }

    /// <summary>
    /// Hook standing in for Kotlin's `String.tr(hideStats)` (translation layer, not yet ported).
    /// Arguments: text, hideStats. Default is identity, i.e. untranslated English without stat icons.
    /// </summary>
    public static Func<string, bool, string> Translate { get; set; } = (text, _) => text;

    // Stripped: Fonts character icons (no font layer yet) - `showIcon` only matters when translating/with icons;
    // without a character table the icon prefix is empty.
    private string Stringify(bool showSign = true, bool showName = true, bool showIcon = true, bool translate = true)
    {
        return string.Join(", ", AsSequence().Select(it =>
        {
            string sign = showSign && it.Value > 0 ? "+" : "";
            string amount = translate ? Translate(((int)it.Value).ToString(), false) : ((int)it.Value).ToString();
            string label;
            if (!showName) label = "";   // Kotlin: the stat's font character
            else if (translate) label = Translate(it.Key.ToString(), !showIcon);
            else label = it.Key.ToString();   // Kotlin: (showIcon ? character : "") + name
            return $"{sign}{amount} {label}";
        }));
    }

    /// <summary>Example output: <c>+1 Production, -1 Food</c>.</summary>
    public override string ToString() => Stringify();

    /// <summary>Notifications are translated on the fly, so saved stats are in English.</summary>
    public string ToStringForNotifications() => Stringify(showIcon: false, translate: false);

    /// <summary>Same as ToString, but without the leading stat icon character and without the sign.</summary>
    public string ToStringWithoutIcons() => Stringify(showIcon: false);

    /// <summary>String of just +/- value and Stat symbol</summary>
    public string ToStringOnlyIcons(bool addPlusSign = true) => Stringify(showSign: addPlusSign, showName: false, translate: false);

    /// <summary>One key/value pair returned by the enumerator</summary>
    public record StatValuePair(Stat Key, float Value);

    /// <summary>Lazily enumerates the non-zero stat/value pairs.</summary>
    public IEnumerable<StatValuePair> AsSequence()
    {
        if (Production != 0f) yield return new StatValuePair(Stat.Production, Production);
        if (Food != 0f) yield return new StatValuePair(Stat.Food, Food);
        if (Gold != 0f) yield return new StatValuePair(Stat.Gold, Gold);
        if (Science != 0f) yield return new StatValuePair(Stat.Science, Science);
        if (Culture != 0f) yield return new StatValuePair(Stat.Culture, Culture);
        if (Happiness != 0f) yield return new StatValuePair(Stat.Happiness, Happiness);
        if (Faith != 0f) yield return new StatValuePair(Stat.Faith, Faith);
    }

    /// <summary>Fast aggregate: sum over all seven stats</summary>
    public float Sum() => Production + Food + Gold + Science + Culture + Happiness + Faith;

    // Kotlin coerceAtMost/AtLeast on Float: `if (this > max) max else this` / `if (this < min) min else this` (differs from Math.Min/Max for NaN)
    private static float CoerceAtMost(float value, float maximum) => value > maximum ? maximum : value;
    private static float CoerceAtLeast(float value, float minimum) => value < minimum ? minimum : value;

    /// <summary>Fast aggregate: min of all seven stats</summary>
    public float Min() =>
        CoerceAtMost(CoerceAtMost(CoerceAtMost(CoerceAtMost(CoerceAtMost(CoerceAtMost(
            Production, Food), Gold), Science), Culture), Happiness), Faith);

    /// <summary>Fast aggregate: max of all seven stats</summary>
    public float Max() =>
        CoerceAtLeast(CoerceAtLeast(CoerceAtLeast(CoerceAtLeast(CoerceAtLeast(CoerceAtLeast(
            Production, Food), Gold), Science), Culture), Happiness), Faith);

    public IEnumerator<StatValuePair> GetEnumerator() => AsSequence().GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // Kotlin's `\d` is ASCII-only, .NET's matches any Unicode digit: use [0-9]. `\z` instead of `$` (which accepts a trailing \n).
    private static readonly string AllStatNames = string.Join("|", Enum.GetNames<Stat>());
    private static readonly string StatRegexPattern = $"([+-])([0-9]+) ({AllStatNames})";
    private static readonly Regex StatRegex = new($"\\A(?:{StatRegexPattern})\\z");
    private static readonly Regex EntireStringRegex = new($"\\A(?:{StatRegexPattern}(?:, {StatRegexPattern})*)\\z");

    /// <summary>
    /// Tests whether a string is a valid representation of Stats, close to what ToString would produce.
    /// Values must carry a sign; separator is ", "; stat names untranslated and case-matching; order irrelevant.
    /// </summary>
    public static bool IsStats(string str)
    {
        if (str.Length == 0 || (str[0] != '+' && str[0] != '-')) return false; // very quick negative check before the heavy Regex
        return EntireStringRegex.IsMatch(str);
    }

    /// <summary>Parses a string to a Stats instance (see <see cref="IsStats"/> for the format). Throws on invalid input.</summary>
    public static Stats Parse(string str)
    {
        var toReturn = new Stats();
        foreach (var statWithBonuses in str.Split(", "))
        {
            var match = StatRegex.Match(statWithBonuses);
            if (!match.Success) throw new NullReferenceException("matchEntire returned null");  // Kotlin `!!`
            var groups = match.Groups;
            string statName = groups[3].Value;
            float statAmount = float.Parse(groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)
                               * (groups[1].Value == "-" ? -1 : 1);
            toReturn.Add(StatExtensions.ValueOf(statName), statAmount);
        }
        return toReturn;
    }

    public static readonly Stats ZERO = new();
    public static readonly Stats DefaultCityCenterMinimum = new(food: 2f, production: 1f);
}

public class StatMap : System.Collections.Generic.OrderedDictionary<string, Stats>
{
    public new void Add(string source, Stats stats)
    {
        // We always clone to avoid touching the mutable stats of uniques
        if (!ContainsKey(source)) this[source] = stats.Clone();
        else this[source].Add(stats);
    }
}
