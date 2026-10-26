using Unciv.View;

namespace Unciv.Queries.Tutorial;

// One paragraph of the card (a FormattedLine of the Event: its text, centered flag and optional ruleset image)
public sealed record TutorialTaskLine(string Text, bool Centered, string ExtraImage, float ImageSize);

// A button of the card (Event choice); Index is its position in the Event's own choice list and addresses it in ChooseTutorialTaskChoice (C060)
public sealed record TutorialTaskChoice(int Index, string Text);

// Event: the ruleset Event's name; Lines: its text then its civilopedia lines; Choices: the choices that match now
public sealed record TutorialTaskResult(string Event, IReadOnlyList<TutorialTaskLine> Lines, IReadOnlyList<TutorialTaskChoice> Choices);

// C054: Kotlin TutorialTaskTable.getCurrentTask. Answers the first available Floating Event for the viewer, or an empty Event name when there is none
// (tutorials off, defeated, nothing available). ShowTutorials is the viewer's own client setting. Pure: the "Create a trade route" write of Kotlin is
// replaced by treating it as completed while the connection exists (see TutorialTasks.Current).
[QueryType("tutorial.Task")]
public sealed record TutorialTaskQuery(bool ShowTutorials = true) : IQuery<TutorialTaskResult>
{
    public string Type => "tutorial.Task";

    public TutorialTaskResult Run(GameView view)
    {
        var viewer = view.CivView.Unwrap();
        var current = TutorialTasks.Current(viewer, ShowTutorials);
        if (current == null) return new TutorialTaskResult("", [], []);
        var (task, choices) = current;
        var lines = new List<TutorialTaskLine>();
        if (task.Text.Length != 0) lines.Add(new TutorialTaskLine(task.Text, false, "", 0f));
        lines.AddRange(task.CivilopediaText.Select(it => new TutorialTaskLine(it.TextToDisplay, it.Centered, it.ExtraImage, float.IsNaN(it.ImageSize) ? 0f : it.ImageSize)));
        return new TutorialTaskResult(task.Name, lines, choices.Select(it => new TutorialTaskChoice(task.Choices.IndexOf(it), it.Text)).ToList());
    }
}
