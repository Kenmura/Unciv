using System.Text.Json.Serialization;
using Unciv.Json;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Transients;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;
using Unciv.Models;
using Unciv.Models.Ruleset.Unique;
using Unciv.UI.Components.Extensions;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Logic.City.Managers;

// Ported from core/src/com/unciv/logic/city/managers/CityReligionManager.kt (T061).
//
// Stripped: the purity annotations. Kept: the deprecated getUniques (City.getMatchingUniques-style callers still use it).
// Traps: `remainders` is a java.util.HashMap whose iteration order picks the tie winner of the largest remainder (JavaHashMap);
// `maxByOrNull` keeps the first maximum and compares Floats with Float.compareTo (AStar.JavaFloatCompare); Counter.get(null) is 0;
// the pressure per follower is an Int division. The members the City tests replace (the majority religion, pressure, followers and
// inquisitor queries) are virtual.

public class CityReligionManager : IsPartOfGameInfoSerialization
{
    private CityObject? city;
    /// <summary>Kotlin <c>@Transient lateinit var</c>, set by <see cref="SetTransients"/>: reading before assignment throws.</summary>
    [JsonIgnore]
    public CityObject City
    {
        get => city ?? throw new InvalidOperationException("lateinit property city has not been initialized");
        private set => city = value;
    }

    // This needs to be kept track of for the
    // "[Stats] when a city adopts this religion for the first time" unique
    [JsonPropertyName("religionsAtSomePointAdopted")]
    [JsonInclude]
    private JavaHashSet<string> religionsAtSomePointAdopted = new();

    /// <summary>T219: rebuild the hash set like a load does, see <see cref="JavaHashMap{TKey,TValue}.Canonicalize"/>.</summary>
    internal void Canonicalize() => religionsAtSomePointAdopted.Canonicalize();

    [JsonPropertyName("pressures")]
    [JsonInclude]
    private Counter<string> pressures = new();
    // Cached because using `updateNumberOfFollowers` to get this value resulted in many calls
    private readonly Counter<string> followers = new();

    private int? pressureFromAdjacentCitiesCache; // Kotlin `by lazy`
    private int PressureFromAdjacentCities => pressureFromAdjacentCitiesCache ??= City.Civ.GameInfo.Speed.ReligiousPressureAdjacentCity;

    [JsonPropertyName("religionThisIsTheHolyCityOf")]
    public string? ReligionThisIsTheHolyCityOf { get; set; }
    [JsonPropertyName("isBlockedHolyCity")]
    public bool IsBlockedHolyCity { get; set; }

    public CityReligionManager()
    {
        ClearAllPressures();
    }

    public CityReligionManager Clone()
    {
        var toReturn = new CityReligionManager
        {
            City = City,
        };
        foreach (var religion in religionsAtSomePointAdopted) toReturn.religionsAtSomePointAdopted.Add(religion);
        toReturn.pressures.PutAll(pressures);
        toReturn.followers.PutAll(followers);
        toReturn.ReligionThisIsTheHolyCityOf = ReligionThisIsTheHolyCityOf;
        toReturn.IsBlockedHolyCity = IsBlockedHolyCity;
        return toReturn;
    }

    public virtual void SetTransients(CityObject city)
    {
        City = city;
        // We don't need to check for changes in the majority religion, and as this
        // loads in the religion, _of course_ the religion changes, but it shouldn't
        // have any effect
        UpdateNumberOfFollowers(false);
    }

    public void EndTurn()
    {
        GetAffectedBySurroundingCities();
    }

    /// <summary>Kotlin @Deprecated: forEachUnique is faster. If not viable, then this can still be used.</summary>
    public IEnumerable<UniqueObject> GetUniques(UniqueType uniqueType)
    {
        var majorityReligion = GetMajorityReligion();
        if (majorityReligion == null) return [];
        return majorityReligion.FollowerBeliefUniqueMap.GetUniques(uniqueType);
    }

    public void ForEachMatchingUnique(UniqueType uniqueType, Action<UniqueObject> op) =>
        ForEachMatchingUnique(uniqueType, City.State, op);

    public void ForEachMatchingUnique(UniqueType uniqueType, GameContext gameContext, Action<UniqueObject> op) =>
        ForEachMatchingUnique(uniqueType, gameContext, UniqueMap.NoUniqueFilter, op);

    public void ForEachMatchingUnique(UniqueType uniqueType, Func<UniqueObject, bool> filter, Action<UniqueObject> op) =>
        ForEachMatchingUnique(uniqueType, City.State, filter, op);

    public void ForEachMatchingUnique(UniqueType uniqueType, GameContext gameContext, Func<UniqueObject, bool> filter, Action<UniqueObject> op)
    {
        var majorityReligion = GetMajorityReligion();
        if (majorityReligion == null) return;
        majorityReligion.FollowerBeliefUniqueMap.ForEachMatchingUnique(uniqueType, gameContext, filter, op);
    }

    public IEnumerable<UniqueObject> GetAllUniques()
    {
        var majorityReligion = GetMajorityReligion();
        if (majorityReligion == null) return [];
        return majorityReligion.FollowerBeliefUniqueMap.GetAllUniques();
    }

    public void ForEachUnique(Func<UniqueObject, bool> filter, Action<UniqueObject> op)
    {
        var majorityReligion = GetMajorityReligion();
        if (majorityReligion == null) return;
        majorityReligion.FollowerBeliefUniqueMap.ForEachUnique(filter, op);
    }

    public Counter<string> GetPressures() => pressures.Clone();

    private void ClearAllPressures()
    {
        pressures.Clear();
        // We add pressure for following no religion
        // Basically used as a failsafe so that there is always some religion,
        // and we don't suddenly divide by 0 somewhere
        // Should be removed when updating the followers so it never becomes the majority religion,
        // `null` is used for that instead.
        pressures.Add(Constants.NoReligionName, 100);
    }

    public virtual void AddPressure(string religionName, int amount, bool shouldUpdateFollowers = true)
    {
        if (!City.Civ.GameInfo.IsReligionEnabled()) return; // No religion, no pressures
        pressures.Add(religionName, amount);

        if (shouldUpdateFollowers) UpdateNumberOfFollowers();
    }

    public void RemoveAllPressuresExceptFor(string religion)
    {
        var pressureFromThisReligion = pressures[religion];
        // Atheism is never removed
        var pressureFromAtheism = pressures[Constants.NoReligionName];
        ClearAllPressures();
        pressures.Add(religion, pressureFromThisReligion);
        if (pressureFromAtheism != 0) pressures[Constants.NoReligionName] = pressureFromAtheism;
        UpdateNumberOfFollowers();
    }

    public void UpdatePressureOnPopulationChange(int populationChangeAmount)
    {
        var majorityReligion =
            GetMajorityReligionName() != null ? GetMajorityReligionName().NotNull()
            : Constants.NoReligionName;

        if (populationChangeAmount > 0)
        {
            AddPressure(majorityReligion, 100 * populationChangeAmount);
        }
        else
        {
            UpdateNumberOfFollowers();
        }
    }

    private void TriggerReligionAdoption(string newMajorityReligion)
    {
        var newMajorityReligionObject = City.Civ.GameInfo.Religions[newMajorityReligion];
        City.Civ.AddNotification($"Your city [{City.Name}] was converted to [{newMajorityReligionObject.GetReligionDisplayName()}]!",
            City.Location, NotificationCategory.Religion, NotificationIcon.Faith);

        if (religionsAtSomePointAdopted.Contains(newMajorityReligion)) return;

        var religionOwningCiv = newMajorityReligionObject.FoundingCiv;
        if (religionOwningCiv.HasUnique(UniqueType.StatsWhenAdoptingReligion))
        {
            var statsGranted =
                religionOwningCiv.GetMatchingUniques(UniqueType.StatsWhenAdoptingReligion)
                    .Select(it => it.Stats * (!it.IsModifiedByGameSpeed() ? 1f : City.Civ.GameInfo.Speed.Modifier))
                    .Aggregate((acc, stats) => acc + stats);

            foreach (var (key, value) in statsGranted)
                religionOwningCiv.AddStat(key, KotlinMath.FloatToInt(value));
            if (religionOwningCiv.HasExplored(City.GetCenterTile()))
                religionOwningCiv.AddNotification(
                    $"You gained [{statsGranted}] as your religion was spread to [{City.Name}]",
                    City.Location,
                    NotificationCategory.Religion,
                    NotificationIcon.Faith);
            else
                religionOwningCiv.AddNotification(
                    $"You gained [{statsGranted}] as your religion was spread to an unknown city",
                    NotificationCategory.Religion,
                    NotificationIcon.Faith);
        }
        religionsAtSomePointAdopted.Add(newMajorityReligion);
    }

    private void UpdateNumberOfFollowers(bool checkForReligionAdoption = true)
    {
        var oldMajorityReligion =
            checkForReligionAdoption ? GetMajorityReligionName()
            : null;

        var previousFollowers = followers.Clone();
        followers.Clear();

        if (City.Population.Population <= 0) return;

        var remainders = new JavaHashMap<string, float>();
        var pressurePerFollower = pressures.SumValues() / City.Population.Population;

        // First give each religion an approximate share based on pressure
        foreach (var (religion, pressure) in pressures)
        {
            var followersOfThisReligion = KotlinMath.FloatToInt(pressure / (float)pressurePerFollower);
            followers.Add(religion, followersOfThisReligion);
            remainders.Put(religion, (float)pressure - followersOfThisReligion * pressurePerFollower);
        }

        var unallocatedPopulation = City.Population.Population - followers.SumValues();

        // Divide up the remaining population
        while (unallocatedPopulation > 0)
        {
            var largestRemainder = MaxRemainder(remainders);
            if (largestRemainder == null)
            {
                followers.Add(Constants.NoReligionName, unallocatedPopulation);
                break;
            }
            followers.Add(largestRemainder, 1);
            remainders.Put(largestRemainder, 0f);
            unallocatedPopulation -= 1;
        }

        followers.Remove(Constants.NoReligionName);

        if (checkForReligionAdoption)
        {
            var newMajorityReligion = GetMajorityReligionName();
            if (oldMajorityReligion != newMajorityReligion && newMajorityReligion != null)
            {
                TriggerReligionAdoption(newMajorityReligion);
            }
            if (oldMajorityReligion != newMajorityReligion)
                City.Civ.Cache.UpdateCivResources(); // follower uniques can provide resources
            if (!followers.Equals(previousFollowers))
                City.CityStats.Update();
        }
    }

    /// <summary><c>remainders.maxByOrNull { it.value }?.key</c>: the first entry of the java.util.HashMap order with the maximal Float.compareTo.</summary>
    private static string? MaxRemainder(JavaHashMap<string, float> remainders)
    {
        string? best = null;
        var bestValue = 0f;
        foreach (var (key, value) in remainders)
        {
            if (best == null || AStar.JavaFloatCompare(bestValue, value) < 0)
            {
                best = key;
                bestValue = value;
            }
        }
        return best;
    }

    public Counter<string> GetNumberOfFollowers() => followers.Clone();
    public virtual int GetFollowersOf(string religion) => followers[religion];

    public virtual int GetFollowersOfMajorityReligion()
    {
        var majorityReligion = GetMajorityReligionName();
        if (majorityReligion == null) return 0;
        return followers[majorityReligion];
    }

    public int GetFollowersOfOtherReligionsThan(string religion) =>
        followers.Where(it => it.Key != religion).Sum(it => it.Value);

    /// <summary>Drops every trace of a religion that no longer exists (dev console <c>civ remove</c> of its founder).</summary>
    public void ForgetReligion(string religionName)
    {
        pressures.Remove(religionName);
        religionsAtSomePointAdopted.Remove(religionName);
        if (ReligionThisIsTheHolyCityOf == religionName) ReligionThisIsTheHolyCityOf = null;
        UpdateNumberOfFollowers(false);
    }

    /// <summary>
    /// Removes all pantheons except for the one founded by the current owner of the city
    /// Should be called whenever a city changes hands, e.g. conquering and trading
    /// </summary>
    public void RemoveUnknownPantheons()
    {
        foreach (var pressure in pressures.Keys.ToList())
        {  // Copy the keys because we might modify
            if (pressure == Constants.NoReligionName) continue;
            var correspondingReligion = City.Civ.GameInfo.Religions[pressure];
            if (correspondingReligion.IsPantheon()
                && !ReferenceEquals(correspondingReligion.FoundingCiv, City.Civ)
            )
            {
                pressures.Remove(pressure);
            }
        }
        UpdateNumberOfFollowers();
    }

    public virtual string? GetMajorityReligionName()
    {
        if (followers.IsEmpty()) return null;
        string? religionWithMaxPressure = null;
        var maxFollowers = 0;
        foreach (var (religion, count) in followers)
        {
            if (religionWithMaxPressure == null || maxFollowers < count)
            {
                religionWithMaxPressure = religion;
                maxFollowers = count;
            }
        }
        if (religionWithMaxPressure == Constants.NoReligionName) return null;
        if (followers[religionWithMaxPressure.NotNull()] >= City.Population.Population / 2) return religionWithMaxPressure;
        return null;
    }

    public virtual Religion? GetMajorityReligion()
    {
        var majorityReligionName = GetMajorityReligionName();
        if (majorityReligionName == null) return null;
        return City.Civ.GameInfo.Religions.TryGetValue(majorityReligionName, out var religion) ? religion : null;
    }

    private void GetAffectedBySurroundingCities()
    {
        if (!City.Civ.GameInfo.IsReligionEnabled()) return; // No religion, no spreading
        // We don't update the amount of followers yet, as only the end result should matter
        // If multiple religions would become the majority religion due to pressure,
        // this will make it so we only receive a notification for the last one.
        // Also, doing it like this increases performance :D
        if (City.IsHolyCity())
        {
            AddPressure(ReligionThisIsTheHolyCityOf.NotNull(), 5 * PressureFromAdjacentCities, false);
        }

        foreach (var otherCity in City.Civ.GameInfo.GetCities())
        {
            if (ReferenceEquals(otherCity, City)) continue;
            var majorityReligionOfCity = otherCity.Religion.GetMajorityReligionName();
            if (majorityReligionOfCity == null) continue;
            if (!City.Civ.GameInfo.Religions[majorityReligionOfCity].IsMajorReligion()) continue;
            if (otherCity.GetCenterTile().AerialDistanceTo(City.GetCenterTile())
                    > otherCity.Religion.GetSpreadRange()) continue;
            AddPressure(majorityReligionOfCity, otherCity.Religion.PressureAmountToAdjacentCities(City), false);
        }

        UpdateNumberOfFollowers();
    }

    private int GetSpreadRange()
    {
        var spreadRange = 10;

        City.ForEachMatchingUnique(UniqueType.ReligionSpreadDistance, unique =>
        {
            spreadRange += unique.Params[0].ToInt();
        });

        var majorityReligion = GetMajorityReligion();
        if (majorityReligion != null)
        {
            majorityReligion.FoundingCiv.ForEachMatchingUnique(UniqueType.ReligionSpreadDistance, unique =>
            {
                spreadRange += unique.Params[0].ToInt();
            });
        }

        return spreadRange;
    }

    /// <summary>Doesn't update the pressures, only returns what they are if the update were to happen right now</summary>
    public Counter<string> GetPressuresFromSurroundingCities()
    {
        var addedPressure = new Counter<string>();
        if (City.IsHolyCity())
        {
            addedPressure[ReligionThisIsTheHolyCityOf.NotNull()] = 5 * PressureFromAdjacentCities;
        }
        var allCitiesWithin10Tiles =
            City.Civ.GameInfo.GetCities()
                .Where(it =>
                    !ReferenceEquals(it, City)
                    && it.GetCenterTile().AerialDistanceTo(City.GetCenterTile()) <= it.Religion.GetSpreadRange());
        foreach (var otherCity in allCitiesWithin10Tiles)
        {
            var majorityReligionOfCity = otherCity.Religion.GetMajorityReligion();
            if (majorityReligionOfCity == null) continue;
            if (!majorityReligionOfCity.IsMajorReligion()) continue;
            addedPressure.Add(majorityReligionOfCity.Name, otherCity.Religion.PressureAmountToAdjacentCities(City));
        }
        return addedPressure;
    }

    public virtual bool IsProtectedByInquisitor(string? fromReligion = null)
    {
        foreach (var tile in City.GetCenterTile().GetTilesInDistance(1))
        {
            foreach (var unit in new[] { tile.CivilianUnit, tile.MilitaryUnit })
            {
                var mapUnit = (MapUnit?)unit;
                if (mapUnit?.Religion != null
                    && (fromReligion == null || mapUnit.Religion != fromReligion)
                    && mapUnit.HasUnique(UniqueType.PreventSpreadingReligion)
                ) return true;
            }
        }
        return false;
    }

    private int PressureAmountToAdjacentCities(CityObject pressuredCity)
    {
        var pressure = (float)PressureFromAdjacentCities;

        // Follower beliefs of this religion
        City.ForEachMatchingUnique(UniqueType.NaturalReligionSpreadStrength, unique =>
        {
            if (pressuredCity.MatchesFilter(unique.Params[1]))
                pressure *= unique.Params[0].ToPercent();
        });

        // Founder beliefs of this religion
        var majorityReligion = GetMajorityReligion();
        if (majorityReligion != null)
        {
            majorityReligion.FoundingCiv.ForEachMatchingUnique(UniqueType.NaturalReligionSpreadStrength, unique =>
            {
                if (pressuredCity.MatchesFilter(unique.Params[1]))
                    pressure *= unique.Params[0].ToPercent();
            });
        }

        if (majorityReligion != null && City.Civ.GameInfo.LeagueReligionSpreadPercent(majorityReligion.Name) is var leagueSpread and not 0)
            pressure *= 1 + leagueSpread / 100f; // B016: World Religion

        return KotlinMath.FloatToInt(pressure);
    }

    /// <summary>
    /// Calculates how much pressure this religion is lacking compared to the majority religion
    /// That is, if we gain more than this, we'll be the majority
    /// </summary>
    public int GetPressureDeficit(string? otherReligion)
    {
        var currentPressures = GetPressures();
        var majorityReligion = GetMajorityReligionName();
        return (majorityReligion == null ? 0 : currentPressures[majorityReligion]) - (otherReligion == null ? 0 : currentPressures[otherReligion]);
    }
}
