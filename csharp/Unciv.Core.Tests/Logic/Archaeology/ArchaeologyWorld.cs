using Unciv.Core.Tests.Logic.GreatWorks;
using Unciv.Logic.Archaeology;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using UnitObject = Unciv.Logic.Map.MapUnits.MapUnit;

namespace Unciv.Core.Tests.Logic.Archaeology;

// B010: the Great Work world with archaeology switched on, plus the ruleset objects a BNW ruleset brings: the two site resources, the dig and
// the Landmark, and a unit that can dig. Artifacts: FindAncient (Battle Melee, Ancient), FindClassical (Battle Melee, Classical),
// FindOther (Battle Melee, no era), FindRazed (Razed City, Ancient). Writing: HiddenWriting (archaeology only).
internal sealed class ArchaeologyWorld
{
    public GreatWorkWorld W { get; }
    public TileImprovement Dig { get; }
    public TileImprovement Landmark { get; }
    public Building Gallery { get; }

    public ArchaeologyWorld(bool enabled = true)
    {
        W = new GreatWorkWorld(archaeology: enabled);
        var ruleset = W.Game.Ruleset;
        void Artifact(string name, string artifactClass, string era) =>
            ruleset.GreatWorks.Put(name, new GreatWork { Name = name, GreatWorkClass = GreatWorkClass.Artifact, Era = era, ArtifactClass = artifactClass });
        Artifact("FindAncient", "Battle Melee", GreatWorkWorld.Ancient);
        Artifact("FindClassical", "Battle Melee", GreatWorkWorld.Classical);
        Artifact("FindOther", "Battle Melee", "");
        Artifact("FindRazed", "Razed City", GreatWorkWorld.Ancient);
        foreach (var name in new[] { ArchaeologyRules.AntiquitySite, ArchaeologyRules.HiddenAntiquitySite })
        {
            var resource = new TileResource { Name = name };
            resource.SetTransients(ruleset);
            ruleset.TileResources.Put(name, resource);
        }
        Dig = W.Game.CreateTileImprovement("Is an Archaeological Dig");
        Dig.TerrainsCanBeBuiltOn = ["Land"];
        Dig.TurnsToBuild = 6;
        Landmark = W.Game.CreateTileImprovement("Is a Landmark", "[+1 Culture] per era since discovery");
        Landmark.TerrainsCanBeBuiltOn = ["Land"];
        Gallery = W.Build(W.CapA, 2, GreatWorkSlotType.ArtArtifact);
    }

    /// <summary>A tile next to A's capital (so A owns it) with a record and an Antiquity Site.</summary>
    public Tile Site(ArtifactClass artifactClass = ArtifactClass.BattleMelee, string era = GreatWorkWorld.Ancient, string p1 = "P1", string p2 = "P2", bool hidden = false)
    {
        var tile = W.CapA.GetCenterTile().GetTilesAtDistance(1).First(it => it.IsLand && it.Improvement == null && it.Resource == null && it.ArchaeologyRecord == null);
        tile.SetTileResource(hidden ? ArchaeologyRules.HiddenAntiquitySite : ArchaeologyRules.AntiquitySite);
        tile.ArchaeologyRecord = new ArchaeologyRecord { ArtifactClass = artifactClass, Era = era, Player1 = p1, Player2 = p2 };
        return tile;
    }

    /// <summary>A civilian of <paramref name="civ"/> that has just finished the dig on <paramref name="tile"/>.</summary>
    public UnitObject FinishDig(Tile tile, Unciv.Logic.Civilization.Civilization? civ = null)
    {
        civ ??= W.A;
        var unit = W.Game.AddUnit("Worker", civ, tile);
        tile.SetImprovement(Dig.Name, civ, unit);
        ArchaeologyRules.OnImprovementBuilt(tile, unit);
        return unit;
    }
}
