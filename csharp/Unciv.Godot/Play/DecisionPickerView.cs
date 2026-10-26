using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Localization;
using Unciv.GodotClient.Ui;
using Unciv.Utils;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C038: the modal decision picker. It shows one <see cref="PickerSpec"/> (free great person, pantheon, religion, beliefs, spies, World Congress vote, promotion): a column
/// of options per slot, the highlighted option's detail, an optional name field and checkbox, and Confirm / "Decide later". The view decides nothing: which options exist,
/// why Confirm is disabled (<see cref="DecisionPickerModel.Problem"/>) and which commands a confirmed choice sends (<see cref="DecisionPickerModel.Commands"/>) come from the model;
/// the owner submits the commands it is given in <see cref="Confirmed"/>.
/// </summary>
public partial class DecisionPickerView : Control
{
    private readonly ColorRect dim = new() { MouseFilter = MouseFilterEnum.Ignore };
    private readonly PanelContainer panel = new() { Name = "PickerPanel" };
    private readonly Label title = new() { Name = "PickerTitle", HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Label prompt = new() { Name = "PickerPrompt", HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly HBoxContainer columns = new() { Name = "PickerSlots" };
    private readonly Label detail = new() { Name = "PickerDetail", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly LineEdit nameField = new() { Name = "PickerName", Visible = false };
    private readonly CheckBox flagBox = new() { Name = "PickerFlag", Visible = false };
    private readonly Label problem = new() { Name = "PickerProblem", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Button confirm = new() { Name = "PickerConfirm" };
    private readonly Button later = new() { Name = "PickerLater" };
    private readonly Dictionary<(int Slot, string Id), Button> optionButtons = [];
    private string?[] selected = [];
    private bool nameEdited;
    private UiTheme theme = null!; // JUSTIFY: C038 assigned by Init before the picker is opened

    public DecisionPickerView()
    {
        Name = "DecisionPicker";
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
        column.AddChild(prompt);
        column.AddChild(columns);
        column.AddChild(detail);
        column.AddChild(nameField);
        column.AddChild(flagBox);
        column.AddChild(problem);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddChild(confirm);
        row.AddChild(later);
        column.AddChild(row);
        confirm.Pressed += () => Confirmed?.Invoke(Spec.NotNull(), DecisionPickerModel.Commands(Spec.NotNull(), Choice()));
        later.Pressed += () => Dismissed?.Invoke(Spec.NotNull());
        nameField.TextChanged += _ =>
        {
            nameEdited = true;
            Update();
        };
        flagBox.Toggled += _ => Update();
    }

    public PickerSpec? Spec { get; private set; }

    public string TitleText => title.Text;

    public string PromptText => prompt.Text;

    public string DetailText => detail.Text;

    public string ProblemText => problem.Text;

    public string NameText => nameField.Text;

    public Button ConfirmButton => confirm;

    public Button LaterButton => later;

    public CheckBox FlagBox => flagBox;

    public LineEdit NameField => nameField;

    public IReadOnlyList<string?> Selected => selected;

    /// <summary>Raised with the spec and the commands to send, in order.</summary>
    public event Action<PickerSpec, IReadOnlyList<GameCommand>>? Confirmed;

    /// <summary>Raised when "Decide later" / the dismiss button was pressed or Escape typed.</summary>
    public event Action<PickerSpec>? Dismissed;

    public Button? OptionButton(int slot, string id) => optionButtons.GetValueOrDefault((slot, id));

    public void Init(UiTheme uiTheme)
    {
        theme = uiTheme;
        Theme = uiTheme.Theme;
        var shade = uiTheme.Panel.BgColor;
        shade.A = 0.6f;
        dim.Color = shade;
        title.AddThemeFontSizeOverride("font_size", uiTheme.Px(22));
    }

    public void LayoutIn(Vector2 screen) => Size = screen;

    public void Open(PickerSpec spec)
    {
        Spec = spec;
        selected = new string?[spec.Slots.Count];
        nameEdited = false;
        title.Text = spec.Title.TrText();
        prompt.Text = spec.Prompt.TrText();
        confirm.Text = spec.ConfirmLabel.TrText();
        later.Text = spec.DismissLabel.TrText();
        nameField.Visible = spec.TextLabel != null;
        nameField.PlaceholderText = (spec.TextLabel ?? "").TrText();
        nameField.Text = "";
        flagBox.Visible = spec.FlagLabel != null;
        flagBox.Text = (spec.FlagLabel ?? "").TrText();
        flagBox.SetPressedNoSignal(false);
        detail.Text = "";
        detail.CustomMinimumSize = new Vector2(theme.Px(420), theme.Px(48));
        optionButtons.Clear();
        foreach (var child in columns.GetChildren())
        {
            columns.RemoveChild(child);
            child.QueueFree();
        }
        for (int s = 0; s < spec.Slots.Count; s++) columns.AddChild(BuildColumn(s, spec.Slots[s]));
        Visible = true;
        Update();
    }

    public void Close()
    {
        Spec = null;
        Visible = false;
    }

    /// <summary>Chooses an option as pressing its button does; choosing the chosen option of an optional slot clears it.</summary>
    public void Choose(int slot, string id)
    {
        if (Spec == null) return;
        var option = Spec.Slots[slot].Options.FirstOrDefault(it => it.Id == id);
        if (option == null) return;
        selected[slot] = selected[slot] == id && Spec.Slots[slot].Optional ? null : id;
        detail.Text = (option.Detail.Length > 0 ? option.Detail : option.Label).TrText();
        if (Spec.TextFollowsSlot == slot && !nameEdited) nameField.Text = (DecisionPickerModel.DefaultText(Spec, Choice()));
        Update();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (Visible && Spec != null && @event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Dismissed?.Invoke(Spec);
            GetViewport().SetInputAsHandled();
        }
    }

    private PickerChoice Choice() => new(selected, nameField.Text, flagBox.ButtonPressed);

    private Control BuildColumn(int slotIndex, PickerSlot slot)
    {
        var box = new VBoxContainer { Name = "Slot_" + slot.Id };
        box.AddChild(new Label { Text = slot.Title.TrText() });
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(theme.Px(190), theme.Px(160)) };
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(list);
        box.AddChild(scroll);
        foreach (var option in slot.Options)
        {
            var button = new Button
            {
                Name = "Opt_" + slotIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + "_" + option.Id,
                Text = option.Label.TrText(), ToggleMode = true, Disabled = !option.Enabled, Alignment = HorizontalAlignment.Left,
                TooltipText = (option.Reason ?? "").TrText(),
            };
            string id = option.Id;
            button.Pressed += () => Choose(slotIndex, id);
            button.MouseEntered += () => detail.Text = (option.Detail.Length > 0 ? option.Detail : option.Label).TrText();
            list.AddChild(button);
            optionButtons[(slotIndex, id)] = button;
        }
        return box;
    }

    private void Update()
    {
        if (Spec == null) return;
        foreach (var ((slot, id), button) in optionButtons) button.SetPressedNoSignal(selected[slot] == id);
        string? why = DecisionPickerModel.Problem(Spec, Choice());
        problem.Text = (why ?? "").TrText();
        confirm.Disabled = why != null;
    }
}
