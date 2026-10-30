using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;

namespace Unciv.GodotClient.Ui;

/// <summary>
/// The UI kit's view of the asset registry: the theme (key <c>ui/theme</c>) at a UI scale, and the icons widgets ask for by key.
/// Widgets take their sizes through <see cref="Px"/> so one scale setting resizes fonts and spacing together; no widget names a file or colour.
/// </summary>
public sealed class UiTheme
{
    public const float MinScale = 0.5f;
    public const float MaxScale = 3f;
    public const int IconSize = 24;

    private readonly Theme baseTheme;
    private readonly bool overridden;
    private float userScale = 1f;
    private float autoScale = 1f;
    private Viewport? bound;
    private int minTarget;

    public UiTheme(AssetRegistry registry, float scale = 1f)
    {
        Registry = registry;
        baseTheme = registry.Get<Theme>(RulesetAssetKeys.UiTheme);
        // an override file (assets/override/ui/theme.tres) is the artist's theme: keep it as it is and scale only its font size
        overridden = registry.Resolve(RulesetAssetKeys.UiTheme).Path != null;
        Theme = overridden ? (Theme)baseTheme.Duplicate() : new Theme();
        userScale = Math.Clamp(scale, MinScale, MaxScale);
        Rebuild();
    }

    public AssetRegistry Registry { get; }

    /// <summary>The scaled theme every screen of the kit inherits; the same instance for the lifetime of this object, so a scale or breakpoint change reaches live controls.</summary>
    public Theme Theme { get; }

    /// <summary>The effective scale: the user's setting times the automatic scale of the bound viewport (1 when none is bound). Everything sized through <see cref="Px"/> follows it.</summary>
    public float Scale => userScale * autoScale;

    /// <summary>The user's setting alone (<see cref="MinScale"/> to <see cref="MaxScale"/>).</summary>
    public float UserScale => userScale;

    /// <summary>The size class of the bound viewport at the effective scale; <see cref="UiBreakpoint.Regular"/> while none is bound.</summary>
    public UiBreakpoint Breakpoint { get; private set; } = UiBreakpoint.Regular;

    /// <summary>Raised after <see cref="SetScale"/> changed the scale, so widgets with fixed pixel sizes can resize.</summary>
    public event Action? ScaleChanged;

    /// <summary>Raised when the layout may need to change: the bound viewport was resized, or the scale changed. Layout code switches structure only when <see cref="Breakpoint"/> differs from what it last built for.</summary>
    public event Action? LayoutChanged;

    /// <summary>Sets the user's scale and rebuilds the whole theme (fonts, margins, borders, radii, icons) into <see cref="Theme"/>.</summary>
    public void SetScale(float scale)
    {
        userScale = Math.Clamp(scale, MinScale, MaxScale);
        Rebuild();
        ScaleChanged?.Invoke();
        LayoutChanged?.Invoke();
    }

    /// <summary>Follows <paramref name="viewport"/>: its size picks the automatic scale and the breakpoint, and a resize raises <see cref="LayoutChanged"/>.</summary>
    public void Bind(Viewport viewport)
    {
        Unbind();
        bound = viewport;
        viewport.SizeChanged += OnViewportResized;
        Refit();
    }

    public void Unbind()
    {
        if (bound != null && GodotObject.IsInstanceValid(bound)) bound.SizeChanged -= OnViewportResized;
        bound = null;
    }

    private void OnViewportResized()
    {
        bool scaleChanged = Refit();
        if (!scaleChanged) LayoutChanged?.Invoke();
    }

    /// <summary>Recomputes the automatic scale and breakpoint from the bound viewport; true when the effective scale changed (and <see cref="ScaleChanged"/> and <see cref="LayoutChanged"/> were raised).</summary>
    private bool Refit()
    {
        if (bound == null) return false;
        var size = bound.GetVisibleRect().Size;
        var profile = DisplayProfile.Current;
        // C096: touch builds derive the scale and the 48 dp floor from the density (in the pixels of this viewport, which a window content scale shrinks)
        float dpi = profile.UsesDensity ? profile.Dpi / ContentFactor() : 0f;
        // T311: a desktop window is measured in real window pixels (the user's content scale multiplies the result on screen, it must not also shrink the measure) and honours the OS display scale
        float content = ContentFactor();
        float auto = dpi > 0f ? UiTokens.TouchAutoScale((int)size.X, (int)size.Y, dpi) : UiTokens.AutoScale((int)MathF.Round(size.X * content, MidpointRounding.AwayFromZero), (int)MathF.Round(size.Y * content, MidpointRounding.AwayFromZero), profile.OsScale);
        int target = dpi > 0f ? UiTokens.DpToPx(UiTokens.TouchTargetDp, dpi) : 0;
        var breakpoint = UiTokens.BreakpointFor(size.X / (userScale * auto));
        bool scaleChanged = auto != autoScale || target != minTarget;
        if (!scaleChanged && breakpoint == Breakpoint) return false;
        autoScale = auto;
        minTarget = target;
        Breakpoint = breakpoint;
        Rebuild();
        if (scaleChanged) ScaleChanged?.Invoke();
        LayoutChanged?.Invoke();
        return scaleChanged;
    }

    private void Rebuild()
    {
        if (overridden) Theme.DefaultFontSize = baseTheme.DefaultFontSize > 0 ? Px(baseTheme.DefaultFontSize) : -1;
        else ThemeBuilder.Populate(Theme, Scale, Breakpoint == UiBreakpoint.Compact, minTarget);
    }

    /// <summary>
    /// T316: runs <paramref name="apply"/> now and again whenever the window is resized or the scale changes while <paramref name="owner"/> is in the scene tree, so a size taken through <see cref="Px"/>
    /// (a column width, a margin, a separation) follows the live window instead of the one the screen was built in. Also runs once on entering the tree, since the theme may have been refitted meanwhile.
    /// </summary>
    public void Fit(Node owner, Action apply)
    {
        apply();
        void Handler() { if (GodotObject.IsInstanceValid(owner)) apply(); }
        owner.TreeEntered += () =>
        {
            LayoutChanged -= Handler;
            LayoutChanged += Handler;
            apply();
        };
        owner.TreeExited += () => LayoutChanged -= Handler;
        if (owner.IsInsideTree()) LayoutChanged += Handler;
    }

    /// <summary>C096: the smallest height of a touch target in this theme's pixels (48 dp); 0 on a mouse build or before a viewport is bound.</summary>
    public int MinTargetPx => minTarget;

    private float ContentFactor() => bound?.GetWindow() is { ContentScaleMode: not Window.ContentScaleModeEnum.Disabled } window && window.ContentScaleFactor > 0f ? window.ContentScaleFactor : 1f;

    /// <summary>A size in unscaled pixels at the current UI scale, rounded half away from zero.</summary>
    public int Px(int unscaled) => UiTokens.Scaled(unscaled, Scale);

    public Texture2D Icon(AssetKey key) => Registry.Get<Texture2D>(key);

    public StyleBoxFlat Panel => (StyleBoxFlat)Theme.GetStylebox("panel", "PanelContainer");
}
