using Unciv.Json;
using Unciv.Net.Remote;
using Unciv.Queries;

namespace Unciv.Core.Tests.Net.Remote;

// T223: the exposure audit. A new query without an exposure decision fails here.
public class RemoteQueryPolicyTests
{
    private static QueryRegistry CoreRegistry()
    {
        var registry = new QueryRegistry(UncivJson.DefaultOptions);
        registry.RegisterFromAssembly(typeof(QueryRegistry).Assembly);
        return registry;
    }

    [Fact]
    public void EveryRegisteredQueryHasAnExposureDecision()
    {
        var undecided = CoreRegistry().RegisteredTypes.Where(it => !RemoteQueryPolicy.Default.TryGet(it, out _)).ToList();
        Assert.True(undecided.Count == 0, "Add an entry to RemoteQueryPolicy.Default (allow with a reason, or deny) for: " + string.Join(", ", undecided));
    }

    [Fact]
    public void NoDecisionNamesAQueryThatDoesNotExist()
    {
        var registered = CoreRegistry().RegisteredTypes.ToHashSet();
        var stale = RemoteQueryPolicy.Default.DecidedTypes.Where(it => !registered.Contains(it)).ToList();
        Assert.True(stale.Count == 0, "Stale exposure decisions: " + string.Join(", ", stale));
    }

    [Fact]
    public void EveryDecisionIsJustifiedAndCostIsSane()
    {
        foreach (var type in RemoteQueryPolicy.Default.DecidedTypes)
        {
            var exposure = RemoteQueryPolicy.Default.Get(type);
            Assert.False(string.IsNullOrWhiteSpace(exposure.Reason), type);
            if (exposure.Allowed) Assert.InRange(exposure.Cost, QueryExposure.Cheap, QueryExposure.Heavy);
            else Assert.Equal(0, exposure.Cost);
        }
    }

    // T240: the dev console reads the whole GameInfo by design and stays host-only.
    [Theory]
    [InlineData("console.Plan")]
    [InlineData("console.Autocomplete")]
    public void TheDevConsoleIsDeniedRemotely(string type)
    {
        var exposure = RemoteQueryPolicy.Default.Get(type);
        Assert.False(exposure.Allowed);
        Assert.Contains("T240", exposure.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("victory.Progress")]
    [InlineData("empire.TradeHint")]
    public void T240QueriesAreRemoteAllowed(string type) => Assert.True(RemoteQueryPolicy.Default.Get(type).Allowed);

    [Fact]
    public void OnlyTradeHintHasAnAskBudgetAndItIsHeavy()
    {
        var budgeted = RemoteQueryPolicy.Default.DecidedTypes.Where(it => RemoteQueryPolicy.Default.Get(it).AskBudget).ToList();
        Assert.Equal(["empire.TradeHint"], budgeted);
        Assert.Equal(QueryExposure.Heavy, RemoteQueryPolicy.Default.Get("empire.TradeHint").Cost);
    }

    [Theory]
    [InlineData("units.ReachableTiles")]
    [InlineData("units.PathPreview")]
    [InlineData("units.AttackableTargets")]
    public void PathfindingQueriesAreHeavy(string type) => Assert.Equal(QueryExposure.Heavy, RemoteQueryPolicy.Default.Get(type).Cost);

    [Fact]
    public void AnUndecidedQueryIsDeniedNotAllowed()
    {
        var exposure = RemoteQueryPolicy.Default.Get("future.Query");
        Assert.False(exposure.Allowed);
        Assert.False(RemoteQueryPolicy.Default.TryGet("future.Query", out _));
    }

    [Fact]
    public void QueriesThatBypassTheViewsAreStillMarkedByTheScanner()
    {
        // The audit is only as good as its list: every query class that reads game.Civilizations or GetCivilizationOrNull directly must be decided.
        // This pins the known raw readers so a rename cannot silently drop them from the audit.
        string[] rawReaders = ["victory.Progress", "empire.DiplomaticVoteResult", "empire.CityStateQuests", "empire.TradeOptions", "empire.SpyDestinations"];
        foreach (var type in rawReaders) Assert.True(RemoteQueryPolicy.Default.TryGet(type, out _), type);
    }
}
