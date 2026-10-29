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
