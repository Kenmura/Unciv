using System.Text.Json.Serialization;
using Unciv.Json;
using Unciv.Logic.Map.Tiles;
using Unciv.Models;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;

namespace Unciv.Logic.Civilization.Managers;

// Ported from core/src/com/unciv/logic/civilization/managers/EspionageManager.kt (T057).
public class EspionageManager : IsPartOfGameInfoSerialization
{
    [JsonPropertyName("spyList")]
    public List<Spy> SpyList { get; set; } = [];
    [JsonPropertyName("erasSpyEarnedFor")]
    [JsonInclude]
    public LinkedHashSet<string> ErasSpyEarnedFor { get; private set; } = new();

    private Civilization? civInfo;
    /// <summary>Transient. Kotlin <c>lateinit var</c>: reading before assignment throws.</summary>
    [JsonIgnore]
    public Civilization CivInfo
    {
        get => civInfo ?? throw new InvalidOperationException("lateinit property civInfo has not been initialized");
        set => civInfo = value;
    }

    /// <summary>
    /// Transient. Part of the nextTurnAction of MoveSpies.
    /// We need to store if the player has clicked the button already
    /// </summary>
    [JsonIgnore]
    public bool DismissedShouldMoveSpies { get; set; }

    public EspionageManager Clone()
    {
        var toReturn = new EspionageManager();
        foreach (var spy in SpyList) toReturn.SpyList.Add(spy.Clone());
        toReturn.ErasSpyEarnedFor.AddAll(ErasSpyEarnedFor);
        return toReturn;
    }

    public void SetTransients(Civilization civInfo)
    {
        CivInfo = civInfo;
        foreach (var spy in SpyList)
        {
            spy.SetTransients(civInfo);
        }
    }

    public void EndTurn()
    {
        foreach (var spy in SpyList.ToList())
            spy.EndTurn();
    }

    public string GetSpyName()
    {
        var rng = CivInfo.State.StateBasedRandom("EspianageManager.getSpyName");
        var usedSpyNames = SpyList.Select(it => it.Name).ToHashSet();
        var validSpyNames = CivInfo.Nation.SpyNames.Where(it => !usedSpyNames.Contains(it)).ToList();
        return validSpyNames.RandomOrNull(rng)
            ?? $"Spy {SpyList.Count + 1}"; // +1 as non-programmers count from 1
    }

    public Spy AddSpy()
    {
        string spyName = GetSpyName();
        var newSpy = new Spy(spyName, GetStartingSpyRank());
        newSpy.SetTransients(CivInfo);
        SpyList.Add(newSpy);
        return newSpy;
    }

    public IEnumerable<Tile> GetTilesVisibleViaSpies() =>
        SpyList
            .Where(it => it.IsSetUp())
            .Select(it => it.GetCityOrNull())
            .OfType<CityObject>()
            .SelectMany(it => it.GetCenterTile().GetTilesInDistance(1));

    public LinkedHashSet<string> GetTechsToSteal(Civilization otherCiv)
    {
        var techsToSteal = new LinkedHashSet<string>();
        foreach (var tech in otherCiv.Tech.TechsResearched)
        {
            if (CivInfo.Tech.IsResearched(tech)) continue;
            if (!CivInfo.Tech.CanBeResearched(tech)) continue;
            techsToSteal.Add(tech);
        }
        return techsToSteal;
    }

    /// <summary>BNW (B020): this civ's diplomats that have finished moving and are in the capital of another major civ.</summary>
    public IEnumerable<Spy> GetDiplomats() =>
        SpyList.Where(it => it.IsDiplomat && it.IsSetUp() && it.GetCityOrNull() is { } c && c.IsCapital() && c.Civ.IsMajorCiv());

    public int CountDiplomats() => GetDiplomats().Count();

    /// <summary>A diplomat of this civ sits in <paramref name="target"/>'s capital (tourism bonus, votes in the World Congress become visible).</summary>
    public bool HasDiplomatIn(Civilization target) => GetDiplomats().Any(it => ReferenceEquals(it.GetCity().Civ, target));

    public List<Spy> GetSpiesInCity(CityObject city) => SpyList.Where(it => it.GetCityOrNull() == city).ToList();

    public int GetStartingSpyRank() =>
        1 + KotlinMath.SumOf(CivInfo.GetMatchingUniques(UniqueType.SpyStartingLevel), it => KotlinMath.ParseInt(it.Params[0]));

    /// <summary>
    /// Returns a list of all cities with our spies in them.
    /// The list needs to be stable across calls on the same turn.
    /// </summary>
    public List<CityObject> GetCitiesWithOurSpies() =>
        SpyList.Where(it => it.IsSetUp()).Select(it => it.GetCityOrNull()).OfType<CityObject>().ToList();

    public Spy? GetSpyAssignedToCity(CityObject city) => SpyList.FirstOrDefault(it => it.GetCityOrNull() == city);

    /// <summary>
    /// Determines whether the NextTurnAction MoveSpies should be shown or not
    /// </summary>
    /// <returns>true if there are spies waiting to be moved</returns>
    public bool ShouldShowMoveSpies() => !DismissedShouldMoveSpies && SpyList.Any(it => it.IsIdle())
        && CivInfo.GameInfo.GetCities().Any(it => CivInfo.HasExplored(it.GetCenterTile()) && GetSpyAssignedToCity(it) == null);

    public List<Spy> GetIdleSpies() => SpyList.Where(it => it.IsIdle()).ToList();

    /// <summary>
    /// Takes all spies away from their cities.
    /// Called when the civ is destroyed.
    /// </summary>
    public void RemoveAllSpies()
    {
        foreach (var spy in SpyList) spy.MoveTo(null);
    }

    /// <summary>
    /// Recalls all spies stationed in any of <paramref name="otherCiv"/>'s cities to the hideout.
    ///
    /// Called when <paramref name="otherCiv"/> is destroyed via console <c>civ remove</c>.
    /// Not using CityEspionageManager.removeAllPresentSpies, so no notifications.
    /// </summary>
    internal void RecallAllSpiesFrom(Civilization otherCiv)
    {
        var toRecall = SpyList.Where(it => it.GetCityOrNull() is { } city && otherCiv.Cities.Contains(city)).ToList();
        foreach (var spy in toRecall)
            spy.MoveTo(null);
    }
}
