using System.Text.Json.Serialization;
using Unciv.Json;
using Unciv.Models.Ruleset.Unique;
using Unciv.UI.Components.Extensions;
using Unciv.Utils;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;

namespace Unciv.Logic.Civilization.Managers;

// Ported from core/src/com/unciv/logic/civilization/managers/GoldenAgeManager.kt (T056).
// Stripped: the purity annotations.

public class GoldenAgeManager : IsPartOfGameInfoSerialization
{
    private Civilization? civInfo;
    /// <summary>Kotlin <c>@Transient lateinit var</c>, set by <c>Civilization.setTransients</c>: reading before assignment throws.</summary>
    [JsonIgnore]
    public Civilization CivInfo
    {
        get => civInfo ?? throw new InvalidOperationException("lateinit property civInfo has not been initialized");
        set => civInfo = value;
    }

    [JsonPropertyName("storedHappiness")]
    public int StoredHappiness { get; set; }
    [JsonPropertyName("numberOfGoldenAges")]
    [JsonInclude]
    private int numberOfGoldenAges;
    [JsonPropertyName("turnsLeftForCurrentGoldenAge")]
    public int TurnsLeftForCurrentGoldenAge { get; set; }

    public GoldenAgeManager Clone()
    {
        var toReturn = new GoldenAgeManager
        {
            numberOfGoldenAges = numberOfGoldenAges,
            StoredHappiness = StoredHappiness,
            TurnsLeftForCurrentGoldenAge = TurnsLeftForCurrentGoldenAge,
        };
        return toReturn;
    }

    public bool IsGoldenAge() => TurnsLeftForCurrentGoldenAge > 0;

    public void AddHappiness(int amount)
    {
        StoredHappiness += amount;
    }

    public int HappinessRequiredForNextGoldenAge()
    {
        var cost = (float)(500 + numberOfGoldenAges * 250);
        cost *= CivInfo.Cities.Count.ToPercent();  //https://forums.civfanatics.com/resources/complete-guide-to-happiness-vanilla.25584/
        cost *= CivInfo.GameInfo.Speed.Modifier;
        return KotlinMath.FloatToInt(cost);
    }

    public int CalculateGoldenAgeLength(int unmodifiedNumberOfTurns)
    {
        var turnsToGoldenAge = (float)unmodifiedNumberOfTurns;
        CivInfo.ForEachMatchingUnique(UniqueType.GoldenAgeLength, unique =>
        {
            turnsToGoldenAge *= unique.Params[0].ToPercent();
        });
        turnsToGoldenAge *= CivInfo.GameInfo.Speed.GoldenAgeLengthModifier;
        return KotlinMath.FloatToInt(turnsToGoldenAge);
    }

    public void EnterGoldenAge(int unmodifiedNumberOfTurns = 10)
    {
        TurnsLeftForCurrentGoldenAge += CalculateGoldenAgeLength(unmodifiedNumberOfTurns);
        CivInfo.AddNotification("You have entered a Golden Age!",
            new CivilopediaAction("Tutorial/Golden Age"),
            NotificationCategory.General, "StatIcons/Happiness");
        CivInfo.PopupAlerts.Add(new PopupAlert(AlertType.GoldenAge, ""));

        CivInfo.ForEachTriggeredUnique(UniqueType.TriggerUponEnteringGoldenAge, CivInfo.State, _ => true, false, unique =>
        {
            UniqueTriggerActivation.TriggerUnique(unique, CivInfo);
        });
        //Golden Age can happen mid turn with Great Artist effects
        foreach (var city in CivInfo.Cities)
            city.CityStats.Update(updateCivStats: true);
    }

    public void EndTurn(int happiness)
    {
        if (!IsGoldenAge())
            StoredHappiness = Math.Max(StoredHappiness + happiness, 0);

        if (IsGoldenAge())
        {
            TurnsLeftForCurrentGoldenAge--;
            if (TurnsLeftForCurrentGoldenAge <= 0)
                CivInfo.ForEachTriggeredUnique(UniqueType.TriggerUpponEndingGoldenAge, CivInfo.State, _ => true, false, unique =>
                {
                    UniqueTriggerActivation.TriggerUnique(unique, CivInfo);
                });
        }
        else if (StoredHappiness >= HappinessRequiredForNextGoldenAge())
        {
            StoredHappiness -= HappinessRequiredForNextGoldenAge();
            EnterGoldenAge();
            numberOfGoldenAges++;
        }
    }
}
