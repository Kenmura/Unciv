using Unciv.Commands;
using Unciv.Commands.GreatWorks;
using Unciv.Core.Tests.Logic.GreatWorks;
using Unciv.Models.Ruleset;

namespace Unciv.Core.Tests.Commands.GreatWorks;

// B007: one valid sample per Great Work command, on a world where each of them is accepted.
internal static class GreatWorkSamples
{
    public static readonly string[] Types = ["create-great-work", "move-great-work", "offer-great-work-swap", "take-great-work-swap"];

    /// <summary>A has a two-slot gallery holding ArtAncient and a Great Artist in the capital; B (at peace) offers ArtClassical.</summary>
    public sealed class Sample
    {
        public GreatWorkWorld World { get; } = new();
        public Building Gallery { get; }
        public Building GalleryB { get; }
        public int ArtistId { get; }

        public Sample()
        {
            World.MakePeace();
            Gallery = World.Build(World.CapA, 2, GreatWorkSlotType.ArtArtifact);
            World.Place(World.CapA, Gallery, 0, "ArtAncient", World.A.CivId);
            ArtistId = World.Game.AddUnit("Great Artist", World.A, World.CapA.GetCenterTile()).Id;
            GalleryB = World.Build(World.CityB, 1, GreatWorkSlotType.ArtArtifact);
            World.Place(World.CityB, GalleryB, 0, "ArtClassical", World.B.CivId, GreatWorkWorld.Classical);
            World.Game.GameInfo.GreatWorkSwapOffers = [new() { Civ = World.B.CivId, Work = "ArtClassical" }];
        }
    }

    public static GameCommand Command(string type, Sample s) => type switch
    {
        "create-great-work" => new CreateGreatWork { UnitId = s.ArtistId },
        "move-great-work" => new MoveGreatWork
        {
            FromCityId = s.World.CapA.Id,
            FromBuilding = s.Gallery.Name,
            FromSlot = 0,
            ToCityId = s.World.CapA.Id,
            ToBuilding = s.Gallery.Name,
            ToSlot = 1,
        },
        "offer-great-work-swap" => new OfferGreatWorkSwap { CityId = s.World.CapA.Id, Building = s.Gallery.Name, Slot = 0 },
        "take-great-work-swap" => new TakeGreatWorkSwap
        {
            CivId = s.World.B.CivId,
            Work = "ArtClassical",
            CityId = s.World.CapA.Id,
            Building = s.Gallery.Name,
            Slot = 0,
        },
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}
