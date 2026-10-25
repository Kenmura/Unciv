using Unciv.Commands;
using Unciv.Commands.Diplomacy;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Commands.Diplomacy;

// T193: the City-State diplomacy table.
[Collection("RulesetCache")]
public sealed class CityStateCommandTests
{
    private const string BuyUnique = "Can spend Gold to annex or puppet a City-State that has been your Ally for [0] turns";
    private readonly DiplomacyWorld w = new();

    private static CityStateFunctions Funcs(CivilizationObject cityState) => (CityStateFunctions)cityState.CityStateFunctions;

    [Fact]
    public void GiftGoldBuysInfluence()
    {
        w.A.AddGold(1000 - w.A.Gold);
        float before = w.Diplo(w.S, w.A).GetInfluence();
        int expected = Funcs(w.S).InfluenceGainedByGift(w.A, 500);

        w.Ok(new GiftGold { CivId = w.S.CivId, Amount = 500 });

        Assert.Equal(500, w.A.Gold);
        Assert.Equal(before + expected, w.Diplo(w.S, w.A).GetInfluence());
    }

    [Fact]
    public void GiftGoldIsRejectedWhereTheScreenDisablesIt()
    {
        w.A.AddGold(1000 - w.A.Gold);
        w.Rejected(new GiftGold { CivId = w.S.CivId, Amount = 300 }, CommandErrors.InvalidTarget);
        w.Rejected(new GiftGold { CivId = w.B.CivId, Amount = 250 }, CommandErrors.InvalidTarget);
        w.A.AddGold(100 - w.A.Gold);
        w.Rejected(new GiftGold { CivId = w.S.CivId, Amount = 250 }, "not-enough-gold");
        w.A.AddGold(1000 - w.A.Gold);
        w.MakeWar(w.A, w.S);
        w.Rejected(new GiftGold { CivId = w.S.CivId, Amount = 250 }, CommandErrors.NotAllowed);
    }

    [Fact]
    public void TributeGoldNeedsWillingnessAndCostsInfluence()
    {
        var command = new TributeGold { CivId = w.S.CivId };
        Assert.True(Funcs(w.S).GetTributeWillingness(w.A) < 0);
        w.Rejected(command, CommandErrors.NotAllowed);

        w.Game.Ruleset.ModOptions.Constants.TributeGlobalModifier = 1000;
        float influence = w.Diplo(w.S, w.A).GetInfluence();
        int gold = w.A.Gold;
        w.Ok(command);
        Assert.Equal(gold + Funcs(w.S).GoldGainedByTribute(), w.A.Gold);
        Assert.Equal(influence - 15f, w.Diplo(w.S, w.A).GetInfluence());
        w.Rejected(command, CommandErrors.NotAllowed); // recently paid tribute
    }

    [Fact]
    public void TributeGoldAndWorkerAreRejectedAtWarAndForMajorCivs()
    {
        w.Game.Ruleset.ModOptions.Constants.TributeGlobalModifier = 1000;
        w.Rejected(new TributeGold { CivId = w.B.CivId }, CommandErrors.InvalidTarget);
        w.Rejected(new TributeWorker { CivId = w.B.CivId }, CommandErrors.InvalidTarget);
        w.MakeWar(w.A, w.S);
        w.Rejected(new TributeGold { CivId = w.S.CivId }, CommandErrors.NotAllowed);
        w.Rejected(new TributeWorker { CivId = w.S.CivId }, CommandErrors.NotAllowed);
    }

    [Fact]
    public void TributeWorkerGivesAWorkerForInfluence()
    {
        var command = new TributeWorker { CivId = w.S.CivId };
        w.Rejected(command, CommandErrors.NotAllowed);
        w.Game.Ruleset.ModOptions.Constants.TributeGlobalModifier = 1000;
        float influence = w.Diplo(w.S, w.A).GetInfluence();
        int units = w.A.Units.GetCivUnits().Count();

        w.Ok(command);

        Assert.Equal(units + 1, w.A.Units.GetCivUnits().Count());
        Assert.Equal(influence - 50f, w.Diplo(w.S, w.A).GetInfluence());
    }

    [Fact]
    public void ProtectionCanBePledgedAndWithdrawnAfterTheCooldown()
    {
        w.Rejected(new WithdrawProtection { CivId = w.S.CivId }, CommandErrors.NotAllowed);
        w.Rejected(new PledgeProtection { CivId = w.B.CivId }, CommandErrors.InvalidTarget);
        w.Ok(new PledgeProtection { CivId = w.S.CivId });
        Assert.Equal(DiplomaticStatus.Protector, w.Diplo(w.S, w.A).DiplomaticStatus);
        w.Rejected(new PledgeProtection { CivId = w.S.CivId }, CommandErrors.NotAllowed);
        w.Rejected(new WithdrawProtection { CivId = w.S.CivId }, CommandErrors.NotAllowed); // pledged too recently

        w.Diplo(w.S, w.A).RemoveFlag(DiplomacyFlags.RecentlyPledgedProtection);
        w.Ok(new WithdrawProtection { CivId = w.S.CivId });
        Assert.NotEqual(DiplomaticStatus.Protector, w.Diplo(w.S, w.A).DiplomaticStatus);
        w.Rejected(new PledgeProtection { CivId = w.S.CivId }, CommandErrors.NotAllowed); // withdrew too recently
    }

    [Fact]
    public void CityStatePeaceIsMadeAtOnce()
    {
        var command = new NegotiateCityStatePeace { CivId = w.S.CivId };
        w.Rejected(command, CommandErrors.NotAllowed); // not at war
        w.MakeWar(w.A, w.S);
        w.Diplo(w.S, w.A).SetFlag(DiplomacyFlags.DeclaredWar, 10);
        w.Rejected(command, CommandErrors.NotAllowed);
        w.Diplo(w.S, w.A).RemoveFlag(DiplomacyFlags.DeclaredWar);

        w.Ok(command);

        Assert.False(w.A.IsAtWarWith(w.S));
        Assert.Empty(w.S.TradeRequests);
    }

    [Fact]
    public void CityStatePeaceIsRejectedWhileAtWarWithItsAllyAndWhenRelationshipsAreFrozen()
    {
        w.MakeWar(w.A, w.S);
        w.MakeWar(w.A, w.B);
        w.Diplo(w.S, w.B).SetInfluence(100f);
        Funcs(w.S).UpdateAllyCivForCityState();
        Assert.Equal(w.B, w.S.AllyCiv);
        w.Rejected(new NegotiateCityStatePeace { CivId = w.S.CivId }, CommandErrors.NotAllowed);

        var frozen = new DiplomacyWorld(modUniques: ["Diplomatic relationships cannot change"]);
        frozen.MakeWar(frozen.A, frozen.S);
        frozen.Rejected(new NegotiateCityStatePeace { CivId = frozen.S.CivId }, CommandErrors.NotAllowed);
    }

    [Fact]
    public void DiplomaticMarriageBuysTheCityStateAndQueuesAPopupPerCity()
    {
        var world = new DiplomacyWorld([BuyUnique]);
        var command = new DiplomaticMarriage { CivId = world.S.CivId };
        world.Rejected(command, CommandErrors.NotAllowed); // not an ally yet
        world.Diplo(world.S, world.A).SetInfluence(100f);
        Funcs(world.S).UpdateAllyCivForCityState();
        world.Diplo(world.A, world.S).RemoveFlag(DiplomacyFlags.MarriageCooldown); // set when the alliance began
        int cost = Funcs(world.S).GetDiplomaticMarriageCost();
        world.A.AddGold(cost - world.A.Gold - 1);
        world.Rejected(command, CommandErrors.NotAllowed); // cannot pay
        world.A.AddGold(1);

        world.Ok(command);

        Assert.True(world.S.IsDefeated());
        Assert.Equal(0, world.A.Gold);
        Assert.Contains(world.CityS, world.A.Cities);
        var alert = Assert.Single(world.A.PopupAlerts);
        Assert.Equal((AlertType.DiplomaticMarriage, world.CityS.Id), (alert.Type, alert.Value));
    }

    [Fact]
    public void DiplomaticMarriageNeedsTheUnique()
    {
        w.Diplo(w.S, w.A).SetInfluence(100f);
        Funcs(w.S).UpdateAllyCivForCityState();
        w.Diplo(w.A, w.S).RemoveFlag(DiplomacyFlags.MarriageCooldown);
        w.A.AddGold(100000);
        w.Rejected(new DiplomaticMarriage { CivId = w.S.CivId }, CommandErrors.NotAllowed);
    }

    // ---- gift improvement

    private Unciv.Logic.Map.HexCoord ImprovableIron(DiplomacyWorld world)
    {
        world.S.Tech.AddTechnology("Iron Working");
        world.S.Tech.AddTechnology("Mining");
        var tile = world.CityS.GetTiles().First(it => it != world.CityS.GetCenterTile() && it.IsLand);
        tile.BaseTerrain = "Hill";
        tile.SetTerrainTransients();
        tile.TileResource = world.Game.Ruleset.TileResources["Iron"];
        tile.SetTransients();
        world.Diplo(world.S, world.A).SetInfluence(70f);
        world.A.AddGold(500 - world.A.Gold);
        return tile.Position;
    }

    [Fact]
    public void GiftImprovementBuildsOnAResourceTileForGold()
    {
        var tile = ImprovableIron(w);
        var command = new GiftImprovement { CivId = w.S.CivId, Tile = tile, Improvement = "Mine" };

        w.Ok(command);

        Assert.Equal("Mine", w.Game.GetTile(tile).Improvement);
        Assert.Equal(300, w.A.Gold);
        w.Rejected(command, CommandErrors.NotAllowed); // nothing left to improve
    }

    [Fact]
    public void GiftImprovementIsRejectedWhereTheScreenDisablesIt()
    {
        var tile = ImprovableIron(w);
        var good = new GiftImprovement { CivId = w.S.CivId, Tile = tile, Improvement = "Mine" };
        w.Rejected(new GiftImprovement { CivId = w.B.CivId, Tile = tile, Improvement = "Mine" }, CommandErrors.InvalidTarget);
        w.Rejected(new GiftImprovement { CivId = w.S.CivId, Tile = tile, Improvement = "Nonsense" }, CommandErrors.InvalidTarget);
        w.Rejected(new GiftImprovement { CivId = w.S.CivId, Tile = tile, Improvement = "Farm" }, CommandErrors.InvalidTarget);
        w.Rejected(new GiftImprovement { CivId = w.S.CivId, Tile = w.CityS.GetCenterTile().Position, Improvement = "Mine" }, CommandErrors.InvalidTarget);

        w.Diplo(w.S, w.A).SetInfluence(59f);
        w.Rejected(good, CommandErrors.NotAllowed);
        w.Diplo(w.S, w.A).SetInfluence(60f);
        w.A.AddGold(199 - w.A.Gold);
        w.Rejected(good, "not-enough-gold");
    }

    [Fact]
    public void GiftImprovementIsRejectedWhenThereIsNothingToImprove()
    {
        w.Diplo(w.S, w.A).SetInfluence(70f);
        w.A.AddGold(500 - w.A.Gold);
        w.Rejected(new GiftImprovement { CivId = w.S.CivId, Tile = w.CityS.GetCenterTile().Position, Improvement = "Mine" }, CommandErrors.NotAllowed);
    }
}
