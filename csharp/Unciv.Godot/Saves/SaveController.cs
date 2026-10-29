using System.Globalization;
using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;
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
    private const float DialogShare = 0.8f;
    private const float CompactDialogShare = 0.94f;
    private const float CompactWidth = 800f;

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
    private Control? dialog;
    private PanelContainer? dialogPanel;
    private ScreenFrame? frame;
    private Button? confirmDelete;
    private DataTable? table;
    private LineEdit? search;
    private ScrollContainer? scroller;
    private EmptyState? empty;
    private Label? detailsTitle;
    private Label? detailsBody;
    private Label? detailsWarning;
    private Label? dialogMessage;
    private string? selectedName;
    private bool compact;
    private IReadOnlyList<SaveEntry> entries = [];
    private IReadOnlyList<SaveEntry> shown = [];
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
    public IReadOnlyList<string> ListedNames => shown.Select(it => it.Name).ToList();

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
        if (dialogPanel != null)
        {
            var panelSize = (Size * (compact ? CompactDialogShare : DialogShare)).Floor();
            dialogPanel.Size = panelSize;
            dialogPanel.Position = ((Size - panelSize) / 2f).Floor();
        }
    }

    /// <summary>Starts watching this session for new turns (autosave) and saves/loads it. Call again with the loaded session.</summary>
    public void Attach(IGameSession gameSession)
    {
        Detach();
        session = gameSession;
        lastTurn = gameSession.Snapshot.Turn;
        progress.Reset(gameSession.Snapshot.Seq);
        gameSession.SnapshotChanged += OnSnapshotChanged;
        // C029: an autosave that fell into a hotseat hand-over (the game cannot be saved then) is made once the next player has taken over
        if (deferredAutosave is { } due && gameSession.Snapshot.Turn >= due)
        {
            deferredAutosave = null;
            Run(AutosaveAsync(due));
        }
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

    private int? deferredAutosave;

    private async Task AutosaveAsync(int turn)
    {
        long seq = session?.Snapshot.Seq ?? 0;
        var text = await ExportAsync(quiet: true);
        if (text == null)
        {
            deferredAutosave = turn;
            return;
        }
        var result = store.Autosave(text, turn, MaxNumberedAutosaves);
        if (result.Ok) progress.MarkSaved(seq);
        Say(result.Ok ? $"Autosaved turn {turn.ToString(CultureInfo.InvariantCulture)}." : "Autosave failed: " + result.Error);
    }

    /// <summary>How many numbered autosaves (one per autosaved turn) are kept besides the latest "Autosave".</summary>
    public const int MaxNumberedAutosaves = 10;

    private async Task<string?> ExportAsync(bool quiet = false)
    {
        if (session == null) return null;
        var text = await session.ExportSaveAsync();
        if (text == null && !quiet) Say("This game cannot be saved from here.");
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
        dialog = new Control { Name = "SaveDialog", MouseFilter = MouseFilterEnum.Stop, Theme = theme.Theme };
        dialog.SetAnchorsPreset(LayoutPreset.FullRect);
        var scrim = new ColorRect { Name = "Scrim", Color = ProceduralAssets.ToColor(UiTokens.Scrim) with { A = UiTokens.ScrimPercent / 100f }, MouseFilter = MouseFilterEnum.Ignore };
        scrim.SetAnchorsPreset(LayoutPreset.FullRect);
        dialog.AddChild(scrim);
        dialogPanel = new PanelContainer { Name = "SaveFrame", ThemeTypeVariation = UiVariants.PanelInset };
        dialog.AddChild(dialogPanel);
        frame = new ScreenFrame(theme, "Saves");
        frame.CloseRequested += CloseDialog;
        dialogPanel.AddChild(frame);

        var content = new HBoxContainer { Name = "SaveContent" };
        content.AddThemeConstantOverride("separation", theme.Px(UiTokens.S3));
        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.5f };
        left.AddThemeConstantOverride("separation", theme.Px(UiTokens.S2));
        search = new LineEdit { Name = "SaveSearch", PlaceholderText = "Search saves".Tr(), ClearButtonEnabled = true };
        search.TextChanged += _ => ApplyFilter();
        left.AddChild(search);
        compact = theme.Breakpoint == UiBreakpoint.Compact || GetViewportRect().Size.X < CompactWidth;
        table = new DataTable(compact
            ? [new DataColumn("Name"), new DataColumn("Turn", true), new DataColumn("Civilization")]
            : [new DataColumn("Name"), new DataColumn("Turn", true), new DataColumn("Civilization"), new DataColumn("Ruleset"), new DataColumn("Date")], selectable: true) { Name = "SaveTable" };
        table.RowTone = row => row < shown.Count && shown[row].Info == null ? ProceduralAssets.ToColor(UiTokens.Warning) : null;
        table.RowSelected += OnRowSelected;
        scroller = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        table.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroller.AddChild(table);
        left.AddChild(scroller);
        empty = new EmptyState(theme, AssetKey.Of(RulesetAssetKeys.UiIcon, "Load"), "No saves yet.") { Name = "SaveEmpty", SizeFlagsVertical = SizeFlags.ExpandFill };
        left.AddChild(empty);
        content.AddChild(left);
        content.AddChild(BuildDetails());
        frame.SetContent(content);

        var resume = new Button { Text = "Resume last".Tr() };
        resume.Pressed += () => Run(ResumeLastAsync());
        var delete = new Button { Text = "Delete".Tr() };
        delete.Pressed += ArmDelete;
        confirmDelete = new Button { Text = "Confirm delete".Tr(), Visible = false };
        confirmDelete.Pressed += () => { if (pendingDelete is { } name) Delete(name); };
        var load = new Button { Text = "Load".Tr() };
        load.Pressed += () => { if (Selected is { } name) Run(LoadAsync(name)); else Say("Select a save first."); };
        var save = new Button { Text = "Save".Tr() };
        save.Pressed += () => Run(SaveAsync(NameField?.Text ?? ""));
        frame.SetFooter([(resume, ChoiceRole.Ghost), (delete, ChoiceRole.Danger), (confirmDelete, ChoiceRole.Danger), (load, ChoiceRole.Secondary), (save, ChoiceRole.Primary)]);
        if (NameField != null) NameField.Text = $"Game-{(session?.Snapshot.Turn ?? 0).ToString(CultureInfo.InvariantCulture)}";
        AddChild(dialog);
        RefreshList();
        Layout();
    }

    private Control BuildDetails()
    {
        var card = new PanelContainer { Name = "SaveDetails", ThemeTypeVariation = UiVariants.PanelCard, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1f };
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", theme.Px(UiTokens.S2));
        detailsTitle = new Label { Name = "SaveDetailsTitle", ThemeTypeVariation = UiVariants.LabelHeading, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        detailsBody = new Label { Name = "SaveDetailsBody", ThemeTypeVariation = UiVariants.LabelSecondary, AutowrapMode = TextServer.AutowrapMode.WordSmart, MaxLinesVisible = 6, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, CustomMinimumSize = new Vector2(theme.Px(96), 0) };
        detailsWarning = new Label { Name = "SaveDetailsWarning", AutowrapMode = TextServer.AutowrapMode.WordSmart, MaxLinesVisible = 4, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, CustomMinimumSize = new Vector2(theme.Px(96), 0), Visible = false };
        detailsWarning.AddThemeColorOverride("font_color", ProceduralAssets.ToColor(UiTokens.Warning));
        column.AddChild(detailsTitle);
        column.AddChild(detailsBody);
        column.AddChild(detailsWarning);
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, theme.Px(UiTokens.S2)) });
        column.AddChild(new Label { Text = "Save name".Tr(), ThemeTypeVariation = UiVariants.LabelOverline });
        NameField = new LineEdit { Name = "SaveName", PlaceholderText = "Save name".Tr() };
        column.AddChild(NameField);
        dialogMessage = new Label { Name = "SaveDialogMessage", ThemeTypeVariation = UiVariants.LabelSecondary, AutowrapMode = TextServer.AutowrapMode.WordSmart, MaxLinesVisible = 4, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, CustomMinimumSize = new Vector2(theme.Px(96), 0) };
        column.AddChild(dialogMessage);
        var scrolled = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; // the card never makes the dialog taller than the window
        column.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scrolled.AddChild(column);
        card.AddChild(scrolled);
        ShowDetails(null);
        return card;
    }

    public void CloseDialog()
    {
        dialog?.QueueFree();
        dialog = null;
        dialogPanel = null;
        frame = null;
        table = null;
        search = null;
        scroller = null;
        empty = null;
        detailsTitle = detailsBody = detailsWarning = dialogMessage = null;
        confirmDelete = null;
        pendingDelete = null;
        selectedName = null;
        NameField = null;
        entries = [];
        shown = [];
    }

    private string? Selected => selectedName != null && shown.Any(it => it.Name == selectedName) ? selectedName : null;

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
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (table == null || search == null) return;
        string query = search.Text.Trim();
        shown = entries.Where(it => query.Length == 0 || it.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        table.SetRows(shown.Select(Row).ToList());
        int index = shown.ToList().FindIndex(it => it.Name == selectedName);
        if (index < 0) selectedName = null;
        table.Select(index, notify: false);
        if (frame != null) frame.TitleLabel.Text = $"{"Saves".Tr()} ({entries.Count.ToString(CultureInfo.InvariantCulture)})";
        scroller!.Visible = shown.Count > 0;
        empty!.Visible = shown.Count == 0;
        empty.SetMessage(entries.Count == 0 ? "No saves yet." : "No save matches the search.");
        ShowDetails(index < 0 ? null : shown[index]);
    }

    private string[] Row(SaveEntry entry)
    {
        if (entry.Info is not { } info) return compact ? [entry.Name, "-", "Damaged".Tr()] : [entry.Name, "-", "Damaged".Tr(), "-", ShortDate(entry)];
        string turn = info.Turn.ToString(CultureInfo.InvariantCulture);
        return compact ? [entry.Name, turn, info.Civ] : [entry.Name, turn, info.Civ, info.Ruleset, ShortDate(entry)];
    }

    private static string ShortDate(SaveEntry entry) => entry.ModifiedUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private void OnRowSelected(int row)
    {
        pendingDelete = null;
        if (confirmDelete != null) confirmDelete.Visible = false;
        if (row < 0 || row >= shown.Count) return;
        var entry = shown[row];
        selectedName = entry.Name;
        if (NameField != null && entry.Info != null) NameField.Text = entry.Name;
        ShowDetails(entry);
    }

    private void ShowDetails(SaveEntry? entry)
    {
        if (detailsTitle == null || detailsBody == null || detailsWarning == null) return;
        detailsTitle.Text = entry?.Name ?? "Select a save".Tr();
        detailsWarning.Visible = entry != null && entry.Info == null;
        detailsWarning.Text = entry?.Problem is { } problem ? $"{"Damaged".Tr()}: {problem}" : "";
        if (entry?.Info is not { } info)
        {
            detailsBody.Text = "";
            return;
        }
        string mods = info.Mods.Count == 0 ? "" : "\n" + string.Join(", ", info.Mods);
        string when = entry.ModifiedUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";
        detailsBody.Text = $"{"Turn".Tr()} {info.Turn.ToString(CultureInfo.InvariantCulture)}\n{info.Civ}\n{info.Ruleset}{mods}\n{when}";
    }

    private void Say(string text)
    {
        messageLabel.Text = text.TrText();
        if (dialogMessage != null) dialogMessage.Text = messageLabel.Text;
    }

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
