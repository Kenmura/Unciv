using Godot;

namespace Unciv.GodotHarness;

/// <summary>
/// C079: the rectangle checks every responsive and localization run asserts, from the real controls and not from pixels: no visible text control leaves
/// the window, no text is cut off without an ellipsis, and no two text controls that are not part of each other overlap. Controls scrolled out of
/// view (clipped by a scroll container or a clipping parent) do not count: only what the player can see does.
/// </summary>
internal static class LayoutChecks
{
    private const float Tolerance = 1.5f;

    /// <summary>A script step asserting <see cref="Problems"/> is empty under <paramref name="root"/> in the current window; the problems are printed.</summary>
    public static ScenarioStep Assert(string what, Control host, Func<Node> root) => new AssertQuery(what, () =>
    {
        var node = root();
        var problems = Problems(node, new Rect2(Vector2.Zero, host.GetViewport().GetVisibleRect().Size));
        foreach (string problem in problems.Take(12)) GD.PrintErr($"[layout] {what}: {problem.Replace("\n", "\\n")}");
        return problems.Count == 0;
    });

    /// <summary>
    /// The steps that run a screen through the window sizes: from 640x480 (only when <paramref name="wideCaption"/> is given; otherwise the screen is already at 1280x720) to 1280x720 and 1920x1080, each with the rectangle asserts (and <paramref name="extra"/>
    /// when given) and a golden (<c>wide</c> unless <paramref name="wideCaption"/> is null, <c>fhd</c>), then back to 640x480 where the screen must still fit.
    /// </summary>
    public static IEnumerable<ScenarioStep> Widths(Control host, string screen, Func<Node> root, string? wideCaption, string fhdCaption, Func<bool>? extra = null)
    {
        ScenarioStep Check(string size) => Assert($"at {size} no {screen} text is cut off, leaves the window or overlaps another", host, root);
        ScenarioStep More(string size) => new AssertQuery($"at {size} the {screen} is intact", extra ?? (() => true));
        ScenarioStep[] Resize(Vector2I size) => [new Act($"resize the window to {size.X}x{size.Y}", () => host.GetWindow().Size = size), new WaitFrames(size.X < 1000 ? 8 : 6)];
        var steps = new List<ScenarioStep>();
        if (wideCaption != null)
        {
            steps.Add(Check("640x480"));
            steps.AddRange(Resize(new Vector2I(1280, 720)));
            steps.AddRange([More("1280x720"), Check("1280x720"), new Capture("wide", wideCaption)]);
        }
        else steps.AddRange([More("1280x720"), Check("1280x720")]);
        steps.AddRange(Resize(new Vector2I(1920, 1080)));
        steps.AddRange([More("1920x1080"), Check("1920x1080"), new Capture("fhd", fhdCaption)]);
        steps.AddRange(Resize(new Vector2I(640, 480)));
        steps.AddRange([More("640x480"), Check("640x480 again")]);
        return steps;
    }

    /// <summary>T284/T286: a step asserting every visible button under <paramref name="root"/> is at least <paramref name="minTargetPx"/> tall (a touch target) unless <paramref name="exempt"/> excuses it; the offenders are printed.</summary>
    public static ScenarioStep Touchable(string tag, string what, int minTargetPx, Func<Node> root, Func<BaseButton, bool>? exempt = null) => new AssertQuery($"every visible button of the {what} is at least 48 dp ({minTargetPx} px) tall", () =>
    {
        var bad = Buttons(root()).Where(it => !(exempt?.Invoke(it) ?? false) && it.IsVisibleInTree() && it.Size.Y > 0 && it.Size.Y + 0.5f < minTargetPx).Select(it => $"{it.GetType().Name} '{(it as Button)?.Text}' {it.Name} {it.Size.Y}").ToList();
        foreach (string line in bad.Take(12)) GD.PrintErr($"[{tag}] {what}: undersized {line}");
        return bad.Count == 0;
    });

    /// <summary>T284/T286: a step asserting no scroll container under <paramref name="root"/> hides content on an axis it cannot scroll.</summary>
    public static ScenarioStep Reachable(string tag, string what, Func<Node> root) => new AssertQuery($"nothing of the {what} is cut off by a container that cannot scroll", () =>
    {
        var bad = Unscrollable(root()).ToList();
        foreach (string line in bad.Take(12)) GD.PrintErr($"[{tag}] {what}: {line}");
        return bad.Count == 0;
    });

    /// <summary>Scroll containers whose content is larger than they are on an axis they do not scroll: what lies beyond is unreachable for a finger.</summary>
    private static IEnumerable<string> Unscrollable(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is ScrollContainer { Visible: true } scroll && scroll.IsVisibleInTree() && scroll.GetChildCount() > 0 && scroll.GetChild(0) is Control content)
            {
                var need = content.GetCombinedMinimumSize();
                if (scroll.VerticalScrollMode == ScrollContainer.ScrollMode.Disabled && need.Y > scroll.Size.Y + 1.5f) yield return $"{scroll.Name} needs {need.Y} high, has {scroll.Size.Y}";
                if (scroll.HorizontalScrollMode == ScrollContainer.ScrollMode.Disabled && need.X > scroll.Size.X + 1.5f) yield return $"{scroll.Name} needs {need.X} wide, has {scroll.Size.X}";
            }
            foreach (string nested in Unscrollable(child)) yield return nested;
        }
    }

    private static IEnumerable<BaseButton> Buttons(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is BaseButton button) yield return button;
            foreach (var nested in Buttons(child)) yield return nested;
        }
    }

    /// <summary>The problems found under <paramref name="root"/> inside a window of <paramref name="window"/> (empty when the layout is sound).</summary>
    public static List<string> Problems(Node root, Rect2 window)
    {
        var problems = new List<string>();
        var seen = new List<(Control Control, Rect2 Rect)>();
        Walk(root, window, problems, seen);
        for (int i = 0; i < seen.Count; i++)
            for (int j = i + 1; j < seen.Count; j++)
            {
                var (a, b) = (seen[i], seen[j]);
                if (a.Control.IsAncestorOf(b.Control) || b.Control.IsAncestorOf(a.Control)) continue;
                var overlap = a.Rect.Intersection(b.Rect);
                if (overlap.Size.X > Tolerance && overlap.Size.Y > Tolerance)
                    problems.Add($"{Describe(a.Control)} {a.Rect} overlaps {Describe(b.Control)} {b.Rect}");
            }
        return problems;
    }

    private static void Walk(Node node, Rect2 window, List<string> problems, List<(Control, Rect2)> seen)
    {
        if (node is Control { Visible: false }) return;
        if (node is Control control && TextOf(control) is { Length: > 0 } && control.IsVisibleInTree())
        {
            if (Visible(control) is { } rect)
            {
                seen.Add((control, rect));
                var full = control.GetGlobalRect();
                if (!window.Grow(Tolerance).Encloses(rect)) problems.Add($"{Describe(control)} {full} leaves the window {window}");
                else if (Clipped(control) is { } cut) problems.Add($"{Describe(control)} {full} cuts its text off without an ellipsis: {cut}");
            }
        }
        foreach (var child in node.GetChildren()) Walk(child, window, problems, seen);
    }

    /// <summary>The part of the control that shows: its rectangle cut by every scrolling or clipping ancestor; null when nothing of it shows or it is only partly scrolled into view.</summary>
    private static Rect2? Visible(Control control)
    {
        var rect = control.GetGlobalRect();
        for (var parent = control.GetParent(); parent != null; parent = parent.GetParent())
        {
            if (parent is Control p && (p is ScrollContainer || p.ClipContents))
            {
                var clip = p.GetGlobalRect();
                if (!clip.Intersects(rect)) return null;
                if (!clip.Encloses(rect)) return null; // partly scrolled out: not judged until it is fully in view
            }
        }
        return rect;
    }

    private static string? TextOf(Control control) => control switch
    {
        Label label => label.Text,
        Button button => button.Text,
        _ => null,
    };

    private static string? Clipped(Control control)
    {
        switch (control)
        {
            case Label label when label.AutowrapMode == TextServer.AutowrapMode.Off && label.TextOverrunBehavior == TextServer.OverrunBehavior.NoTrimming:
                float need = label.Text.Split('\n').Max(line => label.GetThemeFont("font").GetStringSize(line, HorizontalAlignment.Left, -1, label.GetThemeFontSize("font_size")).X);
                return label.Size.X + Tolerance < need ? $"needs {need} wide" : null;
            case Label label when label.AutowrapMode != TextServer.AutowrapMode.Off:
                return label.GetLineCount() > 0 && label.GetVisibleLineCount() < label.GetLineCount() && label.TextOverrunBehavior == TextServer.OverrunBehavior.NoTrimming ? "lines hidden" : null;
            case Button button when button.ClipText && button.TextOverrunBehavior == TextServer.OverrunBehavior.NoTrimming:
                var font = button.GetThemeFont("font");
                float width = font.GetStringSize(button.Text, HorizontalAlignment.Left, -1, button.GetThemeFontSize("font_size")).X;
                return width > button.Size.X + Tolerance ? $"needs {width} wide" : null;
            default:
                return null;
        }
    }

    private static string Describe(Control control) => $"{control.GetType().Name} \"{TextOf(control)}\" ({control.GetPath().ToString().Split('/').TakeLast(3).Aggregate((x, y) => x + "/" + y)})";
}
