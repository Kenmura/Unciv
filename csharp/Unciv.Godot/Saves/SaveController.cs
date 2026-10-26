using System.Globalization;
using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Play;
using Unciv.GodotClient.Settings;
using Unciv.GodotClient.Ui;
using Unciv.Net.Visibility;
using Unciv.Models.Translations;
using Unciv.GodotClient.Localization;

namespace Unciv.GodotClient.Saves;

/// <summary>
/// C016: save and load for the running game. A "Saves" button opens a dialog with the list of saves (name, turn, civ, ruleset and mods, date), Save (under the typed
/// name), Load, Delete (asks again), Resume last and Close; F5 quick saves, F9 quick loads, F10 opens the dialog; an autosave is written whenever a new turn begins
/// that the settings' autosave interval selects. The game is read and replaced only through <see cref="IGameSession"/>: saving is <see cref="IGameSession.ExportSaveAsync"/>,
/// loading hands the file text to the composition root's <c>loader</c> and then to <c>onLoaded</c>, which shows the new session (the screen's Start). All file work is in
/// <see cref="SaveStore"/>; every failure ends as a message on screen, never an exception or a changed game.
/// </summary>
public partial class SaveController : Control
{
    private const float Margin = 12f;
    private static readonly Vector2 DialogSize = new(560, 380);

    private readonly SaveStore store;
    private readonly ClientSettingsService settings;
    private readonly UiTheme theme;
    private readonly Func<string, IGameSession> loader;
    private readonly Action<IGameSession> onLoaded;
    private readonly Label messageLabel = new() { Name = "SaveMessage" };
    private readonly Button openButton = new() { Text = "Saves".Tr(), Name = "SavesButton" };
    private IGameSession? session;
    private int lastTurn = -1;
    private string? pendingDelete;
    private PanelContainer? dialog;
    private Button? confirmDelete;
    private ListPanel? list;
    private IReadOnlyList<SaveEntry> entries = [];
    private readonly SaveProgress progress = new();

    public SaveController(SaveStore store, ClientSettingsService settings, UiTheme theme, Func<string, IGameSession> loader, Action<IGameSession> onLoaded)
    {
        this.store = store;
        this.settings = settings;
        this.theme = theme;
        this.loader = loader;
        this.onLoaded = onLoaded;
        Name = "SaveController";
        MouseFilter = MouseFilterEnum.Ignore;
        AddChild(messageLabel);
        AddChild(openButton);
        openButton.Pressed += ToggleDialog;
    }

    /// <summary>Always set after an action, success or failure.</summary>
    public string Message => messageLabel.Text;

    /// <summary>Whether the corner "Saves" button shows (the main menu has its own buttons for the same dialog).</summary>
    public bool ButtonVisible
    {
        get => openButton.Visible;
        set => openButton.Visible = value;
    }

    public bool IsDialogOpen => dialog != null;

    /// <summary>C044: whether the attached game has accepted commands since the last save, autosave or load. The in-game menu asks before it throws such progress away.</summary>
    public bool HasUnsavedProgress => session != null && progress.IsUnsaved(session.Snapshot.Seq);

    public LineEdit? NameField { get; private set; }

    /// <summary>The names shown in the open dialog, in list order.</summary>
    public IReadOnlyList<string> ListedNames => entries.Select(it => it.Name).ToList();

    public override void _Ready()
    {
        GetViewport().SizeChanged += Layout;
        Layout();
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= Layout;
        Detach();
    }

    private void Layout()
    {
        Size = GetViewportRect().Size;
        openButton.Position = new Vector2(Size.X - openButton.Size.X - Margin, Size.Y - openButton.Size.Y - Margin);
        messageLabel.Position = new Vector2(Margin, Size.Y - Margin - 56f);
        if (dialog != null) dialog.Position = (Size - DialogSize) / 2f;
    }

    /// <summary>Starts watching this session for new turns (autosave) and saves/loads it. Call again with the loaded session.</summary>
    public void Attach(IGameSession gameSession)
    {
        Detach();
        session = gameSession;
        lastTurn = gameSession.Snapshot.Turn;
        progress.Reset(gameSession.Snapshot.Seq);
        gameSession.SnapshotChanged += OnSnapshotChanged;
    }

    private void Detach()
    {
        if (session != null) session.SnapshotChanged -= OnSnapshotChanged;
        session = null;
    }

    private void OnSnapshotChanged(ClientSnapshot snapshot)
    {
        if (snapshot.Turn <= lastTurn) return;
        lastTurn = snapshot.Turn;
        if (SaveStore.AutosaveDue(snapshot.Turn, settings.Current.AutosaveTurns)) Run(AutosaveAsync(snapshot.Turn));
    }

    private async Task AutosaveAsync(int turn)
    {
        long seq = session?.Snapshot.Seq ?? 0;
        var text = await ExportAsync();
        if (text == null) return;
        var result = store.Autosave(text, turn, MaxNumberedAutosaves);
        if (result.Ok) progress.MarkSaved(seq);
        Say(result.Ok ? $"Autosaved turn {turn.ToString(CultureInfo.InvariantCulture)}." : "Autosave failed: " + result.Error);
    }

    /// <summary>How many numbered autosaves (one per autosaved turn) are kept besides the latest "Autosave".</summary>
    public const int MaxNumberedAutosaves = 10;

    private async Task<string?> ExportAsync()
    {
        if (session == null) return null;
        var text = await session.ExportSaveAsync();
        if (text == null) Say("This game cannot be saved from here.");
        return text;
    }

    public async Task<bool> SaveAsync(string name)
    {
        if (SaveStore.ValidateName(name) is { } invalid)
        {
            Say(invalid);
            return false;
        }
        long seq = session?.Snapshot.Seq ?? 0;
        var text = await ExportAsync();
        if (text == null) return false;
        var result = store.Write(name, text);
        if (result.Ok) progress.MarkSaved(seq);
        Say(result.Ok ? $"Saved \"{name}\"." : "Save failed: " + result.Error);
        RefreshList();
        return result.Ok;
    }

    public async Task<bool> LoadAsync(string name)
    {
        var read = store.TryRead(name, out string text);
        if (!read.Ok)
        {
            Say($"Cannot load \"{name}\": {read.Error}");
            return false;
        }
        IGameSession loaded;
        try
        {
            loaded = loader(text);
        }
        catch (InvalidDataException e)
        {
            Say($"Cannot load \"{name}\": {e.Message}");
            return false;
        }
        await loaded.ResyncAsync(); // the snapshot the screen is about to draw is current
        Attach(loaded);
        onLoaded(loaded);
        Say($"Loaded \"{name}\" (turn {loaded.Snapshot.Turn.ToString(CultureInfo.InvariantCulture)}).");
        CloseDialog();
        return true;
    }

    public Task<bool> QuickSaveAsync() => SaveAsync(SaveStore.QuickSaveName);

    public Task<bool> QuickLoadAsync() => LoadAsync(SaveStore.QuickSaveName);

    /// <summary>Loads the newest readable save (autosaves count).</summary>
    public Task<bool> ResumeLastAsync()
    {
        var latest = store.Latest();
        if (latest == null)
        {
            Say("There is no save to resume.");
            return Task.FromResult(false);
        }
        return LoadAsync(latest.Name);
    }

    public void Delete(string name)
    {
        var result = store.Delete(name);
        Say(result.Ok ? $"Deleted \"{name}\"." : "Delete failed: " + result.Error);
        pendingDelete = null;
        RefreshList();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
        switch (key.Keycode)
        {
            case Key.F5:
                Run(QuickSaveAsync());
                break;
            case Key.F9:
                Run(QuickLoadAsync());
                break;
            case Key.F10:
                ToggleDialog();
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }

    // Dialog

    private void ToggleDialog()
    {
        if (dialog != null) CloseDialog();
        else OpenDialog();
    }

    public void OpenDialog()
    {
        if (dialog != null) return;
        dialog = new PanelContainer { Name = "SaveDialog", CustomMinimumSize = DialogSize, Size = DialogSize };
        var column = new VBoxContainer();
        dialog.AddChild(column);
        NameField = new LineEdit { PlaceholderText = "Save name".Tr(), Text = $"Game-{(session?.Snapshot.Turn ?? 0).ToString(CultureInfo.InvariantCulture)}" };
        column.AddChild(NameField);
        var buttons = new HBoxContainer();
        column.AddChild(buttons);
        AddButton(buttons, "Save", () => Run(SaveAsync(NameField.Text)));
        AddButton(buttons, "Load", () => { if (Selected is { } name) Run(LoadAsync(name)); else Say("Select a save first."); });
        AddButton(buttons, "Delete", ArmDelete);
        confirmDelete = AddButton(buttons, "Confirm delete", () => { if (pendingDelete is { } name) Delete(name); });
        confirmDelete.Visible = false;
        AddButton(buttons, "Resume last", () => Run(ResumeLastAsync()));
        AddButton(buttons, "Close", CloseDialog);
        list = new ListPanel(theme, "Saves", new Vector2(DialogSize.X - 24f, 240f));
        column.AddChild(list);
        AddChild(dialog);
        RefreshList();
        Layout();
    }

    public void CloseDialog()
    {
        dialog?.QueueFree();
        dialog = null;
        list = null;
        confirmDelete = null;
        pendingDelete = null;
        NameField = null;
        entries = [];
    }

    private Button AddButton(Container parent, string text, Action pressed)
    {
        var button = new Button { Text = text.TrText() };
        button.Pressed += pressed;
        parent.AddChild(button);
        return button;
    }

    private string? Selected => list != null && list.SelectedIndex is >= 0 and var i && i < entries.Count ? entries[i].Name : null;

    private void ArmDelete()
    {
        if (Selected is not { } name)
        {
            Say("Select a save first.");
            return;
        }
        pendingDelete = name;
        if (confirmDelete != null) confirmDelete.Visible = true;
        Say($"Press Confirm delete to delete \"{name}\".");
    }

    /// <summary>Re-reads the save folder into the open dialog (also after something outside changed the folder).</summary>
    public void RefreshList()
    {
        if (dialog == null) return;
        entries = store.List();
        var fresh = new ListPanel(theme, $"Saves ({entries.Count.ToString(CultureInfo.InvariantCulture)})", new Vector2(DialogSize.X - 24f, 240f));
        foreach (var entry in entries) fresh.AddItem(Describe(entry));
        fresh.Selected += index =>
        {
            pendingDelete = null;
            if (confirmDelete != null) confirmDelete.Visible = false;
            if (NameField != null && index < entries.Count && entries[index].Info != null) NameField.Text = entries[index].Name;
        };
        if (list != null)
        {
            var parent = list.GetParent();
            parent.AddChild(fresh);
            parent.RemoveChild(list);
            list.QueueFree();
        }
        list = fresh;
    }

    /// <summary>One list row: name, then turn, civ, ruleset with mods and the file date, or the reason the file is not a save.</summary>
    public static string Describe(SaveEntry entry)
    {
        if (entry.Info is not { } info) return $"{entry.Name}   (unreadable: {entry.Problem})";
        string mods = info.Mods.Count == 0 ? "" : " + " + string.Join(", ", info.Mods);
        string when = entry.ModifiedUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";
        return $"{entry.Name}   turn {info.Turn.ToString(CultureInfo.InvariantCulture)}   {info.Civ}   {info.Ruleset}{mods}   {when}";
    }

    private void Say(string text) => messageLabel.Text = text.TrText();

    private async void Run(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception e)
        {
            GD.PushError("Save action failed: " + e);
            Say("Save action failed: " + e.Message);
        }
    }
}
