using Godot;
using Unciv.GodotClient.Localization;

namespace Unciv.GodotClient.Ui;

/// <summary>One button of a popup. <see cref="OnChosen"/> runs after the popup has closed.</summary>
public sealed record PopupChoice(string Text, Action? OnChosen = null);

/// <summary>A popup: title, body text and its choices. Escape activates choice <see cref="CancelIndex"/> (default: the last one).</summary>
public sealed record PopupSpec(string Title, string Body, IReadOnlyList<PopupChoice> Choices, int? CancelIndex = null)
{
    public static PopupSpec Ok(string title, string body, Action? onOk = null) => new(title, body, [new PopupChoice("OK", onOk)]);
}

/// <summary>
/// Modal popups, one at a time: showing a popup while another is open queues it (first in, first out). While a popup is open the
/// stack swallows mouse input over the whole window; when none is, it is invisible and passes input through.
/// </summary>
public partial class PopupStack : Control
{
    private readonly UiTheme theme;
    private readonly Queue<PopupSpec> pending = new();
    private Control? view;

    public PopupStack(UiTheme theme)
    {
        this.theme = theme;
        this.Fill(); MouseFilter = MouseFilterEnum.Ignore;
    }

    public PopupSpec? Current { get; private set; }

    public IReadOnlyList<string> PendingTitles => pending.Select(it => it.Title).ToList();

    public void Show(PopupSpec spec)
    {
        if (spec.Choices.Count == 0) throw new ArgumentException("A popup needs at least one choice", nameof(spec));
        if (Current == null) Display(spec);
        else pending.Enqueue(spec);
    }

    /// <summary>Closes the open popup as Escape would (its cancel choice). Returns false when none is open.</summary>
    public bool Cancel()
    {
        if (Current == null) return false;
        Choose(Current.CancelIndex ?? Current.Choices.Count - 1);
        return true;
    }

    private void Display(PopupSpec spec)
    {
        Current = spec;
        MouseFilter = MouseFilterEnum.Stop;
        var dim = theme.Panel.BgColor;
        dim.A = 0.7f;
        var layer = new Control { MouseFilter = MouseFilterEnum.Stop };
        layer.Fill();
        var backdrop = new ColorRect { Color = dim };
        backdrop.Fill();
        layer.AddChild(backdrop);
        var centre = new CenterContainer();
        centre.Fill();
        layer.AddChild(centre);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(theme.Px(320), 0) };
        centre.AddChild(panel);
        var column = new VBoxContainer();
        panel.AddChild(column);
        column.AddChild(new Label { Text = spec.Title.TrText(), HorizontalAlignment = HorizontalAlignment.Center });
        column.AddChild(new Label { Text = spec.Body.TrText(), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(theme.Px(300), 0) });
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        column.AddChild(buttons);
        for (int i = 0; i < spec.Choices.Count; i++)
        {
            int index = i;
            var button = new Button { Text = spec.Choices[i].Text.TrText() };
            button.Pressed += () => Choose(index);
            buttons.AddChild(button);
        }
        AddChild(layer);
        view = layer;
    }

    private void Choose(int index)
    {
        var closing = Current;
        if (closing == null) return;
        Current = null;
        view?.QueueFree();
        view = null;
        MouseFilter = MouseFilterEnum.Ignore;
        if (pending.Count > 0) Display(pending.Dequeue());
        closing.Choices[index].OnChosen?.Invoke();
    }
}
