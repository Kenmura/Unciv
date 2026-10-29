using System.Text.Json.Serialization;
using Unciv.Json;
using Unciv.Logic.Map.MapUnits;
using Unciv.Logic.Tourism;
using Unciv.Models;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.UI.Components;
using Unciv.Utils;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;

namespace Unciv.Logic.Civilization.Managers;

// Ported from core/src/com/unciv/logic/civilization/managers/GreatPersonManager.kt (T056).
// Stripped: the purity annotations.
// Deviation: getGreatPeople() is a Kotlin HashSet<BaseUnit> (identity hash, so its order is arbitrary); it is a LinkedHashSet here
// (ruleset order), which only makes the order of getFreeGreatPersonOptions() deterministic.

// todo: Great Admiral?
// todo: Free GP from policies and wonders should increase threshold according to the wiki
// todo: GP from Maya long count should increase threshold as well - implement together

public class GreatPersonManager : IsPartOfGameInfoSerialization
{
    private Civilization? civInfo;
    /// <summary>Kotlin <c>@Transient lateinit var</c>, set by <c>Civilization.setTransients</c>: reading before assignment throws.</summary>
    [JsonIgnore]
    public Civilization CivInfo
    {
        get => civInfo ?? throw new InvalidOperationException("lateinit property civInfo has not been initialized");
        set => civInfo = value;
    }

    /// <summary>Base points, without speed modifier</summary>
    [JsonPropertyName("pointsForNextGreatPersonCounter")]
    public Counter<string> PointsForNextGreatPersonCounter { get; set; } = new();  // Initial values assigned in getPointsRequiredForGreatPerson as needed
    [JsonPropertyName("pointsForNextGreatGeneral")]
    public int PointsForNextGreatGeneral { get; set; } = 200;
    [JsonPropertyName("pointsForNextGreatGeneralCounter")]
    public Counter<string> PointsForNextGreatGeneralCounter { get; set; } = new(); // Initial values assigned when needed

    [JsonPropertyName("greatPersonPointsCounter")]
    public Counter<string> GreatPersonPointsCounter { get; set; } = new();
    [JsonPropertyName("greatGeneralPointsCounter")]
    public Counter<string> GreatGeneralPointsCounter { get; set; } = new();
    [JsonPropertyName("greatGeneralPoints")]
    public int GreatGeneralPoints { get; set; }
    [JsonPropertyName("freeGreatPeople")]
    public int FreeGreatPeople { get; set; }
    /// <summary>Marks subset of <see cref="FreeGreatPeople"/> as subject to maya ability restrictions (each only once until all used)</summary>
    [JsonPropertyName("mayaLimitedFreeGP")]
    public int MayaLimitedFreeGP { get; set; }
    /// <summary>Remaining candidates for maya ability - whenever empty refilled from all GP, starts out empty</summary>
    [JsonPropertyName("longCountGPPool")]
    public JavaHashSet<string> LongCountGPPool { get; set; } = new();

    public GreatPersonManager Clone()
    {
        var toReturn = new GreatPersonManager
        {
            FreeGreatPeople = FreeGreatPeople,
            GreatPersonPointsCounter = GreatPersonPointsCounter.Clone(),
            PointsForNextGreatPersonCounter = PointsForNextGreatPersonCounter.Clone(),
            PointsForNextGreatGeneralCounter = PointsForNextGreatGeneralCounter.Clone(),
            GreatGeneralPointsCounter = GreatGeneralPointsCounter.Clone(),
            PointsForNextGreatGeneral = PointsForNextGreatGeneral,
            GreatGeneralPoints = GreatGeneralPoints,
            MayaLimitedFreeGP = MayaLimitedFreeGP,
            LongCountGPPool = JavaHashSet<string>.ToHashSet(LongCountGPPool),
        };
        return toReturn;
    }

    private string GetPoolKey(string greatPerson) =>
        // An empty string is used to indicate the Unique wasn't found
        ((IHasUniques)CivInfo.GetEquivalentUnit(greatPerson))
            .GetMatchingUniques(UniqueType.GPPointPool)
            .FirstOrDefault()?.Params[0] ?? "";

    public int GetPointsRequiredForGreatPerson(string greatPerson)
    {
        var key = GetPoolKey(greatPerson);
        if (PointsForNextGreatPersonCounter[key] == 0)
        {
            PointsForNextGreatPersonCounter[key] = 100;
        }
        return KotlinMath.FloatToInt(PointsForNextGreatPersonCounter[key] * CivInfo.GameInfo.Speed.Modifier);
    }

    /// <summary>T197: <see cref="GetPointsRequiredForGreatPerson"/> without its lazy initialisation of the counter (a query must not change the saved state).</summary>
    internal int PeekPointsRequiredForGreatPerson(string greatPerson)
    {
        int points = PointsForNextGreatPersonCounter[GetPoolKey(greatPerson)];
        return KotlinMath.FloatToInt((points == 0 ? 100 : points) * CivInfo.GameInfo.Speed.Modifier);
    }

    public string? GetNewGreatPerson()
    {
        foreach (var (unit, value) in GreatGeneralPointsCounter)
        {
            if (PointsForNextGreatGeneralCounter[unit] == 0)
            {
                PointsForNextGreatGeneralCounter[unit] = 200;
            }
            var requiredPoints = PointsForNextGreatGeneralCounter[unit];
            if (value >= requiredPoints)
            {
                GreatGeneralPointsCounter[unit] -= requiredPoints;
                PointsForNextGreatGeneralCounter[unit] += 50;
                return unit;
            }
        }

        foreach (var (greatPerson, value) in GreatPersonPointsCounter)
        {
            var requiredPoints = GetPointsRequiredForGreatPerson(greatPerson);
            if (value >= requiredPoints)
            {
                GreatPersonPointsCounter.Add(greatPerson, -requiredPoints);
                PointsForNextGreatPersonCounter[GetPoolKey(greatPerson)] *= 2;
                return greatPerson;
            }
        }
        return null;
    }

    public void AddGreatPersonPoints()
    {
        GreatPersonPointsCounter.Add(GetGreatPersonPointsForNextTurn());
    }

    public void TriggerMayanGreatPerson()
    {
        if (CivInfo.IsSpectator()) return;
        var greatPeople = GetGreatPeople();
        if (LongCountGPPool.Count == 0)
            LongCountGPPool = JavaHashSet<string>.ToHashSet(greatPeople.Select(it => it.Name));

        FreeGreatPeople++;
        MayaLimitedFreeGP++;

        // Anyone an idea for a good icon?
        const string notification = "{A new b'ak'tun has just begun!}\n{A Great Person joins you!}";
        CivInfo.AddNotification(notification, new MayaLongCountAction(), NotificationCategory.General, MayaCalendar.NotificationIcon);
    }

    /// <summary>Get Great People specific to this manager's Civilization, already filtered by <c>isHiddenBySettings</c></summary>
    public LinkedHashSet<BaseUnit> GetGreatPeople()
    {
        var result = new LinkedHashSet<BaseUnit>();
        foreach (var unit in CivInfo.GameInfo.Ruleset.Units.Values.Where(it => it.IsGreatPerson))
        {
            var equivalent = CivInfo.GetEquivalentUnit(unit.Name);
            if (!equivalent.IsUnavailableBySettings(CivInfo.GameInfo)) result.Add(equivalent);
        }
        return result;
    }

    /// <summary>
    /// Returns the Great People this Civilization can currently select for a free grant.
    ///
    /// An option being present does not guarantee placement will succeed when it is chosen.
    /// </summary>
    public IReadOnlySet<BaseUnit> GetFreeGreatPersonOptions()
    {
        if (FreeGreatPeople <= 0 || CivInfo.Cities.Count == 0) return new LinkedHashSet<BaseUnit>();

        var greatPeople = GetGreatPeople();
        if (MayaLimitedFreeGP <= 0) return greatPeople;
        return new LinkedHashSet<BaseUnit>(greatPeople.Where(it => LongCountGPPool.Contains(it.Name)));
    }

    /// <summary>
    /// Attempts to grant the named option, returning the placed unit or <c>null</c> if the choice is not
    /// currently available or placement fails. A failed placement consumes no entitlement or Maya
    /// pool entry, though the existing placement internals may already have allocated a unit ID.
    /// </summary>
    public MapUnit? ChooseFreeGreatPerson(string unitName)
    {
        var matching = GetFreeGreatPersonOptions().Where(it => it.Name == unitName).Take(2).ToList();
        if (matching.Count != 1) return null; // singleOrNull
        var choice = matching[0];
        var useMayaLongCount = MayaLimitedFreeGP > 0;
        var unit = CivInfo.Units.AddUnit(choice, CivInfo.GetCapital());
        if (unit == null) return null;

        InfluenceRules.ApplyBirthTourism(CivInfo, choice.Name); // BNW (B011): no-op without the unique
        FreeGreatPeople--;
        if (useMayaLongCount)
        {
            MayaLimitedFreeGP--;
            LongCountGPPool.Remove(choice.Name);
        }
        return unit;
    }

    public Counter<string> GetGreatPersonPointsForNextTurn()
    {
        var greatPersonPoints = new Counter<string>();
        foreach (var city in CivInfo.Cities) greatPersonPoints.Add(city.GetGreatPersonPoints());
        return greatPersonPoints;
    }
}
