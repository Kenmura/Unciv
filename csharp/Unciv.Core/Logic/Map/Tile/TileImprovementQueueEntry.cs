using Unciv.Json;

namespace Unciv.Logic.Map.Tiles;

// Split out of Tile.cs (B010) to keep that file within its size ratchet.
public partial class Tile
{
    internal class ImprovementQueueEntry(string improvement, int turnsToImprovement) : IsPartOfGameInfoSerialization
    {
        public string Improvement { get; } = improvement;
        public int TurnsToImprovement { get; private set; } = turnsToImprovement;
        public override string ToString() => $"{Improvement}: {TurnsToImprovement}⏳";
        /// <returns><c>true</c> if it's still counting and not finished</returns>
        public bool CountDown()
        {
            TurnsToImprovement = Math.Max(TurnsToImprovement - 1, 0);
            return TurnsToImprovement > 0;
        }
        public ImprovementQueueEntry Clone() => new(Improvement, TurnsToImprovement);
    }

    /// <summary>
    /// T293: the city owning this tile changed civilization (conquest, liberation, annexation). Loading a save re-assigns every city tile through
    /// <see cref="SetOwningCity"/>, which takes the city's civ as road owner; doing the same here keeps a live game equal to a reload of its save.
    /// Deliberate deviation from Kotlin, which left the previous civ as road owner of the tiles until the next load.
    /// </summary>
    internal void SetRoadOwnerToOwningCiv()
    {
        if (OwningCity is not { } city) return;
        roadOwner = city.Civ.CivId;
        roadOwnerObject = city.Civ;
    }
}
