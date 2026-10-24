namespace Unciv.Models.StatTypes;

public interface INamed
{
    // This is settable because unit tests set it (see `createRulesetObject` in TestGame.kt)
    string Name { get; set; }
}
