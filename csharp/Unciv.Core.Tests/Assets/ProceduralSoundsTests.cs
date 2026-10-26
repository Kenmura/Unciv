using System.Reflection;
using Unciv.Assets;
using Unciv.Models;

namespace Unciv.Core.Tests.Assets;

public class ProceduralSoundsTests
{
    private static IEnumerable<UncivSound> AllPresets() =>
        typeof(UncivSound).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(it => it.PropertyType == typeof(UncivSound)).Select(it => (UncivSound)it.GetValue(null)!);

    [Fact]
    public void EveryUncivSoundPresetIsInTheTableExceptSilent()
    {
        var presets = AllPresets().ToList();
        Assert.Equal(19, presets.Count);
        foreach (var sound in presets.Where(it => it != UncivSound.Silent))
            Assert.Contains(sound.FileName, ProceduralSounds.PresetNames);
        Assert.Equal(presets.Count - 1, ProceduralSounds.PresetNames.Count);
    }

    [Fact]
    public void EveryPresetResolvesToANonEmptyAudibleStream()
    {
        foreach (var sound in AllPresets().Where(it => it != UncivSound.Silent))
        {
            var key = ProceduralSounds.KeyFor(sound);
            Assert.NotNull(key);
            Assert.Equal(AssetKind.Sound, AssetKinds.Of(key.Value));
            var samples = ProceduralSounds.Samples(key.Value.Name);
            Assert.True(samples.Length >= ProceduralSounds.SampleRate / 50, sound.FileName);
            Assert.Contains(samples, s => System.Math.Abs(s) > 2000);
            Assert.All(samples, s => Assert.InRange((int)s, -(int)(0.8 * short.MaxValue) - 1, (int)(0.8 * short.MaxValue) + 1));
        }
    }

    [Fact]
    public void SilentHasNoKey()
    {
        Assert.Null(ProceduralSounds.KeyFor(UncivSound.Silent));
        Assert.Equal("sound/click", ProceduralSounds.KeyFor(UncivSound.Click)!.Value.ToString());
        Assert.Equal("sound/a_b", ProceduralSounds.KeyFor(new UncivSound("a/b"))!.Value.ToString());
    }

    [Fact]
    public void PresetsSoundDistinct()
    {
        var all = AllPresets().Where(it => it != UncivSound.Silent).Select(it => Convert.ToBase64String(ProceduralSounds.Wav(it.FileName))).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    [Fact]
    public void HostileModNamesStayBoundedAndDeterministic()
    {
        var longest = new string('x', 100_000);
        foreach (var name in new[] { "", " ", "../../etc/passwd", "a\0b", "\ud800", longest, "\u00e9\u4e2d" })
        {
            var samples = ProceduralSounds.Samples(name);
            Assert.InRange(samples.Length, 1, ProceduralSounds.SampleRate);
            Assert.Equal(samples, ProceduralSounds.Samples(name));
            Assert.All(samples, s => Assert.InRange((int)s, -(int)(0.8 * short.MaxValue) - 1, (int)(0.8 * short.MaxValue) + 1));
        }
    }

    [Fact]
    public void OutputIsDeterministic()
    {
        foreach (var name in new[] { "whoosh", "bombard", "mod sound" })
            Assert.Equal(ProceduralSounds.Wav(name), ProceduralSounds.Wav(name));
    }

    [Fact]
    public void LengthIsLastNoteEnd()
    {
        Assert.Equal(0, ProceduralSounds.LengthMs([]));
        Assert.Equal(40, ProceduralSounds.LengthMs(ProceduralSounds.Notes("click")));
        Assert.Equal(940, ProceduralSounds.LengthMs(ProceduralSounds.Notes("chimes")));
        Assert.Equal(40 * ProceduralSounds.SampleRate / 1000, ProceduralSounds.Samples("click").Length);
        Assert.Equal(940 * ProceduralSounds.SampleRate / 1000, ProceduralSounds.Samples("chimes").Length);
    }

    [Fact]
    public void UnknownNamesGetTwoHashDerivedTones()
    {
        var notes = ProceduralSounds.Notes("Mod Ping");
        var low = 300f + StableHash.Of("Mod Ping") % 700;
        Assert.Equal(2, notes.Count);
        Assert.All(notes, n => Assert.Equal(SoundWave.Sine, n.Wave));
        Assert.Equal(low, notes[0].Frequency);
        Assert.Equal(low * 1.5f, notes[1].Frequency);
        Assert.Equal(0, notes[0].StartMs);
        Assert.Equal(120, notes[0].DurationMs);
        Assert.Equal(100, notes[1].StartMs);
        Assert.Equal(200, notes[1].DurationMs);
        Assert.NotEqual(ProceduralSounds.Notes("Other").Select(n => n.Frequency), notes.Select(n => n.Frequency));
    }

    [Fact]
    public void SineToneHasItsFrequencyAndFadesOut()
    {
        // "setup" is one 440 Hz sine of 200 ms: ~88 cycles, so ~176 zero crossings.
        var samples = ProceduralSounds.Samples("setup");
        var crossings = 0;
        for (var i = 1; i < samples.Length; i++)
        {
            if (samples[i - 1] < 0 != samples[i] < 0) crossings++;
        }
        Assert.InRange(crossings, 170, 180);
        Assert.Equal(0, samples[0]);
        Assert.True(System.Math.Abs((int)samples[^1]) < 50);
        var quarter = samples.Length / 4;
        Assert.True(samples.Take(quarter).Max(s => System.Math.Abs((int)s)) > samples.Skip(3 * quarter).Max(s => System.Math.Abs((int)s)) * 3);
    }

    [Fact]
    public void SquareToneIsFlatTopped()
    {
        var samples = ProceduralSounds.Samples("click");
        // 1200 Hz is 18.4 samples a period: the first half wave (samples 1..8) is positive and rises with the 5 ms attack, the second half is negative.
        for (var i = 1; i <= 8; i++) Assert.True(samples[i] > samples[i - 1], i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.All(samples.Skip(10).Take(8), s => Assert.True(s < 0));
    }

    [Fact]
    public void SwellNoiseStartsQuietAndPeaksInTheMiddle()
    {
        var samples = ProceduralSounds.Samples("whoosh");
        int Loudness(int from, int length) => samples.Skip(from).Take(length).Sum(s => System.Math.Abs((int)s)) / length;
        var third = samples.Length / 3;
        Assert.True(Loudness(0, 100) * 4 < Loudness(third / 2, 400));
        Assert.True(Loudness(third / 2, 400) > Loudness(2 * third + third / 2, 400));
        // Fast-attack noise (paper) is loud right away.
        var paper = ProceduralSounds.Samples("paper");
        Assert.True(paper.Skip(200).Take(100).Sum(s => System.Math.Abs((int)s)) / 100 > 3000);
    }

    [Fact]
    public void EnvelopeIsAttackThenSquaredFade()
    {
        // click: one 40 ms square at gain 0.4, 5 ms (110 samples) linear attack, then (1 - i/count)^2 fade.
        var click = ProceduralSounds.Samples("click");
        var count = 40 * ProceduralSounds.SampleRate / 1000;
        foreach (var i in new[] { 4, 8, 100 })
        {
            var sign = System.Math.Sin(2 * System.Math.PI * 1200 * i / ProceduralSounds.SampleRate) >= 0 ? 1f : -1f;
            var fade = 1f - (float)i / count;
            var expected = sign * System.Math.Min(1f, i / 110f) * fade * fade * 0.4f * short.MaxValue;
            Assert.InRange(click[i], expected - 2, expected + 2);
        }
        // whoosh: seeded noise, 350 ms, gain 0.4, fade in over the first half (3858 samples).
        var whoosh = ProceduralSounds.Samples("whoosh");
        var length = 350 * ProceduralSounds.SampleRate / 1000;
        var seed = StableHash.Of("whoosh");
        foreach (var i in new[] { 100, 2000, 5000 })
        {
            var noise = StableHash.Mix(seed, i, 0) / (float)uint.MaxValue * 2f - 1f;
            var fade = 1f - (float)i / length;
            var expected = noise * System.Math.Min(1f, i / 3858f) * fade * fade * 0.4f * short.MaxValue;
            Assert.InRange(whoosh[i], expected - 2, expected + 2);
        }
    }

    [Fact]
    public void NoiseIsNotConstantAndIsSeededByName()
    {
        Assert.NotEqual(ProceduralSounds.Samples("fire"), ProceduralSounds.Samples("paper").Take(ProceduralSounds.Samples("fire").Length));
        Assert.True(ProceduralSounds.Samples("fire").Distinct().Count() > 1000);
    }

    [Fact]
    public void WavHasAValidRiffHeader()
    {
        var wav = ProceduralSounds.Wav("click");
        var pcm = ProceduralSounds.Pcm("click");
        Assert.Equal(ProceduralSounds.WavHeaderBytes + pcm.Length, wav.Length);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal(36 + pcm.Length, BitConverter.ToInt32(wav, 4));
        Assert.Equal("WAVEfmt ", System.Text.Encoding.ASCII.GetString(wav, 8, 8));
        Assert.Equal(16, BitConverter.ToInt32(wav, 16));
        Assert.Equal(1, BitConverter.ToInt16(wav, 20));
        Assert.Equal(1, BitConverter.ToInt16(wav, 22));
        Assert.Equal(ProceduralSounds.SampleRate, BitConverter.ToInt32(wav, 24));
        Assert.Equal(ProceduralSounds.SampleRate * 2, BitConverter.ToInt32(wav, 28));
        Assert.Equal(2, BitConverter.ToInt16(wav, 32));
        Assert.Equal(16, BitConverter.ToInt16(wav, 34));
        Assert.Equal("data", System.Text.Encoding.ASCII.GetString(wav, 36, 4));
        Assert.Equal(pcm.Length, BitConverter.ToInt32(wav, 40));
        Assert.Equal(pcm, wav.Skip(ProceduralSounds.WavHeaderBytes).ToArray());
    }

    [Fact]
    public void PcmIsLittleEndianSamples()
    {
        var samples = ProceduralSounds.Samples("coin");
        var pcm = ProceduralSounds.Pcm("coin");
        Assert.Equal(samples.Length * 2, pcm.Length);
        for (var i = 0; i < samples.Length; i += 37) Assert.Equal(samples[i], BitConverter.ToInt16(pcm, i * 2));
        Assert.Contains(samples, s => s < 0);
    }

    [Fact]
    public void PickVoiceTakesTheFirstIdleThenTheCursor()
    {
        Assert.Equal(-1, ProceduralSounds.PickVoice([], 0));
        Assert.Equal(0, ProceduralSounds.PickVoice([false, true], 1));
        Assert.Equal(1, ProceduralSounds.PickVoice([true, false, false], 2));
        Assert.Equal(2, ProceduralSounds.PickVoice([true, true, true], 2));
        Assert.Equal(1, ProceduralSounds.PickVoice([true, true, true], 4));
        Assert.Equal(2, ProceduralSounds.PickVoice([true, true, true], -1));
    }

    [Fact]
    public void SoundKeysAreOfSoundKind()
    {
        Assert.Equal(AssetKind.Sound, AssetKinds.Of(new AssetKey("sound", "x")));
        Assert.Equal(AssetKind.Icon, AssetKinds.Of(new AssetKey("soundx", "x")));
    }
}
