namespace Unciv.Models.Metadata;

// core/src/com/unciv/models/metadata/BaseRuleset.kt
public enum BaseRuleset
{
    Civ_V_Vanilla,
    Civ_V_GnK
}

public static class BaseRulesetExtensions
{
    public static string FullName(this BaseRuleset baseRuleset) => baseRuleset switch
    {
        BaseRuleset.Civ_V_Vanilla => "Civ V - Vanilla",
        BaseRuleset.Civ_V_GnK => "Civ V - Gods & Kings",
        _ => throw new ArgumentOutOfRangeException(nameof(baseRuleset))
    };
}
