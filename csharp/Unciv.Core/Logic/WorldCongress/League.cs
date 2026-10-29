using System.Text.Json.Serialization;
using Unciv.Json;

namespace Unciv.Logic.WorldCongress;

// Brave New World (ticket B015, spec id B060, bnw-spec/SPEC.md section 2.2 and 4.7). Not part of the Kotlin source.

/// <summary>
/// The World Congress (spec 2.2 <c>GameInfo.League</c>), later the United Nations. Created when the first civ meets the founding condition (4.7.1).
/// A session is a window of one round: it opens at the end of a round (AI members vote at once), humans commit their delegates during the next round,
/// and it is decided at the end of that round.
/// </summary>
public class League : IsPartOfGameInfoSerialization
{
    /// <summary>Civ id of the host.</summary>
    [JsonPropertyName("host")]
    public string Host { get; set; } = "";

    /// <summary>Index into <see cref="LeagueData.Sessions"/> of the last special session that was started.</summary>
    [JsonPropertyName("sessionIndex")]
    public int SessionIndex { get; set; }

    /// <summary>Rounds until the next regular session opens (counted between sessions only).</summary>
    [JsonPropertyName("turnsUntilSession")]
    public int TurnsUntilSession { get; set; }

    /// <summary>A session is open: its proposals can be voted on and no new proposal can be made.</summary>
    [JsonPropertyName("inSession")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool InSession { get; set; }

    /// <summary>Next id of a proposal or an active resolution (monotonic).</summary>
    [JsonPropertyName("nextId")]
    public int NextId { get; set; } = 1;

    /// <summary>The members, in civ order: every major civ at the time of founding.</summary>
    [JsonPropertyName("members")]
    public List<LeagueMember> Members { get; set; } = [];

    /// <summary>The enacted resolutions, in the order they were enacted.</summary>
    [JsonPropertyName("active")]
    public List<ActiveResolution> Active { get; set; } = [];

    /// <summary>The proposals of the open session, or of the period before it, in the order they were made.</summary>
    [JsonPropertyName("proposals")]
    public List<LeagueProposal> Proposals { get; set; } = [];

    public LeagueMember? GetMember(string civId) => Members.FirstOrDefault(it => it.Civ == civId);

    public League Clone() => new()
    {
        Host = Host,
        SessionIndex = SessionIndex,
        TurnsUntilSession = TurnsUntilSession,
        InSession = InSession,
        NextId = NextId,
        Members = Members.Select(it => it.Clone()).ToList(),
        Active = Active.Select(it => it.Clone()).ToList(),
        Proposals = Proposals.Select(it => it.Clone()).ToList(),
    };
}

/// <summary>A civ in the league. <see cref="ExtraVotes"/> is the spec's member field <c>extra</c> (the bonus of a failed diplomatic vote).</summary>
public class LeagueMember : IsPartOfGameInfoSerialization
{
    [JsonPropertyName("civ")]
    public string Civ { get; set; } = "";

    [JsonPropertyName("extraVotes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int ExtraVotes { get; set; }

    /// <summary>Delegates the member had when the open session started (spent ones are in the votes of the proposals).</summary>
    [JsonPropertyName("delegates")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Delegates { get; set; }

    /// <summary>Proposals the member has made since the last session.</summary>
    [JsonPropertyName("proposalsUsed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int ProposalsUsed { get; set; }

    public LeagueMember Clone() => new() { Civ = Civ, ExtraVotes = ExtraVotes, Delegates = Delegates, ProposalsUsed = ProposalsUsed };
}

/// <summary>An enacted resolution. <see cref="Choice"/> is the proposer's decision (a civ id, luxury, religion or ideology name), else empty.</summary>
public class ActiveResolution : IsPartOfGameInfoSerialization
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("resolution")]
    public string Resolution { get; set; } = "";

    [JsonPropertyName("choice")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string Choice { get; set; } = "";

    [JsonPropertyName("proposer")]
    public string Proposer { get; set; } = "";

    [JsonPropertyName("turn")]
    public int Turn { get; set; }

    public ActiveResolution Clone() => new() { Id = Id, Resolution = Resolution, Choice = Choice, Proposer = Proposer, Turn = Turn };
}

/// <summary>A proposal to enact a resolution (<see cref="RepealId"/> is 0) or to repeal the active resolution with that id.</summary>
public class LeagueProposal : IsPartOfGameInfoSerialization
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("resolution")]
    public string Resolution { get; set; } = "";

    /// <summary>The proposer's decision (see <see cref="ActiveResolution.Choice"/>).</summary>
    [JsonPropertyName("choice")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string Choice { get; set; } = "";

    /// <summary>Civ id of the proposer, empty for the automatic proposals of a special session.</summary>
    [JsonPropertyName("proposer")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string Proposer { get; set; } = "";

    [JsonPropertyName("repealId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int RepealId { get; set; }

    [JsonPropertyName("votes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<LeagueVote> Votes { get; set; } = [];

    public bool IsRepeal => RepealId != 0;

    public LeagueProposal Clone() => new()
    {
        Id = Id,
        Resolution = Resolution,
        Choice = Choice,
        Proposer = Proposer,
        RepealId = RepealId,
        Votes = Votes.Select(it => it.Clone()).ToList(),
    };
}

/// <summary>Delegates a civ put on one choice of a proposal. The choice is <see cref="LeagueRules.Yes"/>, <see cref="LeagueRules.No"/> or a civ id.</summary>
public class LeagueVote : IsPartOfGameInfoSerialization
{
    [JsonPropertyName("civ")]
    public string Civ { get; set; } = "";

    [JsonPropertyName("choice")]
    public string Choice { get; set; } = "";

    [JsonPropertyName("count")]
    public int Count { get; set; }

    public LeagueVote Clone() => new() { Civ = Civ, Choice = Choice, Count = Count };
}
