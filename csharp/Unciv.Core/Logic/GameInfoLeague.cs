using System.Text.Json.Serialization;
using Unciv.Logic.WorldCongress;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Logic;

// Brave New World (ticket B015, spec id B060, bnw-spec/SPEC.md section 2.2 and 4.7): the World Congress. Null until it is founded, and omitted
// from the save then, so a game without it (every G&K game) serialises exactly as before.
public partial class GameInfo
{
    [JsonPropertyName("league")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public League? League { get; set; }

    public int LeagueTechDiscountPercent(CivilizationObject civ, string techName) => LeagueEffects.TechDiscountPercent(civ, techName);

    public int LeagueReligionSpreadPercent(string? religion) => LeagueEffects.ReligionSpreadPercent(this, religion);

    private void CloneLeagueTo(GameInfo target) => target.League = League?.Clone();

    /// <summary>The league's part of a round: at the end of every round, after all civs have played (spec 2.3 step 4).</summary>
    private void ProcessLeagueTurn() => LeagueRules.ProcessTurn(this);
}
