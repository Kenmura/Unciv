using Godot;

namespace Unciv.GodotHarness;

/// <summary>Synthetic input for headless interaction tests: events go through the viewport exactly like device input would.</summary>
public static class InputSimulator
{
    public static void SimulateMouseClick(Viewport viewport, Vector2 position, MouseButton button = MouseButton.Left, bool shift = false)
    {
        SimulateMouseMove(viewport, position);
        foreach (bool pressed in new[] { true, false })
        {
            var click = new InputEventMouseButton
            {
                Position = position,
                GlobalPosition = position,
                ButtonIndex = button,
                ShiftPressed = shift,
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

    public static void SimulateWheel(Viewport viewport, Vector2 position, bool up)
    {
        SimulateMouseMove(viewport, position);
        foreach (bool pressed in new[] { true, false })
        {
            viewport.PushInput(new InputEventMouseButton
            {
                Position = position,
                GlobalPosition = position,
                ButtonIndex = up ? MouseButton.WheelUp : MouseButton.WheelDown,
                Pressed = pressed,
            });
        }
    }

    /// <summary>Press at <paramref name="from"/>, move in steps to <paramref name="to"/> with the button held, release.</summary>
    public static void SimulateDrag(Viewport viewport, Vector2 from, Vector2 to, int steps = 4, MouseButton button = MouseButton.Left)
    {
        SimulateMouseMove(viewport, from);
        viewport.PushInput(new InputEventMouseButton { Position = from, GlobalPosition = from, ButtonIndex = button, Pressed = true, ButtonMask = ToMask(button) });
        var previous = from;
        for (int i = 1; i <= steps; i++)
        {
            var position = from.Lerp(to, i / (float)steps);
            viewport.PushInput(new InputEventMouseMotion { Position = position, GlobalPosition = position, Relative = position - previous, ButtonMask = ToMask(button) });
            previous = position;
        }
        viewport.PushInput(new InputEventMouseButton { Position = to, GlobalPosition = to, ButtonIndex = button, Pressed = false });
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
