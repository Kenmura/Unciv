using Unciv.Models;

namespace Unciv.Assets;

/// <summary>The wave a <see cref="SoundNote"/> is made of.</summary>
public enum SoundWave
{
    Sine,
    Square,
    Noise,
}

/// <summary>One tone or noise burst of a placeholder sound.</summary>
/// <param name="Wave">Sine, square or seeded noise.</param>
/// <param name="Frequency">Hertz (ignored for noise).</param>
/// <param name="StartMs">Milliseconds from the start of the sound.</param>
/// <param name="DurationMs">Length in milliseconds.</param>
/// <param name="Gain">Amplitude, 0..1.</param>
/// <param name="Swell">Fade in over the first half instead of the 5 ms click guard (for whooshes).</param>
public readonly record struct SoundNote(SoundWave Wave, float Frequency, int StartMs, int DurationMs, float Gain, bool Swell = false);

/// <summary>
/// C030: the placeholder audio. Every <see cref="UncivSound"/> becomes a few generated tones (or a noise burst) from a table keyed by the sound's file name; a sound
/// without a table entry (mod or custom name) gets two tones derived from its stable hash. Pure functions of the name: 16-bit mono PCM, no sound device, the same
/// samples on every run. Real recordings replace these as <c>assets/override/sound/&lt;name&gt;.ogg</c>.
/// </summary>
public static class ProceduralSounds
{
    public const string Category = "sound";
    public const int SampleRate = 22050;
    public const int WavHeaderBytes = 44;
    private const float PeakGain = 0.8f;
    private const int AttackMs = 5;

    private static readonly Dictionary<string, SoundNote[]> Presets = new(StringComparer.Ordinal)
    {
        ["bombard"] = [new(SoundWave.Noise, 0, 0, 300, 0.7f), new(SoundWave.Sine, 70, 0, 300, 0.7f)],
        ["chimes"] = [new(SoundWave.Sine, 880, 0, 600, 0.35f), new(SoundWave.Sine, 1320, 120, 600, 0.3f), new(SoundWave.Sine, 1760, 240, 700, 0.25f)],
        ["choir"] = [new(SoundWave.Sine, 262, 0, 900, 0.3f), new(SoundWave.Sine, 330, 0, 900, 0.3f), new(SoundWave.Sine, 392, 0, 900, 0.3f)],
        ["click"] = [new(SoundWave.Square, 1200, 0, 40, 0.4f)],
        ["coin"] = [new(SoundWave.Square, 988, 0, 80, 0.3f), new(SoundWave.Square, 1319, 70, 250, 0.3f)],
        ["construction"] = [new(SoundWave.Noise, 0, 0, 60, 0.6f), new(SoundWave.Noise, 0, 150, 60, 0.6f), new(SoundWave.Noise, 0, 300, 60, 0.6f), new(SoundWave.Sine, 180, 0, 360, 0.3f)],
        ["fire"] = [new(SoundWave.Noise, 0, 0, 400, 0.5f)],
        ["fortify"] = [new(SoundWave.Sine, 220, 0, 120, 0.5f), new(SoundWave.Sine, 330, 100, 200, 0.5f)],
        ["notification1"] = [new(SoundWave.Sine, 660, 0, 150, 0.5f), new(SoundWave.Sine, 880, 150, 250, 0.5f)],
        ["notification2"] = [new(SoundWave.Sine, 880, 0, 120, 0.5f), new(SoundWave.Sine, 660, 120, 250, 0.5f)],
        ["paper"] = [new(SoundWave.Noise, 0, 0, 150, 0.4f)],
        ["policy"] = [new(SoundWave.Sine, 392, 0, 150, 0.45f), new(SoundWave.Sine, 523, 150, 150, 0.45f), new(SoundWave.Sine, 659, 300, 300, 0.45f)],
        ["promote"] = [new(SoundWave.Sine, 523, 0, 100, 0.4f), new(SoundWave.Sine, 659, 100, 100, 0.4f), new(SoundWave.Sine, 784, 200, 100, 0.4f), new(SoundWave.Sine, 1047, 300, 300, 0.4f)],
        ["setup"] = [new(SoundWave.Sine, 440, 0, 200, 0.5f)],
        ["slider"] = [new(SoundWave.Sine, 1000, 0, 30, 0.4f)],
        ["swap"] = [new(SoundWave.Sine, 500, 0, 60, 0.5f), new(SoundWave.Sine, 700, 60, 60, 0.5f)],
        ["upgrade"] = [new(SoundWave.Sine, 330, 0, 100, 0.4f), new(SoundWave.Sine, 440, 100, 100, 0.4f), new(SoundWave.Sine, 554, 200, 100, 0.4f), new(SoundWave.Sine, 659, 300, 250, 0.4f)],
        ["whoosh"] = [new(SoundWave.Noise, 0, 0, 350, 0.4f, Swell: true)],
    };

    /// <summary>The file names of the sounds with a hand-made table entry (all <see cref="UncivSound"/> presets except Silent).</summary>
    public static IReadOnlyCollection<string> PresetNames => Presets.Keys;

    /// <summary>The registry key <c>sound/&lt;name&gt;</c> of a sound; null for <see cref="UncivSound.Silent"/> (nothing to play).</summary>
    public static AssetKey? KeyFor(UncivSound sound) => string.IsNullOrEmpty(sound.FileName) ? null : AssetKey.Of(Category, sound.FileName);

    /// <summary>The notes of a sound name: the table entry, or two hash-derived sine tones (300..999 Hz, then a fifth above) for a name that has none.</summary>
    public static IReadOnlyList<SoundNote> Notes(string name)
    {
        if (Presets.TryGetValue(name, out var notes)) return notes;
        var hash = StableHash.Of(name);
        var low = 300f + hash % 700;
        return [new(SoundWave.Sine, low, 0, 120, 0.5f), new(SoundWave.Sine, low * 1.5f, 100, 200, 0.5f)];
    }

    public static int LengthMs(IReadOnlyList<SoundNote> notes) => notes.Count == 0 ? 0 : notes.Max(it => it.StartMs + it.DurationMs);

    /// <summary>The mixed samples of a sound name, 16-bit signed at <see cref="SampleRate"/>; never louder than 0.8 of full scale.</summary>
    public static short[] Samples(string name)
    {
        var notes = Notes(name);
        var mix = new float[(int)((long)LengthMs(notes) * SampleRate / 1000)];
        var seed = StableHash.Of(name);
        foreach (var note in notes) AddNote(mix, note, seed);
        var samples = new short[mix.Length];
        for (var i = 0; i < mix.Length; i++) samples[i] = (short)(Math.Clamp(mix[i], -PeakGain, PeakGain) * short.MaxValue);
        return samples;
    }

    private static void AddNote(float[] mix, SoundNote note, uint seed)
    {
        var start = (int)((long)note.StartMs * SampleRate / 1000);
        var count = (int)((long)note.DurationMs * SampleRate / 1000);
        var attack = Math.Max(1, (int)((long)(note.Swell ? note.DurationMs / 2 : AttackMs) * SampleRate / 1000));
        for (var i = 0; i < count; i++)
        {
            var fade = 1f - (float)i / count;
            var envelope = Math.Min(1f, (float)i / attack) * fade * fade;
            var phase = 2.0 * Math.PI * note.Frequency * i / SampleRate;
            var wave = note.Wave switch
            {
                SoundWave.Sine => (float)Math.Sin(phase),
                SoundWave.Square => Math.Sin(phase) >= 0 ? 1f : -1f,
                _ => StableHash.Mix(seed, start + i, 0) / (float)uint.MaxValue * 2f - 1f,
            };
            mix[start + i] += wave * envelope * note.Gain;
        }
    }

    /// <summary>The raw little-endian PCM bytes of <see cref="Samples"/> (what Godot's <c>AudioStreamWav.Data</c> takes).</summary>
    public static byte[] Pcm(string name)
    {
        var samples = Samples(name);
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            bytes[i * 2] = (byte)(samples[i] & 0xFF);
            bytes[i * 2 + 1] = (byte)((samples[i] >> 8) & 0xFF);
        }
        return bytes;
    }

    /// <summary>A complete RIFF/WAVE file (44 byte header, mono 16-bit PCM) of the sound.</summary>
    public static byte[] Wav(string name)
    {
        var pcm = Pcm(name);
        var wav = new byte[WavHeaderBytes + pcm.Length];
        WriteAscii(wav, 0, "RIFF");
        WriteInt(wav, 4, 36 + pcm.Length);
        WriteAscii(wav, 8, "WAVEfmt ");
        WriteInt(wav, 16, 16);
        wav[20] = 1; // PCM
        wav[22] = 1; // mono
        WriteInt(wav, 24, SampleRate);
        WriteInt(wav, 28, SampleRate * 2);
        wav[32] = 2; // block align
        wav[34] = 16; // bits per sample
        WriteAscii(wav, 36, "data");
        WriteInt(wav, 40, pcm.Length);
        pcm.CopyTo(wav, WavHeaderBytes);
        return wav;
    }

    private static void WriteAscii(byte[] target, int offset, string text)
    {
        for (var i = 0; i < text.Length; i++) target[offset + i] = (byte)text[i];
    }

    private static void WriteInt(byte[] target, int offset, int value)
    {
        for (var i = 0; i < 4; i++) target[offset + i] = (byte)((value >> (8 * i)) & 0xFF);
    }

    /// <summary>Which of the voices to use for a new sound: the first idle one, else the one at <paramref name="cursor"/> (taken over, oldest first when the caller advances the cursor). -1 when there are no voices.</summary>
    public static int PickVoice(IReadOnlyList<bool> busy, int cursor)
    {
        if (busy.Count == 0) return -1;
        for (var i = 0; i < busy.Count; i++)
        {
            if (!busy[i]) return i;
        }
        return ((cursor % busy.Count) + busy.Count) % busy.Count;
    }
}
