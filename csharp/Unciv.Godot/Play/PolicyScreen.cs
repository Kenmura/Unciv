using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.Commands.Turn;
using Unciv.GodotClient.Localization;
using Unciv.GodotClient.Ui;
using Unciv.Models.Translations;
using Unciv.Queries.Empire;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C020: the policy screen. One column per branch (opener on top, members on the grid the ruleset gives them, the completion bonus below), the culture and golden-age
/// lines, and a detail panel with the chosen policy's effects and an Adopt button. Whether a policy can be adopted is the engine's answer
/// (<see cref="PolicyScreenModel"/> from <see cref="PolicyTreeQuery"/>); choosing a policy only selects it, the Adopt button is the confirmation and the only thing that
/// changes the game, through a command submitted via the <see cref="IGameSession"/>. Closing the screen while the engine offers a pick dismisses the picker.
/// </summary>
public partial class PolicyScreen : PanelContainer
{
    private readonly IGameSession session;
    private readonly GameQueries queries;
    private readonly UiTheme uiTheme;
    private readonly Label message = new() { Name = "Message" };
    private readonly Label header = new() { Name = "Header" };
    private readonly Label goldenAge = new() { Name = "GoldenAge" };
    private readonly HBoxContainer branchRow = new() { Name = "Branches" };
    private readonly Label detail = new() { Name = "Detail", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Button adopt = new() { Name = "AdoptButton", Text = "Adopt".Tr(), Disabled = true };
    private readonly Dictionary<string, Button> policyButtons = [];
    private PolicyCell? selected;
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

        var column = new VBoxContainer();
        AddChild(column);
        var head = new HBoxContainer();
        column.AddChild(head);
        var heading = new Label { Text = "Policies".Tr(), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        heading.AddThemeFontSizeOverride("font_size", theme.Px(20));
        head.AddChild(heading);
        message.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        message.ClipText = true;
        message.HorizontalAlignment = HorizontalAlignment.Right;
        head.AddChild(message);
        var close = new Button { Name = "PolicyClose", Text = "Close policies".Tr() };
        close.Pressed += Close;
        head.AddChild(close);
        header.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(header);
        column.AddChild(goldenAge);

        var middle = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        column.AddChild(middle);
        var scroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, VerticalScrollMode = ScrollContainer.ScrollMode.Auto };
        middle.AddChild(scroll);
        branchRow.AddThemeConstantOverride("separation", theme.Px(14));
        scroll.AddChild(branchRow);
        var side = new VBoxContainer { Name = "Side", CustomMinimumSize = new Vector2(theme.Px(200), 0f) };
        middle.AddChild(side);
        detail.SizeFlagsVertical = SizeFlags.ExpandFill;
        side.AddChild(detail);
        adopt.Pressed += () => Fire(AdoptSelected());
        side.AddChild(adopt);
    }

    /// <summary>The engine's tree as last loaded; null while loading or when the engine refused.</summary>
    public PolicyTreeResult? Tree { get; private set; }

    public IReadOnlyList<PolicyBranchView> Branches { get; private set; } = [];

    public PolicyCell? Selected => selected;

    public string HeaderText => header.Text;

    public string GoldenAgeText => goldenAge.Text;

    public string DetailText => detail.Text;

    public string MessageText => message.Text;

    public Button AdoptButton => adopt;

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

    /// <summary>Selects a policy by name (as pressing it does); an unknown name selects nothing.</summary>
    public void Select(string? name)
    {
        selected = null;
        foreach (var view in Branches)
            foreach (var cell in All(view))
                if (cell.Name == name) selected = cell;
        ShowSelection();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (Visible && @event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    private static IEnumerable<PolicyCell> All(PolicyBranchView view)
    {
        yield return view.Start;
        foreach (var member in view.Members) yield return member;
        if (view.Complete != null) yield return view.Complete;
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
        Branches = PolicyScreenModel.Branches(tree);
        header.Text = string.Join("\n", PolicyScreenModel.HeaderLines(tree)).TrText();
        goldenAge.Text = bar == null ? "" : PolicyScreenModel.GoldenAgeLine(bar).TrText();
        string? keep = selected?.Name;
        Build();
        Select(keep);
    }

    private void Build()
    {
        foreach (var child in branchRow.GetChildren())
        {
            branchRow.RemoveChild(child); // at once: a queued free would leave the old branches in the layout until the frame ends
            child.QueueFree();
        }
        policyButtons.Clear();
        foreach (var view in Branches)
        {
            var box = new VBoxContainer { Name = "Branch_" + view.Name };
            box.AddThemeConstantOverride("separation", 4);
            var title = new Label { Text = view.Title.TrText() };
            title.AddThemeFontSizeOverride("font_size", uiTheme.Px(18));
            box.AddChild(title);
            box.AddChild(new Label { Text = view.Progress.TrText() });
            box.AddChild(MakeButton(view.Start));
            if (view.Columns > 0)
            {
                var grid = new GridContainer { Columns = view.Columns };
                grid.AddThemeConstantOverride("h_separation", 4);
                grid.AddThemeConstantOverride("v_separation", 4);
                for (int row = 1; row <= view.Rows; row++)
                    for (int col = 1; col <= view.Columns; col++)
                    {
                        var cell = view.Members.FirstOrDefault(it => it.Row == row && it.Column == col);
                        grid.AddChild(cell == null ? new Control { CustomMinimumSize = new Vector2(uiTheme.Px(120), 0f) } : MakeButton(cell));
                    }
                box.AddChild(grid);
            }
            if (view.Complete != null) box.AddChild(MakeButton(view.Complete));
            branchRow.AddChild(box);
        }
    }

    private Button MakeButton(PolicyCell cell)
    {
        var button = new Button { Name = cell.Id, Text = cell.Name.TrText(), TooltipText = cell.Hint.TrText(), ToggleMode = true, CustomMinimumSize = new Vector2(uiTheme.Px(120), 0f) };
        var godotColour = ToColour(PlaceholderPalette.ForPolicyState(cell.State));
        foreach (string slot in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" })
            button.AddThemeColorOverride(slot, godotColour);
        string name = cell.Name;
        button.Pressed += () => Select(name);
        policyButtons[cell.Id] = button;
        return button;
    }

    private void ShowSelection()
    {
        foreach (var (id, button) in policyButtons) button.SetPressedNoSignal(selected != null && id == selected.Id);
        if (selected == null)
        {
            detail.Text = "Choose a policy to see what it does.".Tr();
            adopt.Text = "Adopt".Tr();
            adopt.Disabled = true;
            return;
        }
        var ruleset = session.Ruleset;
        string text = ruleset.Policies.TryGetValue(selected.Name, out var policy) ? policy.GetDescription()
            : ruleset.PolicyBranches.TryGetValue(selected.Name, out var branch) ? branch.GetDescription() : "";
        detail.Text = PolicyScreenModel.Detail(selected, text).TrText();
        adopt.Text = PolicyScreenModel.AdoptLabel(selected).TrText();
        adopt.Disabled = PolicyScreenModel.AdoptCommand(selected) == null;
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

    private static Color ToColour(Rgb rgb) => new(rgb.R / 255f, rgb.G / 255f, rgb.B / 255f);

    private void SetMessage(string text) => message.Text = text;
}
