using Unciv.Logic.Civilization.Diplomacy;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;

namespace Unciv.View;

// Ported from core/src/com/unciv/view/DiplomacyManagerView.kt (T084).

/// <summary>View of a <see cref="DiplomacyManager"/> from the perspective of the viewer via the <see cref="GameView"/>.</summary>
public class DiplomacyManagerView(DiplomacyManager diplomacyManager, CivilizationClass viewer, bool spectatorMode, GameView gameView)
    : GameBasedView<DiplomacyManager>(diplomacyManager, viewer, spectatorMode, gameView)
{
    /// <summary>The public war/peace/etc. status of this relationship (T212).</summary>
    public DiplomaticStatus Status => Wrapped.DiplomaticStatus;

    public bool CanDeclareWar() => Wrapped.CanDeclareWar();
}
