using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Localization;
using Unciv.GodotClient.Ui;
using Unciv.Models.Translations;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C053: the World Congress result, a modal over the world (<see cref="VoteResultModel"/>, all from the engine's answer): one table row per civ the viewer knows with the votes
/// it got, what its own vote is worth (and the United Nations building for its owner) and whom it voted for, then the engine's verdict and a Continue button. Pressing
/// it only raises <see cref="Continued"/>; the owner sends <c>AckDiplomaticVoteResult</c>. The view shows what it is given.
/// C077: a C066 <see cref="Modal"/> with a <see cref="DataTable"/> (votes right-aligned) in a scroll area, the verdict in its own card and Continue as the primary action.
/// </summary>
public partial class VoteResultScreen : Modal
{
    private readonly DataTable table = new([new DataColumn("Votes", Numeric: true), new DataColumn("Civilization", MinWidth: 100), new DataColumn("Vote worth", MinWidth: 120), new DataColumn("Voted for", MinWidth: 190)]);
    private readonly ScrollContainer scroll = new() { Name = "VoteScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
    private readonly VBoxContainer content = new() { Name = "VoteContent" };
    private readonly Label verdict = new() { Name = "VoteVerdict", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private Button continueButton = new() { Name = "VoteContinue" };

    public VoteResultScreen()
    {
        Name = "VoteResult";
        table.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        content.AddChild(table);
        scroll.AddChild(content);
    }

    public VoteResultView? Current { get; private set; }

    public string TitleText => TitleLabel.Text;

    public string VerdictText => verdict.Text;

    public Button ContinueButton => continueButton;

    /// <summary>The text of every cell, one list per row, top to bottom: votes received, civ, vote worth (with the United Nations building of its owner on a second line), whom it voted for (or Abstained).</summary>
    public IReadOnlyList<IReadOnlyList<string>> RowTexts =>
        Enumerable.Range(0, table.RowCount).Select(r => (IReadOnlyList<string>)Enumerable.Range(0, 4).Select(c => table.CellText(r, c)).ToList()).ToList();

    public event Action? Continued;

    /// <param name="view">What to show.</param>
    public void Open(VoteResultView view)
    {
        Current = view;
        SetHeader(view.Title);
        float width = Fit(MaxWidth);
        verdict.Text = view.Verdict.Tr();
        verdict.CustomMinimumSize = new Vector2(width, 0f);
        continueButton = new Button { Name = "VoteContinue", Text = view.ContinueLabel.TrText() }; // the footer frees its buttons whenever it is refilled
        continueButton.Pressed += () => Continued?.Invoke();
        table.SetRows(view.Rows.Select(row => (IReadOnlyList<string>)
        [
            row.Received,
            row.Civ.Tr(),
            row.Weight.Tr() + (row.UnBuilding == null ? "" : "\n" + row.UnBuilding.Tr()),
            row.Target?.Tr() ?? row.Choice.Tr(),
        ]).ToList());
        foreach (var child in Body.GetChildren()) Body.RemoveChild(child);
        scroll.CustomMinimumSize = new Vector2(width, 0f);
        Body.AddChild(scroll);
        Body.AddChild(verdict);
        FitScroll(scroll, content);
        SetActions([(continueButton, ChoiceRole.Primary)]);
        Visible = true;
        continueButton.GrabFocus();
    }

    public void Close()
    {
        Current = null;
        Visible = false;
    }
}
