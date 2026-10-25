namespace Unciv.AI.GrandStrategy.Scorers;

// Plain-data views of what each BNW victory scorer (SPEC.md section 4) reads from the game.
// The scorers are pure functions of these records so they stay testable and deterministic; a game adapter fills them in.
// Quantities the spec could not recover from the decompile ([?] items) are inputs here, not guesses inside the scorer.

/// <summary>Inputs of <see cref="ConquestScorer"/> (SPEC.md 4.1, BNW).</summary>
/// <param name="VictoryEnabled">Domination victory is enabled in the game options.</param>
/// <param name="BaseTerm">BNW stat term <c>(12 - X) * (Y + max(v0, v1, v2) - v5)</c>; its operands are unrecovered [?], so the caller supplies the result.</param>
/// <param name="Turn">Current game turn.</param>
/// <param name="HasMetMajor">The civ has met at least one other alive major civ.</param>
/// <param name="MyStrength">Own military strength.</param>
/// <param name="ReferenceStrength">Reference military strength (operand roles are [I]); the ratio term needs it above 0.</param>
/// <param name="AtWar">At war with anyone.</param>
/// <param name="Cramped">Rivals share the civ's landmasses.</param>
/// <param name="MetMajorTriggersTail">An alive, met major satisfies the unidentified tail predicate [?] (score -50).</param>
public sealed record ConquestInputs(
    bool VictoryEnabled,
    int BaseTerm,
    int Turn,
    bool HasMetMajor,
    int MyStrength,
    int ReferenceStrength,
    bool AtWar,
    bool Cramped,
    bool MetMajorTriggersTail);

/// <summary>One rival major civ counted by <see cref="CultureScorer"/>; the caller has already excluded filtered civs.</summary>
public readonly record struct RivalCultureStats(int Culture, int Tourism);

/// <summary>Inputs of <see cref="CultureScorer"/> (SPEC.md 4.2, BNW).</summary>
/// <param name="VictoryEnabled">Cultural victory is enabled.</param>
/// <param name="CultureFlavor">The leader's FLAVOR_CULTURE value.</param>
/// <param name="UnknownX">The unrecovered X in <c>(10 - X) * flavor</c> [?].</param>
/// <param name="MyCulture">Own lifetime culture.</param>
/// <param name="MyTourism">Own tourism.</param>
/// <param name="Rivals">Counted rivals, in a stable order.</param>
/// <param name="InfluentialCivCount">Number of civs this civ is Influential over.</param>
public sealed record CultureInputs(
    bool VictoryEnabled,
    int CultureFlavor,
    int UnknownX,
    int MyCulture,
    int MyTourism,
    IReadOnlyList<RivalCultureStats> Rivals,
    int InfluentialCivCount);

/// <summary>Inputs of <see cref="DiplomaticScorer"/> (SPEC.md 4.3, BNW).</summary>
/// <param name="VictoryEnabled">Diplomatic victory is enabled.</param>
/// <param name="MinorsAttacked">Number of city-states this civ has attacked.</param>
/// <param name="LeagueExists">A World Congress / League exists.</param>
/// <param name="DiplomacyFlavor">The leader's FLAVOR_DIPLOMACY value.</param>
/// <param name="UnknownX">The unrecovered X in <c>(10 - X) * flavor</c> [?].</param>
/// <param name="MyVotes">Own League votes.</param>
/// <param name="BestRivalVotes">Highest League votes among other alive non-minor majors.</param>
/// <param name="UnknownRivals">Rivals with no information available.</param>
/// <param name="VotesNeeded">Votes needed to win.</param>
/// <param name="Modifier1">Player-specific modifier c1 [?].</param>
/// <param name="Modifier2">Player-specific modifier c2 [?].</param>
/// <param name="Modifier3">Player-specific modifier c3 [?], subtracted.</param>
public sealed record DiplomaticInputs(
    bool VictoryEnabled,
    int MinorsAttacked,
    bool LeagueExists,
    int DiplomacyFlavor,
    int UnknownX,
    int MyVotes,
    int BestRivalVotes,
    int UnknownRivals,
    int VotesNeeded,
    int Modifier1,
    int Modifier2,
    int Modifier3);

/// <summary>Inputs of <see cref="SpaceshipScorer"/> (SPEC.md 4.4).</summary>
/// <param name="VictoryEnabled">Space race victory is enabled.</param>
/// <param name="ProgressCount">Unrecovered tech/progress-related count [?], multiplied by 5.</param>
/// <param name="HasApolloProgram">The civ has built the Apollo Program.</param>
public sealed record SpaceshipInputs(bool VictoryEnabled, int ProgressCount, bool HasApolloProgram);
