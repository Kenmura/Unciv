using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Ui;
using Unciv.GodotClient.Localization;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C044: the in-game menu, a C075 <see cref="Modal"/> over the world: one <see cref="ListRow"/> (Resign: a danger button) per <see cref="GameMenuEntry"/> the model lists. An entry that carries a <see cref="GameMenuConfirm"/>
/// first swaps the list for the question with a confirm and a cancel button; only the confirmed choice raises <see cref="Chosen"/>. The view decides nothing: the owner
/// reacts to the choice (submits Resign, opens the save dialog, leaves for the main menu).
/// </summary>
public partial class GameMenuView : Modal
{
    private static readonly Dictionary<GameMenuKind, string> Icons = new()
    {
        [GameMenuKind.Resume] = "Resume",
        [GameMenuKind.Save] = "Pencil",
        [GameMenuKind.Load] = "Load",
        [GameMenuKind.NewGame] = "New",
        [GameMenuKind.VictoryStatus] = "Score",
        [GameMenuKind.Options] = "Options",
        [GameMenuKind.MainMenu] = "BackArrow",
    };

    private readonly Dictionary<string, Button> byId = [];

    public GameMenuView()
    {
        Name = "GameMenu";
    }

    /// <summary>The entries as shown, empty while the menu is closed.</summary>
    public IReadOnlyList<GameMenuEntry> Entries { get; private set; } = [];

    /// <summary>The question being asked, null while the list shows.</summary>
    public GameMenuConfirm? Asking { get; private set; }

    public string? PanelTitle { get; private set; }

    public Button? ButtonById(string id) => byId.GetValueOrDefault(id);

    public event Action<GameMenuKind>? Chosen;

    public void Open(IReadOnlyList<GameMenuEntry> entries)
    {
        Entries = entries;
        Asking = null;
        Clear();
        PanelTitle = GameMenuModel.Title;
        SetHeader(GameMenuModel.Title);
        Fit(320);
        SetActions([]);
        foreach (var entry in entries)
        {
            if (entry.Kind == GameMenuKind.MainMenu) Body.AddChild(new HSeparator());
            Button button;
            if (entry.Kind == GameMenuKind.Resign)
            {
                button = new Button { Text = entry.Label.TrText(), Disabled = !entry.Enabled, ThemeTypeVariation = UiVariants.ButtonDanger };
                if (entry.DisabledReason != null) button.TooltipText = entry.DisabledReason.TrText();
            }
            else
            {
                string? hint = entry.Kind switch
                {
                    GameMenuKind.Resume => "Esc",
                    GameMenuKind.Save or GameMenuKind.Load => "F10",
                    _ => null,
                };
                var icon = Icons.TryGetValue(entry.Kind, out string? name) ? AssetKey.Of(RulesetAssetKeys.UiIcon, name) : (AssetKey?)null;
                button = new ListRow(Ui, entry.Label, trailing: hint, icon: icon, disabledReason: entry.Enabled ? null : entry.DisabledReason ?? "");
            }
            button.Name = entry.Id;
            var captured = entry;
            button.Pressed += () => Press(captured);
            Body.AddChild(button);
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
        SetHeader(confirm.Title);
        float width = Fit(Modal.MinWidth);
        Body.AddChild(new Label { Name = "ConfirmText", Text = confirm.Body.TrText(), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(width, 0f) });
        var yes = new Button { Name = "ConfirmYes", Text = confirm.ConfirmLabel.TrText() };
        yes.Pressed += () => Chosen?.Invoke(entry.Kind);
        var no = new Button { Name = "ConfirmNo", Text = GameMenuModel.CancelLabel.TrText() };
        no.Pressed += () => Open(Entries);
        SetActions([(yes, entry.Kind == GameMenuKind.Resign ? ChoiceRole.Danger : ChoiceRole.Primary), (no, ChoiceRole.Secondary)]);
        byId["ConfirmYes"] = yes;
        byId["ConfirmNo"] = no;
        no.GrabFocus(); // the safe choice has the focus
    }

    private void Clear()
    {
        foreach (var child in Body.GetChildren())
        {
            Body.RemoveChild(child);
            child.QueueFree();
        }
        byId.Clear();
    }
}
