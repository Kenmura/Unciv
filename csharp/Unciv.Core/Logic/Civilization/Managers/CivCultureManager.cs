using System.Text.Json.Serialization;
using Unciv.Json;

namespace Unciv.Logic.Civilization.Managers;

// Brave New World (ticket B008, spec id B031, bnw-spec/SPEC.md section 2.2 and 4.3). Not part of the Kotlin source.
// The influence ("culture on") a civ has over every other civ, and the snapshot of last turn the trend needs.

/// <summary>What one civ knows about its cultural influence over another civ.</summary>
public class InfluenceEntry : IsPartOfGameInfoSerialization
{
    /// <summary>Civ id of the civ the influence is over.</summary>
    [JsonPropertyName("civ")]
    public string Civ { get; set; } = "";

    /// <summary>Influence accumulated over <see cref="Civ"/> (<c>InfluenceOn[civ]</c>).</summary>
    [JsonPropertyName("influence")]
    public int Influence { get; set; }

    /// <summary><c>LastTurnInfluenceOn[civ]</c>: the influence at the start of this civ's previous turn processing.</summary>
    [JsonPropertyName("lastTurnInfluence")]
    public int LastTurnInfluence { get; set; }

    /// <summary><c>LastTurnLifetimeCulture[civ]</c>: the lifetime culture of <see cref="Civ"/> at the same moment.</summary>
    [JsonPropertyName("lastTurnLifetimeCulture")]
    public int LastTurnLifetimeCulture { get; set; }

    public InfluenceEntry Clone() => new() { Civ = Civ, Influence = Influence, LastTurnInfluence = LastTurnInfluence, LastTurnLifetimeCulture = LastTurnLifetimeCulture };
}

/// <summary>
/// Per-civ tourism state (spec 2.2 <c>Civilization.Culture</c>). Null on <see cref="Civilization"/> until the first turn of a game with Great Works
/// and tourism, so every G&amp;K save serialises as before. Entries are kept sorted by civ id (ordinal): a canonical form for saves and the checksum.
/// </summary>
public class CivCultureManager : IsPartOfGameInfoSerialization
{
    [JsonPropertyName("influence")]
    public List<InfluenceEntry> Entries { get; set; } = [];

    /// <summary>Turns left of the temporary tourism boost (<c>TEMPORARY_TOURISM_BOOST_MOD</c>, spec 4.3.1). Who grants it is open (no ticket yet).</summary>
    [JsonPropertyName("tourismBonusTurns")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int TourismBonusTurns { get; set; }

    public InfluenceEntry? Find(string civ) => Entries.FirstOrDefault(it => it.Civ == civ);

    public int InfluenceOn(string civ) => Find(civ)?.Influence ?? 0;

    public int LastTurnInfluenceOn(string civ) => Find(civ)?.LastTurnInfluence ?? 0;

    public int LastTurnLifetimeCultureOf(string civ) => Find(civ)?.LastTurnLifetimeCulture ?? 0;

    /// <summary>The entry for <paramref name="civ"/>, created in sorted position when missing.</summary>
    public InfluenceEntry GetOrAdd(string civ)
    {
        var entry = Find(civ);
        if (entry != null) return entry;
        entry = new InfluenceEntry { Civ = civ };
        Entries.Add(entry);
        Entries = Entries.OrderBy(it => it.Civ, StringComparer.Ordinal).ToList();
        return entry;
    }

    /// <summary>Adds (unchecked, like every Civ V counter) to the influence over <paramref name="civ"/>.</summary>
    public void AddInfluence(string civ, int amount)
    {
        var entry = GetOrAdd(civ);
        entry.Influence = unchecked(entry.Influence + amount);
    }

    public void AddTourismBonusTurns(int turns) => TourismBonusTurns = Math.Max(0, unchecked(TourismBonusTurns + turns));

    public CivCultureManager Clone() => new() { Entries = Entries.Select(it => it.Clone()).ToList(), TourismBonusTurns = TourismBonusTurns };
}
