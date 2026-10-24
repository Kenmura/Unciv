namespace Unciv.Logic.Civilization.Diplomacy;

// core/src/com/unciv/logic/civilization/diplomacy/DiplomacyManager.kt (enum DiplomaticModifiers). Serialized by name (see diplomaticModifiers).

public enum DiplomaticModifiers
{
    // Negative
    DeclaredWarOnUs,
    WarMongerer,
    CapturedOurCities,
    DeclaredFriendshipWithOurEnemies,
    BetrayedDeclarationOfFriendship,
    SignedDefensivePactWithOurEnemies,
    BetrayedDefensivePact,
    Denunciation,
    DenouncedOurAllies,
    RefusedToNotSettleCitiesNearUs,
    RefusedToNotSpreadReligionToUs,
    RefusedToNotSendingSpiesToUs,
    RefusedToPromiseNotToAttackUs,
    BetrayedPromiseToNotSettleCitiesNearUs,
    BetrayedPromiseToNotSpreadReligionToUs,
    BetrayedPromiseToNotSendingSpiesToUs,
    BetrayedPromiseToNotAttackUs,
    BetrayedPromiseToNotAttackOtherCiv,

    UnacceptableDemands,
    UsedNuclearWeapons,
    StealingTerritory,
    DestroyedProtectedMinor,
    AttackedProtectedMinor,
    AttackedAlliedMinor,
    BulliedProtectedMinor,
    SidedWithProtectedMinor,
    SpiedOnUs,
    StoleOurAlly,

    // Positive
    EstablishedEmbassy,
    ReceivedEmbassy,
    SharedEmbassies,
    YearsOfPeace,
    SharedEnemy,
    LiberatedCity,
    DeclarationOfFriendship,
    DeclaredFriendshipWithOurAllies,
    DefensivePact,
    SignedDefensivePactWithOurAllies,
    DenouncedOurEnemies,
    OpenBorders,
    FulfilledPromiseToNotSettleCitiesNearUs,
    FulfilledPromiseToNotSpreadReligion,
    FulfilledPromiseToNotSpy,
    FulfilledPromiseToNotAttackUs,
    GaveUsUnits,
    GaveUsGifts,
    ReturnedCapturedUnits,
    BelieveSameReligion,

}

public static class DiplomaticModifiersExtensions
{
    /// <summary>Kotlin <c>DiplomaticModifiers.text</c>.</summary>
    public static string Text(this DiplomaticModifiers modifier) => modifier switch
    {
        DiplomaticModifiers.DeclaredWarOnUs => "You declared war on us!",
        DiplomaticModifiers.WarMongerer => "Your warmongering ways are unacceptable to us.",
        DiplomaticModifiers.CapturedOurCities => "You have captured our cities!",
        DiplomaticModifiers.DeclaredFriendshipWithOurEnemies => "You have declared friendship with our enemies!",
        DiplomaticModifiers.BetrayedDeclarationOfFriendship => "Your so-called 'friendship' is worth nothing.",
        DiplomaticModifiers.SignedDefensivePactWithOurEnemies => "You have signed a defensive pact with our enemy!",
        DiplomaticModifiers.BetrayedDefensivePact => "Your so-called 'defensive pact' is worth nothing.",
        DiplomaticModifiers.Denunciation => "You have publicly denounced us!",
        DiplomaticModifiers.DenouncedOurAllies => "You have denounced our allies",
        DiplomaticModifiers.RefusedToNotSettleCitiesNearUs => "You refused to stop settling cities near us",
        DiplomaticModifiers.RefusedToNotSpreadReligionToUs => "You refused to stop spreading religion to us",
        DiplomaticModifiers.RefusedToNotSendingSpiesToUs => "You refused to stop spying on us",
        DiplomaticModifiers.RefusedToPromiseNotToAttackUs => "You declared war on us after we questioned the positioning of your military.",
        DiplomaticModifiers.BetrayedPromiseToNotSettleCitiesNearUs => "You betrayed your promise to not settle cities near us",
        DiplomaticModifiers.BetrayedPromiseToNotSpreadReligionToUs => "You betrayed your promise to not spread your religion to us",
        DiplomaticModifiers.BetrayedPromiseToNotSendingSpiesToUs => "You betrayed your promise to stop spying on us",
        DiplomaticModifiers.BetrayedPromiseToNotAttackUs => "You betrayed your promise to not attack us!",
        DiplomaticModifiers.BetrayedPromiseToNotAttackOtherCiv => "You were observed betraying your promise to not declare war!",
        DiplomaticModifiers.UnacceptableDemands => "Your arrogant demands are in bad taste",
        DiplomaticModifiers.UsedNuclearWeapons => "Your use of nuclear weapons is disgusting!",
        DiplomaticModifiers.StealingTerritory => "You have stolen our lands!",
        DiplomaticModifiers.DestroyedProtectedMinor => "You destroyed City-States that were under our protection!",
        DiplomaticModifiers.AttackedProtectedMinor => "You attacked City-States that were under our protection!",
        DiplomaticModifiers.AttackedAlliedMinor => "You attacked our allied City-States!",
        DiplomaticModifiers.BulliedProtectedMinor => "You demanded tribute from City-States that were under our protection!",
        DiplomaticModifiers.SidedWithProtectedMinor => "You sided with a City-State over us",
        DiplomaticModifiers.SpiedOnUs => "You spied on us!",
        DiplomaticModifiers.StoleOurAlly => "You took the alliance we had with a City-State",
        DiplomaticModifiers.EstablishedEmbassy => "We have an embassy in your capital",
        DiplomaticModifiers.ReceivedEmbassy => "You have an embassy in our capital",
        DiplomaticModifiers.SharedEmbassies => "We have shared embassies",
        DiplomaticModifiers.YearsOfPeace => "Years of peace have strengthened our relations.",
        DiplomaticModifiers.SharedEnemy => "Our mutual military struggle brings us closer together.",
        DiplomaticModifiers.LiberatedCity => "We applaud your liberation of conquered cities!",
        DiplomaticModifiers.DeclarationOfFriendship => "We have signed a public declaration of friendship",
        DiplomaticModifiers.DeclaredFriendshipWithOurAllies => "You have declared friendship with our allies",
        DiplomaticModifiers.DefensivePact => "We have signed a promise to protect each other.",
        DiplomaticModifiers.SignedDefensivePactWithOurAllies => "You have signed a defensive pact with our ally",
        DiplomaticModifiers.DenouncedOurEnemies => "You have denounced our enemies",
        DiplomaticModifiers.OpenBorders => "Our open borders have brought us closer together.",
        DiplomaticModifiers.FulfilledPromiseToNotSettleCitiesNearUs => "You fulfilled your promise to stop settling cities near us!",
        DiplomaticModifiers.FulfilledPromiseToNotSpreadReligion => "You fulfilled your promise to stop spreading religion to us!",
        DiplomaticModifiers.FulfilledPromiseToNotSpy => "You fulfilled your promise to stop spying on us!",
        DiplomaticModifiers.FulfilledPromiseToNotAttackUs => "You fulfilled your promise to not attack us!",
        DiplomaticModifiers.GaveUsUnits => "You gave us units!",
        DiplomaticModifiers.GaveUsGifts => "We appreciate your gifts",
        DiplomaticModifiers.ReturnedCapturedUnits => "You returned captured units to us",
        DiplomaticModifiers.BelieveSameReligion => "We believe in the same religion",
        _ => throw new ArgumentOutOfRangeException(nameof(modifier), modifier, null),
    };

    /// <summary>Kotlin <c>DiplomaticModifiers.safeValueOf</c>: an exact name match, or <c>null</c>.</summary>
    public static DiplomaticModifiers? SafeValueOf(string name) =>
        Enum.TryParse<DiplomaticModifiers>(name, out var value) && Enum.IsDefined(value) && value.ToString() == name ? value : null;
}
