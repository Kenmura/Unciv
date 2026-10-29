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

    private static int emulatedFinger = -1;

    /// <summary>
    /// C096: a finger lands. Like Godot with <c>emulate_mouse_from_touch</c> (the project default), the first finger down also produces mouse events (device
    /// <see cref="InputEvent.DeviceIdEmulation"/>): a left press at that position; later fingers produce only the touch event.
    /// </summary>
    public static void SimulateTouchDown(Viewport viewport, int finger, Vector2 position)
    {
        viewport.PushInput(new InputEventScreenTouch { Index = finger, Position = position, Pressed = true });
        if (emulatedFinger != -1) return;
        emulatedFinger = finger;
        viewport.PushInput(new InputEventMouseMotion { Position = position, GlobalPosition = position, Device = (int)InputEvent.DeviceIdEmulation });
        viewport.PushInput(new InputEventMouseButton { Position = position, GlobalPosition = position, ButtonIndex = MouseButton.Left, Pressed = true, ButtonMask = MouseButtonMask.Left, Device = (int)InputEvent.DeviceIdEmulation });
    }

    /// <summary>C096: a finger moves by <paramref name="to"/> minus <paramref name="from"/> (the emulated finger also drags the emulated mouse).</summary>
    public static void SimulateTouchMove(Viewport viewport, int finger, Vector2 from, Vector2 to)
    {
        viewport.PushInput(new InputEventScreenDrag { Index = finger, Position = to, Relative = to - from });
        if (finger != emulatedFinger) return;
        viewport.PushInput(new InputEventMouseMotion { Position = to, GlobalPosition = to, Relative = to - from, ButtonMask = MouseButtonMask.Left, Device = (int)InputEvent.DeviceIdEmulation });
    }

    /// <summary>C096: a finger lifts at <paramref name="position"/>.</summary>
    public static void SimulateTouchUp(Viewport viewport, int finger, Vector2 position)
    {
        viewport.PushInput(new InputEventScreenTouch { Index = finger, Position = position, Pressed = false });
        if (finger != emulatedFinger) return;
        emulatedFinger = -1;
        viewport.PushInput(new InputEventMouseButton { Position = position, GlobalPosition = position, ButtonIndex = MouseButton.Left, Pressed = false, Device = (int)InputEvent.DeviceIdEmulation });
    }

    /// <summary>C096: touch and lift on one spot.</summary>
    public static void SimulateTap(Viewport viewport, Vector2 position)
    {
        SimulateTouchDown(viewport, 0, position);
        SimulateTouchUp(viewport, 0, position);
    }

    /// <summary>C096: a two-finger gesture from <paramref name="a0"/>/<paramref name="b0"/> to <paramref name="a1"/>/<paramref name="b1"/> in <paramref name="steps"/> moves, then both lift.</summary>
    public static void SimulatePinch(Viewport viewport, Vector2 a0, Vector2 b0, Vector2 a1, Vector2 b1, int steps = 4)
    {
        SimulateTouchDown(viewport, 0, a0);
        SimulateTouchDown(viewport, 1, b0);
        Vector2 pa = a0, pb = b0;
        for (int i = 1; i <= steps; i++)
        {
            var na = a0.Lerp(a1, i / (float)steps);
            var nb = b0.Lerp(b1, i / (float)steps);
            SimulateTouchMove(viewport, 0, pa, na);
            SimulateTouchMove(viewport, 1, pb, nb);
            pa = na;
            pb = nb;
        }
        SimulateTouchUp(viewport, 1, pb);
        SimulateTouchUp(viewport, 0, pa);
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
