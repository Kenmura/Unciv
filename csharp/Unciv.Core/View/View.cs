using CivilizationClass = Unciv.Logic.Civilization.Civilization;

namespace Unciv.View;

// Ported from core/src/com/unciv/view/View.kt, GameBasedView.kt and OwnedView.kt (T084).
// Stripped: the yairm210.purity annotations (@Readonly/@Cache/@LocalState), which have no C# counterpart. A member that Kotlin marks
// @Readonly is read-only by convention; "try..." members (Actions sections) mutate state.
// Kotlin's `protected fun <U> View<U>.unwrap()` (lets any View read any other View's wrapped object without exposing it publicly) is
// the assembly-internal extension ViewExtensions.Unwrap, because C# protected access does not reach other instances of other types.

/// <summary>
/// A View is the API a player has, to the game; It defines allowed behavior for a player.
/// It allows players to access data and functions they have permission to access.
/// All UI code should use ONLY views, and all "automate as civ" code should do the same,
/// if we want to retain "legal access to known data" for AI automation (this may be a non-goal. TBD)
/// <list type="bullet">
/// <item>Any ruleset objects are fair game - the entire ruleset should be available, AND they should all be readonly anyway.</item>
/// <item>Any game state objects (Civilization, City, Tile, Unit, etc) should be wrapped in a View, and only exposed to the player via that View. This applies to function inputs and outputs.</item>
/// <item>Any data accessed in the UI from the base object directly should be converted to a readonly function (not optional) to get that data.</item>
/// <item>Data should use readonly interfaces when possible.</item>
/// <item>Any state-changing function in the UI should be converted to a boolean-returning "try apply state change" function; no setters. Only objects under the user's control (civ, city, unit) have state-changing functions.</item>
/// <item>Inputs to functions should be ID-able ruleset objects and game objects (civ, city, tile, mapunit).</item>
/// <item>Retain minimal API - anything the UI can derive from existing calls should not be part of the view.</item>
/// </list>
/// </summary>
public class View<T>(
    T wrapped,
    CivilizationClass? viewer,
    bool spectatorMode = false) : IViewWrapped
{
    internal T Wrapped { get; } = wrapped;

    /// <summary>
    /// The civ we're viewing as.
    /// Spectators can either be viewing as themselves - full view permissions - or as another civ,
    /// in which case they will "see" only what that civ sees.
    /// </summary>
    protected virtual CivilizationClass? Viewer { get; } = viewer;

    /// <summary>
    /// Indicates whether we are really a spectator, "looking in" to the view of another civ.
    /// In this case we should not be able to execute any state-changing action.
    /// </summary>
    public bool SpectatorMode { get; } = spectatorMode;

    // Required for equality checks in collections, and for using views as keys in maps
    public override bool Equals(object? obj) => obj is IViewWrapped other && Equals(Wrapped, other.WrappedObject);

    object? IViewWrapped.WrappedObject => Wrapped;

    public override int GetHashCode() => Wrapped is null ? 0 : Wrapped.GetHashCode();
}

/// <summary>Non-generic window onto <see cref="View{T}.Wrapped"/>, so views of different wrapped types can compare themselves.</summary>
internal interface IViewWrapped
{
    object? WrappedObject { get; }
}

internal static class ViewExtensions
{
    /// <summary>Lets any view read the wrapped object of any other view, without exposing it outside the assembly.</summary>
    internal static U Unwrap<U>(this View<U> view) => view.Wrapped;
}

/// <summary>Base class for views that always know their viewer civilization.</summary>
public class GameBasedView<T>(T wrapped, CivilizationClass viewer, bool spectatorMode, GameView gameView)
    : View<T>(wrapped, viewer, spectatorMode)
{
    protected override CivilizationClass Viewer { get; } = viewer;

    public virtual GameView GameView { get; } = gameView;
}

/// <summary>Base class for <see cref="GameBasedView{T}"/>s that represent something owned by a specific civ (a city, a unit, a spy, etc).</summary>
public abstract class OwnedView<T>(T wrapped, CivilizationClass viewer, bool spectatorMode, GameView gameView)
    : GameBasedView<T>(wrapped, viewer, spectatorMode, gameView)
{
    internal abstract CivilizationClass Owner();

    public bool IsOwnedByViewer() => ReferenceEquals(Owner(), Viewer);
}
