using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Ui;
using Unciv.Models.Translations;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C053: the World Congress result, a modal over the world (<see cref="VoteResultModel"/>, all from the engine's answer): one line per civ the viewer knows with the votes
/// it got, what its own vote is worth (and the United Nations building for its owner) and whom it voted for, then the engine's verdict and a Continue button. Pressing
/// it only raises <see cref="Continued"/>; the owner sends <c>AckDiplomaticVoteResult</c>. The view shows what it is given.
/// </summary>
public partial class VoteResultScreen : Control
{
    private readonly ColorRect dim = new() { MouseFilter = MouseFilterEnum.Ignore };
    private readonly PanelContainer panel = new() { Name = "VotePanel" };
    private readonly Label title = new() { Name = "VoteTitle", HorizontalAlignment = HorizontalAlignment.Center };
    private readonly GridContainer rows = new() { Name = "VoteRows", Columns = 6 };
    private readonly Label verdict = new() { Name = "VoteVerdict", HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Button continueButton = new() { Name = "VoteContinue" };

    public VoteResultScreen()
    {
        Name = "VoteResult";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        dim.Fill();
        AddChild(dim);
        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        centre.Fill();
        AddChild(centre);
        centre.AddChild(panel);
        var column = new VBoxContainer();
        panel.AddChild(column);
        column.AddChild(title);
        column.AddChild(rows);
        column.AddChild(verdict);
        column.AddChild(continueButton);
        continueButton.Pressed += () => Continued?.Invoke();
    }

    public VoteResultView? Current { get; private set; }

    public string TitleText => title.Text;

    public string VerdictText => verdict.Text;

    public Button ContinueButton => continueButton;

    /// <summary>The text of every cell, one list per row, top to bottom: votes received, civ, weight, United Nations (or empty), choice, target (or empty).</summary>
    public IReadOnlyList<IReadOnlyList<string>> RowTexts
    {
        get
        {
            var cells = rows.GetChildren().Select(CellText).ToList();
            return cells.Chunk(rows.Columns).Select(it => (IReadOnlyList<string>)it).ToList();
        }
    }

    public event Action? Continued;

    public void Init(UiTheme uiTheme)
    {
        Theme = uiTheme.Theme;
        var shade = uiTheme.Panel.BgColor;
        shade.A = 0.75f;
        dim.Color = shade;
        title.AddThemeFontSizeOverride("font_size", uiTheme.Px(26));
        rows.AddThemeConstantOverride("h_separation", uiTheme.Px(14));
        verdict.CustomMinimumSize = new Vector2(uiTheme.Px(420), 0f);
    }

    public void LayoutIn(Vector2 screen) => Size = screen;

    /// <param name="view">What to show.</param>
    /// <param name="colourOf">The civ's colour by name; null for a civ the ruleset does not know.</param>
    public void Open(VoteResultView view, Func<string, Rgb?> colourOf)
    {
        Current = view;
        title.Text = view.Title.Tr();
        verdict.Text = view.Verdict.Tr();
        continueButton.Text = view.ContinueLabel.Tr();
        foreach (var child in rows.GetChildren())
        {
            rows.RemoveChild(child);
            child.QueueFree();
        }
        foreach (var row in view.Rows)
        {
            var colour = colourOf(row.Civ) ?? PlaceholderPalette.FromHash(row.Civ, 150, 210);
            rows.AddChild(new Label { Text = row.Received, HorizontalAlignment = HorizontalAlignment.Right });
            var name = new Label { Text = row.Civ.Tr() };
            name.AddThemeColorOverride("font_color", ToColour(colour));
            rows.AddChild(name);
            rows.AddChild(new Label { Text = row.Weight.Tr() });
            rows.AddChild(new Label { Text = row.UnBuilding?.Tr() ?? "" });
            rows.AddChild(new Label { Text = row.Choice.Tr() });
            rows.AddChild(new Label { Text = row.Target?.Tr() ?? "" });
        }
        Visible = true;
        continueButton.GrabFocus();
    }

    public void Close()
    {
        Current = null;
        Visible = false;
    }

    private static Color ToColour(Rgb rgb) => new(rgb.R / 255f, rgb.G / 255f, rgb.B / 255f);

    private static string CellText(Node node) => node is Label label ? label.Text : "";
}
