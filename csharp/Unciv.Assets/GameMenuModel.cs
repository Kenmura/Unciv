using Unciv.Queries.Victory;

namespace Unciv.Assets;

public enum GameMenuKind
{
    /// <summary>Close the menu.</summary>
    Resume,
    Save,
    Load,
    NewGame,
    VictoryStatus,
    Options,

    /// <summary>Submits the engine's <c>Resign</c> command for the viewer.</summary>
    Resign,
    MainMenu,
}

/// <summary>The question asked before a destructive entry runs. Declining does nothing.</summary>
public sealed record GameMenuConfirm(string Title, string Body, string ConfirmLabel);

/// <summary><see cref="Id"/> is stable (node names, tests). A disabled entry is shown greyed with <see cref="DisabledReason"/> as its tooltip.</summary>
public sealed record GameMenuEntry(string Id, GameMenuKind Kind, string Label, bool Enabled, string? DisabledReason, GameMenuConfirm? Confirm);

/// <summary>
/// C044: the in-game menu as plain data, Godot-free so it is unit-tested. Which entries exist, which are refused and which ask first. It decides no game fact:
/// whether the viewer may resign is the engine's (<c>Resign</c> validates the actor); the menu only greys the entry for a defeated viewer, which is what the engine would refuse.
/// Texts are translation keys the client translates with <c>Tr()</c>.
/// </summary>
public static class GameMenuModel
{
    public const string Title = "Menu";

    public const string CancelLabel = "Cancel";

    public const string CloseLabel = "Close";

    public static IReadOnlyList<GameMenuEntry> Build(string gameOverStatus, bool unsavedProgress)
    {
        bool defeated = gameOverStatus == GameOverQuery.Defeated;
        return
        [
            new("Resume", GameMenuKind.Resume, "Resume game", true, null, null),
            new("Save", GameMenuKind.Save, "Save game", true, null, null),
            new("Load", GameMenuKind.Load, "Load game", true, null, unsavedProgress ? Leaving("Load game", "Load a saved game") : null),
            new("NewGame", GameMenuKind.NewGame, "Start new game", true, null, unsavedProgress ? Leaving("Start new game", "Start a new game") : null),
            new("VictoryStatus", GameMenuKind.VictoryStatus, "Victory status", true, null, null),
            new("Options", GameMenuKind.Options, "Options", true, null, null),
            new("Resign", GameMenuKind.Resign, "Resign", !defeated, defeated ? "Your civilization is already defeated" : null,
                new GameMenuConfirm("Resign", "Do you want to resign? Your civilization will be played by the AI.", "Resign")),
            new("MainMenu", GameMenuKind.MainMenu, "Main menu", true, null, unsavedProgress ? Leaving("Main menu", "Leave for the main menu") : null),
        ];
    }

    private static GameMenuConfirm Leaving(string title, string confirmLabel) =>
        new(title, "The game has progress that is not saved. Continue without saving?", confirmLabel);

    /// <summary>The wording after an accepted resignation (the game-over screen's text for it).</summary>
    public const string ResignedTitle = "Resigned";

    public const string ResignedText = "You resigned. Your civilization is now played by the AI.";
}
