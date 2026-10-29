using Unciv.Utils;

namespace Unciv.Models.Ruleset.Unique;

/// <summary>
/// Ported from core/src/com/unciv/models/ruleset/unique/UniqueMap.kt.
/// Iteration orders are Kotlin's: the tag map is a <c>java.util.HashMap</c> (<see cref="JavaHashMap{TKey,TValue}"/>, T044), the typed map an
/// <c>EnumMap</c> (ordinal order), and the unique lists keep insertion order.
/// </summary>
public class UniqueMap
{
    private readonly JavaHashMap<string, List<Unique>> tagUniqueMap = new();

    // *shares* the list of uniques with the other map, to save on memory and allocations
    // This is a memory/speed tradeoff, since there are *600 unique types*,
    // 750 including deprecated, and EnumMap creates a N-sized array where N is the number of objects in the enum
    private readonly SortedDictionary<UniqueType, List<Unique>> typedUniqueMap = new();

    public UniqueMap()
    {
    }

    public UniqueMap(IEnumerable<Unique> uniques)
    {
        AddUniques(uniques);
    }

    /// <summary>Adds one <paramref name="unique"/> unless it has a ConditionalTimedUnique conditional</summary>
    public virtual void AddUnique(Unique unique)
    {
        if (tagUniqueMap.TryGetValue(unique.PlaceholderText, out var existingArrayList)) existingArrayList.Add(unique);
        else tagUniqueMap[unique.PlaceholderText] = new List<Unique> { unique };

        if (unique.Type == null) return;
        if (typedUniqueMap.ContainsKey(unique.Type.Value)) return;
        typedUniqueMap[unique.Type.Value] = tagUniqueMap[unique.PlaceholderText];
    }

    /// <summary>Calls <see cref="AddUnique"/> on each item from <paramref name="uniques"/></summary>
    public void AddUniques(IEnumerable<Unique> uniques)
    {
        foreach (var unique in uniques) AddUnique(unique);
    }

    public void RemoveUnique(Unique unique)
    {
        if (tagUniqueMap.TryGetValue(unique.PlaceholderText, out var existingArrayList))
            existingArrayList.Remove(unique);
    }

    public void Clear()
    {
        tagUniqueMap.Clear();
        typedUniqueMap.Clear();
    }

    public bool IsEmpty() => tagUniqueMap.IsEmpty;

    // T262: the queries below are the hottest calls of the engine; plain loops instead of LINQ with a capturing lambda (same order, same short-circuit).
    public bool HasUnique(UniqueType uniqueType, GameContext? state = null)
    {
        if (!typedUniqueMap.TryGetValue(uniqueType, out var uniques)) return false;
        var context = state ?? GameContext.EmptyState;
        for (int i = 0; i < uniques.Count; i++)
            if (uniques[i].ConditionalsApply(context) && !uniques[i].IsTimedTriggerable) return true;
        return false;
    }

    public bool HasUnique(string uniqueTag, GameContext? state = null)
    {
        if (!tagUniqueMap.TryGetValue(uniqueTag, out var uniques)) return false;
        var context = state ?? GameContext.EmptyState;
        for (int i = 0; i < uniques.Count; i++)
            if (uniques[i].ConditionalsApply(context) && !uniques[i].IsTimedTriggerable) return true;
        return false;
    }

    public bool HasTagUnique(string tagUnique) => tagUniqueMap.ContainsKey(tagUnique);

    /// <summary><see cref="ForEachUnique(UniqueType, Action{Unique})"/> is faster, for cases that require high perf</summary>
    public IEnumerable<Unique> GetUniques(UniqueType uniqueType) =>
        typedUniqueMap.TryGetValue(uniqueType, out var uniques) ? uniques : Enumerable.Empty<Unique>();

    public void ForEachUnique(UniqueType uniqueType, Action<Unique> op)
    {
        if (!typedUniqueMap.TryGetValue(uniqueType, out var uniques)) return;
        for (int i = 0; i < uniques.Count; i++)
            op(uniques[i]);
    }

    /// <summary><c>ForEachTagUnique</c> is faster, for cases that require high perf</summary>
    public IEnumerable<Unique> GetTagUniques(string uniqueTag) =>
        tagUniqueMap.TryGetValue(uniqueTag, out var uniques) ? uniques : Enumerable.Empty<Unique>();

    /// <summary><see cref="ForEachMatchingUnique(UniqueType, GameContext, Action{Unique})"/> faster, for cases that require high perf</summary>
    public IEnumerable<Unique> GetMatchingUniques(UniqueType uniqueType, GameContext? state = null) =>
        typedUniqueMap.TryGetValue(uniqueType, out var uniques)
            ? MatchingMultiplied(uniques, state ?? GameContext.EmptyState)
            : Enumerable.Empty<Unique>();

    public void ForEachMatchingUnique(UniqueType uniqueType, GameContext gameContext, Action<Unique> op) =>
        ForEachMatchingUnique(uniqueType, gameContext, NoUniqueFilter, op);

    public void ForEachMatchingUnique(UniqueType uniqueType, GameContext gameContext, Func<Unique, bool> filter, Action<Unique> op)
    {
        if (!typedUniqueMap.TryGetValue(uniqueType, out var list)) return;
        ForEachMatchingUnique(list, gameContext, filter, op);
    }

    /// <summary><c>ForEachMatchingTagUnique</c> faster, for cases that require high perf</summary>
    public IEnumerable<Unique> GetMatchingTagUniques(string uniqueTag, GameContext? state = null) =>
        tagUniqueMap.TryGetValue(uniqueTag, out var uniques)
            ? MatchingMultiplied(uniques, state ?? GameContext.EmptyState)
            : Enumerable.Empty<Unique>();

    // Same as .filter | .flatMap, but more cpu/mem performant (7.7 GB vs ?? for test)
    // T262: an iterator instead of SelectMany with a capturing lambda; the same lazy order, and a multiplier of 1 (nearly always) yields without a Repeat.
    private static IEnumerable<Unique> MatchingMultiplied(List<Unique> uniques, GameContext state)
    {
        foreach (var it in uniques)
        {
            if (it.IsTimedTriggerable) continue;
            if (!it.ConditionalsApply(state)) continue;
            var multiplier = it.GetUniqueMultiplier(state);
            if (multiplier == 1) yield return it;
            else foreach (var multiplied in Enumerable.Repeat(it, multiplier)) yield return multiplied;
        }
    }

    public void ForEachMatchingTagUnique(string uniqueTag, GameContext gameContext, Func<Unique, bool> filter, Action<Unique> op)
    {
        if (!tagUniqueMap.TryGetValue(uniqueTag, out var list)) return;
        ForEachMatchingUnique(list, gameContext, filter, op);
    }

    public void ForEachMatchingUnique(List<Unique> list, GameContext gameContext, Func<Unique, bool> filter, Action<Unique> op)
    {
        for (int i = 0; i < list.Count; i++)
        {
            var unique = list[i];
            if (unique.IsTimedTriggerable) continue;
            if (!filter(unique)) continue;
            if (!unique.ConditionalsApply(gameContext)) continue;
            unique.ForEachMultiplied(gameContext, op);
        }
    }

    public bool HasMatchingUnique(UniqueType uniqueType, GameContext? state = null)
    {
        if (!typedUniqueMap.TryGetValue(uniqueType, out var uniques)) return false;
        var context = state ?? GameContext.EmptyState;
        for (int i = 0; i < uniques.Count; i++)
            if (uniques[i].ConditionalsApply(context)) return true;
        return false;
    }

    public bool HasMatchingTagUnique(string uniqueTag, GameContext? state = null)
    {
        if (!tagUniqueMap.TryGetValue(uniqueTag, out var tagUniques)) return false;
        for (int i = 0; i < tagUniques.Count; i++)
        {
            if (tagUniques[i].ConditionalsApply(state ?? GameContext.EmptyState)) return true;
        }
        return false;
    }

    public IEnumerable<Unique> GetAllUniques() => tagUniqueMap.Values.SelectMany(it => it);

    // UniqueMap lacks a way to iterate over all Uniques without allocations, so this is not *dramatically* faster than getLocalTriggeredUniques
    public void ForEachUnique(Action<Unique> op)
    {
        foreach (var unique in GetAllUniques()) op(unique);
    }

    public void ForEachUnique(Func<Unique, bool> filter, Action<Unique> op)
    {
        foreach (var unique in GetAllUniques().Where(filter)) op(unique);
    }

    public IEnumerable<Unique> GetTriggeredUniques(UniqueType trigger, GameContext gameContext, Func<Unique, bool>? triggerFilter = null)
    {
        var filter = triggerFilter ?? (_ => true);
        return typedUniqueMap.Values.SelectMany(it => it)
            .Where(unique => unique.GetModifiers(trigger).Any(filter) && unique.ConditionalsApply(gameContext))
            .SelectMany(it => it.GetMultiplied(gameContext));
    }

    public static readonly UniqueMap Empty = new();
    public static readonly Func<Unique, bool> NoUniqueFilter = _ => true;
}
