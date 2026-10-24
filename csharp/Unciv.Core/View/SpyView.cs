using Unciv.Models;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;

namespace Unciv.View;

// Ported from core/src/com/unciv/view/SpyView.kt (T084).
// Deferred to T085 because ForeignCityView is ported there: getCityViewOrNull, canMoveTo, tryMoveTo (TODO(T085)).

/// <summary>View of a <see cref="Spy"/> from the perspective of the viewer.</summary>
public class SpyView(Spy spy, CivilizationClass viewer, bool spectatorMode, GameView gameView)
    : OwnedView<Spy>(spy, viewer, spectatorMode, gameView)
{
    internal override CivilizationClass Owner() => Wrapped.CivInfo;

    // Data retrieval
    public string Name => Wrapped.Name;

    public int GetEffectiveRank() => Wrapped.GetEffectiveRank();
}
