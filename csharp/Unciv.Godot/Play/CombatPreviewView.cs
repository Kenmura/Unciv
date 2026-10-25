using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Ui;
using Unciv.Models.Translations;
using Unciv.GodotClient.Localization;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C012: the battle table: both fighters, the damage ranges and the outcome (<see cref="CombatPreviewText"/>, worded from the engine's preview query), with an
/// Attack button that raises <see cref="ConfirmRequested"/> and a Cancel button. It sits above the unit panel, centred at the bottom of the screen.
/// </summary>
public partial class CombatPreviewView : PanelContainer
{
    private const int Width = 380;

    private readonly VBoxContainer column = new();
    private readonly Label title = new() { Name = "Title" };
    private readonly Label body = new() { Name = "Body", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Button confirm = new() { Name = "Confirm" };
    private readonly Button cancel = new() { Name = "Cancel", Text = "Cancel".Tr() };
    private UiTheme? theme;

    public CombatPreviewView()
    {
        Name = "CombatPreview";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        var row = new HBoxContainer();
        row.AddChild(confirm);
        row.AddChild(cancel);
        column.AddChild(title);
        column.AddChild(body);
        column.AddChild(row);
        AddChild(column);
        confirm.Pressed += () => ConfirmRequested?.Invoke();
        cancel.Pressed += () => CancelRequested?.Invoke();
    }

    public string TitleText => title.Text;

    public string BodyText => body.Text;

    public string ConfirmText => confirm.Text;

    public bool ConfirmEnabled => !confirm.Disabled;

    public event Action? ConfirmRequested;

    public event Action? CancelRequested;

    public void Init(UiTheme uiTheme)
    {
        theme = uiTheme;
        Theme = uiTheme.Theme;
    }

    public void Show(CombatPreviewText text)
    {
        title.Text = text.Title.TrText();
        body.Text = string.Join("\n", text.Lines.Select(it => it.TrText()));
        confirm.Text = text.ConfirmLabel.TrText();
        confirm.Disabled = !text.CanConfirm;
        Visible = true;
        LayoutIn(GetViewportRect().Size);
    }

    public void Clear() => Visible = false;

    public void LayoutIn(Vector2 screen)
    {
        float width = theme?.Px(Width) ?? Width;
        body.CustomMinimumSize = new Vector2(width, 0f);
        Size = Vector2.Zero;
        var size = GetCombinedMinimumSize();
        Position = new Vector2(Math.Max((screen.X - size.X) / 2f, (theme?.Px(250) ?? 250f)), screen.Y - 60f - size.Y); // right of the unit panel
    }
}
