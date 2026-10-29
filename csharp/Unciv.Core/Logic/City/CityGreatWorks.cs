using System.Text.Json.Serialization;
using Unciv.Logic.Civilization.Transients;
using Unciv.Logic.GreatWorks;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;

namespace Unciv.Logic.City;

// Brave New World (ticket B007, spec id B030, bnw-spec/SPEC.md section 4.2). Not part of the Kotlin source.
// The works held by the slots of the buildings of this city, the theming bonus of those buildings and the Culture they give. The slots of a
// building come from its ruleset object (greatWorkSlots), the works in them from GreatWorkSlotData, which is null until the first work is placed.

/// <summary>One slot of a building of a city, and the work in it (null when empty).</summary>
public readonly record struct GreatWorkSlot(Building Building, int Index, string? Work);

public partial class City
{
    /// <summary>Serialized slot contents: sorted by building name, trailing empty slots trimmed, empty buildings absent (a canonical form).</summary>
    [JsonPropertyName("greatWorkSlots")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<BuildingGreatWorks>? GreatWorkSlotData { get; set; }

    /// <summary>Built buildings that hold Great Works, ordered by name (ordinal) so slot choice never depends on construction history.</summary>
    public IEnumerable<Building> GetGreatWorkBuildings() =>
        CityConstructions.GetBuiltBuildings()
            .Where(it => it.GreatWorkSlots > 0 && it.GreatWorkSlotType != null)
            .OrderBy(it => it.Name, StringComparer.Ordinal);

    /// <summary>Every slot of every building of the city, buildings by name, slots by index.</summary>
    public IEnumerable<GreatWorkSlot> GetGreatWorkSlots()
    {
        foreach (var building in GetGreatWorkBuildings())
            for (int slot = 0; slot < building.GreatWorkSlots; slot++)
                yield return new GreatWorkSlot(building, slot, GetGreatWorkIn(building.Name, slot));
    }

    public string? GetGreatWorkIn(string building, int slot)
    {
        var entry = GreatWorkSlotData?.FirstOrDefault(it => it.Building == building);
        return entry != null && slot >= 0 && slot < entry.Works.Count ? entry.Works[slot] : null;
    }

    /// <summary>Gives a just-built <paramref name="building"/> the works its uniques say it contains.</summary>
    public void PlaceFreeGreatWorks(Building building) => GreatWorkRules.PlaceFreeWorks(this, building);

    public int GetNumberOfGreatWorks() => GetGreatWorkSlots().Count(it => it.Work != null);

    /// <summary>Puts <paramref name="work"/> (or nothing) into a slot. The caller has checked that the slot exists and fits.</summary>
    public void SetGreatWorkIn(string building, int slot, string? work)
    {
        var entry = GreatWorkSlotData?.FirstOrDefault(it => it.Building == building);
        if (entry == null && work == null) return;
        var data = GreatWorkSlotData ??= [];
        if (entry == null)
        {
            entry = new BuildingGreatWorks { Building = building };
            data.Add(entry);
            data = GreatWorkSlotData = data.OrderBy(it => it.Building, StringComparer.Ordinal).ToList();
        }
        while (entry.Works.Count <= slot) entry.Works.Add(null);
        entry.Works[slot] = work;
        while (entry.Works.Count > 0 && entry.Works[^1] == null) entry.Works.RemoveAt(entry.Works.Count - 1);
        if (entry.Works.Count == 0) data.Remove(entry);
        if (data.Count == 0) GreatWorkSlotData = null;
    }

    /// <summary>The works of a building that is sold or destroyed are lost (they stay in the game's list, so they cannot be created again).</summary>
    public void ClearGreatWorks(string building)
    {
        if (GreatWorkSlotData == null) return;
        GreatWorkSlotData.RemoveAll(it => it.Building == building);
        if (GreatWorkSlotData.Count == 0) GreatWorkSlotData = null;
    }

    /// <summary>
    /// The theming bonus of one building (spec 4.2.4): zero unless every slot is filled, else the bonus of the first row that applies,
    /// raised by the civ's theming policies and, in the capital, by its capital theming uniques.
    /// </summary>
    public int GetThemingBonus(Building building)
    {
        if (building.ThemingBonuses.Count == 0 || building.GreatWorkSlots <= 0 || !CityConstructions.IsBuilt(building.Name)) return 0;
        var ruleset = GetRuleset();
        var game = (GameInfo)Civ.GameInfo;
        var classes = new List<GreatWorkClass>();
        var eras = new List<string>();
        var creators = new List<string>();
        for (int slot = 0; slot < building.GreatWorkSlots; slot++)
        {
            string? name = GetGreatWorkIn(building.Name, slot);
            if (name == null || game.GetGreatWork(name) is not { } instance || !ruleset.GreatWorks.TryGetValue(name, out var work)) return 0;
            classes.Add(work.GreatWorkClass);
            eras.Add(instance.Era);
            creators.Add(instance.Creator);
        }
        foreach (var row in building.ThemingBonuses)
            if (GreatWorkRules.RowApplies(row, classes, eras, creators, Civ.CivId))
                return ApplyThemingMultipliers(row.Bonus);
        return 0;
    }

    // [K] unverified: Civ V multiplies and divides by 100 once per modifier, truncating (spec 4.2.4).
    private int ApplyThemingMultipliers(int bonus)
    {
        int percent = 0;
        ForEachMatchingUnique(UniqueType.ThemingBonusMultiplier, unique =>
        {
            if (MatchesFilter(unique.Params[1])) percent += unique.Params[0].ToInt();
        });
        bonus = unchecked(bonus * (100 + percent) / 100);
        if (!IsCapital()) return bonus;
        int capitalPercent = 0;
        ForEachMatchingUnique(UniqueType.CapitalThemingBonusMultiplier, unique => capitalPercent += unique.Params[0].ToInt());
        return unchecked(bonus * (100 + capitalPercent) / 100);
    }

    /// <summary>The theming bonus of all buildings of the city.</summary>
    public int GetThemingBonuses() => GetGreatWorkBuildings().Sum(GetThemingBonus);

    /// <summary>
    /// The stats the works of the city give (spec 4.2.2): per work <c>baseCulturePerGreatWork</c> Culture plus each
    /// <c>[stats] from each Great Work [cityFilter]</c> unique, plus the theming bonuses as Culture. Empty unless the ruleset enables Great Works.
    /// Tourism is not a stat of this engine: it is computed by <see cref="Tourism.TourismRules"/> (B008).
    /// </summary>
    public Stats GetGreatWorkStats()
    {
        var stats = new Stats();
        if (!GreatWorkRules.IsEnabled(GetRuleset())) return stats;
        int works = GetNumberOfGreatWorks();
        if (works == 0) return stats;
        stats.Culture += unchecked(works * GetRuleset().ModOptions.Constants.BaseCulturePerGreatWork);
        ForEachMatchingUnique(UniqueType.YieldPerGreatWork, unique =>
        {
            if (MatchesFilter(unique.Params[1])) stats.Add(unique.Stats * (float)works);
        });
        stats.Culture += GetThemingBonuses();
        return stats;
    }
}
