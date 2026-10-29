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
/// The remaining columns are the effects of B016 (applied through <see cref="LeagueEffects"/>): Project: the league project the resolution enables (B017 builds it);
/// the embargoes, NoResourceHappiness (the proposer's luxury), UnitMaintenanceGoldPercent, MemberDiscoveredTechMod (percent off techs a member knows), the culture
/// per World Wonder and Natural Wonder, NoTrainingNuclearWeapons, ReligionSpreadStrengthMod (percent, the chosen religion), OtherIdeologyRebellionMod (public opinion
/// pressure toward the chosen ideology), the artsy and sciencey Great Person rate percents and the culture of Great Person improvements and Landmarks.
/// </summary>
public sealed record LeagueResolution(string Name, VoterDecision Voter = VoterDecision.YesNo, ProposerDecision Proposer = ProposerDecision.None,
    string TechPrereq = "", bool NoProposalByPlayer = false, int LeadersVoteBonusOnFail = 0, bool DiplomaticVictory = false, bool ChangeLeagueHost = false,
    int VotesForFollowingReligion = 0, int HolyCityTourism = 0, int VotesForFollowingIdeology = 0,
    string Project = "", bool EmbargoCityStates = false, bool EmbargoPlayer = false, bool NoResourceHappiness = false, int UnitMaintenanceGoldPercent = 0,
    int MemberDiscoveredTechMod = 0, int CulturePerWonder = 0, int CulturePerNaturalWonder = 0, bool NoTrainingNuclearWeapons = false,
    int ReligionSpreadStrengthMod = 0, int OtherIdeologyRebellionMod = 0, int ArtsyGreatPersonRateMod = 0, int ScienceyGreatPersonRateMod = 0,
    int GreatPersonTileImprovementCulture = 0, int LandmarkCulture = 0);

/// <summary>
/// One row of <c>LeagueSpecialSessions.tsv</c> (spec 4.7.2). Era: the era (by name) that triggers it. Immediate: the resolution proposed when it starts.
/// Recurring: a resolution proposed in every session of this level, or empty.
/// </summary>
public sealed record LeagueSession(string Name, string Era, string Immediate, string Recurring, int TurnsBetweenSessions, int CivDelegates, int HostDelegates,
    int CityStateDelegates);

/// <summary>A league project (spec 4.7.7): <paramref name="CostPerPlayer"/> production for every living member, before the game speed.</summary>
public sealed record LeagueProjectDef(string Name, int CostPerPlayer);

public static class LeagueData
{
    public static IReadOnlyList<LeagueProjectDef> Projects { get; } =
    [
        new("World's Fair", 350),
        new("International Games", 720),
        new("International Space Station", 850),
    ];

    public const string DiplomaticVictory = "Diplomatic Victory";
    public const string ChangeLeagueHost = "Change League Host";
    public const string WorldReligion = "World Religion";
    public const string WorldIdeology = "World Ideology";

    public static IReadOnlyList<LeagueResolution> Resolutions { get; } =
    [
        new(DiplomaticVictory, VoterDecision.MajorCiv, NoProposalByPlayer: true, LeadersVoteBonusOnFail: 2, DiplomaticVictory: true),
        new(ChangeLeagueHost, VoterDecision.MajorCiv, NoProposalByPlayer: true, ChangeLeagueHost: true),
        new("All City-States Embargo", EmbargoCityStates: true),
        new("Player Embargo", Proposer: ProposerDecision.OtherMajorCiv, EmbargoPlayer: true),
        new("Ban Luxury", Proposer: ProposerDecision.Luxury, NoResourceHappiness: true),
        new("Standing Army Tax", UnitMaintenanceGoldPercent: 25),
        new("Scholars in Residence", MemberDiscoveredTechMod: 20),
        new("World's Fair", Project: "World's Fair"),
        new("International Games", TechPrereq: "Radio", Project: "International Games"),
        new("International Space Station", TechPrereq: "Satellites", Project: "International Space Station"),
        new("Cultural Heritage Sites", CulturePerWonder: 3),
        new("Natural Heritage Sites", CulturePerNaturalWonder: 5),
        new("Nuclear Non-Proliferation", TechPrereq: "Advanced Ballistics", NoTrainingNuclearWeapons: true),
        new(WorldReligion, Proposer: ProposerDecision.Religion, VotesForFollowingReligion: 2, HolyCityTourism: 50, ReligionSpreadStrengthMod: 25),
        new(WorldIdeology, Proposer: ProposerDecision.Ideology, TechPrereq: "Radio", VotesForFollowingIdeology: 2, OtherIdeologyRebellionMod: 2),
        new("Arts Funding", ArtsyGreatPersonRateMod: 33, ScienceyGreatPersonRateMod: -33),
        new("Sciences Funding", ArtsyGreatPersonRateMod: -33, ScienceyGreatPersonRateMod: 33),
        new("Historical Landmarks", TechPrereq: "Archaeology", GreatPersonTileImprovementCulture: 2, LandmarkCulture: 4),
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
