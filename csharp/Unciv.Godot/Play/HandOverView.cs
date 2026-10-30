using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Localization;
using Unciv.GodotClient.Ui;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C029: the hotseat "pass the device" screen (<see cref="HotseatModel"/>). A fully opaque full-screen cover (the dim of other overlays would let the map show through) with the next
/// player's name and a Ready button; it takes every mouse event, so nothing underneath can be used while the device changes hands.
/// C066: a <see cref="Modal"/> with an opaque scrim; Escape is swallowed (there is nothing to cancel, and the game menu must not open behind the cover).
/// </summary>
public partial class HandOverView : Modal
{
    private readonly Label message = new() { Name = "HandOverMessage", HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label failure = new() { Name = "HandOverFailure", HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false };
    private readonly Button ready = new() { Name = "ReadyButton" };

    public HandOverView()
    {
        Name = "HandOver";
        TitleLabel.Name = "HandOverHeading";
        TitleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        Opaque = true;
        Body.AddChild(message);
        Body.AddChild(failure);
        SetActions([(ready, ChoiceRole.Primary)]);
        ready.Pressed += () =>
        {
            if (CivId is { } civ) Confirmed?.Invoke(civ);
        };
    }

    /// <summary>Raised with the civ id when the next player presses Ready.</summary>
    public event Action<string>? Confirmed;

    /// <summary>The civ the game is handed to while the screen is up, else null.</summary>
    public string? CivId { get; private set; }

    public string HeadingText => TitleLabel.Text;

    public string MessageText => message.Text;

    public string FailureLabelText => failure.Visible ? failure.Text : "";

    public Button ReadyButton => ready;

    public override void Init(UiTheme uiTheme)
    {
        base.Init(uiTheme);
        failure.AddThemeColorOverride("font_color", ProceduralAssets.ToColor(UiTokens.DangerText));
    }

    public override void _Ready() => GetViewport().SizeChanged += Layout;

    public override void _ExitTree() => GetViewport().SizeChanged -= Layout;

    // Sized from the viewport, like the game screen: the parent may be a Control without a size (the scenario runner).
    private void Layout() => Size = GetViewportRect().Size;

    /// <summary>Covers the screen with this view.</summary>
    public void Show(HandOverViewData view)
    {
        CivId = view.CivId;
        SetHeader(view.Heading);
        Fit(Modal.MinWidth, message); // T323: re-fitted on a live resize
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

    protected override bool OnCancel() => CivId != null;
}
