namespace Unciv.Commands.Events;

// B015 (bnw-spec/SPEC.md section 2.4 and 4.7): the World Congress events. The league is public: its members, proposals and results are known to everybody.

/// <summary><paramref name="HostCivId"/> founded the World Congress.</summary>
[EventType("league-founded", EventAudience.Everyone)]
public sealed record LeagueFounded(string HostCivId) : EngineEvent;

/// <summary>A special session (<paramref name="Session"/>, by name) started; the host is <paramref name="HostCivId"/>.</summary>
[EventType("special-session-started", EventAudience.Everyone)]
public sealed record SpecialSessionStarted(string Session, string HostCivId) : EngineEvent;

/// <summary><paramref name="CivId"/> proposed proposal <paramref name="ProposalId"/>: to enact <paramref name="Resolution"/> with <paramref name="Choice"/>, or (<paramref name="RepealId"/> non-zero) to repeal it.</summary>
[EventType("resolution-proposed", EventAudience.Everyone)]
public sealed record ResolutionProposed(int ProposalId, string Resolution, string Choice, string CivId, int RepealId) : EngineEvent;

/// <summary><paramref name="Resolution"/> was enacted (<paramref name="Choice"/> is the proposer's choice, or the winner of a vote on a civ).</summary>
[EventType("resolution-enacted", EventAudience.Everyone)]
public sealed record ResolutionEnacted(string Resolution, string Choice) : EngineEvent;

/// <summary>The active resolution <paramref name="ActiveId"/> (<paramref name="Resolution"/>) was repealed.</summary>
[EventType("resolution-repealed", EventAudience.Everyone)]
public sealed record ResolutionRepealed(string Resolution, int ActiveId) : EngineEvent;

/// <summary>The vote on <paramref name="Resolution"/> failed.</summary>
[EventType("resolution-failed", EventAudience.Everyone)]
public sealed record ResolutionFailed(string Resolution, string Choice) : EngineEvent;
