using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Ui;
using Unciv.Queries.Empire;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C019: the tech screen. The engine's tech tree (<see cref="TechTreeQuery"/>) laid out by column and row by <see cref="TechScreenModel"/>, scrollable, each node coloured
/// by its state (researched, queued, researchable, locked) with prerequisite links; choosing a node shows its unlocks and the buttons that research it now, add it to the
/// queue or take it out. Everything shown is the engine's answer and the only change is a command sent through the <see cref="IGameSession"/>.
/// </summary>
public partial class TechScreen : PanelContainer
{
    private const int NodeWidth = 132;
    private const int NodeHeight = 46;
    private const int GapX = 36;
    private const int GapY = 10;
    private const int HeadingHeight = 22;

    private readonly IGameSession session;
    private readonly GameQueries queries;
    private readonly UiTheme theme;
    private readonly Label message = new() { Name = "Message" };
    private readonly Label header = new() { Name = "Header" };
    private readonly ScrollContainer scroll = new() { Name = "TreeScroll", SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly EdgeCanvas canvas = new() { Name = "TreeCanvas" };
    private readonly Label title = new() { Name = "Title" };
    private readonly Label state = new() { Name = "State", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label unlocks = new() { Name = "Unlocks", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly VBoxContainer actions = new() { Name = "Actions" };
    private readonly Label queueLabel = new() { Name = "Queue", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Dictionary<string, Button> nodeButtons = [];
    private readonly Dictionary<string, Button> actionButtons = [];
    private IReadOnlyList<TechAction> current = [];
    private int generation;

    public TechScreen(IGameSession gameSession, UiTheme uiTheme)
    {
        session = gameSession;
        queries = new GameQueries(gameSession);
        theme = uiTheme;
        Name = "TechPanel";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        Theme = uiTheme.Theme;

        var column = new VBoxContainer();
        AddChild(column);
        var head = new HBoxContainer();
        column.AddChild(head);
        var heading = new Label { Text = "Technology" };
        heading.AddThemeFontSizeOverride("font_size", uiTheme.Px(20));
        head.AddChild(heading);
        header.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.HorizontalAlignment = HorizontalAlignment.Center;
        header.ClipText = true;
        head.AddChild(header);
        message.ClipText = true;
        message.CustomMinimumSize = new Vector2(uiTheme.Px(60), 0f);
        head.AddChild(message);
        var close = new Button { Name = "TechClose", Text = "Close technology" };
        close.Pressed += Close;
        head.AddChild(close);

        var middle = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        column.AddChild(middle);
        scroll.AddChild(canvas);
        middle.AddChild(scroll);
        var side = new VBoxContainer { CustomMinimumSize = new Vector2(uiTheme.Px(190), 0f) };
        middle.AddChild(side);
        title.AddThemeFontSizeOverride("font_size", uiTheme.Px(18));
        side.AddChild(title);
        side.AddChild(state);
        actions.AddThemeConstantOverride("separation", 2);
        side.AddChild(actions);
        side.AddChild(queueLabel);
        var unlockScroll = new ScrollContainer { Name = "UnlockScroll", SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        unlocks.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        unlockScroll.AddChild(unlocks);
        side.AddChild(unlockScroll);
    }

    public TechTreeResult? Tree { get; private set; }

    public TechLayout Layout { get; private set; } = new([], [], 0, 0, []);

    public string? SelectedTech { get; private set; }

    public IReadOnlyList<TechAction> Buttons => current;

    public string HeaderText => header.Text;

    public string TitleText => title.Text;

    public string StateText => state.Text;

    public string UnlockText => unlocks.Text;

    public string QueueText => queueLabel.Text;

    public string MessageText => message.Text;

    /// <summary>Raised after the engine accepted a command made here, so the owner refreshes the world.</summary>
    public event Action? CommandAccepted;

    public event Action? Closed;

    public Button? NodeButton(string tech) => nodeButtons.GetValueOrDefault(tech);

    public Button? ActionButton(string id) => actionButtons.GetValueOrDefault(id);

    /// <summary>Shows the screen with the engine's current tree, choosing <paramref name="tech"/> (default: the current research, else the first researchable tech).</summary>
    public async Task OpenAsync(string? tech = null)
    {
        Visible = true;
        await ReloadAsync(tech ?? SelectedTech);
    }

    public void Close()
    {
        generation++;
        Visible = false;
        Closed?.Invoke();
    }

    /// <summary>Chooses a node of the tree and shows its unlocks and buttons.</summary>
    public void Select(string? tech)
    {
        SelectedTech = tech;
        foreach (var (name, button) in nodeButtons) Style(button, Layout.Nodes.First(it => it.Name == name), name == tech);
        Fill();
    }

    /// <summary>Scrolls the tree so the node is inside the visible part (a click needs it there).</summary>
    public void EnsureVisible(string tech)
    {
        if (nodeButtons.TryGetValue(tech, out var node)) scroll.EnsureControlVisible(node);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (Visible && @event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    private async Task ReloadAsync(string? choose)
    {
        int ticket = ++generation;
        var tree = await queries.TechTree();
        if (ticket != generation) return;
        if (tree == null)
        {
            Tree = null;
            header.Text = "The engine would not show the tree.";
            return;
        }
        Tree = tree;
        Layout = TechScreenModel.Layout(tree);
        header.Text = TechScreenModel.Header(tree);
        BuildTree();
        string? pick = choose != null && Layout.Nodes.Any(it => it.Name == choose) ? choose
            : tree.CurrentTech != null && Layout.Nodes.Any(it => it.Name == tree.CurrentTech) ? tree.CurrentTech
            : Layout.Nodes.FirstOrDefault(it => it.State == TechScreenModel.Researchable)?.Name;
        Select(pick);
        if (pick != null && nodeButtons.TryGetValue(pick, out var node)) CallDeferred(nameof(ScrollTo), (int)node.Position.X);
    }

    private void ScrollTo(int x) => scroll.ScrollHorizontal = Math.Max(0, x - theme.Px(NodeWidth));

    private void BuildTree()
    {
        foreach (var child in canvas.GetChildren())
        {
            canvas.RemoveChild(child); // at once: a queued free would leave the old nodes in the layout until the frame ends
            child.QueueFree();
        }
        nodeButtons.Clear();
        int w = theme.Px(NodeWidth), h = theme.Px(NodeHeight), gx = theme.Px(GapX), gy = theme.Px(GapY), top = theme.Px(HeadingHeight);
        canvas.CustomMinimumSize = new Vector2(Layout.Columns * (w + gx), top + Layout.Rows * (h + gy));
        for (int i = 0; i < Layout.ColumnEras.Count; i++)
            canvas.AddChild(new Label { Text = Layout.ColumnEras[i], Position = new Vector2(i * (w + gx), 0f), Name = "Era_" + i });
        var centres = new Dictionary<string, Vector2>(StringComparer.Ordinal);
        foreach (var node in Layout.Nodes)
        {
            var button = new Button
            {
                Name = "Tech_" + node.Name, Text = node.Label, Alignment = HorizontalAlignment.Left, ClipText = true,
                Position = new Vector2(node.GridColumn * (w + gx), top + node.GridRow * (h + gy)), Size = new Vector2(w, h), CustomMinimumSize = new Vector2(w, h),
                TooltipText = node.Name + ": " + node.State,
            };
            string name = node.Name;
            button.Pressed += () => Select(name);
            nodeButtons[name] = button;
            canvas.AddChild(button);
            centres[name] = button.Position + new Vector2(w, h) / 2f;
        }
        canvas.Links = Layout.Edges.Select(it => (centres[it.From] + new Vector2(w / 2f, 0f), centres[it.To] - new Vector2(w / 2f, 0f))).ToList();
        canvas.LineColour = ToColour(PlaceholderPalette.PanelBorder.Scale(150));
        canvas.QueueRedraw();
    }

    private void Fill()
    {
        foreach (var child in actions.GetChildren())
        {
            actions.RemoveChild(child);
            child.QueueFree();
        }
        actionButtons.Clear();
        current = [];
        var node = SelectedTech == null ? null : Layout.Nodes.FirstOrDefault(it => it.Name == SelectedTech);
        queueLabel.Text = Tree == null ? "" : "Research queue\n" + (Tree.Queue.Count == 0 ? "empty" : string.Join("\n", TechScreenModel.QueueLines(Tree)));
        if (node == null || Tree == null)
        {
            title.Text = "";
            state.Text = "";
            unlocks.Text = "";
            return;
        }
        title.Text = node.Name;
        state.Text = node.Era + ": " + TechScreenModel.Detail(Tree.Techs.First(it => it.Name == node.Name));
        unlocks.Text = "Unlocks\n" + string.Join("\n", TechScreenModel.UnlockLines(node));
        current = TechScreenModel.Actions(Tree, node.Name);
        foreach (var entry in current)
        {
            var button = new Button { Name = entry.Id, Text = entry.Label, Disabled = !entry.Enabled, TooltipText = entry.Enabled ? "" : entry.Why, Alignment = HorizontalAlignment.Left };
            var chosen = entry;
            button.Pressed += () => Fire(Send(chosen));
            actionButtons[entry.Id] = button;
            actions.AddChild(button);
        }
    }

    private async Task Send(TechAction entry)
    {
        if (entry.Command is not { } command) return;
        var result = await session.SubmitAsync(command);
        if (!result.Accepted)
        {
            await ReloadAsync(SelectedTech);
            message.Text = "Refused: " + result.Message;
            return;
        }
        CommandAccepted?.Invoke();
        await ReloadAsync(SelectedTech);
        message.Text = "";
    }

    /// <summary>Observes a UI-triggered Task: a transport failure is shown instead of vanishing (IGameSession error contract).</summary>
    private async void Fire(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception e)
        {
            GD.PushError("Session call failed: " + e);
            message.Text = "Connection problem: " + e.Message;
        }
    }

    private static void Style(Button button, TechNode node, bool selected)
    {
        var fill = ToColour(node.Fill);
        foreach (string slot in new[] { "normal", "hover", "pressed", "disabled", "focus" })
        {
            var box = new StyleBoxFlat { BgColor = slot == "hover" ? fill.Lightened(0.2f) : fill, BorderColor = ToColour(selected ? PlaceholderPalette.AccentHover : PlaceholderPalette.PanelBorder) };
            int width = selected ? 3 : 1;
            box.SetBorderWidthAll(width);
            box.SetContentMarginAll(4f);
            button.AddThemeStyleboxOverride(slot, box);
        }
        button.AddThemeColorOverride("font_color", ToColour(node.State == TechScreenModel.Locked ? PlaceholderPalette.TextDisabled : PlaceholderPalette.Text));
    }

    private static Color ToColour(Rgb rgb) => new(rgb.R / 255f, rgb.G / 255f, rgb.B / 255f);

    /// <summary>The scrolled surface: draws the prerequisite links behind the node buttons.</summary>
    private sealed partial class EdgeCanvas : Control
    {
        public IReadOnlyList<(Vector2 From, Vector2 To)> Links { get; set; } = [];

        public Color LineColour { get; set; }

        public override void _Draw()
        {
            foreach (var (from, to) in Links) DrawLine(from, to, LineColour, 2f);
        }
    }
}
