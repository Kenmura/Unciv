using Godot;
using Unciv.Assets;
using Unciv.Assets.Localization;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Ui;

namespace Unciv.GodotClient.Settings;

/// <summary>
/// C028 / C075: the options screen: a <see cref="ScreenFrame"/> titled "Options" around the tabbed <see cref="SettingsPanel"/>. The close button raises <see cref="CloseRequested"/>
/// and the owner decides what closing means. Every control applies and saves through <see cref="ClientSettingsService"/> the moment it changes; the panel's members are reachable
/// through <see cref="Panel"/>.
/// </summary>
public partial class OptionsScreen : Control
{
    private readonly ScreenFrame frame;

    public OptionsScreen(ClientSettingsService service, UiTheme theme, ClientLocalization? localization = null, IEnumerable<string>? packDirectories = null, Unciv.Models.Ruleset.Ruleset? tutorialRuleset = null)
    {
        Name = "OptionsScreen";
        Theme = theme.Theme;
        var backdrop = new ColorRect { Name = "Backdrop", Color = ProceduralAssets.ToColor(UiTokens.Bg1), MouseFilter = MouseFilterEnum.Ignore };
        backdrop.Fill();
        AddChild(backdrop);
        frame = new ScreenFrame(theme, "Options");
        frame.Fill();
        AddChild(frame);
        Panel = new SettingsPanel(service, theme, localization, packDirectories, tutorialRuleset);
        var cap = new WidthCap(theme, UiTokens.MaxOptionsWidth); // T322: tabs and page stay together on a 4K or ultrawide window
        cap.AddChild(Panel);
        frame.SetContent(cap);
        frame.CloseRequested += () => CloseRequested?.Invoke();
    }

    public SettingsPanel Panel { get; }

    public ScreenFrame Frame => frame;

    public event Action? CloseRequested;
}
