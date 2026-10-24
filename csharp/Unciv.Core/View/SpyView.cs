using Unciv.Models;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;

namespace Unciv.View;

// Ported from core/src/com/unciv/view/SpyView.kt (T084).

/// <summary>View of a <see cref="Spy"/> from the perspective of the viewer.</summary>
public class SpyView(Spy spy, CivilizationClass viewer, bool spectatorMode, GameView gameView)
    : OwnedView<Spy>(spy, viewer, spectatorMode, gameView)
{
    internal override CivilizationClass Owner() => Wrapped.CivInfo;

    // Navigation
    public ForeignCityView? GetCityViewOrNull()
    {
        var city = Wrapped.GetCityOrNull();
        return city == null ? null : GameView.GetForeignCityView(city);
    }

    // Data retrieval
    public string Name => Wrapped.Name;

    public int GetEffectiveRank() => Wrapped.GetEffectiveRank();

    public bool CanMoveTo(ForeignCityView foreignCityView) => Wrapped.CanMoveTo(foreignCityView.Unwrap());

    // Actions
    public bool TryMoveTo(ForeignCityView? foreignCityView)
    {
        Wrapped.MoveTo(foreignCityView?.Unwrap());
        return true;
    }
}
