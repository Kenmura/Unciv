using Unciv.View;

namespace Unciv.Queries.Empire;

// Name: The quest
// Description: The quest text with its data filled in
// IsGlobal: Offered to every civ at once
// RemainingTurns: Turns left, or null when the quest does not expire
// Influence: Influence gained for completing it
// Standing: For contests, who leads (only if every leader is the viewer or a civ it has met), else empty
public sealed record QuestEntry(string Name, string Description, bool IsGlobal, int? RemainingTurns, float Influence, string Standing);

public sealed record CityStateQuestsResult(string CivName, IReadOnlyList<QuestEntry> Quests);

// T220: the quests a city-state has given the viewer (CityStateDiplomacyTable). Kotlin shows the leaders of a contest in full; a leader the viewer has
// not met is not named here (the whole line is dropped), so unmet civs do not leak through quest scores.
[QueryType("empire.CityStateQuests")]
public sealed record CityStateQuestsQuery(string CivName) : IQuery<CityStateQuestsResult>
{
    public string Type => "empire.CityStateQuests";

    public CityStateQuestsResult Run(GameView view)
    {
        var civ = view.CivView.Unwrap();
        var other = EmpireQueryHelpers.KnownCiv(view, CivName);
        if (!other.IsCityState)
            throw new QueryException(EmpireQueryCodes.BadRequest, $"{CivName} is not a city-state");
        var quests = other.QuestManager.GetAssignedQuestsFor(civ).Select(it => new QuestEntry(it.QuestName, it.GetDescription(), it.IsGlobal(),
            it.DoesExpire() ? it.GetRemainingTurns() : null, it.GetInfluence(), it.IsGlobal() ? Standing(civ, other, it) : "")).ToList();
        return new CityStateQuestsResult(other.CivName, quests);
    }

    private static string Standing(Logic.Civilization.Civilization viewer, Logic.Civilization.Civilization cityState, Logic.Civilization.Managers.AssignedQuest quest)
    {
        var standing = cityState.QuestManager.GetScoreStringForGlobalQuest(quest);
        if (standing.Length == 0) return "";
        // Naming rule: the string names civs; drop it when it names one the viewer does not know.
        foreach (var civ in viewer.GameInfo.Civilizations)
        {
            if (civ == viewer || viewer.Knows(civ) || civ.IsBarbarian) continue;
            if (standing.Contains(civ.CivName, StringComparison.Ordinal)) return "";
        }
        return standing;
    }
}
