using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C081: drives one figure's clips from the presenter's clock (UNIT-MODELS.md section 2). Never plays: every call stands the figure at an explicit time of a clip and keeps the player paused, so a
/// figure never moves with the frame clock and goldens, manual stepping and the backlog speed-up all work. A clip the model lacks is reported (false) so the presenter falls back to its procedural pose.
/// </summary>
public sealed class UnitAnimator(AnimationPlayer player, float idlePhase)
{
    /// <summary>The clip the figure is standing in, null when it has none assigned.</summary>
    public string? Clip => string.IsNullOrEmpty(player.AssignedAnimation) ? null : player.AssignedAnimation.ToString();

    /// <summary>Seconds into <see cref="Clip"/> the figure stands.</summary>
    public float Position => (float)player.CurrentAnimationPosition;

    public bool Has(string clip) => player.HasAnimation(clip);

    /// <summary>Stands in <paramref name="clip"/> a fraction <paramref name="progress"/> of the way through (the clip is stretched over the cue). False when the model has no such clip.</summary>
    public bool Show(string clip, float progress)
    {
        if (!Has(clip)) return false;
        var length = (float)player.GetAnimation(clip).Length;
        return Seek(clip, CuePoses.ClipTime(Math.Clamp(float.IsNaN(progress) ? 0f : progress, 0f, 1f) * length, length, false));
    }

    /// <summary>Stands in the looping <paramref name="clip"/> <paramref name="seconds"/> into it (a walk cycle keeps its own pace whatever the cue's length). False when the model has no such clip.</summary>
    public bool ShowLooped(string clip, float seconds)
    {
        if (!Has(clip)) return false;
        var animation = player.GetAnimation(clip);
        animation.LoopMode = Animation.LoopModeEnum.Linear;
        return Seek(clip, CuePoses.ClipTime(seconds, (float)animation.Length, true));
    }

    /// <summary>Back to the idle pose it was built in.</summary>
    public void Rest() => UnitModels.HoldIdle(player, idlePhase);

    private bool Seek(string clip, float time)
    {
        player.Play(clip);
        player.Seek(time, true);
        player.Pause();
        return true;
    }
}
