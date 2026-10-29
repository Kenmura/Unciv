using Unciv.Assets;
using Unciv.Commands.Events;
using Unciv.Logic.Map;

namespace Unciv.Core.Tests.Assets;

public class CombatEffectsTests
{
    private static readonly HexCoord A = new(0, 0);
    private static readonly HexCoord B = new(1, 0);
    private static readonly HexCoord C = new(2, 0);

    private static Cue Make(CueKind kind, int? unit = 1, int amount = 0, IReadOnlyList<HexCoord>? path = null) => new(kind, unit, A, B, 0f, 1f, amount, path);

    [Fact]
    public void MeleeSwingSoundsAtTheStepInAndTheBlowSparksAtTheDefender()
    {
        var swing = CombatEffects.For(Make(CueKind.Strike));
        Assert.Contains(swing, it => it.Sound == CombatEffects.SwingSound && it.At == CuePoses.StepInEnd);
        Assert.Contains(swing, it => it.Effect == EffectKind.Dust && it.Tile == A);
        var hit = CombatEffects.For(Make(CueKind.Hit, amount: 7));
        var moment = Assert.Single(hit);
        Assert.Equal((0f, B, EffectKind.Spark, CombatEffects.HitSound), (moment.At, moment.Tile, moment.Effect, moment.Sound));
    }

    [Fact]
    public void ABlowThatDoesNoDamageMakesNoSparks() => Assert.Empty(CombatEffects.For(Make(CueKind.Hit, amount: 0)));

    [Fact]
    public void AUnitsShotFlashesAtTheReleaseAndACitysShotIsSilentButItsFlashBooms()
    {
        var shot = Assert.Single(CombatEffects.For(Make(CueKind.Shot)));
        Assert.Equal(CuePoses.ShotRelease, shot.At);
        Assert.Equal((EffectKind.Muzzle, CombatEffects.ShootSound, A), (shot.Effect, shot.Sound, shot.Tile));
        Assert.Equal(0f, CuePoses.ShotFlight(CuePoses.ShotRelease));
        Assert.Empty(CombatEffects.For(Make(CueKind.Shot, unit: null)));
        var flash = Assert.Single(CombatEffects.For(Make(CueKind.Bombard, unit: null)));
        Assert.Equal((EffectKind.Muzzle, CombatEffects.BombardSound), (flash.Effect, flash.Sound));
    }

    [Fact]
    public void AWalkRaisesDustAtTheStartOfEveryStep()
    {
        var moments = CombatEffects.For(Make(CueKind.Move, path: [A, B, C]));
        Assert.Equal([(0f, A), (0.5f, B)], moments.Select(it => (it.At, it.Tile)));
        Assert.All(moments, it => Assert.Equal(EffectKind.Dust, it.Effect));
        Assert.Empty(CombatEffects.For(Make(CueKind.Move, path: [A])));
    }

    [Fact]
    public void ADeathGroansAtOnceAndRaisesDustWhenTheUnitFalls()
    {
        var moments = CombatEffects.For(Make(CueKind.Death));
        Assert.Equal(CombatEffects.DeathSound, moments[0].Sound);
        Assert.Equal(EffectKind.Dust, moments[1].Effect);
        Assert.True(moments[0].At < moments[1].At);
    }

    [Fact]
    public void MomentsAreInTimeOrderAndInsideTheCue()
    {
        foreach (var kind in Enum.GetValues<CueKind>())
        {
            var moments = CombatEffects.For(Make(kind, amount: 3, path: [A, B, C]));
            Assert.Equal(moments.OrderBy(it => it.At), moments);
            Assert.All(moments, it => Assert.InRange(it.At, 0f, 1f));
        }
    }

    [Fact]
    public void EveryPlannedCueMomentNamesASoundThatExists()
    {
        var attacked = new UnitAttacked("a", 1, null, A, "b", 2, null, B, 5, 3, true, true);
        var cues = CombatPresentation.Plan([attacked]).SelectMany(it => it);
        foreach (var name in cues.SelectMany(CombatEffects.For).Select(it => it.Sound).OfType<string>())
        {
            Assert.Contains(name, CombatEffects.SoundNames);
            Assert.Contains(ProceduralSounds.Samples(name), s => System.Math.Abs(s) > 2000);
        }
    }

    [Fact]
    public void CombatSoundsAreDistinctAndNotUncivSoundPresets()
    {
        Assert.Equal(CombatEffects.SoundNames.Count, CombatEffects.SoundNames.Select(it => Convert.ToBase64String(ProceduralSounds.Wav(it))).Distinct().Count());
        foreach (var name in CombatEffects.SoundNames.Where(it => it.StartsWith("combat_", StringComparison.Ordinal)))
            Assert.DoesNotContain(name, ProceduralSounds.PresetNames);
    }

    [Theory]
    [InlineData(EffectKind.Spark)]
    [InlineData(EffectKind.Dust)]
    [InlineData(EffectKind.Muzzle)]
    public void ParticlesFadeOutStayFiniteAndAreDeterministic(EffectKind kind)
    {
        for (var i = 0; i < CombatEffects.ParticleCount(kind); i++)
        {
            Assert.Equal(CombatEffects.Particle(kind, i, 0.4f), CombatEffects.Particle(kind, i, 0.4f));
            Assert.Equal(0f, CombatEffects.Particle(kind, i, 1f).Opacity);
            Assert.True(CombatEffects.Particle(kind, i, 0f).Opacity > 0f);
            var (x, y, z, opacity, scale) = CombatEffects.Particle(kind, i, float.NaN);
            Assert.All(new[] { x, y, z, opacity, scale }, v => Assert.False(float.IsNaN(v)));
        }
        Assert.True(CombatEffects.Lifetime(kind) > 0f);
    }

    [Fact]
    public void SparksFanOutInDifferentDirections()
    {
        var ends = Enumerable.Range(0, CombatEffects.ParticleCount(EffectKind.Spark)).Select(i => CombatEffects.Particle(EffectKind.Spark, i, 0.6f)).Select(it => (it.X, it.Z)).Distinct();
        Assert.Equal(CombatEffects.ParticleCount(EffectKind.Spark), ends.Count());
    }
}
