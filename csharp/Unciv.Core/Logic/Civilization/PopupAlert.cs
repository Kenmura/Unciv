using System.Text.Json.Serialization;
using Unciv.Json;

namespace Unciv.Logic.Civilization;

// core/src/com/unciv/logic/civilization/PopupAlert.kt

/// <summary>Serialized by name (the member order is the Kotlin declaration order).</summary>
public enum AlertType
{
    Defeated,
    WonderBuilt,
    TechResearched,
    WarDeclaration,
    FirstContact,
    CityConquered,
    CityTraded,
    BorderConflict,
    TilesStolen,

    DemandToStopSettlingCitiesNear,
    CitySettledNearOtherCivDespiteOurPromise,

    DemandToStopSpreadingReligion,
    ReligionSpreadDespiteOurPromise,

    DemandToStopSpyingOnUs,
    SpyingOnUsDespiteOurPromise,

    DemandToNotAttackUs,
    AttackedUsDespitePromise,

    AcceptingDemand,
    RejectingDemand,

    GoldenAge,
    DeclarationOfFriendship,
    StartIntro,
    DiplomaticMarriage,
    BulliedProtectedMinor,
    AttackedProtectedMinor,
    AttackedAllyMinor,
    RecapturedCivilian,
    GameHasBeenWon,
    Event,

    Denounced,
}

public class PopupAlert : IsPartOfGameInfoSerialization
{
    /// <summary>Kotlin <c>lateinit var</c>: reading before assignment throws.</summary>
    [JsonPropertyName("type")]
    public AlertType Type
    {
        get => type ?? throw new InvalidOperationException("lateinit property type has not been initialized");
        set => type = value;
    }
    private AlertType? type;

    /// <summary>Kotlin <c>lateinit var</c>: reading before assignment throws.</summary>
    [JsonPropertyName("value")]
    public string Value
    {
        get => value ?? throw new InvalidOperationException("lateinit property value has not been initialized");
        set => this.value = value;
    }
    private string? value;

    public PopupAlert(AlertType type, string value)
    {
        Type = type;
        Value = value;
    }

    /// <summary>For json serialization.</summary>
    [JsonConstructor]
    public PopupAlert()
    {
    }
}
