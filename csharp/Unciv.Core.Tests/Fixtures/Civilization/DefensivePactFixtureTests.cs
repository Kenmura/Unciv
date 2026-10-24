using Unciv.Core.Tests.Support;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;

namespace Unciv.Core.Tests.Fixtures.CivilizationFixtures;

// T142: port of tests/src/com/unciv/logic/civilization/DefensivePactTests.kt over the real TestGame.
[Collection("RulesetCache")]
public sealed class DefensivePactFixtureTests
{
    private readonly TestGame testGame = new();
    private readonly Civilization a;
    private readonly Civilization b;
    private readonly Civilization c;

    public DefensivePactFixtureTests()
    {
        a = AddCiv();
        b = AddCiv();
        c = AddCiv();
    }

    private Civilization AddCiv()
    {
        var civ = testGame.AddCiv();
        testGame.AddUnit("Warrior", civ, null);
        return civ;
    }

    private void MeetAll()
    {
        a.DiplomacyFunctions.MakeCivilizationsMeet(b);
        a.DiplomacyFunctions.MakeCivilizationsMeet(c);
        b.DiplomacyFunctions.MakeCivilizationsMeet(c);
    }

    [Fact]
    public void CivsWithDefensivePactsAreCalledIn()
    {
        MeetAll();
        a.GetDiplomacyManager(b)!.SignDefensivePact(100);
        Assert.True(a.GetDiplomacyManager(b)!.DiplomaticStatus == DiplomaticStatus.DefensivePact
            && b.GetDiplomacyManager(a)!.DiplomaticStatus == DiplomaticStatus.DefensivePact);
        c.GetDiplomacyManager(a)!.DeclareWar();

        Assert.True(c.IsAtWarWith(b) && c.IsAtWarWith(a));
        Assert.True(b.IsAtWarWith(c) && a.IsAtWarWith(c));
        Assert.True(b.GetDiplomacyManager(a)!.DiplomaticStatus == DiplomaticStatus.DefensivePact);
    }

    [Fact]
    public void DefensivePactCancelWhenAttacking()
    {
        MeetAll();
        a.GetDiplomacyManager(b)!.SignDefensivePact(100);
        a.GetDiplomacyManager(c)!.DeclareWar();

        Assert.False(a.GetDiplomacyManager(b)!.DiplomaticStatus == DiplomaticStatus.DefensivePact);
    }

    [Fact]
    public void DefensivePactTimeout()
    {
        MeetAll();
        a.GetDiplomacyManager(b)!.SignDefensivePact(1);
        Assert.True(a.GetDiplomacyManager(b)!.DiplomaticStatus == DiplomaticStatus.DefensivePact);
        a.GetDiplomacyManager(b)!.NextTurn();
        b.GetDiplomacyManager(a)!.NextTurn();

        Assert.False(a.GetDiplomacyManager(b)!.DiplomaticStatus == DiplomaticStatus.DefensivePact
            || b.GetDiplomacyManager(a)!.DiplomaticStatus == DiplomaticStatus.DefensivePact);
    }

    [Fact]
    public void BreakingDefensivePact()
    {
        MeetAll();
        var abDiploManager = a.GetDiplomacyManager(b)!;
        abDiploManager.SignDefensivePact(100);
        abDiploManager.DeclareWar();
        Assert.True(abDiploManager.OtherCivDiplomacy().HasModifier(DiplomaticModifiers.BetrayedDefensivePact));
        Assert.True(abDiploManager.HasModifier(DiplomaticModifiers.DefensivePact));
        Assert.False(abDiploManager.OtherCivDiplomacy().HasModifier(DiplomaticModifiers.DefensivePact));
    }

    [Fact]
    public void BreakingDefensivePactWithFriends()
    {
        MeetAll();
        var abDiploManager = a.GetDiplomacyManager(b)!;
        var bcDiploManager = b.GetDiplomacyManager(c)!;
        abDiploManager.SignDefensivePact(100);
        bcDiploManager.SignDeclarationOfFriendship();

        abDiploManager.DeclareWar();
        Assert.True(c.GetDiplomacyManager(a)!.HasModifier(DiplomaticModifiers.BetrayedDefensivePact));
    }
}
