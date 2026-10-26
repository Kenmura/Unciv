using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace Unciv.Net.Remote;

/// <summary>Coarse failure codes of <see cref="QueryResult"/>. Deliberately few: a query's own error text and code can say whether something exists (see <see cref="RemoteQueryHost"/>).</summary>
public static class QueryWireCodes
{
    /// <summary>Not registered, or registered but denied for remote use: indistinguishable on purpose.</summary>
    public const string UnknownQuery = "unknown-query";
    public const string MalformedQuery = "malformed-query";
    /// <summary>The query ran and refused: unknown or invisible id, feature off, nothing to show. One code for all of them.</summary>
    public const string Rejected = "query-rejected";
    public const string RateLimited = "rate-limited";
    public const string TurnResolving = "turn-resolving";
    public const string ResultTooLarge = "result-too-large";
    public const string InternalError = "internal-error";
    // Client side only (never sent by a host).
    public const string TooManyPending = "too-many-pending";
    public const string SessionClosed = "session-closed";
    public const string MalformedResult = "malformed-result";
}

/// <summary>The exposure decision for one query type: may a remote viewer run it, what it costs the rate limiter, and why.</summary>
public sealed record QueryExposure(bool Allowed, int Cost, string Reason, bool AskBudget = false)
{
    public const int Cheap = 1;
    public const int Moderate = 3;
    public const int Heavy = 5;

    public static QueryExposure Allow(string reason, int cost = Cheap) => new(true, cost, reason);

    /// <summary>Allow, and also cap how often one bound civ may have this query actually run (<see cref="RemoteQueryLimits.AskBudget"/>), for queries whose answers can be probed.</summary>
    public static QueryExposure AllowBudgeted(string reason, int cost = Heavy) => new(true, cost, reason, AskBudget: true);

    public static QueryExposure Deny(string reason) => new(false, 0, reason);
}

/// <summary>
/// The per-query exposure audit (T223): for every registered query, whether a remote client may ask it. A query not listed here is denied
/// (fail closed); the test <c>RemoteQueryPolicyTests</c> fails when a registered query has no entry, so adding a query forces a decision.
/// Rule for Allow: the query reads the game only through the viewer's <c>GameView</c> (fog, other civs' private state are filtered there), or it
/// reads raw state but names/anonymises every other civ by what the viewer knows, and the Reason says which. Cost is in rate-limit tokens.
/// </summary>
public sealed class RemoteQueryPolicy
{
    private readonly FrozenDictionary<string, QueryExposure> decisions;

    public RemoteQueryPolicy(IEnumerable<KeyValuePair<string, QueryExposure>> decisions)
    {
        this.decisions = decisions.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public IReadOnlyCollection<string> DecidedTypes => decisions.Keys;

    public bool TryGet(string type, [MaybeNullWhen(false)] out QueryExposure exposure) => decisions.TryGetValue(type, out exposure);

    /// <summary>The exposure of a type; an undecided type is denied.</summary>
    public QueryExposure Get(string type) => decisions.TryGetValue(type, out var exposure) ? exposure : QueryExposure.Deny("no exposure decision");

    private const string ViewOnly = "reads only through the viewer's GameView";

    public static RemoteQueryPolicy Default { get; } = new(new Dictionary<string, QueryExposure>
    {
        // ---- cities
        ["cities.BuyableTiles"] = QueryExposure.Allow("viewer's own city; " + ViewOnly),
        ["cities.CityPanel"] = QueryExposure.Allow("own city in full, foreign city only what ForeignCityView shows; " + ViewOnly),
        ["cities.CityTiles"] = QueryExposure.Allow("viewer's own city; " + ViewOnly),
        ["cities.ConstructionList"] = QueryExposure.Allow("viewer's own city; " + ViewOnly),
        // ---- empire
        ["empire.CityStateQuests"] = QueryExposure.Allow("only civs the viewer knows (KnownCiv, same error as a missing civ); a standing string naming an unmet civ is dropped"),
        ["empire.DiplomacyActions"] = QueryExposure.Allow("only civs the viewer knows (KnownCiv); " + ViewOnly),
        ["empire.DiplomacyPanel"] = QueryExposure.Allow("only civs the viewer knows (KnownCiv); " + ViewOnly),
        ["empire.DiplomaticVote"] = QueryExposure.Allow("viewer's own pending vote and the civs it knows"),
        ["empire.DiplomaticVoteResult"] = QueryExposure.Allow("rows, vote targets, winner text and UN owner of unmet civs are dropped or anonymised; total votes received by candidates are public election totals"),
        ["empire.GreatPeople"] = QueryExposure.Allow("viewer's own great people; " + ViewOnly),
        ["empire.Notifications"] = QueryExposure.Allow("viewer's own notifications"),
        ["empire.Overview"] = QueryExposure.Allow("viewer's own empire; other civs only as the GameParameters.ShowRankings setting allows, as in Kotlin", QueryExposure.Moderate),
        ["empire.PendingDecisions"] = QueryExposure.Allow("viewer's own alerts and pending choices"),
        ["empire.PolicyTree"] = QueryExposure.Allow("viewer's own policies; ruleset data", QueryExposure.Moderate),
        ["empire.Religion"] = QueryExposure.Allow("viewer's own religion and the public founded religions", QueryExposure.Moderate),
        ["empire.SpyDestinations"] = QueryExposure.Allow("only explored cities (matches Spy.CanMoveTo: an explored tile is sufficient to send undercover spies even in fog of war); owner is omitted when unmet"),
        ["empire.TechTree"] = QueryExposure.Allow("viewer's own research; ruleset data", QueryExposure.Moderate),
        ["empire.TopBar"] = QueryExposure.Allow("viewer's own treasury and rates"),
        ["empire.TradeHint"] = QueryExposure.AllowBudgeted("only civs the viewer knows (KnownCiv, through the view's known civs), only offers TradeOptions lists, answer is a 3-way bucket and never the score; off unless the host enabled TradeHintsEnabled; a per-civ ask budget stops bisecting the AI's valuation with gold offers"),
        ["empire.TradeOptions"] = QueryExposure.Allow("only civs the viewer knows; offers are the viewer's and what the partner may trade", QueryExposure.Moderate),
        // ---- console
        ["console.Autocomplete"] = QueryExposure.Deny("T240, kept denied: the dev console is a host-side cheat (God mode) and completes against the whole GameInfo (every civ, unit, city and tile name, hidden ones included); a remote view cannot express that, so it is not remote-queryable"),
        ["console.Plan"] = QueryExposure.Deny("T240, kept denied: the dev console is a host-side cheat (God mode); planning a line runs ConsoleRunner on the whole GameInfo, so its hints and errors name hidden civs, units and tiles"),
        // ---- tiles
        ["tiles.TileInfo"] = QueryExposure.Allow("TileMapView returns nothing for an unexplored tile and hides what fog hides"),
        // ---- tutorial
        ["tutorial.Task"] = QueryExposure.Allow("viewer's own tutorial state"),
        // ---- units
        ["units.AttackableTargets"] = QueryExposure.Allow("targets are visible tiles only; " + ViewOnly, QueryExposure.Heavy),
        ["units.AttackPreview"] = QueryExposure.Allow("defender through CombatantView of a visible tile; " + ViewOnly, QueryExposure.Moderate),
        ["units.PathPreview"] = QueryExposure.Allow("viewer's own unit over tiles the viewer knows; " + ViewOnly, QueryExposure.Heavy),
        ["units.PromotableUnits"] = QueryExposure.Allow("viewer's own units"),
        ["units.PromotionChoices"] = QueryExposure.Allow("viewer's own unit"),
        ["units.ReachableTiles"] = QueryExposure.Allow("viewer's own unit over tiles the viewer knows; " + ViewOnly, QueryExposure.Heavy),
        ["units.UnitActions"] = QueryExposure.Allow("viewer's own unit; " + ViewOnly, QueryExposure.Moderate),
        // ---- victory
        ["victory.Demographics"] = QueryExposure.Allow("standings of other civs only when the game lets the viewer see them (CanViewCivStats); unmet civs are anonymous", QueryExposure.Moderate),
        ["victory.GameOver"] = QueryExposure.Allow("answers only once the game has ended or for the viewer's own defeat"),
        ["victory.History"] = QueryExposure.Allow("series of other civs only when CanViewCivStats; other civs omit mid-turn points during an active game to prevent mid-turn probing; unmet civs are anonymous", QueryExposure.Moderate),
        ["victory.Progress"] = QueryExposure.Allow("rival rows only for civs the viewer knows, itself or defeated ones and only when CanViewCivStats; unmet civs are not listed, the hidden-civ-count rule uses the viewer's knowledge", QueryExposure.Moderate),
        ["victory.Rankings"] = QueryExposure.Allow("rows of other civs only when CanViewCivStats; other civs use frozen turn-snapshot stats during an active game to prevent mid-turn probing; unmet civs are anonymous", QueryExposure.Moderate),
        ["victory.Scores"] = QueryExposure.Allow("rows of other civs only when CanViewCivStats; unmet civs are anonymous", QueryExposure.Moderate),
    });
}
