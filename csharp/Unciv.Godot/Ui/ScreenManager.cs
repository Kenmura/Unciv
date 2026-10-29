using Godot;

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
    /// C101: on a touch build the menu theme follows the window like the world screen's does: the density-derived scale and the 48 dp target floor reach every menu screen built
    /// below this root. A mouse build keeps the unbound theme (scale 1), as before.
    /// </summary>
    public override void _EnterTree()
    {
        if (DisplayProfile.Current.UsesDensity) UiTheme.Bind(GetViewport());
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

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } && Back()) GetViewport().SetInputAsHandled();
    }
}
