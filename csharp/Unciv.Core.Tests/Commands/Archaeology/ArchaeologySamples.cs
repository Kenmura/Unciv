using Unciv.Commands;
using Unciv.Commands.Archaeology;
using Unciv.Core.Tests.Logic.Archaeology;

namespace Unciv.Core.Tests.Commands.Archaeology;

// B010: one valid sample of the dig command: A has a finished dig and a free gallery slot.
internal static class ArchaeologySamples
{
    public static readonly string[] Types = ["choose-dig-reward"];

    public sealed class Sample
    {
        public ArchaeologyWorld World { get; } = new();
        public Unciv.Logic.Map.Tiles.Tile Site { get; }

        public Sample()
        {
            World.W.MakePeace();
            Site = World.Site();
            World.FinishDig(Site);
        }
    }

    public static GameCommand Command(string type, Sample s) => type switch
    {
        "choose-dig-reward" => new ChooseDigReward { Site = s.Site.Position, TakeArtifact = true },
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}
