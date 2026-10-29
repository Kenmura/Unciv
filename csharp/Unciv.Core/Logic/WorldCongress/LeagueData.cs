namespace Unciv.Logic.WorldCongress;

// Brave New World (ticket B015, spec id B060, bnw-spec/SPEC.md section 4.7.2 and 4.7.5). Not part of the Kotlin source.
//
// Deliberate deviation from spec 2.1: Resolutions.tsv and LeagueSpecialSessions.tsv are engine tables here, not ruleset objects (a ruleset
// object type with loader, validator and mod support is out of proportion for 18 + 4 rows, and the engine reads the same numbers either way).
// The tables are only consulted in a ruleset with the World Congress gate ("Enables the World Congress"). Resolutions are named as in the
// spec table 4.7.5; the effect columns of Resolutions.tsv that B016 applies are added to the row there.

/// <summary>What the members vote on for a resolution.</summary>
public enum VoterDecision
{
    /// <summary>Yes or No: the proposal passes with more Yes than No delegates.</summary>
    YesNo,

    /// <summary>One of the major civs in the league: the plurality wins, a tie or no vote fails.</summary>
    MajorCiv,
}

/// <summary>What the proposer has to name when enacting a resolution.</summary>
public enum ProposerDecision
{
    None,
    OtherMajorCiv,
    Luxury,
    Religion,
    Ideology,
}

/// <summary>
/// One row of <c>Resolutions.tsv</c> (spec 4.7.5). TechPrereq: a tech any member must know before it can be proposed (or empty). NoProposalByPlayer: only a session
/// proposes it. LeadersVoteBonusOnFail: extra delegates for the top vote-getters of a failed vote (applied by B018). VotesForFollowingReligion: delegates for the
/// founder of the chosen religion. HolyCityTourism: percent tourism of that religion's holy city. VotesForFollowingIdeology: delegates for each civ following the chosen ideology.
/// </summary>
public sealed record LeagueResolution(string Name, VoterDecision Voter = VoterDecision.YesNo, ProposerDecision Proposer = ProposerDecision.None,
    string TechPrereq = "", bool NoProposalByPlayer = false, int LeadersVoteBonusOnFail = 0, bool DiplomaticVictory = false, bool ChangeLeagueHost = false,
    int VotesForFollowingReligion = 0, int HolyCityTourism = 0, int VotesForFollowingIdeology = 0);

/// <summary>
/// One row of <c>LeagueSpecialSessions.tsv</c> (spec 4.7.2). Era: the era (by name) that triggers it. Immediate: the resolution proposed when it starts.
/// Recurring: a resolution proposed in every session of this level, or empty.
/// </summary>
public sealed record LeagueSession(string Name, string Era, string Immediate, string Recurring, int TurnsBetweenSessions, int CivDelegates, int HostDelegates,
    int CityStateDelegates);

public static class LeagueData
{
    public const string DiplomaticVictory = "Diplomatic Victory";
    public const string ChangeLeagueHost = "Change League Host";
    public const string WorldReligion = "World Religion";
    public const string WorldIdeology = "World Ideology";

    public static IReadOnlyList<LeagueResolution> Resolutions { get; } =
    [
        new(DiplomaticVictory, VoterDecision.MajorCiv, NoProposalByPlayer: true, LeadersVoteBonusOnFail: 2, DiplomaticVictory: true),
        new(ChangeLeagueHost, VoterDecision.MajorCiv, NoProposalByPlayer: true, ChangeLeagueHost: true),
        new("All City-States Embargo"),
        new("Player Embargo", Proposer: ProposerDecision.OtherMajorCiv),
        new("Ban Luxury", Proposer: ProposerDecision.Luxury),
        new("Standing Army Tax"),
        new("Scholars in Residence"),
        new("World's Fair"),
        new("International Games", TechPrereq: "Radio"),
        new("International Space Station", TechPrereq: "Satellites"),
        new("Cultural Heritage Sites"),
        new("Natural Heritage Sites"),
        new("Nuclear Non-Proliferation", TechPrereq: "Advanced Ballistics"),
        new(WorldReligion, Proposer: ProposerDecision.Religion, VotesForFollowingReligion: 2, HolyCityTourism: 50),
        new(WorldIdeology, Proposer: ProposerDecision.Ideology, TechPrereq: "Radio", VotesForFollowingIdeology: 2),
        new("Arts Funding"),
        new("Sciences Funding"),
        new("Historical Landmarks", TechPrereq: "Archaeology"),
    ];

    public static IReadOnlyList<LeagueSession> Sessions { get; } =
    [
        new("World Congress Founded", "Renaissance era", ChangeLeagueHost, "", 30, 1, 1, 0),
        new("World Congress Welcomes City-States", "Industrial era", ChangeLeagueHost, "", 25, 2, 1, 1),
        new("World Congress Expands Infrastructure", "Modern era", ChangeLeagueHost, "", 20, 3, 2, 1),
        new("World Congress Becomes United Nations", "Atomic era", ChangeLeagueHost, DiplomaticVictory, 10, 4, 2, 2),
    ];

    public static LeagueResolution? Find(string name) => Resolutions.FirstOrDefault(it => it.Name == name);
}
