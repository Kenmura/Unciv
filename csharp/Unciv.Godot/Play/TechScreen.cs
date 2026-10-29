using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Ui;
using Unciv.Queries.Empire;
using Unciv.GodotClient.Localization;
using Unciv.Models.Translations;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C019, redesigned in C070: the tech screen in a <see cref="ScreenFrame"/>. The engine's tech tree (<see cref="TechTreeQuery"/>) laid out by column and row by <see cref="TechScreenModel"/>:
/// a node card per tech (<see cref="TechNodeCard"/>), orthogonal prerequisite links drawn in one layer with the path to the chosen tech highlighted, one sticky header band per era, a
/// search box that dims the techs that do not match, a detail panel (unlocks, research queue) and the actions in the footer. Everything shown is the engine's answer and the only change is a
/// command sent through the <see cref="IGameSession"/>. Sizes are fixed when the screen is built, so it is built again when the scale or breakpoint changes.
/// </summary>
public partial class TechScreen : PanelContainer
{
    private const int NodeWidth = 200;
    private const int NodeHeight = 60;
    private const int GapX = 36;
    private const int GapY = 10;
    private const int BandHeight = 28;

    private readonly IGameSession session;
    private readonly GameQueries queries;
    private readonly UiTheme theme;
    private readonly Dictionary<string, TechNodeCard> nodeButtons = [];
    private readonly Dictionary<string, Button> actionButtons = [];
    private readonly List<Control> eraBands = [];
    private ScreenFrame frame = null!; // JUSTIFY: C070 assigned by BuildFrame in the constructor
    private Label message = null!; // JUSTIFY: C070 assigned by BuildFrame in the constructor
    private Label header = null!; // JUSTIFY: C070 assigned by BuildFrame in the constructor
    private LineEdit search = null!; // JUSTIFY: C070 assigned by BuildFrame in the constructor
    private ScrollContainer scroll = null!; // JUSTIFY: C070 assigned by BuildFrame in the constructor
    private Control bandRow = null!; // JUSTIFY: C070 assigned by BuildFrame in the constructor
    private EdgeCanvas canvas = null!; // JUSTIFY: C070 assigned by BuildFrame in the constructor
    private VBoxContainer detail = null!; // JUSTIFY: C070 assigned by BuildFrame in the constructor
    private string titleText = "";
    private string stateText = "";
    private IReadOnlyList<TechAction> current = [];
    private string unlockText = "";
    private string queueText = "";
    private string messageText = "";
    private string headerText = "";
    private string searchText = "";
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
        BuildFrame();
    }

    public override void _EnterTree() => theme.LayoutChanged += OnLayoutChanged;

    public override void _ExitTree() => theme.LayoutChanged -= OnLayoutChanged;

    /// <summary>Sizes are fixed when a view is built, so a change of scale or breakpoint builds the frame and the open tree again from the answers it has.</summary>
    private void OnLayoutChanged()
    {
        BuildFrame();
        if (Tree != null)
        {
            BuildTree();
            Select(SelectedTech);
            ApplySearch(false);
        }
    }

    public TechTreeResult? Tree { get; private set; }

    public TechLayout Layout { get; private set; } = new([], [], 0, 0, []);

    public string? SelectedTech { get; private set; }

    public IReadOnlyList<TechAction> Buttons => current;

    public string HeaderText => headerText;

    public string TitleText => titleText;

    public string StateText => stateText;

    /// <summary>"Unlocks" and the engine's unlock lines of the chosen tech.</summary>
    public string UnlockText => unlockText;

    public string QueueText => queueText;

    public string MessageText => messageText;

    /// <summary>The search box's text.</summary>
    public string SearchText => searchText;

    /// <summary>The era header bands, one per era span.</summary>
    public IReadOnlyList<Control> EraBands => eraBands;

    /// <summary>The scrolled area's visible rectangle.</summary>
    public Rect2 TreeRect => scroll.GetGlobalRect();

    /// <summary>The techs the search matches (all of them while the box is empty).</summary>
    public IReadOnlyList<string> MatchingTechs => Layout.Nodes.Where(it => TechScreenModel.Matches(it, searchText, text => text.TrText())).Select(it => it.Name).ToList();

    /// <summary>Raised after the engine accepted a command made here, so the owner refreshes the world.</summary>
    public event Action? CommandAccepted;

    public event Action? Closed;

    public TechNodeCard? NodeButton(string tech) => nodeButtons.GetValueOrDefault(tech);

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

    /// <summary>Types into the search box (what typing does): the techs that do not match are dimmed, and the first match is scrolled into view.</summary>
    public void Search(string text)
    {
        search.Text = text;
        searchText = text;
        ApplySearch(true);
    }

    /// <summary>Chooses a node of the tree and shows its unlocks, its path and its buttons.</summary>
    public void Select(string? tech)
    {
        SelectedTech = tech;
        foreach (var (name, card) in nodeButtons) card.SetPressedNoSignal(name == tech);
        Fill();
        RefreshEdges();
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

    private void BuildFrame()
    {
        if (frame != null)
        {
            RemoveChild(frame); // at once: a queued free would leave the old frame in the layout until the frame ends
            frame.QueueFree();
        }
        nodeButtons.Clear();
        actionButtons.Clear();
        eraBands.Clear();
        frame = new ScreenFrame(theme, "Technology", closeLabel: "Close technology");
        frame.CloseRequested += Close;
        AddChild(frame);

        header = new Label { Name = "Header", ThemeTypeVariation = UiVariants.LabelSecondary, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center, Text = headerText };
        search = new LineEdit
        {
            Name = "TechSearch", PlaceholderText = "Search".Tr(), ClearButtonEnabled = true, Text = searchText, CustomMinimumSize = new Vector2(theme.Px(140), 0f),
            TooltipText = "Search technologies by name, by what they unlock, or by a stat".Tr(),
        };
        search.TextChanged += text =>
        {
            searchText = text;
            ApplySearch(true);
        };
        var strip = new HBoxContainer();
        strip.AddThemeConstantOverride("separation", theme.Px(UiTokens.S2));
        strip.AddChild(header);
        strip.AddChild(search);
        frame.SetHeaderStrip(strip);

        var body = new HBoxContainer();
        body.AddThemeConstantOverride("separation", theme.Px(UiTokens.S3));
        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        left.AddThemeConstantOverride("separation", 0);
        bandRow = new Control { Name = "EraBands", ClipContents = true, CustomMinimumSize = new Vector2(0f, theme.Px(BandHeight)) };
        left.AddChild(bandRow);
        var treeFrame = new PanelContainer { ThemeTypeVariation = UiVariants.PanelInset, SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll = new ScrollContainer { Name = "TreeScroll", SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        canvas = new EdgeCanvas { Name = "TreeCanvas", Pan = delta => { scroll.ScrollHorizontal -= (int)delta.X; scroll.ScrollVertical -= (int)delta.Y; } };
        scroll.AddChild(canvas);
        scroll.GetHScrollBar().ValueChanged += _ => LayoutBands();
        scroll.GetHScrollBar().Resized += LayoutBands;
        treeFrame.AddChild(scroll);
        left.AddChild(treeFrame);
        body.AddChild(left);

        var side = new PanelContainer { Name = "Side", ThemeTypeVariation = UiVariants.PanelCard, CustomMinimumSize = new Vector2(theme.Px(theme.Breakpoint == UiBreakpoint.Compact ? 170 : 280), 0f) };
        var sideScroll = new ScrollContainer { Name = "SideScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        detail = new VBoxContainer { Name = "Detail", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        detail.AddThemeConstantOverride("separation", theme.Px(UiTokens.S2));
        sideScroll.AddChild(detail);
        side.AddChild(sideScroll);
        body.AddChild(side);
        frame.SetContent(body);

    }

    private async Task ReloadAsync(string? choose)
    {
        int ticket = ++generation;
        var tree = await queries.TechTree();
        if (ticket != generation) return;
        if (tree == null)
        {
            Tree = null;
            SetHeader("The engine would not show the tree.".Tr());
            return;
        }
        Tree = tree;
        Layout = TechScreenModel.Layout(tree, session.Ruleset);
        SetHeader(TechScreenModel.Header(tree).TrText());
        BuildTree();
        string? pick = choose != null && Layout.Nodes.Any(it => it.Name == choose) ? choose
            : tree.CurrentTech != null && Layout.Nodes.Any(it => it.Name == tree.CurrentTech) ? tree.CurrentTech
            : Layout.Nodes.FirstOrDefault(it => it.State == TechScreenModel.Researchable)?.Name;
        Select(pick);
        ApplySearch(false);
        if (pick != null) CallDeferred(nameof(ScrollTo), pick);
    }

    private void SetHeader(string text)
    {
        headerText = text;
        header.Text = text;
    }

    private Label NewMessage() => new() { Name = "Message", ThemeTypeVariation = UiVariants.LabelSmall, AutowrapMode = TextServer.AutowrapMode.WordSmart, Text = messageText };

    private void SetMessage(string text)
    {
        messageText = text;
        message.Text = text;
    }

    /// <summary>Centres a node in the visible part of the tree.</summary>
    private void ScrollTo(string tech)
    {
        if (!nodeButtons.TryGetValue(tech, out var node)) return;
        scroll.ScrollHorizontal = Math.Max(0, (int)(node.Position.X + node.Size.X / 2f - scroll.Size.X / 2f));
        scroll.ScrollVertical = Math.Max(0, (int)(node.Position.Y + node.Size.Y / 2f - scroll.Size.Y / 2f));
    }

    private int ColumnWidth => theme.Px(NodeWidth) + theme.Px(GapX);

    private int Pad => theme.Px(UiTokens.S2);

    private void BuildTree()
    {
        foreach (var child in canvas.GetChildren())
        {
            canvas.RemoveChild(child); // at once: a queued free would leave the old nodes in the layout until the frame ends
            child.QueueFree();
        }
        nodeButtons.Clear();
        int w = theme.Px(NodeWidth), h = theme.Px(NodeHeight), gx = theme.Px(GapX), gy = theme.Px(GapY), pad = Pad;
        canvas.CustomMinimumSize = new Vector2(pad * 2 + Layout.Columns * (w + gx) - gx, pad * 2 + Layout.Rows * (h + gy) - gy + theme.Px(UiTokens.S4));
        var centres = new Dictionary<string, Vector2>(StringComparer.Ordinal);
        foreach (var node in Layout.Nodes)
        {
            var card = new TechNodeCard(theme, node, w, h) { Position = new Vector2(pad + node.GridColumn * (w + gx), pad + node.GridRow * (h + gy)) };
            string name = node.Name;
            card.Pressed += () => Select(name);
            card.Activated += () => Quick(name, "Research");
            card.QueueRequested += () => Quick(name, "Enqueue");
            nodeButtons[name] = card;
            canvas.AddChild(card);
            centres[name] = card.Position + new Vector2(w, h) / 2f;
        }
        canvas.Links = Layout.Edges.Select(it => (it.From, it.To, centres[it.From] + new Vector2(w / 2f, 0f), centres[it.To] - new Vector2(w / 2f, 0f))).ToList();
        canvas.Gap = gx;
        canvas.LineWidth = theme.Px(2);
        canvas.LineColour = ToColour(UiTokens.BorderStrong);
        canvas.PathColour = ToColour(UiTokens.Accent);
        BuildBands();
        canvas.QueueRedraw();
    }

    /// <summary>One header band per era span, drawn once; they live outside the scrolled area, so they stay at the top while the tree scrolls vertically.</summary>
    private void BuildBands()
    {
        foreach (var child in bandRow.GetChildren())
        {
            bandRow.RemoveChild(child);
            child.QueueFree();
        }
        eraBands.Clear();
        int index = 0;
        foreach (var span in Layout.EraSpans)
        {
            var band = new ColorRect { Name = "Era_" + span.Era, ClipContents = true, Color = ToColour(index++ % 2 == 0 ? UiTokens.Surface1 : UiTokens.Surface2), MouseFilter = MouseFilterEnum.Ignore };
            band.SetMeta("first", span.FirstColumn);
            band.SetMeta("columns", span.Columns);
            band.AddChild(new Label { Name = "EraName", Text = span.Era.TrText().ToUpperInvariant(), ThemeTypeVariation = UiVariants.LabelOverline, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore });
            bandRow.AddChild(band);
            eraBands.Add(band);
        }
        LayoutBands();
    }

    /// <summary>Places the bands over their columns at the current horizontal scroll; the name stays at the left edge while its era is in view.</summary>
    private void LayoutBands()
    {
        int scrolled = scroll.ScrollHorizontal;
        int height = theme.Px(BandHeight);
        float inset = scroll.GetGlobalRect().Position.X - bandRow.GetGlobalRect().Position.X; // the tree sits inside a framed panel
        foreach (var band in eraBands)
        {
            float x = inset + Pad + (int)band.GetMeta("first") * ColumnWidth - scrolled;
            float width = (int)band.GetMeta("columns") * ColumnWidth - theme.Px(GapX);
            band.Position = new Vector2(x, 0f);
            band.Size = new Vector2(width, height);
            var label = (Label)band.GetChild(0);
            float room = Math.Max(0f, width - label.GetMinimumSize().X - theme.Px(UiTokens.S2));
            label.Position = new Vector2(theme.Px(UiTokens.S2) + Math.Clamp(-x, 0f, room), 0f);
            label.Size = new Vector2(label.GetMinimumSize().X, height);
        }
    }

    private void ApplySearch(bool reveal)
    {
        bool active = searchText.Trim().Length > 0;
        string? first = null;
        foreach (var (name, card) in nodeButtons)
        {
            bool hit = !active || TechScreenModel.Matches(card.Node, searchText, text => text.TrText());
            card.Dimmed = !hit;
            if (active && hit && first == null) first = name;
        }
        if (reveal && first != null) CallDeferred(nameof(EnsureVisible), first);
    }

    private void RefreshEdges()
    {
        var lit = new HashSet<string>(StringComparer.Ordinal);
        if (Tree != null)
        {
            foreach (string queued in Tree.Queue) lit.Add(queued);
            if (SelectedTech != null && TechScreenModel.PathTo(Tree, SelectedTech, new HashSet<string>(StringComparer.Ordinal)) is { } path)
                foreach (string name in path) lit.Add(name);
        }
        canvas.Lit = lit;
        canvas.QueueRedraw();
    }

    /// <summary>Double click and Shift+click: the node's own Research or Add to queue action, when the engine offers it.</summary>
    private void Quick(string tech, string id)
    {
        if (Tree == null) return;
        Select(tech);
        if (current.FirstOrDefault(it => it.Id == id) is { Enabled: true } action) Fire(Send(action));
    }

    private void Fill()
    {
        foreach (var child in detail.GetChildren())
        {
            detail.RemoveChild(child);
            child.QueueFree();
        }
        actionButtons.Clear();
        current = [];
        var node = SelectedTech == null ? null : Layout.Nodes.FirstOrDefault(it => it.Name == SelectedTech);
        var queueLines = Tree == null ? [] : TechScreenModel.QueueLines(Tree);
        queueText = Tree == null ? "" : "Research queue".Tr() + "\n" + (queueLines.Count == 0 ? "empty".Tr() : string.Join("\n", queueLines.Select(it => it.TrText())));
        if (node == null || Tree == null)
        {
            titleText = "";
            stateText = "";
            unlockText = "";
            frame.SetFooter([]);
            AddQueue(queueLines);
            message = NewMessage();
            detail.AddChild(message);
            return;
        }
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", theme.Px(UiTokens.S2));
        top.AddChild(new IconBadge(theme, node.IconKey, ToColour(UiTokens.Info.Scale(60)), ToColour(UiTokens.TextPrimary), 32));
        titleText = node.Name.TrText();
        top.AddChild(new Label { Name = "Title", Text = titleText, ThemeTypeVariation = UiVariants.LabelHeading, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, SizeFlagsHorizontal = SizeFlags.ExpandFill });
        detail.AddChild(top);
        var entry = Tree.Techs.First(it => it.Name == node.Name);
        stateText = node.Era.TrText() + ": " + TechScreenModel.Detail(entry);
        detail.AddChild(new Label { Name = "State", Text = stateText, ThemeTypeVariation = UiVariants.LabelSecondary, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        if (node.State != TechScreenModel.Researched && node.Cost > 0) detail.AddChild(new StatBar(theme, "Science", node.Progress, node.Cost));

        var lines = TechScreenModel.UnlockLines(node);
        unlockText = "Unlocks".Tr() + "\n" + string.Join("\n", lines.Select(it => it.TrText()));
        detail.AddChild(new SectionHeader("Unlocks"));
        if (node.Unlocks.Count > 0) detail.AddChild(UnlockStrip(node));
        var unlocks = new Label { Name = "Unlocks", AutowrapMode = TextServer.AutowrapMode.WordSmart, Text = string.Join("\n", lines.Select(it => it.TrText())), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        detail.AddChild(unlocks);
        AddQueue(queueLines);
        message = NewMessage();
        detail.AddChild(message);

        current = TechScreenModel.Actions(Tree, node.Name);
        var footer = new List<(Button Button, ChoiceRole Role)>();
        foreach (var action in current)
        {
            string text = action.Label.TrText();
            var button = new Button { Name = action.Id, Text = text, Disabled = !action.Enabled, TooltipText = action.Enabled ? text : action.Why.TrText() };
            var chosen = action;
            button.Pressed += () => Fire(Send(chosen));
            actionButtons[action.Id] = button;
            footer.Add((button, action.Id is "Research" or "TakeFree" ? ChoiceRole.Primary : action.Id == "Dequeue" ? ChoiceRole.Ghost : ChoiceRole.Secondary));
        }
        frame.SetFooter(footer);
    }

    private HFlowContainer UnlockStrip(TechNode node)
    {
        var strip = new HFlowContainer { Name = "UnlockIcons" };
        strip.AddThemeConstantOverride("h_separation", theme.Px(UiTokens.S1));
        foreach (var unlock in node.Unlocks)
        {
            strip.AddChild(new TextureRect
            {
                Texture = theme.Icon(unlock.Icon), CustomMinimumSize = new Vector2(theme.Px(20), theme.Px(20)), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TextureFilter = TextureFilterEnum.LinearWithMipmaps, TooltipText = unlock.Name.TrText(),
                MouseFilter = MouseFilterEnum.Stop,
            });
        }
        return strip;
    }

    private void AddQueue(IReadOnlyList<string> queueLines)
    {
        detail.AddChild(new SectionHeader("Research queue"));
        string text = queueLines.Count == 0 ? "Queue empty. Shift+click a tech to queue it.".Tr() : string.Join("\n", queueLines.Select(it => it.TrText()));
        detail.AddChild(new Label { Name = "Queue", Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill });
    }

    private async Task Send(TechAction entry)
    {
        if (entry.Command is not { } command) return;
        var result = await session.SubmitAsync(command);
        if (!result.Accepted)
        {
            await ReloadAsync(SelectedTech);
            SetMessage("Refused".Tr() + ": " + result.Message.TrText());
            return;
        }
        CommandAccepted?.Invoke();
        await ReloadAsync(SelectedTech);
        SetMessage("");
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
            SetMessage("Connection problem".Tr() + ": " + e.Message);
        }
    }

    private static Color ToColour(Rgb rgb) => ProceduralAssets.ToColor(rgb);

    /// <summary>The scrolled surface: draws the prerequisite links behind the node cards as orthogonal elbows (one layer for the whole tree) and pans the tree when its empty part is dragged.</summary>
    private sealed partial class EdgeCanvas : Control
    {
        public IReadOnlyList<(string From, string To, Vector2 Start, Vector2 End)> Links { get; set; } = [];

        public ISet<string> Lit { get; set; } = new HashSet<string>();

        public Color LineColour { get; set; }

        public Color PathColour { get; set; }

        public float LineWidth { get; set; } = 2f;

        public int Gap { get; set; }

        public Action<Vector2>? Pan { get; set; }

        public override void _Draw()
        {
            foreach (var link in Links.Where(it => !IsLit(it))) Elbow(link.Start, link.End, LineColour);
            foreach (var link in Links.Where(IsLit)) Elbow(link.Start, link.End, PathColour);
        }

        public override void _GuiInput(InputEvent @event)
        {
            if (@event is InputEventMouseMotion { ButtonMask: MouseButtonMask.Left } motion) Pan?.Invoke(motion.Relative);
        }

        private bool IsLit((string From, string To, Vector2 Start, Vector2 End) link) => Lit.Contains(link.From) && Lit.Contains(link.To);

        private void Elbow(Vector2 from, Vector2 to, Color colour)
        {
            float mid = from.X + Gap / 2f;
            if (Math.Abs(from.Y - to.Y) < 0.5f)
            {
                DrawLine(from, to, colour, LineWidth);
                return;
            }
            DrawPolyline([from, new Vector2(mid, from.Y), new Vector2(mid, to.Y), to], colour, LineWidth);
        }
    }
}
