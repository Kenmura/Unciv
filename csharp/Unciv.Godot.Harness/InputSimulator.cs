using Godot;

namespace Unciv.GodotHarness;

/// <summary>Synthetic input for headless interaction tests: events go through the viewport exactly like device input would.</summary>
public static class InputSimulator
{
    public static void SimulateMouseClick(Viewport viewport, Vector2 position, MouseButton button = MouseButton.Left)
    {
        SimulateMouseMove(viewport, position);
        foreach (bool pressed in new[] { true, false })
        {
            var click = new InputEventMouseButton
            {
                Position = position,
                GlobalPosition = position,
                ButtonIndex = button,
                Pressed = pressed,
                ButtonMask = pressed ? ToMask(button) : 0,
            };
            viewport.PushInput(click);
        }
    }

    public static void SimulateMouseMove(Viewport viewport, Vector2 position)
    {
        viewport.PushInput(new InputEventMouseMotion { Position = position, GlobalPosition = position });
    }

    public static void SimulateKey(Viewport viewport, Key key, bool shift = false, bool ctrl = false, bool alt = false)
    {
        foreach (bool pressed in new[] { true, false })
        {
            viewport.PushInput(new InputEventKey
            {
                Keycode = key,
                PhysicalKeycode = key,
                Pressed = pressed,
                ShiftPressed = shift,
                CtrlPressed = ctrl,
                AltPressed = alt,
            });
        }
    }

    private static MouseButtonMask ToMask(MouseButton button) => button switch
    {
        MouseButton.Left => MouseButtonMask.Left,
        MouseButton.Right => MouseButtonMask.Right,
        MouseButton.Middle => MouseButtonMask.Middle,
        _ => 0,
    };
}
