using System.Text.Json.Serialization;
using Unciv.Json;
using Unciv.Utils;

namespace Unciv.Models.Ruleset.Unique;

/// <summary>Ported from core/src/com/unciv/models/ruleset/unique/TemporaryUnique.kt.</summary>
public sealed class TemporaryUnique : IsPartOfGameInfoSerialization
{
    public TemporaryUnique()
    {
    }

    public TemporaryUnique(Unique uniqueObject, int turns)
    {
        string turnsText = uniqueObject.GetModifiers(UniqueType.ConditionalTimedUnique)[0].Text;
        string template = $"<{turnsText}>";
        int index = uniqueObject.Text.IndexOf(template, StringComparison.Ordinal);
        string removed = index < 0 ? uniqueObject.Text : uniqueObject.Text[..index] + uniqueObject.Text[(index + template.Length)..];
        UniqueText = removed.KotlinTrim();
        SourceObjectType = uniqueObject.SourceObjectType;
        SourceObjectName = uniqueObject.SourceObjectName;
        TurnsLeft = turns;
    }

    /// <summary>Kotlin <c>unique</c>; renamed because a member cannot be called like the type <see cref="Unique"/> it works with.</summary>
    [JsonPropertyName("unique")]
    public string UniqueText { get; set; } = "";

    // `private` in Kotlin, but serialized with the game
    [JsonPropertyName("sourceObjectType")]
    public UniqueTarget? SourceObjectType { get; set; }

    [JsonPropertyName("sourceObjectName")]
    public string? SourceObjectName { get; set; }

    private Unique? uniqueObject;
    [JsonIgnore]
    public Unique UniqueObject => uniqueObject ??= new Unique(UniqueText, SourceObjectType, SourceObjectName);

    [JsonPropertyName("turnsLeft")]
    public int TurnsLeft { get; set; }

    public TemporaryUnique Clone() => new()
    {
        UniqueText = UniqueText,
        SourceObjectType = SourceObjectType,
        SourceObjectName = SourceObjectName,
        TurnsLeft = TurnsLeft,
    };
}

public static class TemporaryUniqueExtensions
{
    public static void EndTurn(this List<TemporaryUnique> uniques)
    {
        foreach (var unique in uniques)
        {
            if (unique.TurnsLeft >= 0)
                unique.TurnsLeft -= 1;
        }
        uniques.RemoveAll(it => it.TurnsLeft == 0);
    }

    public static IEnumerable<Unique> GetMatchingTagUniques(this List<TemporaryUnique> uniques, UniqueType uniqueType, GameContext gameContext)
    {
        if (uniques.Count == 0) return Enumerable.Empty<Unique>(); // T262: nearly always, and no iterator then
        return MatchingTagUniques(uniques, uniqueType, gameContext);
    }

    private static IEnumerable<Unique> MatchingTagUniques(List<TemporaryUnique> uniques, UniqueType uniqueType, GameContext gameContext)
    {
        foreach (var temporary in uniques)
        {
            var unique = temporary.UniqueObject;
            if (unique.Type != uniqueType || !unique.ConditionalsApply(gameContext)) continue;
            foreach (var multiplied in unique.GetMultiplied(gameContext)) yield return multiplied;
        }
    }

    public static void ForEachMatchingUnique(this List<TemporaryUnique> uniques, UniqueType uniqueType, GameContext gameContext, Action<Unique> op)
    {
        for (int i = 0; i < uniques.Count; i++)
        {
            var unique = uniques[i].UniqueObject;
            if (unique.Type == uniqueType && unique.ConditionalsApply(gameContext))
                unique.ForEachMultiplied(gameContext, op);
        }
    }
}
