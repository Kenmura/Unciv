using Unciv.Models.Ruleset.Unique;

namespace Unciv.Models.Ruleset;

// core/src/com/unciv/models/ruleset/RuinReward.kt
public class RuinReward : RulesetObject
{
    public string Notification { get; set; } = "";

    public override UniqueTarget GetUniqueTarget() => UniqueTarget.Ruins;

    public List<string> ExcludedDifficulties { get; set; } = [];
    public int Weight { get; set; } = 1;
    /// <summary>For Civilopedia</summary>
    public string Color { get; set; } = "";

    /// <summary>No own category on Civilopedia screen</summary>
    public string MakeLink() => "";

    public override bool IsUnavailableBySettings(IGameContextGameInfo gameInfo) =>
        ExcludedDifficulties.Contains(gameInfo.GetDifficultyName()) ||
        base.IsUnavailableBySettings(gameInfo);
}
