using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Localization;
using Unciv.GodotClient.Ui;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C029: the hotseat "pass the device" screen (<see cref="HotseatModel"/>). A fully opaque full-screen cover (the dim of other overlays would let the map show through) with the next
/// player's name and a Ready button; it takes every mouse event, so nothing underneath can be used while the device changes hands.
/// </summary>
public partial class HandOverView : Control
{
    private readonly ColorRect cover = new() { Name = "Cover", MouseFilter = MouseFilterEnum.Ignore };
    private readonly Label heading = new() { Name = "HandOverHeading", HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Label message = new() { Name = "HandOverMessage", HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Label failure = new() { Name = "HandOverFailure", HorizontalAlignment = HorizontalAlignment.Center, Visible = false };
    private readonly Button ready = new() { Name = "ReadyButton" };

    public HandOverView()
    {
        Name = "HandOver";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        cover.Fill();
        AddChild(cover);
        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        centre.Fill();
        AddChild(centre);
        var column = new VBoxContainer { Name = "HandOverColumn" };
        centre.AddChild(column);
        column.AddChild(heading);
        column.AddChild(message);
        column.AddChild(ready);
        column.AddChild(failure);
        ready.Pressed += () =>
        {
            if (CivId is { } civ) Confirmed?.Invoke(civ);
        };
    }

    /// <summary>Raised with the civ id when the next player presses Ready.</summary>
    public event Action<string>? Confirmed;

    /// <summary>The civ the game is handed to while the screen is up, else null.</summary>
    public string? CivId { get; private set; }

    public string HeadingText => heading.Text;

    public string MessageText => message.Text;

    public string FailureLabelText => failure.Visible ? failure.Text : "";

    public Button ReadyButton => ready;

    public void Init(UiTheme uiTheme)
    {
        Theme = uiTheme.Theme;
        cover.Color = uiTheme.Panel.BgColor with { A = 1f };
        heading.AddThemeFontSizeOverride("font_size", uiTheme.Px(26));
    }

    public override void _Ready() => GetViewport().SizeChanged += Layout;

    public override void _ExitTree() => GetViewport().SizeChanged -= Layout;

    // Sized from the viewport, like the game screen: the parent may be a Control without a size (the scenario runner).
    private void Layout() => Size = GetViewportRect().Size;

    /// <summary>Covers the screen with this view.</summary>
    public void Show(HandOverViewData view)
    {
        CivId = view.CivId;
        heading.Text = view.Heading.TrText();
        message.Text = view.Message.TrText();
        ready.Text = view.ButtonText.TrText();
        failure.Visible = false;
        ready.Disabled = false;
        Layout();
        Visible = true;
        ready.GrabFocus();
    }

    /// <summary>Shows why taking over failed; the cover stays.</summary>
    public void ShowFailure(string text)
    {
        failure.Text = text.TrText();
        failure.Visible = true;
        ready.Disabled = false;
    }

    /// <summary>Disables Ready while the new turn is being started, so a double press cannot start it twice.</summary>
    public void SetBusy() => ready.Disabled = true;

    public void Close()
    {
        CivId = null;
        Visible = false;
    }
}
