using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;

namespace Unciv.Logic.Map;

// Ported from core/src/com/unciv/logic/map/CarrierSlotMatcher.kt (T040). Stripped: the purity annotations.
// Retyped from IGameContextUnit to MapUnit in T134.
// unitToSlot is a Dictionary (Kotlin LinkedHashMap): it is only looked up by key, searched by value (values are unique) or read as a
// whole set of values, so enumeration order never reaches the result.

/// <summary>
/// Bipartite matching helper for carrier slot assignment.
/// Guarantees optimal allocation of carried units to slot rules, returning the maximum capacity available for a new unit.
/// Time complexity: O(S * C), where S = total slots, C = carried units.
/// </summary>
public static class CarrierSlotMatcher
{
    public readonly record struct SlotRule(string Filter, int Capacity);

    private abstract record BipartiteMatchingResult
    {
        public sealed record Success(ICollection<int> Allocations) : BipartiteMatchingResult;
        public sealed record Failure(int OverAllocation) : BipartiteMatchingResult;
    }

    /// <summary>Computes maximum available slots for <paramref name="newUnit"/> given existing assignments.</summary>
    /// <param name="slotRules">The carrier's slot rules, (e.g., "Fighter" cap 1, "all" cap 2). Duplicates of filters are no problem and not even a performance penalty.</param>
    /// <param name="carriedUnits">Units already carried by this carrier</param>
    /// <param name="newUnit">The unit we want to fit</param>
    /// <returns>Number of available slots for newUnit (negative if carrier is overcapacity, counting number of units that would need to be removed to conform to the ruleset)</returns>
    public static int AvailableCapacity(ICollection<SlotRule> slotRules, IEnumerable<MapUnitClass> carriedUnits, MapUnitClass newUnit)
    {
        if (slotRules.Count == 0) return 0;

        // Expand rules into individual slot instances
        var slots = new List<string>(); // filter string for each slot
        foreach (var rule in slotRules)
        {
            for (int i = 0; i < rule.Capacity; i++) slots.Add(rule.Filter);
        }

        // Find optimal assignment of carried units including the new unit to slots
        var allUnits = new List<MapUnitClass> { newUnit };
        allUnits.AddRange(carriedUnits);

        switch (MaxBipartiteMatching(slots, allUnits))
        {
            case BipartiteMatchingResult.Failure failure:
                return -failure.OverAllocation;
            case BipartiteMatchingResult.Success success:
                // Count unmatched slots that newUnit can fill
                int availableSlots = 0;
                for (int slotIdx = 0; slotIdx < slots.Count; slotIdx++)
                {
                    if (!success.Allocations.Contains(slotIdx) && newUnit.MatchesFilter(slots[slotIdx]))
                        availableSlots++;
                }
                return availableSlots + 1; // newUnit was allocated, count that slot too
            default:
                throw new InvalidOperationException();
        }
    }

    /// <summary>
    /// Maximum bipartite matching using augmenting paths (DFS-based). Each unit gets at most one slot, each slot at most one unit,
    /// and the matching size is maximized.
    /// </summary>
    /// <returns>Success: the allocated slot indices. Failure: Overallocation count (0: no place for new unit, 1: there's even one too many already loaded,...)</returns>
    private static BipartiteMatchingResult MaxBipartiteMatching(List<string> slots, List<MapUnitClass> units)
    {
        var unitToSlot = new Dictionary<int, int>(); // unit index -> slot index
        var slotUsed = new bool[slots.Count];

        int CountOverAllocation()
        {
            if (unitToSlot.Count == 0) return 0;
            var slot = slots[unitToSlot[0]];
            int count = 0;
            for (int i = 0; i < units.Count; i++)
            {
                if (!unitToSlot.ContainsKey(i) && units[i].MatchesFilter(slot)) count++;
            }
            return count - 1; // The -1 again because newUnit is included
        }

        // Process each unit, trying to assign it to a slot
        for (int unitIdx = 0; unitIdx < units.Count; unitIdx++)
        {
            var visited = new bool[slots.Count];
            if (!DfsAugmentingPath(unitIdx, units, slots, slotUsed, visited, unitToSlot))
                return new BipartiteMatchingResult.Failure(CountOverAllocation());
        }

        return new BipartiteMatchingResult.Success(unitToSlot.Values);
    }

    /// <summary>
    /// DFS to find an augmenting path for a unit.
    /// Either assigns the unit to a free slot, or recursively reassigns the current occupant of a slot and takes it.
    /// </summary>
    private static bool DfsAugmentingPath(int unitIdx, List<MapUnitClass> units, List<string> slots,
        bool[] slotUsed, bool[] visited, Dictionary<int, int> unitToSlot)
    {
        var unit = units[unitIdx];

        // Try each slot that matches this unit's properties
        for (int slotIdx = 0; slotIdx < slots.Count; slotIdx++)
        {
            if (visited[slotIdx] || !unit.MatchesFilter(slots[slotIdx])) continue;
            visited[slotIdx] = true;

            // Case 1: Slot is free -> assign unit directly
            if (!slotUsed[slotIdx])
            {
                unitToSlot[unitIdx] = slotIdx;
                slotUsed[slotIdx] = true;
                return true;
            }

            // Case 2: Slot is occupied -> try to recursively reassign its current occupant
            int? currentOccupant = null;
            foreach (var entry in unitToSlot)
            {
                if (entry.Value == slotIdx) { currentOccupant = entry.Key; break; }
            }
            if (currentOccupant != null)
            {
                if (DfsAugmentingPath(currentOccupant.Value, units, slots, slotUsed, visited, unitToSlot))
                {
                    unitToSlot[unitIdx] = slotIdx;
                    return true;
                }
            }
        }

        return false;
    }
}
