using Godot;
using Unciv.Assets.Localization;
using Unciv.GodotClient.Assets;
using Unciv.Models.Translations;

namespace Unciv.GodotClient.Settings;

/// <summary>
/// C028: the options screen. The C043 <see cref="SettingsPanel"/> (language, engine options, UI scale) followed by sound volume, window mode, scroll speed, edge-pan,
/// the map icon toggles, asset pack selection and key rebinding. Every control applies and saves through <see cref="ClientSettingsService"/> the moment it changes
/// (the App applies volume, window mode and UI scale to the window, the game screen follows the rest). A rebound key is checked by <see cref="Hotkeys"/>: an unusable
/// key is refused, a key another action holds is swapped.
/// </summary>
public partial class OptionsScreen : ScrollContainer
{
    private static readonly (HotkeyAction Action, string Label)[] ActionLabels =
    [
        (HotkeyAction.EndTurn, "End turn"),
        (HotkeyAction.NextUnit, "Next unit"),
        (HotkeyAction.OpenTech, "Technology"),
        (HotkeyAction.OpenPolicies, "Policies"),
        (HotkeyAction.OpenDiplomacy, "Diplomacy"),
        (HotkeyAction.PanUp, "Pan up"),
        (HotkeyAction.PanDown, "Pan down"),
        (HotkeyAction.PanLeft, "Pan left"),
        (HotkeyAction.PanRight, "Pan right"),
    ];

    private readonly ClientSettingsService service;
    private readonly VBoxContainer column = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly Dictionary<HotkeyAction, Button> keyButtons = [];
    private readonly Label messageLabel = new() { Name = "OptionsMessage" };
    private readonly List<Action> refreshers = [];
    private readonly HSlider volume = new() { Name = "VolumeSlider", MinValue = 0, MaxValue = 1, Step = 0.05, CustomMinimumSize = new Vector2(160, 0) };
    private readonly HSlider scroll = new() { Name = "ScrollSpeedSlider", MinValue = OptionsModel.MinScrollSpeed, MaxValue = OptionsModel.MaxScrollSpeed, Step = 0.25, CustomMinimumSize = new Vector2(160, 0) };
    private readonly OptionButton windowMode = new() { Name = "WindowModePicker" };
    private readonly OptionButton packPicker = new() { Name = "AssetPackPicker" };
    private IReadOnlyList<string> packNames = [];
    private bool refreshing;

    public OptionsScreen(ClientSettingsService service, ClientLocalization? localization = null, IEnumerable<string>? packDirectories = null)
    {
        this.service = service;
        Name = "OptionsScreen";
        HorizontalScrollMode = ScrollMode.Disabled;
        AddChild(column);
        column.AddChild(new SettingsPanel(service, localization));

        AddSlider("Sound volume", volume, (s, v) => s with { SoundVolume = v }, s => s.SoundVolume);
        foreach (var mode in OptionsModel.WindowModes) windowMode.AddItem(mode.Tr());
        windowMode.ItemSelected += index => service.Update(s => s with { WindowMode = OptionsModel.WindowModes[(int)index] });
        AddRow("Window mode", windowMode);
        AddSlider("Scroll speed", scroll, (s, v) => s with { ScrollSpeed = v }, s => s.ScrollSpeed);
        AddToggle("Scroll the map at the window edge", s => s.EdgePan, (s, v) => s with { EdgePan = v });
        AddToggle("Show resources on the map", s => s.ShowResources, (s, v) => s with { ShowResources = v });
        AddToggle("Show tile yields", s => s.ShowYields, (s, v) => s with { ShowYields = v });

        packNames = OptionsModel.PackNames(packDirectories ?? GodotAssetFiles.ListDirectories("assets/packs"));
        foreach (var pack in packNames) packPicker.AddItem(pack);
        packPicker.ItemSelected += index => service.Update(s => s with { AssetPack = packNames[(int)index] });
        AddRow("Asset pack (applies to the next game)", packPicker);

        column.AddChild(Heading("Key bindings"));
        foreach (var (action, label) in ActionLabels)
        {
            var button = new Button { Name = "Key_" + action, CustomMinimumSize = new Vector2(120, 0) };
            button.Pressed += () => BeginRebind(action);
            keyButtons[action] = button;
            AddRow(label, button);
        }
        var reset = new Button { Name = "ResetKeys" };
        refreshers.Add(() => reset.Text = "Reset keys".Tr());
        reset.Pressed += ResetKeys;
        column.AddChild(reset);
        column.AddChild(messageLabel);

        service.Changed += _ => Refresh();
        Refresh();
    }

    /// <summary>The action waiting for its new key, or null.</summary>
    public HotkeyAction? Listening { get; private set; }

    public string Message => messageLabel.Text;

    public Button KeyButton(HotkeyAction action) => keyButtons[action];

    public HSlider VolumeSlider => volume;

    public HSlider ScrollSlider => scroll;

    public OptionButton WindowModePicker => windowMode;

    public OptionButton PackPicker => packPicker;

    /// <summary>The next key press (not Escape, which cancels) becomes the key of <paramref name="action"/>.</summary>
    public void BeginRebind(HotkeyAction action)
    {
        Listening = action;
        messageLabel.Text = "Press the new key, or Escape to cancel".Tr();
        Refresh();
    }

    /// <summary>Binds the listening action to a key text; false when nothing was listening or the key is not usable (the old binding stays).</summary>
    public bool TryBind(string? keyText)
    {
        if (Listening is not { } action) return false;
        var bound = Hotkeys.Rebind(service.Current.KeyBindings, action, keyText, out var swapped);
        if (bound == null)
        {
            messageLabel.Text = "That key cannot be used".Tr();
            return false;
        }
        Listening = null;
        service.Update(s => s with { KeyBindings = bound });
        messageLabel.Text = swapped is { } other
            ? "{0} and {1} swapped keys".Tr().Replace("{0}", LabelOf(action).Tr(), StringComparison.Ordinal).Replace("{1}", LabelOf(other).Tr(), StringComparison.Ordinal)
            : "";
        return true;
    }

    public void ResetKeys()
    {
        Listening = null;
        messageLabel.Text = "";
        service.Update(s => s with { KeyBindings = Hotkeys.ResetAll() });
    }

    public override void _Input(InputEvent @event)
    {
        if (Listening == null || @event is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.Keycode == Key.Escape)
        {
            Listening = null;
            messageLabel.Text = "";
            Refresh();
        }
        else if (HotkeyInput.TextOf(key) is { } text) TryBind(text);
        else if (key.Keycode is Key.Ctrl or Key.Shift or Key.Alt or Key.Meta) return; // a modifier alone: wait for the real key
        else messageLabel.Text = "That key cannot be used".Tr();
        GetViewport().SetInputAsHandled();
    }

    private static string LabelOf(HotkeyAction action) => ActionLabels.First(it => it.Action == action).Label;

    private Label Heading(string key)
    {
        var label = new Label();
        refreshers.Add(() => label.Text = key.Tr());
        return label;
    }

    private void AddRow(string key, Control control)
    {
        var row = new HBoxContainer();
        var label = new Label { CustomMinimumSize = new Vector2(190, 0) };
        refreshers.Add(() => label.Text = key.Tr());
        row.AddChild(label);
        row.AddChild(control);
        column.AddChild(row);
    }

    private void AddSlider(string key, HSlider slider, Func<ClientSettings, float, ClientSettings> write, Func<ClientSettings, float> read)
    {
        slider.ValueChanged += value =>
        {
            if (!refreshing) service.Update(s => write(s, (float)value));
        };
        refreshers.Add(() => slider.Value = read(service.Current));
        AddRow(key, slider);
    }

    private void AddToggle(string key, Func<ClientSettings, bool> read, Func<ClientSettings, bool, ClientSettings> write)
    {
        var box = new CheckBox();
        box.Toggled += on =>
        {
            if (!refreshing) service.Update(s => write(s, on));
        };
        refreshers.Add(() =>
        {
            box.Text = key.Tr();
            box.ButtonPressed = read(service.Current);
        });
        column.AddChild(box);
    }

    private static int IndexOf(IReadOnlyList<string> list, string value)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == value) return i;
        }
        return -1;
    }

    private void Refresh()
    {
        refreshing = true;
        foreach (var refresh in refreshers) refresh();
        var s = service.Current;
        windowMode.Select(Math.Max(0, IndexOf(OptionsModel.WindowModes, s.WindowMode)));
        packPicker.Select(Math.Max(0, IndexOf(packNames, s.AssetPack)));
        foreach (var (action, button) in keyButtons)
            button.Text = Listening == action ? "..." : Hotkeys.KeyFor(s.KeyBindings, action);
        refreshing = false;
    }
}
