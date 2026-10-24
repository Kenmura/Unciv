using Unciv.Utils;

namespace Unciv.Models.Ruleset.Unique;

/// <summary>
/// Ported from <c>core/src/com/unciv/models/ruleset/unique/UniqueFlag.kt</c>.
/// </summary>
public enum UniqueFlag
{
    HiddenToUsers = 0,
    NoConditionals = 1,
    AcceptsSpeedModifier = 2,
    AcceptsGameProgressModifier = 3,
}

public static class UniqueFlagSets
{
    public static readonly IReadOnlySet<UniqueFlag> SetOfHiddenToUsers =
        new LinkedHashSet<UniqueFlag>([UniqueFlag.HiddenToUsers]);

    public static readonly IReadOnlySet<UniqueFlag> SetOfNoConditionals =
        new LinkedHashSet<UniqueFlag>([UniqueFlag.NoConditionals]);

    public static readonly IReadOnlySet<UniqueFlag> SetOfHiddenNoConditionals =
        new LinkedHashSet<UniqueFlag>([UniqueFlag.HiddenToUsers, UniqueFlag.NoConditionals]);
}
