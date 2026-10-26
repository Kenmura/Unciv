using Godot;
using Unciv.Commands;
using Unciv.Assets;
using Unciv.GodotClient.Assets;
using Unciv.Models;

namespace Unciv.GodotHarness;

/// <summary>
/// C030: the placeholder audio through the real registry and player. Every <see cref="UncivSound"/> preset except Silent resolves to a non-empty 16-bit mono
/// <c>AudioStreamWav</c> whose data equals the Godot-free generator's PCM, the same key gives the same cached stream, Silent has no stream and plays nothing, a custom
/// (mod) name still gets a stream, and flooding the player past its voices neither throws nor loses the sound player's pool. Nothing is drawn, so there is no capture.
/// </summary>
public sealed class AudioScenario : Scenario
{
    public override string Name => "audio";

    public override bool Solo => true;

    public override IReadOnlyList<string> Checklist => [];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var registry = AssetRegistry.ForRuleset(session.Game.Ruleset);
        var player = new SoundPlayer(registry);
        host.AddChild(player);
        var presets = typeof(UncivSound).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).Select(it => it.GetValue(null)).OfType<UncivSound>().Where(it => it != UncivSound.Silent).ToList();

        bool AllStreamsAudible() => presets.All(sound =>
            registry.Sound(sound) is AudioStreamWav { Data.Length: > 0 } wav && wav.MixRate == ProceduralSounds.SampleRate && !wav.Stereo
            && wav.Format == AudioStreamWav.FormatEnum.Format16Bits && wav.Data.AsEnumerable().SequenceEqual(ProceduralSounds.Pcm(sound.FileName)));

        return
        [
            new WaitFrames(2),
            new AssertQuery("all 18 sound presets resolve to audible 16-bit streams", () => presets.Count == 18 && AllStreamsAudible()),
            new AssertQuery("the stream is cached per key", () => ReferenceEquals(registry.Sound(UncivSound.Click), registry.Sound(UncivSound.Click))),
            new AssertQuery("Silent has no stream and plays nothing", () => registry.Sound(UncivSound.Silent) == null && !player.Play(UncivSound.Silent)),
            new AssertQuery("a mod sound name still gets a stream", () => registry.Sound(new UncivSound("my mod ping")) is AudioStreamWav { Data.Length: > 0 }),
            new AssertQuery("sounds play, also past the voice pool", () => Enumerable.Range(0, SoundPlayer.Voices * 2).All(_ => player.Play(UncivSound.Chimes)) && player.HasSound("click") && !player.HasSound("") && player.PlayingVoices <= SoundPlayer.Voices),
        ];
    }
}
