using Godot;
using Unciv.Assets;

namespace Unciv.GodotClient.Ui;

/// <summary>
/// C100: the touch stand-in for a right click on a control: on a touch build a finger (the emulated left button) held on its target control for
/// <see cref="UiTokens.LongPressSeconds"/> without moving more than the drag threshold raises its action. <see cref="TakeFired"/> then tells the target's own
/// press handler (the release still presses a button) that this touch was a long press, once. Does nothing on a mouse build, where the right button exists.
/// </summary>
public sealed class LongPress
{
    private readonly Control target;
    private readonly Action action;
    private int touch;
    private bool held;
    private bool fired;
    private Vector2 start;

    public LongPress(Control target, Action action)
    {
        this.target = target;
        this.action = action;
        target.GuiInput += OnInput;
    }

    /// <summary>True once after a long press fired (and clears), so the release that follows it is not also treated as a tap.</summary>
    public bool TakeFired()
    {
        bool was = fired;
        fired = false;
        return was;
    }

    private void OnInput(InputEvent input)
    {
        if (!DisplayProfile.Current.Touch) return;
        switch (input)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button when button.Device == InputEvent.DeviceIdEmulation:
                touch++;
                held = button.Pressed;
                if (!button.Pressed) break;
                fired = false;
                start = button.Position;
                int mine = touch;
                if (!GodotObject.IsInstanceValid(target) || !target.IsInsideTree()) break;
                target.GetTree().CreateTimer(UiTokens.LongPressSeconds).Timeout += () => Fire(mine);
                break;
            case InputEventMouseMotion motion when held && motion.Device == InputEvent.DeviceIdEmulation:
                var profile = DisplayProfile.Current;
                float slop = profile.UsesDensity ? UiTokens.DpToPx(UiTokens.TouchDragThresholdDp, profile.Dpi) : 12f;
                if (start.DistanceTo(motion.Position) > slop) held = false;
                break;
        }
    }

    private void Fire(int mine)
    {
        if (!held || touch != mine || !GodotObject.IsInstanceValid(target)) return;
        held = false;
        fired = true;
        action();
    }
}
