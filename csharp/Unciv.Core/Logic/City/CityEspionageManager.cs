using System.Text.Json.Serialization;
using Unciv.Models;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Logic.City;

// Ported from core/src/com/unciv/logic/city/managers/CityEspionageManager.kt (T060).
// (The Kotlin file lives in the `managers` package; it is kept next to City because it is the only manager this ticket ports.)

public enum SpyFleeReason
{
    CityDestroyed,
    CityCaptured,
    CityBought,
    CityTakenOverByMarriage,
    Other,
}

public class CityEspionageManager
{
    // Note: Instance lives in City, but since we don't carry data, it's transient, and no `: IsPartOfGameInfoSerialization` here.

    private CityObject? city;
    /// <summary>Kotlin <c>@Transient lateinit var</c>: reading before assignment throws.</summary>
    [JsonIgnore]
    public CityObject City
    {
        get => city ?? throw new InvalidOperationException("lateinit property city has not been initialized");
        set => city = value;
    }

    public void SetTransients(CityObject city)
    {
        this.city = city;
    }

    public bool HasSpyOf(CivilizationObject civInfo) => civInfo.EspionageManager.SpyList.Any(it => it.GetCityOrNull() == City);

    public List<Spy> GetAllStationedSpies() =>
        City.Civ.GameInfo.Civilizations.SelectMany(it => it.EspionageManager.GetSpiesInCity(City)).ToList();

    public void RemoveAllPresentSpies(SpyFleeReason reason)
    {
        foreach (var spy in GetAllStationedSpies())
        {
            var notificationString = reason switch
            {
                SpyFleeReason.CityDestroyed => $"After the city of [{City.Name}] was destroyed, your spy [{spy.Name}] has fled back to our hideout.",
                SpyFleeReason.CityCaptured => $"After the city of [{City.Name}] was conquered, your spy [{spy.Name}] has fled back to our hideout.",
                SpyFleeReason.CityBought or SpyFleeReason.CityTakenOverByMarriage => $"After the city of [{City.Name}] was taken over, your spy [{spy.Name}] has fled back to our hideout.",
                _ => $"Due to the chaos ensuing in [{City.Name}], your spy [{spy.Name}] has fled back to our hideout.",
            };
            spy.AddNotification(notificationString);
            spy.MoveTo(null);
        }
    }
}
