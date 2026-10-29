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
}
