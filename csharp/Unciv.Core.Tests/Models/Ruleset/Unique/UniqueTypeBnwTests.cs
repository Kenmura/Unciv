using Unciv.Models.Ruleset.Unique;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Core.Tests.Models.Ruleset.Unique;

/// <summary>Brave New World UniqueType hooks (bnw-spec section 3.2, ticket B003): declarations, parameter types and targets only.</summary>
public class UniqueTypeBnwTests
{
    private static readonly UniqueType[] Hooks = UniqueTypeExtensions.Entries
        .Where(it => (int)it > (int)UniqueType.BnwHappinessRules).ToArray();

    [Fact]
    public void HooksFollowTheGatesAndKeepOrdinalsContiguous()
    {
        Assert.Equal(106, Hooks.Length);
        Assert.Equal(UniqueType.BnwHappinessRules + 1, Hooks[0]);
        for (int i = 1; i < Hooks.Length; i++) Assert.Equal((int)Hooks[i - 1] + 1, (int)Hooks[i]);
        Assert.Equal(UniqueType.IsArchaeologicalDig, Hooks[^1]);
    }

    [Fact]
    public void EveryParameterPlaceholderResolvesToAKnownParameterType()
    {
        foreach (var hook in Hooks)
        {
            var map = hook.ParameterTypeMap();
            Assert.Equal(hook.Text().Count(c => c == '['), map.Count);
            Assert.All(map, types => Assert.NotEmpty(types));
        }
    }

    [Fact]
    public void HooksHaveTargetsAndAreFoundByTheirText()
    {
        foreach (var hook in Hooks)
        {
            Assert.NotEmpty(hook.TargetTypes());
            Assert.DoesNotContain(UniqueTarget.ModOptions, hook.TargetTypes());
            Assert.Equal(hook, UniqueTypeExtensions.UniqueTypeMap[hook.PlaceholderText()]);
        }
    }

    [Theory]
    [InlineData(UniqueType.ContainsGreatWorkWhenBuilt, UniqueTarget.Building, true)]
    [InlineData(UniqueType.ContainsGreatWorkWhenBuilt, UniqueTarget.Wonder, true)]
    [InlineData(UniqueType.ContainsGreatWorkWhenBuilt, UniqueTarget.Unit, false)]
    [InlineData(UniqueType.TourismOpenBorders, UniqueTarget.Policy, true)]
    [InlineData(UniqueType.TradeRouteSlots, UniqueTarget.Tech, true)]
    [InlineData(UniqueType.CanEstablishTradeRoutes, UniqueTarget.Unit, true)]
    [InlineData(UniqueType.CanEstablishTradeRoutes, UniqueTarget.Building, false)]
    [InlineData(UniqueType.TechFromCityConquest, UniqueTarget.Nation, true)]
    [InlineData(UniqueType.TechFromCityConquest, UniqueTarget.Policy, false)]
    [InlineData(UniqueType.PostCombatPromotion, UniqueTarget.Promotion, true)]
    [InlineData(UniqueType.IsLandmark, UniqueTarget.Improvement, true)]
    [InlineData(UniqueType.AllowsWorldCongress, UniqueTarget.Tech, true)]
    public void TargetsMatchTheSpecColumns(UniqueType type, UniqueTarget target, bool accepted) =>
        Assert.Equal(accepted, type.CanAcceptUniqueTarget(target));

    [Fact]
    public void SpecExamplesParse()
    {
        var unique = new UniqueObject("[+2] Tourism <after discovering [Radio]>");
        Assert.Equal(UniqueType.TourismAmount, unique.Type);
        Assert.Equal("+2", unique.Params[0]);
        Assert.Equal(UniqueType.TradeRouteSlots, new UniqueObject("[+1] trade route slots").Type);
        Assert.Equal(UniqueType.GoldFromLandTradeRoutes, new UniqueObject("[+200] Gold (x1/100) from land trade routes [in this city]").Type);
        Assert.Equal(UniqueType.AllowsDomesticTradeRoutes, new UniqueObject("Allows domestic [Food] trade routes from this city").Type);
    }
}
