using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Translations;
using Unciv.UI.Components;
using Unciv.UI.Components.Extensions;
using Unciv.UI.Screens.Civilopedia;
using Unciv.Utils;

namespace Unciv.UI.ObjectDescriptions;

// core/src/com/unciv/ui/objectdescriptions/TileDescription.kt

public static class TileDescription
{
    /// <summary>
    /// Get info on a selected tile, used on WorldScreen (right side above minimap), CityScreen or MapEditorViewTab.
    /// </summary>
    public static List<FormattedLine> ToMarkup(ITileView tileView, bool hideUnits = false, ICityView? spyCity = null)
    {
        var lineList = new List<FormattedLine>();
        var viewingCiv = tileView.GetCivView();
        var isViewableToPlayer = viewingCiv == null || tileView.IsVisibleMap || viewingCiv.CanSeeTile(tileView);

        if (tileView.IsCityCenter())
        {
            var cityView = tileView.OwningCity().NotNull();
            var cityString = cityView.Name.Tr();
            if (isViewableToPlayer) cityString += $" ({cityView.GetHealth()})";
            lineList.Add(new FormattedLine(cityString));
            if (tileView.IsVisibleMap || (viewingCiv != null && cityView.IsOwnedByViewer() && (spyCity == null || Equals(spyCity, cityView))))
                lineList.Add(cityView.GetProductionMarkup());
        }

        lineList.Add(new FormattedLine(tileView.BaseTerrain, link: $"Terrain/{tileView.BaseTerrain}"));
        foreach (var terrainFeature in tileView.TerrainFeatures)
            lineList.Add(new FormattedLine(terrainFeature, link: $"Terrain/{terrainFeature}"));

        var resource = tileView.GetViewableResource(viewingCiv);
        if (resource != null)
        {
            lineList.Add(resource.ResourceType == ResourceType.Strategic
                ? new FormattedLine($"{{{tileView.Resource}}} ({tileView.ResourceAmount})", link: $"Resource/{tileView.Resource}")
                : new FormattedLine(resource.Name, link: $"Resource/{resource.Name}"));
        }

        if (viewingCiv != null && resource != null)
            AddNeedsResearchLine(lineList, tileView, viewingCiv, resource);

        if (tileView.NaturalWonder != null)
            lineList.Add(new FormattedLine(tileView.NaturalWonder, link: $"Terrain/{tileView.NaturalWonder}"));

        if (tileView.RoadStatus != RoadStatus.None && !tileView.IsCityCenter())
        {
            var pillageText = tileView.RoadIsPillaged ? " (Pillaged!)" : "";
            lineList.Add(new FormattedLine($"[{tileView.RoadStatus}]{pillageText}", link: $"Improvement/{tileView.RoadStatus}"));
        }

        var shownImprovement = tileView.GetShownImprovement();
        if (shownImprovement != null)
        {
            var pillageText = tileView.ImprovementIsPillaged ? " (Pillaged!)" : "";
            lineList.Add(new FormattedLine($"[{shownImprovement}]{pillageText}", link: $"Improvement/{shownImprovement}"));
        }

        if (tileView.ImprovementInProgress != null && isViewableToPlayer)
        {
            // Negative turnsToImprovement is used for UniqueType.CreatesOneImprovement
            var line = $"{{{tileView.ImprovementInProgress}}}" +
                (tileView.TurnsToImprovement > 0 ? $" - {tileView.TurnsToImprovement}{Fonts.Turn}" : " ({Under construction})");
            lineList.Add(new FormattedLine(line, link: $"Improvement/{tileView.ImprovementInProgress}"));
        }

        if (tileView.CivilianUnit != null && isViewableToPlayer && !hideUnits)
        {
            var civilian = tileView.CivilianUnit.NotNull();
            lineList.Add(new FormattedLine(
                $"{civilian.Name.Tr()} - {civilian.CivName.Tr()}",
                link: $"Unit/{civilian.Name}"
            ));
        }

        if (tileView.MilitaryUnit != null && isViewableToPlayer && !hideUnits)
        {
            var military = tileView.MilitaryUnit.NotNull();
            var milUnitString = military.Name.Tr() +
                (military.UnitHealth < 100 ? $"({military.UnitHealth})" : "") +
                $" - {military.CivName.Tr()}";
            lineList.Add(new FormattedLine(milUnitString, link: $"Unit/{military.Name}"));
        }

        var defenceBonus = tileView.GetDefensiveBonus();
        if (defenceBonus != 0f)
        {
            var defencePercentString = (KotlinMath.FloatToInt(defenceBonus * 100)).ToStringSigned() + "%";
            lineList.Add(new FormattedLine($"[{defencePercentString}] to unit defence"));
        }

        if (tileView.IsImpassible()) lineList.Add(new FormattedLine(Constants.Impassable));
        if (tileView.IsLand && tileView.IsAdjacentTo(Constants.FreshWater)) lineList.Add(new FormattedLine(Constants.FreshWater));

        return lineList;
    }

    private static void AddNeedsResearchLine(List<FormattedLine> lineList, ITileView tileView, ICivView viewingCiv, TileResource resource)
    {
        var tileImprovements = resource.GetImprovements()
            .Select(it => tileView.GetRuleset().TileImprovements.GetValueOrDefault(it))
            .Where(it => it != null)
            .Select(it => it.NotNull())
            .ToList();

        if (tileImprovements.Any(it => viewingCiv.CanBuildImprovementOn(it, tileView)))
            return;

        var researchableImprovements = tileImprovements.Where(improvement =>
        {
            var problems = viewingCiv.GetImprovementBuildingProblems(improvement, tileView)
                .Where(it => it != ImprovementBuildingProblem.OutsideBorders)
                .ToList();
            return problems.Count != 0 && problems.All(it => it == ImprovementBuildingProblem.MissingTech);
        }).ToList();

        if (researchableImprovements.Count == 0) return;

        var techRequired = researchableImprovements
            .Select(it => viewingCiv.TechnologyByName(it.TechRequired))
            .Where(it => it != null && !viewingCiv.IsResearched(it.NotNull().Name))
            .Select(it => it.NotNull())
            .MinBy(it => it.Cost);

        if (techRequired == null) return;

        lineList.Add(new FormattedLine(
            $"Requires [{techRequired.Name}]",
            link: techRequired.MakeLink(),
            color: "#FAA"
        ));
    }
}
