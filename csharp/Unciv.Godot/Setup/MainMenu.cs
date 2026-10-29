using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Ui;
using Unciv.Models.Translations;
using Unciv.GodotClient.Localization;

namespace Unciv.GodotClient.Setup;

/// <summary>
/// C015: the first screen. New game, Resume last, Load game, Multiplayer (C006: join a hosted game), Host game (C034), Options and Quit. It only raises events; the composition root decides what each does.
/// Resume last is disabled while there is nothing to resume (<see cref="SetCanResume"/>).
/// </summary>
public partial class MainMenu : Control
{
    private readonly Button resume;

    /// <param name="theme">C101: when given, the column is 260 px at the theme's scale (wider on a touch build; a mouse build with the default scale gets 260 px); without it 260 px.</param>
    public MainMenu(UiTheme? theme = null)
    {
        Name = "MainMenu";
        var backdrop = new ColorRect { Name = "Backdrop", Color = ProceduralAssets.ToColor(UiTokens.Bg1), MouseFilter = MouseFilterEnum.Ignore };
        backdrop.Fill();
        AddChild(backdrop);
        var centre = new CenterContainer();
        centre.Fill();
        AddChild(centre);
        var column = new VBoxContainer { Name = "MenuColumn", CustomMinimumSize = new Vector2(theme?.Px(260) ?? 260, 0) };
        column.AddThemeConstantOverride("separation", UiTokens.S2);
        centre.AddChild(column);
        column.AddChild(new Label { Name = "Logo", Text = "Unciv".Tr(), HorizontalAlignment = HorizontalAlignment.Center, ThemeTypeVariation = UiVariants.LabelDisplay });
        var rule = new ColorRect { Name = "LogoRule", Color = ProceduralAssets.ToColor(UiTokens.Accent), CustomMinimumSize = new Vector2(0, UiTokens.HairlineWidth) };
        column.AddChild(rule);
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, UiTokens.S3) });
        NewGame = Add(column, "New game", () => NewGameRequested?.Invoke());
        NewGame.ThemeTypeVariation = UiVariants.ButtonPrimary;
        resume = Add(column, "Resume last", () => ResumeRequested?.Invoke());
        resume.Disabled = true;
        Add(column, "Load game", () => LoadRequested?.Invoke());
        Add(column, "Multiplayer", () => JoinRequested?.Invoke());
        Add(column, "Host game", () => HostRequested?.Invoke());
        Add(column, "Options", () => OptionsRequested?.Invoke());
        Add(column, "Quit", () => QuitRequested?.Invoke()).ThemeTypeVariation = UiVariants.ButtonGhost;
    }

    public Button NewGame { get; }

    public event Action? NewGameRequested;

    public event Action? ResumeRequested;

    public event Action? LoadRequested;

    public event Action? JoinRequested;

    public event Action? HostRequested;

    public event Action? OptionsRequested;

    public event Action? QuitRequested;

    public bool CanResume => !resume.Disabled;

    public void SetCanResume(bool canResume) => resume.Disabled = !canResume;

    private static Button Add(Container parent, string text, Action pressed)
    {
        var button = new Button { Text = text.TrText() };
        button.Pressed += pressed;
        parent.AddChild(button);
        return button;
    }
}
