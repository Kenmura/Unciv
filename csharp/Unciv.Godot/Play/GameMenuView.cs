using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Ui;
using Unciv.GodotClient.Localization;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C044: the in-game menu, a modal over the world: one button per <see cref="GameMenuEntry"/> the model lists. An entry that carries a <see cref="GameMenuConfirm"/>
/// first swaps the list for the question with a confirm and a cancel button; only the confirmed choice raises <see cref="Chosen"/>. The view decides nothing: the owner
/// reacts to the choice (submits Resign, opens the save dialog, leaves for the main menu).
/// </summary>
public partial class GameMenuView : Control
{
    private readonly ColorRect dim = new() { MouseFilter = MouseFilterEnum.Ignore };
    private readonly PanelContainer panel = new() { Name = "MenuPanel" };
    private readonly VBoxContainer column = new() { Name = "MenuColumn" };
    private readonly Dictionary<string, Button> byId = [];
    private UiTheme? theme;

    public GameMenuView()
    {
        Name = "GameMenu";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        dim.Fill();
        AddChild(dim);
        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        centre.Fill();
        AddChild(centre);
        centre.AddChild(panel);
        panel.AddChild(column);
    }

    /// <summary>The entries as shown, empty while the menu is closed.</summary>
    public IReadOnlyList<GameMenuEntry> Entries { get; private set; } = [];

    /// <summary>The question being asked, null while the list shows.</summary>
    public GameMenuConfirm? Asking { get; private set; }

    public string? PanelTitle { get; private set; }

    public Button? ButtonById(string id) => byId.GetValueOrDefault(id);

    public event Action<GameMenuKind>? Chosen;

    public void Init(UiTheme uiTheme)
    {
        theme = uiTheme;
        Theme = uiTheme.Theme;
        var shade = uiTheme.Panel.BgColor;
        shade.A = 0.75f;
        dim.Color = shade;
        panel.CustomMinimumSize = new Vector2(uiTheme.Px(260), 0f);
    }

    public void LayoutIn(Vector2 screen) => Size = screen;

    public void Open(IReadOnlyList<GameMenuEntry> entries)
    {
        Entries = entries;
        Asking = null;
        Clear();
        PanelTitle = GameMenuModel.Title;
        column.AddChild(Heading(GameMenuModel.Title));
        foreach (var entry in entries)
        {
            var button = new Button { Name = entry.Id, Text = entry.Label.TrText(), Disabled = !entry.Enabled };
            if (entry.DisabledReason != null) button.TooltipText = entry.DisabledReason.TrText();
            var captured = entry;
            button.Pressed += () => Press(captured);
            column.AddChild(button);
            byId[entry.Id] = button;
        }
        Visible = true;
    }

    public void Close()
    {
        Entries = [];
        Asking = null;
        Visible = false;
        Clear();
    }

    /// <summary>Escape: back from a question to the list, else close the menu. False when the menu is not open.</summary>
    public bool Cancel()
    {
        if (!Visible) return false;
        if (Asking != null) Open(Entries);
        else Close();
        return true;
    }

    private void Press(GameMenuEntry entry)
    {
        if (!entry.Enabled) return;
        if (entry.Confirm is { } confirm)
        {
            Ask(entry, confirm);
            return;
        }
        Chosen?.Invoke(entry.Kind);
    }

    private void Ask(GameMenuEntry entry, GameMenuConfirm confirm)
    {
        Asking = confirm;
        Clear();
        PanelTitle = confirm.Title;
        column.AddChild(Heading(confirm.Title));
        column.AddChild(new Label { Name = "ConfirmText", Text = confirm.Body.TrText(), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(theme?.Px(260) ?? 260f, 0f) });
        var yes = new Button { Name = "ConfirmYes", Text = confirm.ConfirmLabel.TrText() };
        yes.Pressed += () => Chosen?.Invoke(entry.Kind);
        var no = new Button { Name = "ConfirmNo", Text = GameMenuModel.CancelLabel.TrText() };
        no.Pressed += () => Open(Entries);
        column.AddChild(yes);
        column.AddChild(no);
        byId["ConfirmYes"] = yes;
        byId["ConfirmNo"] = no;
    }

    private Label Heading(string text) => new() { Name = "MenuTitle", Text = text.TrText(), HorizontalAlignment = HorizontalAlignment.Center };

    private void Clear()
    {
        foreach (var child in column.GetChildren())
        {
            column.RemoveChild(child);
            child.QueueFree();
        }
        byId.Clear();
    }
}
