using Godot;
using Unciv.GodotClient.Ui;
using Unciv.Models.Translations;
using Unciv.GodotClient.Localization;

namespace Unciv.GodotClient.Setup;

/// <summary>
/// C015: the first screen. New game, Resume last, Load game, Multiplayer (not available yet: C006), Options and Quit. It only raises events; the composition root decides what each does.
/// Resume last is disabled while there is nothing to resume (<see cref="SetCanResume"/>).
/// </summary>
public partial class MainMenu : Control
{
    private readonly Button resume;

    public MainMenu()
    {
        Name = "MainMenu";
        var centre = new CenterContainer();
        centre.Fill();
        AddChild(centre);
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(240, 0) };
        centre.AddChild(column);
        column.AddChild(new Label { Text = "Unciv".Tr(), HorizontalAlignment = HorizontalAlignment.Center });
        NewGame = Add(column, "New game", () => NewGameRequested?.Invoke());
        resume = Add(column, "Resume last", () => ResumeRequested?.Invoke());
        resume.Disabled = true;
        Add(column, "Load game", () => LoadRequested?.Invoke());
        var multiplayer = Add(column, "Multiplayer", () => { });
        multiplayer.Disabled = true;
        multiplayer.TooltipText = "Not available yet".Tr();
        Add(column, "Options", () => OptionsRequested?.Invoke());
        Add(column, "Quit", () => QuitRequested?.Invoke());
    }

    public Button NewGame { get; }

    public event Action? NewGameRequested;

    public event Action? ResumeRequested;

    public event Action? LoadRequested;

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
