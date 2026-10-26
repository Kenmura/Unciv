using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C029: runs the hotseat hand-over for a <see cref="GameScreen"/>. When the screen reports that the next turn is another human's it is concealed in the same frame and an opaque
/// <see cref="HandOverView"/> takes its place; when that human presses Ready the game is handed to them (<see cref="LocalHost.TakeOverAsync"/> retires the previous player's session and
/// begins the new turn), the screen is started with the new session and revealed. Nothing of the previous player's game is drawn or reachable in between.
/// </summary>
public sealed partial class HotseatController : Node
{
    private readonly GameScreen screen;
    private readonly HandOverView cover;
    private readonly Func<IGameSession, AssetRegistry> registryFor;
    private readonly Func<string, string> displayName;
    private bool taking;

    /// <param name="screen">The game screen; it must already be in the tree.</param>
    /// <param name="cover">The pass-the-device cover; the owner has added it to the tree above the screen.</param>
    /// <param name="registryFor">The asset registry the screen is started with for a session.</param>
    /// <param name="displayName">Maps a civ id to the name the players see.</param>
    public HotseatController(GameScreen screen, HandOverView cover, Func<IGameSession, AssetRegistry> registryFor, Func<string, string> displayName)
    {
        Name = "Hotseat";
        this.screen = screen;
        this.cover = cover;
        this.registryFor = registryFor;
        this.displayName = displayName;
        screen.HandOverRequested += Cover;
        cover.Confirmed += civ => _ = TakeOver(civ);
    }

    /// <summary>Raised after the game was handed to the next player's new session (the owner re-attaches whatever follows the session, such as autosave).</summary>
    public event Action<LocalHost>? TookOver;

    /// <summary>True while the pass-the-device cover is up.</summary>
    public bool IsCovering => cover.Visible;

    /// <summary>
    /// Starts the screen with a game (new, loaded or handed over). A hotseat game whose current human has not begun their turn (as after loading it) starts behind the pass-the-device cover;
    /// any other game is shown straight away, also when the screen was concealed by the previous one.
    /// </summary>
    public void Present(IGameSession session)
    {
        screen.Start(session, registryFor(session));
        if (session is LocalHost { TurnNeedsBegin: true } waiting)
        {
            Cover(waiting.ViewerCivId);
            return;
        }
        cover.Close();
        if (screen.IsConcealed) screen.Reveal();
    }

    /// <summary>Covers the screen for a game that is waiting for <paramref name="civId"/> to begin (after a hotseat game was loaded, or when End turn handed over).</summary>
    public void Cover(string civId)
    {
        screen.Conceal();
        cover.Show(HotseatModel.HandOver(civId, displayName));
    }

    private async Task TakeOver(string civId)
    {
        if (taking || screen.Session is not LocalHost current) return;
        taking = true;
        cover.SetBusy();
        try
        {
            var next = await current.TakeOverAsync(civId);
            Present(next);
            TookOver?.Invoke(next);
        }
        catch (Exception e) when (e is InvalidOperationException)
        {
            GD.PushError("Hotseat hand-over failed: " + e);
            cover.ShowFailure(HotseatModel.FailureText(e.Message));
        }
        finally
        {
            taking = false;
        }
    }
}
