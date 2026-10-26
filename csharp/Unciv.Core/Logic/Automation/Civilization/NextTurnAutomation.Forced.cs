using CivilizationClass = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Logic.Automation.Civilization;

// T226: the single decisions of the AI turn, reachable on their own. A forced end of turn (the host's timer) settles what a human left open the way the
// AI would, without playing the rest of its turn (no unit moves, no diplomacy).
public static partial class NextTurnAutomation
{
    /// <summary>Fills the research queue, and spends free techs, as the AI does.</summary>
    public static void ChooseResearchForced(CivilizationClass civ) => ChooseTechToResearch(ViewOf(civ).CivView);

    /// <summary>Adopts policies while the civ can, as the AI does.</summary>
    public static void AdoptPolicyForced(CivilizationClass civ) => AdoptPolicy(ViewOf(civ).CivView);

    /// <summary>Casts the diplomatic victory vote if the civ may vote, as the AI does.</summary>
    public static void VoteForced(CivilizationClass civ) => TryVoteForDiplomaticVictory(ViewOf(civ).CivView);
}
