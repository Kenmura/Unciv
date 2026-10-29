namespace Unciv.Assets;

/// <summary>UI size class of the window: <see cref="UiTokens.BreakpointFor"/>.</summary>
public enum UiBreakpoint
{
    /// <summary>Logical width below 1000: icon-only nav, overlay side panels, tight padding (includes the 640x480 harness and phones).</summary>
    Compact,

    /// <summary>1000 to 1600.</summary>
    Regular,

    /// <summary>Above 1600: three-column city screen, docked detail panes.</summary>
    Wide,
}

/// <summary>One step of the type scale (C062, design/ui-redesign.md 2.3). <see cref="Weight"/> is the OpenType wght axis value; <see cref="Tracking"/> is extra letter spacing in unscaled pixels.</summary>
public sealed record TypeStyle(string Name, int Size, bool Serif, int Weight, int Tracking = 0, bool Tabular = false)
{
    /// <summary>The body style has no variation of its own: it is the theme's default font.</summary>
    public bool IsDefault => Name == "Body";
}

/// <summary>
/// The design tokens of the UI (C062): colours, type scale, spacing, radii, borders and component metrics, Godot-free so they can be unit-tested
/// (contrast, scaling). <c>ThemeBuilder</c> in the client turns them into one <c>Theme</c>; no view reads a colour or a size from anywhere else.
/// Terrain, unit and stat colours stay in <see cref="PlaceholderPalette"/>.
/// </summary>
public static class UiTokens
{
    // Surfaces, dark to light
    public static Rgb Bg0 { get; } = new(0x0B, 0x0F, 0x15);
    public static Rgb Bg1 { get; } = new(0x12, 0x18, 0x22);
    public static Rgb Surface1 { get; } = new(0x18, 0x20, 0x2B);
    public static Rgb Surface2 { get; } = new(0x20, 0x2A, 0x38);
    public static Rgb Surface3 { get; } = new(0x2A, 0x36, 0x47);
    public static Rgb Inset { get; } = new(0x0F, 0x14, 0x1C);
    public static Rgb BorderSubtle { get; } = new(0x2C, 0x37, 0x46);
    public static Rgb BorderStrong { get; } = new(0x44, 0x53, 0x6A);

    // Accent (brass)
    public static Rgb Accent { get; } = new(0xD6, 0xAA, 0x3C);
    public static Rgb AccentHi { get; } = new(0xF0, 0xC8, 0x5A);
    public static Rgb AccentLo { get; } = new(0x7A, 0x5E, 0x1E);
    public static Rgb OnAccent { get; } = new(0x1A, 0x13, 0x07);

    // Text
    public static Rgb TextPrimary { get; } = new(0xEC, 0xE8, 0xDF);
    public static Rgb TextSecondary { get; } = new(0xA8, 0xB0, 0xBC);
    public static Rgb TextMuted { get; } = new(0x6F, 0x79, 0x88);
    public static Rgb TextDisabled { get; } = new(0x54, 0x5D, 0x6A);

    // Semantic
    public static Rgb Info { get; } = new(0x4A, 0x90, 0xE2);
    public static Rgb Success { get; } = new(0x5D, 0xBB, 0x63);
    public static Rgb Warning { get; } = new(0xE3, 0xA3, 0x3B);
    public static Rgb Danger { get; } = new(0xD6, 0x54, 0x4A);

    /// <summary>C066: <see cref="Danger"/> lightened for text and thin lines on <see cref="Surface2"/>/<see cref="Surface3"/> (the fill itself is only 3.6:1 there); at least 4.5:1 on every raised surface.</summary>
    public static Rgb DangerText { get; } = new(0xEF, 0x7B, 0x71);

    /// <summary>Modal backdrop colour; drawn at <see cref="ScrimPercent"/> opacity.</summary>
    public static Rgb Scrim { get; } = new(0x06, 0x08, 0x0C);
    public const int ScrimPercent = 72;

    /// <summary>Shadow of modals, tooltips and the HUD dock: <see cref="Bg0"/> at this opacity.</summary>
    public const int ShadowPercent = 60;
    public const int ShadowSize = 8;
    public const int ShadowOffsetY = 2;

    /// <summary>Opacity of the <see cref="AccentLo"/> tint behind a toggled chip.</summary>
    public const int ChipTintPercent = 35;

    // 4px grid
    public const int S1 = 4;
    public const int S2 = 8;
    public const int S3 = 12;
    public const int S4 = 16;
    public const int S6 = 24;
    public const int S8 = 32;

    // Radii
    public const int RadiusControl = 4;
    public const int RadiusCard = 6;
    public const int RadiusPanel = 8;
    public const int RadiusPill = 12;

    // Borders
    public const int BorderWidth = 1;
    public const int RingWidth = 2;
    public const int HairlineWidth = 2;

    // Component metrics
    public const int ControlMinHeight = 32;
    public const int CompactControlMinHeight = 28;
    public const int RowHeight = 32;
    public const int CompactRowHeight = 28;
    public const int TooltipMaxWidth = 340;
    public const int BarThin = 6;
    public const int BarLabelled = 14;
    public const int SwitchWidth = 40;
    public const int SwitchHeight = 22;

    // Type scale (px at 1x)
    public static TypeStyle Display { get; } = new("Display", 34, true, 700);
    public static TypeStyle Title { get; } = new("Title", 24, true, 700);
    public static TypeStyle Heading { get; } = new("Heading", 18, false, 600);
    public static TypeStyle Body { get; } = new("Body", 15, false, 400);
    public static TypeStyle BodyStrong { get; } = new("BodyStrong", 15, false, 600);
    public static TypeStyle Small { get; } = new("Small", 13, false, 400);
    public static TypeStyle Overline { get; } = new("Overline", 11, false, 700, 1);

    /// <summary>C065: values, costs and table numbers: BodyStrong with tabular figures so columns of digits line up. Not a step of the scale.</summary>
    public static TypeStyle Number { get; } = new("Number", 15, false, 600, 0, true);

    /// <summary>Every step of the scale, largest first.</summary>
    public static IReadOnlyList<TypeStyle> TypeScale { get; } = [Display, Title, Heading, Body, BodyStrong, Small, Overline];

    public const int FontSize = 15;

    /// <summary>Logical widths where <see cref="UiBreakpoint"/> changes.</summary>
    public const int RegularFromWidth = 1000;
    public const int WideFromWidth = 1600;

    /// <summary>The breakpoint of a logical width (window width divided by the effective scale).</summary>
    public static UiBreakpoint BreakpointFor(float logicalWidth) =>
        logicalWidth < RegularFromWidth ? UiBreakpoint.Compact : logicalWidth <= WideFromWidth ? UiBreakpoint.Regular : UiBreakpoint.Wide;

    /// <summary>The automatic part of the UI scale: min(w/1280, h/720) rounded down to a 0.25 step, never below 1 or above 2.</summary>
    public static float AutoScale(int width, int height)
    {
        if (width <= 0 || height <= 0) return 1f;
        float raw = Math.Min(width / 1280f, height / 720f);
        return Math.Clamp(MathF.Floor(raw * 4f) / 4f, 1f, 2f);
    }

    /// <summary>C096: Android's guideline for the smallest touch target, in density-independent pixels (160 dp per inch).</summary>
    public const int TouchTargetDp = 48;

    /// <summary>C096: the smallest finger movement that is a drag and not a tap, in dp (about 20 px on a 400 ppi phone).</summary>
    public const int TouchDragThresholdDp = 8;

    /// <summary>C096: how long a finger must rest on one spot for a long press, in seconds.</summary>
    public const double LongPressSeconds = 0.5;

    /// <summary>C096: the logical viewport a touch build keeps at least: the world HUD was designed for about 900x415 (design/ANDROID.md section 3).</summary>
    public const int TouchMinLogicalWidth = 900;
    public const int TouchMinLogicalHeight = 415;

    /// <summary>Upper end of the density-derived scale before the quarter-step floor (so 2.5 in practice; a total of about 2.0 to 2.6 once the user's scale is applied, never the 3.75 a 32 px control would need for 48 dp).</summary>
    public const float MaxTouchScale = 2.6f;

    /// <summary>
    /// C096: the automatic UI scale of a touch build, from the screen density instead of the pixel count: dpi / 160 (the Android density), rounded down to a
    /// quarter step, never above <see cref="MaxTouchScale"/>, never so large that the logical viewport falls below <see cref="TouchMinLogicalWidth"/> x
    /// <see cref="TouchMinLogicalHeight"/>, never below 1. An unknown density (<paramref name="dpi"/> not positive) falls back to <see cref="AutoScale"/>.
    /// </summary>
    public static float TouchAutoScale(int width, int height, float dpi)
    {
        if (width <= 0 || height <= 0 || !(dpi > 0f)) return AutoScale(width, height);
        float raw = Math.Min(Math.Min(dpi / 160f, MaxTouchScale), Math.Min(width / (float)TouchMinLogicalWidth, height / (float)TouchMinLogicalHeight));
        return Math.Max(1f, MathF.Floor(raw * 4f) / 4f);
    }

    /// <summary>C096: <paramref name="dp"/> density-independent pixels in pixels of a screen of <paramref name="dpi"/> (rounded half away from zero); 0 when the density is unknown.</summary>
    public static int DpToPx(float dp, float dpi) => dpi > 0f ? (int)MathF.Round(dp * dpi / 160f, MidpointRounding.AwayFromZero) : 0;

    /// <summary>A size in unscaled pixels at <paramref name="scale"/>, rounded half away from zero.</summary>
    public static int Scaled(int unscaled, float scale) => (int)MathF.Round(unscaled * scale, MidpointRounding.AwayFromZero);

    /// <summary>As <see cref="Scaled"/> but never below 1: for border widths, which must not vanish at a small scale.</summary>
    public static int ScaledAtLeastOne(int unscaled, float scale) => Math.Max(1, Scaled(unscaled, scale));

    /// <summary>WCAG 2.x contrast ratio of two colours (1 to 21).</summary>
    public static double Contrast(Rgb a, Rgb b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(Rgb c) => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);

    private static double Linear(byte channel)
    {
        double s = channel / 255.0;
        return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }
}
