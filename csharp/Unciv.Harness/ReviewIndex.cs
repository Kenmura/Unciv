using System.Globalization;
using System.Text;

namespace Unciv.Harness;

/// <summary>One scenario's contribution to the owner review: its outcome, captures (PNG paths relative to the index) and checklist.</summary>
public sealed record ReviewSection(string Scenario, bool Passed, IReadOnlyList<string> Failures, IReadOnlyList<ScenarioCapture> Captures, IReadOnlyList<string> Checklist);

/// <summary>Renders <c>out/review/index.md</c>: what the owner opens next to <c>contact-sheet.png</c> (CLIENT-SLICES.md, "Owner review").</summary>
public static class ReviewIndex
{
    /// <summary>The questions every slice is judged against (CLIENT-SLICES.md); scenarios add their own below.</summary>
    public static readonly IReadOnlyList<string> GeneralChecklist =
    [
        "Can I tell terrain types apart at default zoom? Are fogged tiles clearly dimmer than visible ones?",
        "Can I tell my units from enemy units, and unit types from each other, at a glance?",
        "Is the selected unit, its reachable area and its path obvious?",
        "After End Turn, can I see what the AI did (moves, attacks, new cities)?",
        "Is every number on screen explained by a tooltip on hover?",
        "Can I do the thing without a mouse accident (undo/confirm for destructive actions: disband, declare war, raze)?",
        "Does every screen close with Esc and return to the map in the same camera position?",
        "Any text clipped, overlapping or in a fallback box (tofu) in the language I read?",
    ];

    /// <param name="sections">One per scenario, in the order shown.</param>
    /// <param name="sheetFile">The contact sheet image, relative to the index.</param>
    /// <param name="imageRoot">Prefix from the index to the scenario output directories (the index lives in <c>out/review</c>, the captures in <c>out/&lt;scenario&gt;</c>).</param>
    public static string Render(IReadOnlyList<ReviewSection> sections, string sheetFile = "contact-sheet.png", string imageRoot = "")
    {
        var sb = new StringBuilder();
        sb.Append("# Owner review\n\n");
        sb.Append(CultureInfo.InvariantCulture, $"![contact sheet]({sheetFile})\n\n");
        int failed = sections.Count(it => !it.Passed);
        sb.Append(CultureInfo.InvariantCulture, $"{sections.Count} scenario(s), {failed} failing. Goldens prove \"did not change\", not \"looks right\": answer yes / no / notes for each question.\n\n");
        foreach (var section in sections)
        {
            sb.Append(CultureInfo.InvariantCulture, $"## {section.Scenario}: {(section.Passed ? "PASS" : "FAIL")}\n\n");
            foreach (string failure in section.Failures) sb.Append("- FAILED: ").Append(failure).Append('\n');
            if (section.Failures.Count > 0) sb.Append('\n');
            foreach (var capture in section.Captures)
                sb.Append(CultureInfo.InvariantCulture, $"- `{capture.Step}`: {capture.Caption}\n  ![{capture.Step}]({imageRoot}{section.Scenario}/{capture.Step}.png)\n");
            sb.Append("\nChecklist (yes / no / notes):\n\n");
            foreach (string item in section.Checklist.Count > 0 ? section.Checklist : GeneralChecklist)
                sb.Append("- [ ] ").Append(item).Append('\n');
            sb.Append('\n');
        }
        return sb.ToString();
    }
}
