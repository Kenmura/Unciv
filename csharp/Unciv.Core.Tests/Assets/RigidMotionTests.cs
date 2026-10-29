using Unciv.Assets;

namespace Unciv.Core.Tests.Assets;

/// <summary>C083: the code-driven motion of ships, siege, armour and aircraft.</summary>
public sealed class RigidMotionTests
{
    [Theory]
    [InlineData("WaterMelee", RigidKind.Ship)]
    [InlineData("Submarine", RigidKind.Ship)]
    [InlineData("Siege", RigidKind.Siege)]
    [InlineData("Armor", RigidKind.Armor)]
    [InlineData("Bomber", RigidKind.Air)]
    [InlineData("Helicopter", RigidKind.Air)]
    [InlineData("Sword", RigidKind.None)]
    [InlineData("Mounted", RigidKind.None)]
    [InlineData(null, RigidKind.None)]
    public void UnitTypesMapToKinds(string? type, RigidKind kind) => Assert.Equal(kind, RigidMotion.KindOf(type));

    [Theory]
    [InlineData("Sword", "Battering Ram", RigidKind.Siege)]
    [InlineData("Sword", "Swordsman", RigidKind.None)]
    [InlineData("Siege", "Catapult", RigidKind.Siege)]
    [InlineData(null, "Battering Ram", RigidKind.Siege)]
    [InlineData("Armor", "Tank", RigidKind.Armor)]
    [InlineData(null, null, RigidKind.None)]
    public void TheBatteringRamIsSiegeByName(string? type, string? name, RigidKind kind) => Assert.Equal(kind, RigidMotion.KindOf(type, name));

    [Fact]
    public void ACueIsAtRestAtItsEndsAndMovesInTheMiddle()
    {
        foreach (var kind in new[] { RigidKind.Siege, RigidKind.Armor, RigidKind.Ship, RigidKind.Air })
        {
            Assert.Equal(0f, RigidMotion.Pose(kind, CueKind.Shot, 0f, 0f).Recoil);
            Assert.Equal(0f, RigidMotion.Pose(kind, CueKind.Shot, 1f, 1f).Recoil);
        }
        Assert.Equal(RigidMotion.SiegeRecoil, RigidMotion.Pose(RigidKind.Siege, CueKind.Shot, 0.5f, 0.5f).Recoil, 5);
        Assert.Equal(RigidMotion.ArmorRecoil, RigidMotion.Pose(RigidKind.Armor, CueKind.Strike, 0.5f, 0.5f).Recoil, 5);
        Assert.True(RigidMotion.Pose(RigidKind.Air, CueKind.Strike, 0.5f, 0.5f).Pitch < 0f);
        Assert.True(RigidMotion.Pose(RigidKind.Air, CueKind.Move, 0.5f, 0.5f).Roll > 0f);
    }

    [Fact]
    public void AnArmorTurretSwingsOntoTheTargetAndStays()
    {
        Assert.Equal(RigidMotion.TurretSweep, RigidMotion.Pose(RigidKind.Armor, CueKind.Shot, 0f, 0f).TurretYaw, 5);
        Assert.Equal(0f, RigidMotion.Pose(RigidKind.Armor, CueKind.Shot, 0.5f, 0f).TurretYaw);
        Assert.Equal(0f, RigidMotion.Pose(RigidKind.Armor, CueKind.Shot, 1f, 0f).TurretYaw);
    }

    [Fact]
    public void AShipBobsAndRollsUnderWayFromTheClock()
    {
        var a = RigidMotion.Pose(RigidKind.Ship, CueKind.Move, 0.3f, RigidMotion.BobSeconds / 4f);
        Assert.Equal(RigidMotion.ShipBob, a.Bob, 5);
        Assert.Equal(RigidMotion.Pose(RigidKind.Ship, CueKind.Move, 0.9f, RigidMotion.BobSeconds / 4f), a); // clock, not progress
        Assert.Equal(0f, RigidMotion.Pose(RigidKind.Ship, CueKind.Move, 0f, 0f).Bob, 5);
    }

    [Fact]
    public void OtherCuesAndOtherKindsLeaveTheBodyAlone()
    {
        Assert.Equal(RigidPose.Rest, RigidMotion.Pose(RigidKind.None, CueKind.Shot, 0.5f, 0.5f));
        Assert.Equal(RigidPose.Rest, RigidMotion.Pose(RigidKind.Siege, CueKind.Hit, 0.5f, 0.5f));
        Assert.Equal(RigidPose.Rest, RigidMotion.Pose(RigidKind.Siege, CueKind.Move, 0.5f, 0.5f));
        Assert.Equal(RigidPose.Rest, RigidMotion.Pose(RigidKind.Air, CueKind.Death, 0.5f, 0.5f));
    }

    [Fact]
    public void NonFiniteInputsGiveRest()
    {
        Assert.Equal(RigidPose.Rest, RigidMotion.Pose(RigidKind.Ship, CueKind.Move, float.NaN, 0f));
        Assert.Equal(RigidPose.Rest, RigidMotion.Pose(RigidKind.Ship, CueKind.Move, 0.5f, float.PositiveInfinity));
        Assert.Equal(RigidMotion.SiegeRecoil, RigidMotion.Pose(RigidKind.Siege, CueKind.Shot, 7f, 0f).Recoil + RigidMotion.SiegeRecoil, 5); // progress is clamped: 7 is the end, at rest
    }
}
