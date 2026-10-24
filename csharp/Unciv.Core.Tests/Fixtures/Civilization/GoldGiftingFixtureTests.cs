using Unciv.Core.Tests.Support;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;

namespace Unciv.Core.Tests.Fixtures.CivilizationFixtures;

// T142: port of tests/src/com/unciv/logic/civilization/diplomacy/GoldGiftingTests.kt over the real TestGame.
// The five trade cases are in GoldGiftingTradeFixtureTests (T150).
[Collection("RulesetCache")]
public sealed class GoldGiftingFixtureTests
{
    private readonly TestGame testGame = new();
    private readonly Civilization a;
    private readonly Civilization b;
    private readonly DiplomacyManager aDiplomacy;
    private readonly DiplomacyManager bDiplomacy;

    public GoldGiftingFixtureTests()
    {
        a = testGame.AddCiv();
        b = testGame.AddCiv();
        a.DiplomacyFunctions.MakeCivilizationsMeet(b);
        aDiplomacy = a.GetDiplomacyManager(b)!;
        bDiplomacy = b.GetDiplomacyManager(a)!;
        Constants().GoldGiftMultiplier = 1f;
        Constants().GoldGiftTradeMultiplier = 1f;
        Constants().GoldGiftDegradationMultiplier = 1f;
    }

    private Unciv.Models.ModConstants Constants() => a.GameInfo.Ruleset.ModOptions.Constants;

    [Fact]
    public void GoldGiftRecieveTest()
    {
        Assert.Equal(0, aDiplomacy.GetGoldGifts());
        Assert.Equal(0, bDiplomacy.GetGoldGifts());
        aDiplomacy.RecieveGoldGifts(10);
        Assert.True(aDiplomacy.GetGoldGifts() > 0);
        Assert.Equal(0, bDiplomacy.GetGoldGifts());
    }

    [Fact]
    public void GoldGiftTest()
    {
        Assert.Equal(0, aDiplomacy.GetGoldGifts());
        Assert.Equal(0, bDiplomacy.GetGoldGifts());
        aDiplomacy.GiftGold(10, true);
        Assert.True(aDiplomacy.GetGoldGifts() > 0);
        Assert.Equal(0, bDiplomacy.GetGoldGifts());
    }

    [Fact]
    public void GiftedGoldDisapears()
    {
        aDiplomacy.RecieveGoldGifts(10);
        Assert.True(aDiplomacy.GetGoldGifts() > 0);
        var gold = aDiplomacy.GetGoldGifts();
        aDiplomacy.NextTurn();
        var gold2 = aDiplomacy.GetGoldGifts();
        Assert.True(gold > gold2);
        Assert.True(gold2 >= 0);
    }

    [Fact]
    public void GiftedGoldIsReducedLessThan10Percent()
    {
        aDiplomacy.RecieveGoldGifts(1000);
        Assert.True(aDiplomacy.GetGoldGifts() > 0);
        var gold = aDiplomacy.GetGoldGifts();
        aDiplomacy.NextTurn();
        var gold2 = aDiplomacy.GetGoldGifts();
        Assert.True(gold > gold2);
        Assert.True(gold2 >= gold * .9);
        Assert.True(gold2 >= 0);
    }

    [Fact]
    public void GoldGiftedIsLostDuringWar()
    {
        aDiplomacy.RecieveGoldGifts(1000);
        bDiplomacy.RecieveGoldGifts(1000);
        Assert.True(aDiplomacy.GetGoldGifts() > 0);
        Assert.True(bDiplomacy.GetGoldGifts() > 0);
        bDiplomacy.DeclareWar();
        Assert.Equal(0, aDiplomacy.GetGoldGifts());
        Assert.True(bDiplomacy.GetGoldGifts() > 0);
    }

    [Fact]
    public void GiftingGoldReducesPreviousGiftsTaken()
    {
        aDiplomacy.GiftGold(1000, true);
        bDiplomacy.GiftGold(500, true);
        Assert.True(aDiplomacy.GetGoldGifts() > 0);
        Assert.True(aDiplomacy.GetGoldGifts() < 1000);
        Assert.Equal(0, bDiplomacy.GetGoldGifts());
    }

    [Fact]
    public void GiftedGoldDisapearsUsingGoldGiftDegradationMultiplierConstant()
    {
        var c = testGame.AddCiv();
        c.DiplomacyFunctions.MakeCivilizationsMeet(a);
        var caDiplomacy = c.GetDiplomacyManager(a)!;
        Constants().GoldGiftDegradationMultiplier = 1f;
        aDiplomacy.RecieveGoldGifts(100);
        caDiplomacy.RecieveGoldGifts(100);
        Assert.True(aDiplomacy.GetGoldGifts() > 0);
        Assert.True(caDiplomacy.GetGoldGifts() > 0);

        aDiplomacy.NextTurn();
        Constants().GoldGiftDegradationMultiplier = 10f;
        caDiplomacy.NextTurn();

        Assert.True(caDiplomacy.GetGoldGifts() < aDiplomacy.GetGoldGifts());
    }

    [Fact]
    public void GiftedGoldDoesntDisapearWhenDegradationMultiplierIsZero()
    {
        Constants().GoldGiftDegradationMultiplier = 0f;
        aDiplomacy.RecieveGoldGifts(1000);
        Assert.True(aDiplomacy.GetGoldGifts() > 0);
        var gold = aDiplomacy.GetGoldGifts();
        aDiplomacy.NextTurn();
        Assert.Equal(gold, aDiplomacy.GetGoldGifts());
    }

    [Fact]
    public void GiftedGoldDoesntDisapearWhenDegradationMultiplierIsZeroWithLowGold()
    {
        Constants().GoldGiftDegradationMultiplier = 0f;
        aDiplomacy.RecieveGoldGifts(10);
        Assert.True(aDiplomacy.GetGoldGifts() > 0);
        var gold = aDiplomacy.GetGoldGifts();
        aDiplomacy.NextTurn();
        Assert.Equal(gold, aDiplomacy.GetGoldGifts());
    }
}
