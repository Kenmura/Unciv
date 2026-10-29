namespace Unciv.Models.Ruleset.Unique;

/// <summary>
/// Rows of <see cref="UniqueType"/> that the Kotlin enum does not have (Brave New World, bnw-spec/SPEC.md), appended after
/// the Kotlin rows by <see cref="UniqueTypeExtensions"/> so the Kotlin ordinals stay as in the golden file.
/// </summary>
internal static class UniqueTypeDataBnw
{
    // Gates (spec section 2.1, ticket B001): each BNW subsystem is switched on by the ruleset's ModOptions, never by its name.
    internal static readonly System.Collections.Immutable.ImmutableArray<UniqueTypeRow> Rows =
    [
        new("Enables international trade routes",
            [UniqueTarget.ModOptions],
            [UniqueFlag.NoConditionals],
            "Brave New World gate (bnw-spec section 2.1): turns on international trade routes."),
        new("Enables Great Works and tourism",
            [UniqueTarget.ModOptions],
            [UniqueFlag.NoConditionals],
            "Brave New World gate: turns on Great Works, theming, tourism and influence."),
        new("Enables archaeology",
            [UniqueTarget.ModOptions],
            [UniqueFlag.NoConditionals],
            "Brave New World gate: turns on antiquity sites and archaeologists."),
        new("Enables ideologies",
            [UniqueTarget.ModOptions],
            [UniqueFlag.NoConditionals],
            "Brave New World gate: turns on ideologies, tenets and public opinion."),
        new("Enables the World Congress",
            [UniqueTarget.ModOptions],
            [UniqueFlag.NoConditionals],
            "Brave New World gate: turns on the World Congress and the United Nations."),
        new("BNW happiness rules",
            [UniqueTarget.ModOptions],
            [UniqueFlag.NoConditionals],
            "Brave New World gate: graduated happiness penalties, rebels and city revolts."),
    ];
}
