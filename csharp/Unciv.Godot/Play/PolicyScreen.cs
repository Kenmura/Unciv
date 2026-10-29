using System.Globalization;
using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.Commands.Turn;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Localization;
using Unciv.GodotClient.Ui;
using Unciv.Models.Translations;
using Unciv.Queries.Empire;
using TabBar = Unciv.GodotClient.Ui.TabBar;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C020, redesigned in C071: the policy screen in a <see cref="ScreenFrame"/>. A summary band with the culture bar and the golden-age line, one card per branch (a header with the branch
/// icon, era and progress, the opener on top, the members as <see cref="PolicyNodeCard"/> nodes on the grid the ruleset gives them with connector lines, the completion bonus as a strip below),
/// a search box that dims the policies that do not match, and a detail panel with the chosen policy's effects and the Adopt button right under its name. At the compact breakpoint a tab bar
/// shows one branch at a time. Whether a policy can be adopted is the engine's answer (<see cref="PolicyScreenModel"/> from <see cref="PolicyTreeQuery"/>); choosing a policy only selects it, the Adopt
/// button is the confirmation and the only thing that changes the game, through a command submitted via the <see cref="IGameSession"/>. Closing the screen while the engine offers a pick dismisses the
/// picker. Sizes are fixed when the screen is built, so it is built again when the scale or breakpoint changes.
/// </summary>
public partial class PolicyScreen : PanelContainer
{
    private const int NodeWidth = 92;
    private const int NodeHeight = 84;
    private const int GapX = 12;
    private const int GapY = 16;
    private const int CompactNodeWidth = 78;
    private const int CompactNodeHeight = 84;
    private const int CompactGapX = 2;
    private const int CompactGapY = 12;

    private readonly IGameSession session;
    private readonly GameQueries queries;
    private readonly UiTheme uiTheme;
    private readonly Dictionary<string, Button> policyButtons = [];
    private readonly Dictionary<string, Control> branchCards = [];
    private readonly List<(string Branch, Control Card)> cardOrder = [];
    private ScreenFrame frame = null!; // JUSTIFY: C071 assigned by BuildFrame in the constructor
    private Label message = null!; // JUSTIFY: C071 assigned by BuildFrame in the constructor
    private Label status = null!; // JUSTIFY: C071 assigned by BuildFrame in the constructor
    private Label goldenAgeLabel = null!; // JUSTIFY: C071 assigned by BuildFrame in the constructor
    private StatBar cultureBar = null!; // JUSTIFY: C071 assigned by BuildFrame in the constructor
    private LineEdit search = null!; // JUSTIFY: C071 assigned by BuildFrame in the constructor
    private Control branchHost = null!; // JUSTIFY: C071 assigned by BuildFrame in the constructor
    private VBoxContainer detail = null!; // JUSTIFY: C071 assigned by BuildFrame in the constructor
    private Button adopt = null!; // JUSTIFY: C071 assigned by BuildFrame in the constructor
    private TabBar? tabs;
    private ScrollContainer? branchScroll;
    private PolicyCell? selected;
    private string headerText = "";
    private string goldenAgeText = "";
    private string detailText = "";
    private string messageText = "";
    private string searchText = "";
    private int generation;

    public PolicyScreen(IGameSession gameSession, UiTheme theme)
    {
        session = gameSession;
        queries = new GameQueries(gameSession);
        uiTheme = theme;
        Name = "PolicyPanel";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        Theme = theme.Theme;
        BuildFrame();
    }

    public override void _EnterTree() => uiTheme.LayoutChanged += OnLayoutChanged;

    public override void _ExitTree() => uiTheme.LayoutChanged -= OnLayoutChanged;

    /// <summary>Sizes are fixed when a view is built, so a change of scale or breakpoint builds the frame and the open tree again from the answers it has.</summary>
    private void OnLayoutChanged()
    {
        BuildFrame();
        if (Tree == null) return;
        FillSummary();
        Build();
        Select(selected?.Name);
        ApplySearch(false);
    }

    /// <summary>The engine's tree as last loaded; null while loading or when the engine refused.</summary>
    public PolicyTreeResult? Tree { get; private set; }

    public IReadOnlyList<PolicyBranchView> Branches { get; private set; } = [];

    public PolicyCell? Selected => selected;

    public string HeaderText => headerText;

    public string GoldenAgeText => goldenAgeText;

    /// <summary>The chosen policy's name, why it can or cannot be adopted, and its effects (or the prompt to choose one).</summary>
    public string DetailText => detailText;

    public string MessageText => messageText;

    public string SearchText => searchText;

    public Button AdoptButton => adopt;

    /// <summary>True when one branch at a time is shown behind tabs (the compact breakpoint).</summary>
    public bool IsTabbed => tabs != null;

    /// <summary>The branch cards by branch name; the ones behind an inactive tab exist but are hidden.</summary>
    public IReadOnlyDictionary<string, Control> BranchCards => branchCards;

    /// <summary>The policies the search matches (all of them while the box is empty).</summary>
    public IReadOnlyList<string> MatchingPolicies => Branches.SelectMany(it => it.Cells).Where(it => PolicyScreenModel.Matches(it, searchText, text => text.TrText())).Select(it => it.Name).ToList();

    /// <summary>Raised after the engine accepted a command made here, so the owner refreshes the world.</summary>
    public event Action? CommandAccepted;

    public event Action? Closed;

    public Button? PolicyButton(string name) => policyButtons.GetValueOrDefault("Policy_" + name);

    /// <summary>Shows the screen with the engine's current tree, keeping the chosen policy.</summary>
    public async Task OpenAsync()
    {
        Visible = true;
        await ReloadAsync();
    }

    public void Close()
    {
        generation++;
        bool wasOpen = Visible;
        Visible = false;
        if (wasOpen && Tree is { ShouldShowPicker: true }) Fire(Dismiss());
        Closed?.Invoke();
    }

    /// <summary>Types into the search box (what typing does): the policies that do not match are dimmed, and the first match is shown.</summary>
    public void Search(string text)
    {
        search.Text = text;
        searchText = text;
        ApplySearch(true);
    }

    /// <summary>Selects a policy by name (as pressing it does); an unknown name selects nothing.</summary>
    public void Select(string? name)
    {
        selected = null;
        string? branch = null;
        foreach (var view in Branches)
            foreach (var cell in view.Cells)
                if (cell.Name == name)
                {
                    selected = cell;
                    branch = view.Name;
                }
        if (branch != null) ShowBranch(branch);
        ShowSelection();
    }

    /// <summary>Brings a branch's card into view: its tab, or its place in the scrolled grid.</summary>
    public void ShowBranch(string branch)
    {
        int index = Branches.ToList().FindIndex(it => it.Name == branch);
        if (index < 0) return;
        if (tabs != null) tabs.Select(index);
        else if (branchCards.TryGetValue(branch, out var card)) branchScroll?.EnsureControlVisible(card);
    }

    /// <summary>Scrolls the policy's node into view (a click needs it inside the visible part of its branch).</summary>
    public void EnsureVisible(string policy)
    {
        if (PolicyButton(policy) is not { } button) return;
        for (var parent = button.GetParent(); parent != null; parent = parent.GetParent())
            if (parent is ScrollContainer scroll)
            {
                scroll.EnsureControlVisible(button);
                return;
            }
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
        policyButtons.Clear();
        branchCards.Clear();
        cardOrder.Clear();
        tabs = null;
        frame = new ScreenFrame(uiTheme, "Policies", closeLabel: "Close policies");
        frame.CloseRequested += Close;
        AddChild(frame);

        search = new LineEdit
        {
            Name = "PolicySearch", PlaceholderText = "Search".Tr(), ClearButtonEnabled = true, Text = searchText, CustomMinimumSize = new Vector2(uiTheme.Px(140), 0f),
            TooltipText = "Search policies by name or by what they do".Tr(),
        };
        search.TextChanged += text =>
        {
            searchText = text;
            ApplySearch(true);
        };
        frame.SetHeaderStrip(search);

        var page = new VBoxContainer();
        page.AddThemeConstantOverride("separation", uiTheme.Px(UiTokens.S2));
        page.AddChild(BuildSummary());
        var body = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", uiTheme.Px(UiTokens.S3));
        branchHost = new VBoxContainer { Name = "Branches", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        body.AddChild(branchHost);

        var side = new PanelContainer { Name = "Side", ThemeTypeVariation = UiVariants.PanelCard, CustomMinimumSize = new Vector2(uiTheme.Px(uiTheme.Breakpoint == UiBreakpoint.Compact ? 170 : 280), 0f) };
        var sideScroll = new ScrollContainer { Name = "SideScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        detail = new VBoxContainer { Name = "Detail", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        detail.AddThemeConstantOverride("separation", uiTheme.Px(UiTokens.S2));
        sideScroll.AddChild(detail);
        side.AddChild(sideScroll);
        body.AddChild(side);
        page.AddChild(body);
        frame.SetContent(page);

        adopt = new Button { Name = "AdoptButton", Text = "Adopt".Tr(), Disabled = true, ThemeTypeVariation = UiVariants.ButtonPrimary, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        adopt.Pressed += () => Fire(AdoptSelected());
        message = NewMessage();
    }

    /// <summary>The band under the header: the culture bar with what is missing, and the golden-age line.</summary>
    private Control BuildSummary()
    {
        var box = new VBoxContainer { Name = "Summary" };
        box.AddThemeConstantOverride("separation", uiTheme.Px(2));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", uiTheme.Px(UiTokens.S3));
        cultureBar = new StatBar(uiTheme, "Culture", Tree?.StoredCulture ?? 0, Tree?.CultureNeededForNextPolicy ?? 0, labelled: true) { Name = "CultureBar", CustomMinimumSize = new Vector2(uiTheme.Px(120), 0f) };
        row.AddChild(cultureBar);
        status = new Label { Name = "Header", ThemeTypeVariation = UiVariants.LabelSecondary, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = 2f, VerticalAlignment = VerticalAlignment.Center };
        row.AddChild(status);
        box.AddChild(row);
        goldenAgeLabel = new Label { Name = "GoldenAge", ThemeTypeVariation = UiVariants.LabelSmall, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        box.AddChild(goldenAgeLabel);
        return box;
    }

    private void FillSummary()
    {
        if (Tree == null) return;
        cultureBar.SetProgress(Tree.StoredCulture, Tree.CultureNeededForNextPolicy);
        cultureBar.Caption = headerText.Split('\n')[0].Replace("Culture: ", "", StringComparison.Ordinal);
        status.Text = string.Join(" · ", headerText.Split('\n').Skip(1));
        status.TooltipText = headerText;
        goldenAgeLabel.Text = goldenAgeText;
    }

    private Label NewMessage() => new() { Name = "Message", ThemeTypeVariation = UiVariants.LabelSmall, AutowrapMode = TextServer.AutowrapMode.WordSmart, Text = messageText };

    private void SetMessage(string text)
    {
        messageText = text;
        message.Text = text;
    }

    private async Task ReloadAsync()
    {
        int ticket = ++generation;
        var tree = await queries.PolicyTree();
        var bar = await queries.TopBar();
        if (ticket != generation) return;
        if (tree == null)
        {
            SetMessage(PolicyScreenModel.UnavailableLine());
            return;
        }
        Tree = tree;
        Branches = PolicyScreenModel.Branches(tree, session.Ruleset);
        headerText = string.Join("\n", PolicyScreenModel.HeaderLines(tree)).TrText();
        goldenAgeText = bar == null ? "" : PolicyScreenModel.GoldenAgeLine(bar).TrText();
        FillSummary();
        string? keep = selected?.Name;
        Build();
        Select(keep);
        ApplySearch(false);
    }

    private void Build()
    {
        foreach (var child in branchHost.GetChildren())
        {
            branchHost.RemoveChild(child); // at once: a queued free would leave the old branches in the layout until the frame ends
            child.QueueFree();
        }
        policyButtons.Clear();
        branchCards.Clear();
        cardOrder.Clear();
        tabs = null;
        branchScroll = null;
        var cards = Branches.Select(Card).ToList();
        for (int i = 0; i < cards.Count; i++)
        {
            branchCards[Branches[i].Name] = cards[i];
            cardOrder.Add((Branches[i].Name, cards[i]));
        }
        if (uiTheme.Breakpoint == UiBreakpoint.Compact)
        {
            tabs = new TabBar { Name = "BranchTabs", SizeFlagsVertical = Control.SizeFlags.ExpandFill };
            for (int i = 0; i < cards.Count; i++)
            {
                var scroll = new ScrollContainer { Name = "Page_" + Branches[i].Name, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
                scroll.AddChild(cards[i]);
                tabs.AddTab(Branches[i].Name, scroll);
            }
            branchHost.AddChild(tabs);
            return;
        }
        var frameBox = new PanelContainer { ThemeTypeVariation = UiVariants.PanelInset, SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var scrollAll = branchScroll = new ScrollContainer { Name = "BranchScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var flow = new HFlowContainer { Name = "BranchGrid", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        flow.AddThemeConstantOverride("h_separation", uiTheme.Px(UiTokens.S3));
        flow.AddThemeConstantOverride("v_separation", uiTheme.Px(UiTokens.S3));
        foreach (var card in cards) flow.AddChild(card);
        var margin = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, uiTheme.Px(UiTokens.S2));
        margin.AddChild(flow);
        scrollAll.AddChild(margin);
        frameBox.AddChild(scrollAll);
        branchHost.AddChild(frameBox);
    }

    /// <summary>One branch as a card: header, opener, members on the grid with their connectors, completion strip.</summary>
    private PanelContainer Card(PolicyBranchView view)
    {
        bool compact = uiTheme.Breakpoint == UiBreakpoint.Compact; // smaller nodes, so a four-column branch fits next to the detail panel at 640x480
        int w = uiTheme.Px(compact ? CompactNodeWidth : NodeWidth), h = uiTheme.Px(compact ? CompactNodeHeight : NodeHeight);
        int gx = uiTheme.Px(compact ? CompactGapX : GapX), gy = uiTheme.Px(compact ? CompactGapY : GapY);
        var card = new PanelContainer { Name = "Branch_" + view.Name, ThemeTypeVariation = UiVariants.PanelCard, SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", uiTheme.Px(UiTokens.S2));
        card.AddChild(box);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", uiTheme.Px(UiTokens.S2));
        var fill = view.Start.IsAdopted ? UiTokens.AccentLo : view.IsOpen ? UiTokens.Info.Scale(60) : UiTokens.Surface3;
        head.AddChild(new IconBadge(uiTheme, AssetKey.Of(RulesetAssetKeys.PolicyIcon, view.Name), ProceduralAssets.ToColor(fill), ProceduralAssets.ToColor(UiTokens.TextPrimary), 28));
        var titles = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        titles.AddThemeConstantOverride("separation", 0);
        titles.AddChild(new Label { Name = "BranchName", Text = view.Name.TrText(), ThemeTypeVariation = UiVariants.LabelHeading, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis });
        string era = view.Era.Length > 0 ? view.Era.TrText() + " · " : "";
        titles.AddChild(new Label { Name = "BranchProgress", Text = era + Int(view.Adopted) + " / " + Int(view.Total), ThemeTypeVariation = UiVariants.LabelSmall, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis });
        head.AddChild(titles);
        box.AddChild(head);
        if (!view.IsOpen)
            box.AddChild(new Label { Name = "BranchHint", Text = view.Start.Hint.TrText(), ThemeTypeVariation = UiVariants.LabelSmall, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(w, 0f) });

        int columns = Math.Max(1, view.Columns);
        float width = columns * (w + gx) - gx;
        var grid = new BranchCanvas { Name = "Grid", CustomMinimumSize = new Vector2(width, (view.Rows + 1) * (h + gy) - gy), LineWidth = uiTheme.Px(2), Colour = ProceduralAssets.ToColor(UiTokens.BorderStrong), LitColour = ProceduralAssets.ToColor(UiTokens.Accent) };
        var centres = new Dictionary<string, Vector2>(StringComparer.Ordinal);
        PolicyNodeCard Place(PolicyCell cell, float x, int row)
        {
            var node = MakeNode(cell, w, h);
            node.Position = new Vector2(x, row * (h + gy));
            grid.AddChild(node);
            centres[cell.Name] = node.Position + new Vector2(w / 2f, 0f);
            return node;
        }
        Place(view.Start, (width - w) / 2f, 0);
        foreach (var member in view.Members) Place(member, (member.Column - view.ColumnOrigin) * (w + gx), member.Row - view.RowOrigin + 1);
        var links = new List<(PolicyCell From, PolicyCell To, Vector2 Start, Vector2 End)>();
        var byName = view.Cells.ToDictionary(it => it.Name, StringComparer.Ordinal);
        foreach (var member in view.Members)
        {
            var parents = member.Requires.Count == 0 ? [view.Start.Name] : member.Requires;
            foreach (string parent in parents)
                if (centres.TryGetValue(parent, out var from) && byName.TryGetValue(parent, out var parentCell))
                    links.Add((parentCell, member, from + new Vector2(0f, h), centres[member.Name]));
        }
        grid.Links = links.Select(it => (it.Start, it.End, it.From.IsAdopted && it.To.IsAdopted)).ToList();
        box.AddChild(grid);
        if (view.Complete != null)
        {
            var strip = new Button
            {
                Name = view.Complete.Id, Text = view.Complete.Name.TrText(), TooltipText = view.Complete.Name.TrText() + "\n" + view.Complete.Hint.TrText(), ToggleMode = true, Alignment = HorizontalAlignment.Center,
                ThemeTypeVariation = UiVariants.PolicyNode(view.Complete.State, view.Complete.Pickable), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true,
                CustomMinimumSize = new Vector2(0f, uiTheme.Px(UiTokens.ControlMinHeight)),
            };
            string completeName = view.Complete.Name;
            strip.Pressed += () => Select(completeName);
            policyButtons[view.Complete.Id] = strip;
            box.AddChild(strip);
        }
        return card;
    }

    private PolicyNodeCard MakeNode(PolicyCell cell, int width, int height)
    {
        var node = new PolicyNodeCard(uiTheme, cell, width, height);
        string name = cell.Name;
        node.Pressed += () => Select(name);
        policyButtons[cell.Id] = node;
        return node;
    }

    private void ApplySearch(bool reveal)
    {
        bool active = searchText.Trim().Length > 0;
        string? firstBranch = null;
        foreach (var view in Branches)
        {
            int hits = 0;
            foreach (var cell in view.Cells)
            {
                bool hit = !active || PolicyScreenModel.Matches(cell, searchText, text => text.TrText());
                if (hit) hits++;
                switch (policyButtons.GetValueOrDefault(cell.Id))
                {
                    case PolicyNodeCard node:
                        node.Dimmed = !hit;
                        break;
                    case { } strip:
                        var colour = strip.Modulate;
                        colour.A = hit ? 1f : 0.3f;
                        strip.Modulate = colour;
                        break;
                }
            }
            if (active && hits > 0 && firstBranch == null) firstBranch = view.Name;
        }
        if (reveal && firstBranch != null) ShowBranch(firstBranch);
    }

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    private void ShowSelection()
    {
        foreach (var (id, button) in policyButtons) button.SetPressedNoSignal(selected != null && id == selected.Id);
        foreach (var child in detail.GetChildren())
        {
            if (child == adopt || child == message) detail.RemoveChild(child);
            else
            {
                detail.RemoveChild(child);
                child.QueueFree();
            }
        }
        if (selected == null)
        {
            detailText = "Choose a policy to see what it does.".Tr();
            adopt.Text = "Adopt".Tr();
            adopt.Disabled = true;
            detail.AddChild(new EmptyState(uiTheme, AssetKey.Of(RulesetAssetKeys.UiIcon, "Policies"), "Choose a policy to see what it does."));
            detail.AddChild(message);
            return;
        }
        var cell = selected;
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", uiTheme.Px(UiTokens.S2));
        var fill = cell.IsAdopted ? UiTokens.AccentLo : cell.State == "Locked" ? UiTokens.Surface3 : UiTokens.Info.Scale(60);
        top.AddChild(new IconBadge(uiTheme, cell.IconKey, ProceduralAssets.ToColor(fill), ProceduralAssets.ToColor(UiTokens.TextPrimary), 32));
        top.AddChild(new Label { Name = "Title", Text = cell.Name.TrText(), ThemeTypeVariation = UiVariants.LabelHeading, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        detail.AddChild(top);
        detail.AddChild(new Label { Name = "State", Text = cell.Hint.TrText(), ThemeTypeVariation = UiVariants.LabelSecondary, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        adopt.Text = PolicyScreenModel.AdoptLabel(cell).TrText();
        adopt.Disabled = PolicyScreenModel.AdoptCommand(cell) == null;
        detail.AddChild(adopt);
        detail.AddChild(new SectionHeader("Effects"));
        foreach (var effect in cell.Effects) detail.AddChild(EffectRow(effect));
        detail.AddChild(message);
        detailText = PolicyScreenModel.Detail(cell, text => text.TrText());
    }

    /// <summary>One effect line; a line with a stat glyph is drawn with the stat's icon.</summary>
    private Control EffectRow(PolicyEffect effect)
    {
        if (UiText.Runs(effect.Display).Any(it => it.Stat != null)) return new StatText(uiTheme, effect.Display) { Name = "Effect", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        return new Label { Name = "Effect", Text = effect.Display, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
    }

    private async Task AdoptSelected()
    {
        var command = selected == null ? null : PolicyScreenModel.AdoptCommand(selected);
        if (command == null) return;
        await Send(command);
    }

    private async Task Dismiss()
    {
        var result = await session.SubmitAsync(new DismissPolicyPicker());
        if (result.Accepted) CommandAccepted?.Invoke();
    }

    private async Task Send(GameCommand command)
    {
        var result = await session.SubmitAsync(command);
        if (!result.Accepted)
        {
            await ReloadAsync();
            SetMessage(PolicyScreenModel.RefusedLine(result.Message));
            return;
        }
        CommandAccepted?.Invoke();
        await ReloadAsync();
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
            SetMessage(PolicyScreenModel.ConnectionProblemLine(e.Message));
        }
    }

    /// <summary>The grid of one branch: the nodes sit on it at absolute positions and the prerequisite links are drawn behind them as vertical elbows, lit when both ends are adopted.</summary>
    private sealed partial class BranchCanvas : Control
    {
        public IReadOnlyList<(Vector2 Start, Vector2 End, bool Lit)> Links { get; set; } = [];

        public float LineWidth { get; set; } = 2f;

        public Color Colour { get; set; }

        public Color LitColour { get; set; }

        public override void _Draw()
        {
            foreach (var link in Links.Where(it => !it.Lit)) Elbow(link.Start, link.End, Colour);
            foreach (var link in Links.Where(it => it.Lit)) Elbow(link.Start, link.End, LitColour);
        }

        private void Elbow(Vector2 from, Vector2 to, Color colour)
        {
            if (Math.Abs(from.X - to.X) < 0.5f)
            {
                DrawLine(from, to, colour, LineWidth);
                return;
            }
            float mid = from.Y + (to.Y - from.Y) / 2f;
            DrawPolyline([from, new Vector2(from.X, mid), new Vector2(to.X, mid), to], colour, LineWidth);
        }
    }
}
