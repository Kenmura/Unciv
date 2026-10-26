using Godot;
using Unciv.Assets;
using Unciv.Models;

namespace Unciv.GodotClient.Assets;

/// <summary>
/// C030: plays <see cref="UncivSound"/>s through a small pool of players on the master bus (whose level the options screen sets). The streams come from
/// <see cref="AssetRegistry.Sound"/>, so a dropped-in <c>assets/override/sound/&lt;name&gt;.ogg</c> replaces the generated tones. C057: the game screen plays the engine's
/// <c>SoundRequested</c> cues through it (<see cref="PlaySound"/>); every non-empty sound name has a stream, music is not played yet.
/// </summary>
public sealed partial class SoundPlayer : Node
{
    public const int Voices = 8;

    private readonly AssetRegistry registry;
    private readonly List<AudioStreamPlayer> voices = [];
    private int cursor;

    public SoundPlayer(AssetRegistry registry)
    {
        this.registry = registry;
        Name = "SoundPlayer";
        for (var i = 0; i < Voices; i++)
        {
            var voice = new AudioStreamPlayer { Name = "Voice" + i };
            voices.Add(voice);
            AddChild(voice);
        }
    }

    /// <summary>Starts the sound; false when it is silent. A busy pool takes over its oldest voice.</summary>
    public bool Play(UncivSound sound)
    {
        if (registry.Sound(sound) is not { } stream) return false;
        var index = ProceduralSounds.PickVoice(voices.Select(it => it.Playing).ToList(), cursor);
        cursor = (index + 1) % Voices;
        voices[index].Stream = stream;
        voices[index].Play();
        return true;
    }

    public bool HasSound(string soundName) => !string.IsNullOrEmpty(soundName);

    /// <summary>Voices currently playing (for the harness).</summary>
    public int PlayingVoices => voices.Count(it => it.Playing);

    public void PlaySound(string soundName) => Play(new UncivSound(soundName));
}
