namespace Unciv.Logic.Civilization.Diplomacy;

// core/src/com/unciv/logic/civilization/diplomacy/Demand.kt
// The Kotlin enum class carries 16 constructor values and overrides show() per entry, so it is a class with one static instance
// per entry (reference equality, declaration order in Values), not a C# enum.

/// <summary>
/// After creating the required flags, modifiers, and alert type, the only remaining work should be
/// - Adding the new alerts in the when() of AlertPopup.kt
/// - Triggering the violation (adding the violationOccurred flag) from somewhere in the code
/// (Translation templates are found automatically)
/// </summary>
public class Demand
{
    private readonly Func<Civilization, bool> showDemand;

    private Demand(
        string name,
        int ordinal,
        DiplomacyFlags agreedToDemand,
        DiplomacyFlags violationOccurred,
        DiplomacyFlags willIgnoreViolation,
        DiplomaticModifiers refusedDiplomaticModifier,
        DiplomaticModifiers betrayedPromiseDiplomacyModifier,
        DiplomaticModifiers fulfilledPromiseDiplomacyModifier,
        AlertType demandAlert,
        AlertType violationDiscoveredAlert,
        string demandText,
        string acceptDemandText,
        string refuseDemandText,
        string violationNoticedText,
        string agreedToDemandText,
        string refusedDemandText,
        string wePromisedText,
        string theyPromisedText,
        Func<Civilization, bool>? show = null)
    {
        Name = name;
        Ordinal = ordinal;
        AgreedToDemand = agreedToDemand;
        ViolationOccurred = violationOccurred;
        WillIgnoreViolation = willIgnoreViolation;
        RefusedDiplomaticModifier = refusedDiplomaticModifier;
        BetrayedPromiseDiplomacyModifier = betrayedPromiseDiplomacyModifier;
        FulfilledPromiseDiplomacyModifier = fulfilledPromiseDiplomacyModifier;
        DemandAlert = demandAlert;
        ViolationDiscoveredAlert = violationDiscoveredAlert;
        DemandText = demandText;
        AcceptDemandText = acceptDemandText;
        RefuseDemandText = refuseDemandText;
        ViolationNoticedText = violationNoticedText;
        AgreedToDemandText = agreedToDemandText;
        RefusedDemandText = refusedDemandText;
        WePromisedText = wePromisedText;
        TheyPromisedText = theyPromisedText;
        showDemand = show ?? (_ => true);
    }

    /// <summary>Kotlin <c>Enum.name</c>.</summary>
    public string Name { get; }

    /// <summary>Kotlin <c>Enum.ordinal</c>.</summary>
    public int Ordinal { get; }

    // All these are set on the promisee, not the promiser
    public DiplomacyFlags AgreedToDemand { get; }
    public DiplomacyFlags ViolationOccurred { get; }
    public DiplomacyFlags WillIgnoreViolation { get; }
    public DiplomaticModifiers RefusedDiplomaticModifier { get; }
    public DiplomaticModifiers BetrayedPromiseDiplomacyModifier { get; }
    public DiplomaticModifiers FulfilledPromiseDiplomacyModifier { get; }
    public AlertType DemandAlert { get; }
    public AlertType ViolationDiscoveredAlert { get; }
    public string DemandText { get; }
    public string AcceptDemandText { get; }
    public string RefuseDemandText { get; }
    public string ViolationNoticedText { get; }
    /// <summary>Must contain 1 parameter, to be replaced with civ name</summary>
    public string AgreedToDemandText { get; }
    /// <summary>Must contain 1 parameter, to be replaced with civ name</summary>
    public string RefusedDemandText { get; }
    /// <summary>Must contain 1 parameter, to be replaced with turns left</summary>
    public string WePromisedText { get; }
    /// <summary>Must contain 1 parameter, to be replaced with turns left</summary>
    public string TheyPromisedText { get; }

    /// <summary>Determines whether to offer the demand to players on the diplomacy screen</summary>
    public bool Show(Civilization civ) => showDemand(civ);

    public override string ToString() => Name;

    public static readonly Demand DontSpyOnUs = new(
        "DontSpyOnUs", 0,
        agreedToDemand: DiplomacyFlags.AgreedToNotSendSpies,
        violationOccurred: DiplomacyFlags.DiscoveredSpiesInOurCities,
        willIgnoreViolation: DiplomacyFlags.IgnoreThemSendingSpies,
        refusedDiplomaticModifier: DiplomaticModifiers.RefusedToNotSendingSpiesToUs,
        betrayedPromiseDiplomacyModifier: DiplomaticModifiers.BetrayedPromiseToNotSendingSpiesToUs,
        fulfilledPromiseDiplomacyModifier: DiplomaticModifiers.FulfilledPromiseToNotSpy,
        demandAlert: AlertType.DemandToStopSpyingOnUs,
        violationDiscoveredAlert: AlertType.SpyingOnUsDespiteOurPromise,
        demandText: "Stop spying on us.",
        acceptDemandText: "We see our people are not welcome in your lands... we will take our attention elsewhere.",
        refuseDemandText: "I'll do what's necessary for my empire to survive.",
        agreedToDemandText: "[civName] agreed to stop spying on us!",
        refusedDemandText: "[civName] refused to stop spying on us!",
        violationNoticedText: "Take back your spy and your broken promises.",
        wePromisedText: "We promised not to send spies to them ([turns] turns remaining)",
        theyPromisedText: "They promised not to send spies to us ([turns] turns remaining)",
        show: civ => civ.GameInfo.IsEspionageEnabled());

    public static readonly Demand DoNotSpreadReligion = new(
        "DoNotSpreadReligion", 1,
        agreedToDemand: DiplomacyFlags.AgreedToNotSpreadReligion,
        violationOccurred: DiplomacyFlags.SpreadReligionInOurCities,
        willIgnoreViolation: DiplomacyFlags.IgnoreThemSpreadingReligion,
        refusedDiplomaticModifier: DiplomaticModifiers.RefusedToNotSpreadReligionToUs,
        betrayedPromiseDiplomacyModifier: DiplomaticModifiers.BetrayedPromiseToNotSpreadReligionToUs,
        fulfilledPromiseDiplomacyModifier: DiplomaticModifiers.FulfilledPromiseToNotSpreadReligion,
        demandAlert: AlertType.DemandToStopSpreadingReligion,
        violationDiscoveredAlert: AlertType.ReligionSpreadDespiteOurPromise,
        demandText: "Please don't spread your religion to us.",
        acceptDemandText: "Very well, we shall spread our faith elsewhere.",
        refuseDemandText: "We shall do as we please.",
        agreedToDemandText: "[civName] agreed to stop spreading religion to us!",
        refusedDemandText: "[civName] refused to stop spreading religion to us!",
        violationNoticedText: "We noticed you have continued spreading your religion to us, despite your promise. This will have....consequences.",
        wePromisedText: "We promised not to spread religion to them ([turns] turns remaining)",
        theyPromisedText: "They promised not to spread religion to us ([turns] turns remaining)",
        show: civ => civ.GameInfo.IsReligionEnabled());

    public static readonly Demand DoNotSettleNearUs = new(
        "DoNotSettleNearUs", 2,
        agreedToDemand: DiplomacyFlags.AgreedToNotSettleNearUs,
        violationOccurred: DiplomacyFlags.SettledCitiesNearUs,
        willIgnoreViolation: DiplomacyFlags.IgnoreThemSettlingNearUs,
        refusedDiplomaticModifier: DiplomaticModifiers.RefusedToNotSettleCitiesNearUs,
        betrayedPromiseDiplomacyModifier: DiplomaticModifiers.BetrayedPromiseToNotSettleCitiesNearUs,
        fulfilledPromiseDiplomacyModifier: DiplomaticModifiers.FulfilledPromiseToNotSettleCitiesNearUs,
        demandAlert: AlertType.DemandToStopSettlingCitiesNear,
        violationDiscoveredAlert: AlertType.CitySettledNearOtherCivDespiteOurPromise,
        demandText: "Please don't settle new cities near us.",
        acceptDemandText: "Very well, we shall look for new lands to settle.",
        refuseDemandText: "We shall do as we please.",
        agreedToDemandText: "[civName] agreed to stop settling cities near us!",
        refusedDemandText: "[civName] refused to stop settling cities near us!",
        violationNoticedText: "We noticed your new city near our borders, despite your promise. This will have....implications.",
        wePromisedText: "We promised not to settle near them ([turns] turns remaining)",
        theyPromisedText: "They promised not to settle near us ([turns] turns remaining)");

    public static readonly Demand DoNotAttackUs = new(
        "DoNotAttackUs", 3,
        agreedToDemand: DiplomacyFlags.AgreedToNotAttackUs,
        // first violation = notice military presence near border
        // second violation = declare war after promising not to
        violationOccurred: DiplomacyFlags.MilitaryPresenceNearBorderOrAttackedUsDespitePromise,
        willIgnoreViolation: DiplomacyFlags.IgnoreMilitaryPresenceNearBorder,
        refusedDiplomaticModifier: DiplomaticModifiers.RefusedToPromiseNotToAttackUs,
        betrayedPromiseDiplomacyModifier: DiplomaticModifiers.BetrayedPromiseToNotAttackUs,
        fulfilledPromiseDiplomacyModifier: DiplomaticModifiers.FulfilledPromiseToNotAttackUs,
        demandAlert: AlertType.DemandToNotAttackUs,
        violationDiscoveredAlert: AlertType.AttackedUsDespitePromise,
        demandText: "We have noticed substantial military activity near our borders. How should we interpret this?",
        acceptDemandText: "Do not worry. Our soldiers are merely passing by.",
        refuseDemandText: "Our military stands ready to finish what diplomacy could not! (DECLARE WAR)",
        agreedToDemandText: "[civName] has promised not to attack us!",
        refusedDemandText: "[civName] declared war on us after we questioned the positioning of their military!",
        violationNoticedText: "You have exposed yourself as a treacherous warmonger.",
        wePromisedText: "We promised not to attack them ([turns] turns remaining)",
        theyPromisedText: "They promised not to attack us ([turns] turns remaining)",
        // This particular demand can currently only be made by AI to human players
        // Kotlin notes that the AI logic to respond to this ultimatum is still open, and suggests enabling the button only when there is a
        // genuine military presence (see DiplomacyAutomation, T088).
        show: _ => false);

    /// <summary>Kotlin <c>Demand.entries</c>, in declaration order.</summary>
    public static IReadOnlyList<Demand> Values { get; } = [DontSpyOnUs, DoNotSpreadReligion, DoNotSettleNearUs, DoNotAttackUs];
}
