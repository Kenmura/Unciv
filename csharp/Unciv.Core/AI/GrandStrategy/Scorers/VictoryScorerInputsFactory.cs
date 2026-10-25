using Unciv.Logic.Civilization;
using Unciv.UI.Screens.VictoryScreen;

namespace Unciv.AI.GrandStrategy.Scorers;

/// <summary>
/// Game adapter for the victory scorers (SPEC.md section 4): fills the plain-data inputs from a <see cref="Civilization"/>.
/// Operands the spec could not recover ([?]) or that Unciv has no equivalent for are supplied by the caller (default 0/false);
/// Unciv has no tourism or Influence, so those inputs are 0.
/// </summary>
public static class VictoryScorerInputsFactory
{
    public const string DominationVictory = "Domination";
    public const string CulturalVictory = "Cultural";
    public const string DiplomaticVictory = "Diplomatic";
    public const string ScientificVictory = "Scientific";

    private static bool IsVictoryEnabled(Civilization civ, string victoryName) =>
        civ.VictoryManager.GetAvailableVictories().Any(it => it.Name == victoryName);

    /// <summary>Alive major civs this civ knows (the AI only reasons about civs it has met), excluding itself, in diplomacy order.</summary>
    private static List<Civilization> KnownMajors(Civilization civ) =>
        civ.GetKnownCivs().Where(it => it.IsMajorCiv() && !ReferenceEquals(it, civ)).ToList();

    // baseTerm: The unrecovered BNW stat term (SPEC.md 4.1).
    public static ConquestInputs Conquest(Civilization civ, int baseTerm = 0)
    {
        ArgumentNullException.ThrowIfNull(civ);
        var majors = KnownMajors(civ);
        int reference = 0; // [I] reference strength: the strongest met major
        foreach (var other in majors)
            reference = Math.Max(reference, other.GetStatForRanking(RankingType.Force));

        return new ConquestInputs(
            VictoryEnabled: IsVictoryEnabled(civ, DominationVictory),
            BaseTerm: baseTerm,
            Turn: civ.GameInfo.Turns,
            HasMetMajor: majors.Count > 0,
            MyStrength: civ.GetStatForRanking(RankingType.Force),
            ReferenceStrength: reference,
            AtWar: civ.IsAtWar(),
            Cramped: IsCramped(civ, majors),
            MetMajorTriggersTail: false); // tail predicate is unidentified [?]
    }

    private static bool IsCramped(Civilization civ, List<Civilization> majors)
    {
        var mine = new HashSet<int>(civ.Cities.Select(it => it.GetCenterTile().GetContinent()));
        return majors.Any(other => other.Cities.Any(it => mine.Contains(it.GetCenterTile().GetContinent())));
    }

    // cultureFlavor: The leader's FLAVOR_CULTURE.
    // unknownX: The unrecovered X (SPEC.md 4.2).
    public static CultureInputs Culture(Civilization civ, int cultureFlavor, int unknownX = 0)
    {
        ArgumentNullException.ThrowIfNull(civ);
        var rivals = KnownMajors(civ)
            .Select(it => new RivalCultureStats(it.TotalCultureForContests, Tourism: 0))
            .ToList();
        return new CultureInputs(
            VictoryEnabled: IsVictoryEnabled(civ, CulturalVictory),
            CultureFlavor: cultureFlavor,
            UnknownX: unknownX,
            MyCulture: civ.TotalCultureForContests,
            MyTourism: 0,
            Rivals: rivals,
            InfluentialCivCount: 0);
    }

    // diplomacyFlavor: The leader's FLAVOR_DIPLOMACY.
    public static DiplomaticInputs Diplomatic(
        Civilization civ, int diplomacyFlavor, int unknownX = 0, int modifier1 = 0, int modifier2 = 0, int modifier3 = 0)
    {
        ArgumentNullException.ThrowIfNull(civ);
        var victoryManager = civ.VictoryManager;
        bool leagueExists = victoryManager.GetUNBuildingAndOwnerNames().Item1 != null;
        var results = victoryManager.GetDiplomaticVictoryVoteBreakdown().Results;

        int bestRival = 0;
        int unknown = 0;
        var known = civ.GetKnownCivs().ToList();
        foreach (var other in civ.GameInfo.Civilizations)
        {
            if (ReferenceEquals(other, civ) || !other.IsMajorCiv() || other.IsDefeated()) continue;
            bestRival = Math.Max(bestRival, results[other.CivId]);
            if (!known.Contains(other)) unknown++;
        }

        return new DiplomaticInputs(
            VictoryEnabled: IsVictoryEnabled(civ, DiplomaticVictory),
            MinorsAttacked: civ.NumMinorCivsAttacked,
            LeagueExists: leagueExists,
            DiplomacyFlavor: diplomacyFlavor,
            UnknownX: unknownX,
            MyVotes: results[civ.CivId],
            BestRivalVotes: bestRival,
            UnknownRivals: unknown,
            VotesNeeded: victoryManager.VotesNeededForDiplomaticVictory(),
            Modifier1: modifier1,
            Modifier2: modifier2,
            Modifier3: modifier3);
    }

    // progressCount: The unrecovered progress count (SPEC.md 4.4).
    public static SpaceshipInputs Spaceship(Civilization civ, int progressCount = 0)
    {
        ArgumentNullException.ThrowIfNull(civ);
        return new SpaceshipInputs(
            VictoryEnabled: IsVictoryEnabled(civ, ScientificVictory),
            ProgressCount: progressCount,
            HasApolloProgram: civ.HasUnique(Unciv.Models.Ruleset.Unique.UniqueType.EnablesConstructionOfSpaceshipParts));
    }
}
