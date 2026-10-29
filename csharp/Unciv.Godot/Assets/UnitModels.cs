using Godot;
using Unciv.Assets;

namespace Unciv.GodotClient.Assets;

/// <summary>C080: what the client does with a unit model file beyond tinting it: finds its <see cref="AnimationPlayer"/>, lists the contract clips it has and holds its idle pose.</summary>
public static class UnitModels
{
    /// <summary>The model's animation player (an imported glTF has one at the root), null for a generated block or a model without animations.</summary>
    public static AnimationPlayer? FindPlayer(Node model) =>
        model as AnimationPlayer ?? model.FindChildren("*", nameof(AnimationPlayer), true, false).OfType<AnimationPlayer>().FirstOrDefault();

    /// <summary>The contract clips (<see cref="UnitModelContract.ClipNames"/>) the player has, in contract order; clips with other names are ignored.</summary>
    public static IReadOnlyList<string> Clips(AnimationPlayer player) => [.. UnitModelContract.ClipNames.Where(it => player.HasAnimation(it))];

    /// <summary>
    /// Stands the figure in its <c>idle</c> pose, <paramref name="phase"/> (0..1) of the way through the clip, and holds it there: the presenter's clock drives clip time (a later ticket), so a figure never
    /// moves with the frame clock and goldens stay exact. Returns false when the model has no idle clip.
    /// </summary>
    public static bool HoldIdle(AnimationPlayer player, float phase)
    {
        if (!player.HasAnimation(UnitModelContract.IdleClip)) return false;
        var idle = player.GetAnimation(UnitModelContract.IdleClip);
        idle.LoopMode = Animation.LoopModeEnum.Linear;
        player.Play(UnitModelContract.IdleClip);
        player.Seek(idle.Length * Math.Clamp(phase, 0f, 1f), true);
        player.Pause();
        return true;
    }

    /// <summary>Where in its idle clip a figure stands: a fixed spread over (unit id, figure index), so a squad does not pose in lockstep.</summary>
    public static float IdlePhase(int unitId, int figure) => (StableHash.Mix(0x1D1Eu, unitId, figure) % 1000u) / 1000f;
}
