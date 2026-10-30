using Godot;
using Unciv.GodotClient.Settings;

namespace Unciv.GodotClient.Ui;

/// <summary>
/// The root of the client UI: a stack of full-screen screens (the top one visible), the modal <see cref="Popups"/> above them and the
/// <see cref="Tooltips"/> above everything. Escape closes the open popup if there is one, else pops the top screen; the bottom screen never pops.
/// </summary>
public partial class ScreenManager : Control
{
    private readonly List<Control> screens = [];
    private readonly Control screenLayer = new();

    public ScreenManager(UiTheme theme)
    {
        UiTheme = theme;
        Theme = theme.Theme;
        this.Fill();
        MouseFilter = MouseFilterEnum.Ignore;
        screenLayer.Fill();
        screenLayer.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(screenLayer);
        Popups = new PopupStack(theme);
        AddChild(Popups);
        Tooltips = new TooltipHost(theme);
        AddChild(Tooltips);
    }

    public UiTheme UiTheme { get; }

    /// <summary>
    /// C101: the menu theme follows the window like the world screen's does: the density-derived scale and the 48 dp target floor on a touch build, the T311 automatic scale on a desktop window,
    /// reach every menu screen built below this root. T312: a desktop build used to keep the unbound theme (scale 1), so every menu screen stayed tiny on a 1440p or 4K window.
    /// </summary>
    public override void _EnterTree()
    {
        UiTheme.Bind(GetViewport());
    }

    public override void _ExitTree() => UiTheme.Unbind();

    /// <summary>
    /// C079: mirrors every screen, popup and tooltip below this root for a right-to-left language (the containers then lay their children out from the right): the direction is
    /// set on this root, so every control below it inherits it, and follows the language from now on.
    /// </summary>
    public void BindLocalization(Unciv.Assets.Localization.ClientLocalization localization)
    {
        ApplyDirection(localization.IsRightToLeft(localization.Language));
        localization.LanguageChanged += language => ApplyDirection(localization.IsRightToLeft(language));
    }

    public void ApplyDirection(bool rightToLeft) => LayoutDirection = rightToLeft ? LayoutDirectionEnum.Rtl : LayoutDirectionEnum.Ltr;

    public PopupStack Popups { get; }

    public TooltipHost Tooltips { get; }

    public int Depth => screens.Count;

    public Control? Top => screens.Count > 0 ? screens[^1] : null;

    /// <summary>Raised after the stack changed (push or pop), with the new top screen.</summary>
    public event Action<Control>? TopChanged;

    public void Push(Control screen)
    {
        if (Top is { } below) below.Visible = false;
        screen.Fill();
        screenLayer.AddChild(screen);
        screens.Add(screen);
        TopChanged?.Invoke(screen);
    }

    /// <summary>Replaces the whole stack with <paramref name="screen"/> (Escape then cannot pop back into what was there), e.g. when a game starts.</summary>
    public void Reset(Control screen)
    {
        foreach (var old in screens)
        {
            screenLayer.RemoveChild(old);
            old.QueueFree();
        }
        screens.Clear();
        Push(screen);
    }

    /// <summary>Removes the top screen and shows the one below it. Returns false (and does nothing) when only one screen is left.</summary>
    public bool Pop()
    {
        if (screens.Count <= 1) return false;
        var closing = screens[^1];
        screens.RemoveAt(screens.Count - 1);
        closing.QueueFree();
        var top = screens[^1];
        top.Visible = true;
        TopChanged?.Invoke(top);
        return true;
    }

    /// <summary>What Escape does. Returns whether it changed anything.</summary>
    public bool Back() => Popups.Cancel() || Pop();

    /// <summary>
    /// C099: the Android back button (<c>NOTIFICATION_WM_GO_BACK_REQUEST</c>; the project does not quit on it) does what Escape does, and leaves the app only when no popup or screen is
    /// left to close. On a touch build the screens are also kept inside the safe area (notch, gesture bar) and above the virtual keyboard.
    /// </summary>
    public override void _Notification(int what)
    {
        if (what != NotificationWMGoBackRequest) return;
        if (AndroidShell.BackOutcome(Back()) == BackOutcome.Handled) return;
        // Most screens and dialogs (city, tech, save dialog, the in-game menu...) answer Escape themselves, so back is delivered to them as Escape; the app only
        // leaves from the bottom screen when the owner says nothing else is there to close (the main menu).
        if (CanQuitOnBack()) GetTree().Quit();
        else PressEscape();
    }

    /// <summary>C099: whether the back button may leave the app now (the bottom screen is the main menu). Defaults to true, the owner narrows it.</summary>
    public Func<bool> CanQuitOnBack { get; set; } = () => true;

    private void PressEscape()
    {
        foreach (bool pressed in new[] { true, false })
            GetViewport().PushInput(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = pressed }, true);
    }

    private Insets appliedInsets;
    private int appliedKeyboard;

    public override void _Process(double delta)
    {
        if (!DisplayProfile.Current.Touch) return;
        var window = GetWindow();
        var windowSize = window.Size;
        var safe = DisplayServer.GetDisplaySafeArea();
        int keyboard = DisplayServer.VirtualKeyboardGetHeight();
        var insets = AndroidShell.ComputeInsets(windowSize.X, windowSize.Y, safe.Position.X, safe.Position.Y, safe.Size.X, safe.Size.Y, keyboard);
        if (insets == appliedInsets && keyboard == appliedKeyboard) return;
        appliedInsets = insets;
        bool keyboardChanged = keyboard != appliedKeyboard;
        appliedKeyboard = keyboard;
        float scale = windowSize.X > 0 ? GetViewportRect().Size.X / windowSize.X : 1f;
        screenLayer.OffsetLeft = insets.Left * scale;
        screenLayer.OffsetTop = insets.Top * scale;
        screenLayer.OffsetRight = -insets.Right * scale;
        screenLayer.OffsetBottom = -insets.Bottom * scale;
        if (keyboardChanged && keyboard > 0) CallDeferred(MethodName.ScrollFocusedFieldIntoView);
    }

    // The layer shrank above the keyboard; a focused text field inside a scroll area is brought into view.
    private void ScrollFocusedFieldIntoView()
    {
        if (GetViewport().GuiGetFocusOwner() is not LineEdit field) return;
        for (var node = field.GetParent(); node != null; node = node.GetParent())
        {
            if (node is ScrollContainer scroll)
            {
                scroll.EnsureControlVisible(field);
                return;
            }
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } && Back()) GetViewport().SetInputAsHandled();
    }
}
