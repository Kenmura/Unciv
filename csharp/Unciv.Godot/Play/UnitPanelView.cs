using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Ui;
using Unciv.Models.Translations;
using Unciv.GodotClient.Localization;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C012: the selected unit's panel at the bottom left: name, strength, moves, health, experience, standing order, promotions, and the action menu, one button per
/// <see cref="MenuEntry"/> (disabled entries stay visible with the reason as tooltip, so the menu is discoverable). It shows what it is given and raises
/// <see cref="EntryChosen"/>; a chosen entry that needs confirming is asked here (<see cref="AskConfirmation"/>) before it is raised again as <see cref="Confirmed"/>.
/// </summary>
public partial class UnitPanelView : PanelContainer
{
    private const int Width = 230;
    private const int MenuHeight = 150;
    private const float Margin = 12f;

    private readonly VBoxContainer column = new() { Name = "Column" };
    private readonly Label title = new() { Name = "Title" };
    private readonly Label stats = new() { Name = "Stats" };
    private readonly VBoxContainer menuBox = new() { Name = "Menu" };
    private readonly ScrollContainer scroll = new() { Name = "MenuScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
    private readonly VBoxContainer confirmBox = new() { Name = "Confirm", Visible = false };
    private readonly Label question = new() { Name = "Question", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Button yes = new() { Name = "ConfirmYes" };
    private readonly Button no = new() { Name = "ConfirmNo", Text = "Cancel".Tr() };
    private readonly List<(MenuEntry Entry, Button Button)> buttons = [];
    private UiTheme? theme;
    private MenuEntry? asking;

    public UnitPanelView()
    {
        Name = "UnitPanel";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop; // clicks on the panel must not reach the map behind it
        stats.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        scroll.AddChild(menuBox);
        confirmBox.AddChild(question);
        var answers = new HBoxContainer();
        answers.AddChild(yes);
        answers.AddChild(no);
        confirmBox.AddChild(answers);
        column.AddChild(title);
        column.AddChild(stats);
        column.AddChild(scroll);
        column.AddChild(confirmBox);
        AddChild(column);
        yes.Pressed += () =>
        {
            if (asking is { } entry) Confirmed?.Invoke(entry);
            EndConfirmation();
        };
        no.Pressed += EndConfirmation;
    }

    /// <summary>The entries of the menu as shown, in order.</summary>
    public IReadOnlyList<MenuEntry> Entries => buttons.Select(it => it.Entry).ToList();

    public string TitleText => title.Text;

    public string StatsText => stats.Text;

    public bool IsAsking => confirmBox.Visible;

    public string QuestionText => question.Text;

    public event Action<MenuEntry>? EntryChosen;

    public event Action<MenuEntry>? Confirmed;

    public Button? ButtonFor(string label) => buttons.FirstOrDefault(it => it.Entry.Label == label).Button;

    public void Init(UiTheme uiTheme)
    {
        theme = uiTheme;
        Theme = uiTheme.Theme;
        uiTheme.ScaleChanged += () => LayoutIn(GetViewportRect().Size);
    }

    /// <summary>Shows <paramref name="info"/> and the menu, replacing what was there.</summary>
    public void Show(UnitPanelInfo info, IReadOnlyList<MenuEntry> menu)
    {
        EndConfirmation();
        title.Text = info.Title.TrText();
        var lines = new List<string> { info.Strength, info.Moves, info.Health, info.Xp };
        if (info.Order.Length > 0) lines.Add("Order: " + info.Order);
        if (info.Promotions.Count > 0) lines.Add("Promotions: " + string.Join(", ", info.Promotions));
        stats.Text = string.Join("\n", lines.Where(it => it.Length > 0).Select(it => it.TrText()));
        foreach (var child in menuBox.GetChildren())
        {
            menuBox.RemoveChild(child); // at once: the tests and the layout read the box in the same frame
            child.QueueFree();
        }
        buttons.Clear();
        foreach (var entry in menu)
        {
            var button = new Button { Text = entry.Label.TrText(), Disabled = !entry.Enabled, Alignment = HorizontalAlignment.Left, TooltipText = (entry.Reason ?? "").TrText() };
            button.Pressed += () => EntryChosen?.Invoke(entry);
            menuBox.AddChild(button);
            buttons.Add((entry, button));
        }
        Visible = true;
        LayoutIn(GetViewportRect().Size);
    }

    public void Clear()
    {
        EndConfirmation();
        Visible = false;
    }

    /// <summary>Replaces the menu by a question with a confirm button (named <paramref name="confirmText"/>) and Cancel.</summary>
    public void AskConfirmation(MenuEntry entry, string questionText, string confirmText)
    {
        asking = entry;
        question.Text = questionText.TrText();
        yes.Text = (confirmText.Length > 0 ? confirmText : "Yes").TrText();
        scroll.Visible = false;
        confirmBox.Visible = true;
        LayoutIn(GetViewportRect().Size);
    }

    private void EndConfirmation()
    {
        asking = null;
        confirmBox.Visible = false;
        scroll.Visible = true;
    }

    /// <summary>Places the panel at the bottom left of a screen of <paramref name="screen"/> pixels, above the status line.</summary>
    public void LayoutIn(Vector2 screen)
    {
        float width = theme?.Px(Width) ?? Width;
        float menuHeight = theme?.Px(MenuHeight) ?? MenuHeight;
        scroll.CustomMinimumSize = new Vector2(width, menuHeight);
        question.CustomMinimumSize = new Vector2(width, 0f);
        stats.CustomMinimumSize = new Vector2(width, 0f); // C055: an autowrapping label measured at width 0 is as tall as it has characters
        CustomMinimumSize = new Vector2(width, 0f);
        Size = Vector2.Zero; // shrink to the content
        float statusRoom = 36f;
        Position = new Vector2(Margin, screen.Y - Margin - statusRoom - GetCombinedMinimumSize().Y);
    }
}
