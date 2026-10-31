using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Localization;

namespace Unciv.GodotClient.Ui;

/// <summary>C065: what a list shows when there is nothing in it: a muted icon, one line of text and an optional action ("No saves yet." and "Save game"). T339: the constructor option wrap lets a long sentence wrap to the width it is given.</summary>
public partial class EmptyState : VBoxContainer
{
    private readonly Label message = new() { HorizontalAlignment = HorizontalAlignment.Center, ThemeTypeVariation = UiVariants.LabelSecondary };

    public EmptyState(UiTheme theme, AssetKey? icon, string text, string? actionText = null, bool wrap = false)
    {
        Alignment = AlignmentMode.Center;
        this.FitConstant(theme, "separation", UiTokens.S2);
        if (icon is { } key)
        {
            AddChild(new TextureRect
            {
                Texture = theme.Icon(key),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                TextureFilter = TextureFilterEnum.LinearWithMipmaps,
                Modulate = ProceduralAssets.ToColor(UiTokens.TextMuted),
                SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
                MouseFilter = MouseFilterEnum.Ignore,
            }.FitMin(theme, UiTokens.S8, UiTokens.S8));
        }
        if (wrap)
        {
            message.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            message.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        }
        message.Text = text.TrText();
        AddChild(message);
        if (actionText == null) return;
        ActionButton = new Button { Text = actionText.TrText(), SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        ActionButton.Pressed += () => ActionPressed?.Invoke();
        AddChild(ActionButton);
    }

    public string Message => message.Text;

    /// <summary>C075: changes the line (a list that is empty for different reasons).</summary>
    public void SetMessage(string text) => message.Text = text.TrText();

    /// <summary>The action button, or null when there is no action.</summary>
    public Button? ActionButton { get; }

    public event Action? ActionPressed;
}
