using System.Text.Json.Serialization;
using Unciv.Logic.GreatWorks;

namespace Unciv.Logic;

// Brave New World (ticket B007, spec id B030, bnw-spec/SPEC.md section 2.2 and 4.2): the game-wide Great Work state. Both lists are null
// until the first work is created, so a game without Great Works serialises exactly as before (G&K saves are unaffected).
//
// Deliberate deviation from spec 2.2: there is no separate UsedGreatWorkNames list. The names of GreatWorks are the used names (a created
// work stays in the list even when its building is gone), so the two cannot disagree.
public partial class GameInfo
{
    [JsonPropertyName("greatWorks")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<GreatWorkInstance>? GreatWorkInstances { get; set; }

    [JsonPropertyName("greatWorkSwapOffers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<GreatWorkSwapOffer>? GreatWorkSwapOffers { get; set; }

    /// <summary>Every created Great Work, in creation order.</summary>
    [JsonIgnore]
    public IReadOnlyList<GreatWorkInstance> GreatWorks => GreatWorkInstances ?? [];

    public GreatWorkInstance? GetGreatWork(string name) => GreatWorks.FirstOrDefault(it => it.Name == name);

    /// <summary>Has the named work been created in this game (it may since have been destroyed)?</summary>
    public bool IsGreatWorkUsed(string name) => GetGreatWork(name) != null;

    private void CloneGreatWorksTo(GameInfo target)
    {
        target.GreatWorkInstances = GreatWorkInstances?.Select(it => it.Clone()).ToList();
        target.GreatWorkSwapOffers = GreatWorkSwapOffers?.Select(it => it.Clone()).ToList();
    }
}
