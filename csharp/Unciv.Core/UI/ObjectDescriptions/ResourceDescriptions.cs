using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.UI.Screens.Civilopedia;

namespace Unciv.UI.ObjectDescriptions;

// core/src/com/unciv/ui/objectdescriptions/ResourceDescriptions.kt

public static class ResourceDescriptions
{
    public static List<FormattedLine> GetCivilopediaTextLines(TileResource resource, CivilopediaContext context)
    {
        var ruleset = context.Ruleset;
        var textList = new List<FormattedLine>
        {
            new($"{resource.ResourceType} resource", header: 4, color: resource.ResourceType.GetColorString()),
            new()
        };

        resource.UniquesToCivilopediaTextLines(textList, context);

        if (!((Stats)resource).IsEmpty())
            textList.Add(new FormattedLine(resource.CloneStats().ToString()));

        if (!string.IsNullOrEmpty(resource.RevealedBy))
        {
            textList.Add(new FormattedLine());
            textList.Add(new FormattedLine("Revealed by:"));
            textList.Add(new FormattedLine(resource.RevealedBy, link: $"Technology/{resource.RevealedBy}", indent: 1));
        }

        if (resource.TerrainsCanBeFoundOn.Count != 0)
        {
            textList.Add(new FormattedLine());
            if (resource.TerrainsCanBeFoundOn.Count == 1)
            {
                var terrainName = resource.TerrainsCanBeFoundOn[0];
                textList.Add(new FormattedLine($"{{Can be found on}} {{{terrainName}}}", link: $"Terrain/{terrainName}"));
            }
            else
            {
                textList.Add(new FormattedLine("{Can be found on}:"));
                foreach (var terrainName in resource.TerrainsCanBeFoundOn)
                    textList.Add(new FormattedLine(terrainName, link: $"Terrain/{terrainName}", indent: 1));
            }
        }

        foreach (var improvement in resource.GetImprovements())
        {
            textList.Add(new FormattedLine());
            textList.Add(new FormattedLine($"Improved by [{improvement}]", link: $"Improvement/{improvement}"));
            if (resource.ImprovementStats != null && !resource.ImprovementStats.IsEmpty())
                textList.Add(new FormattedLine("{Bonus stats for improvement}: " + resource.ImprovementStats.ToString()));
        }

        var improvementsThatProvideThis = ruleset.TileImprovements.Values
            .Where(improvement => ((IHasUniques)improvement).UniqueObjects.Any(unique =>
                unique.Type == UniqueType.ProvidesResources && unique.Params[1] == resource.Name))
            .ToList();
        if (improvementsThatProvideThis.Count != 0)
        {
            textList.Add(new FormattedLine());
            textList.Add(new FormattedLine("{Improvements that provide this resource}:"));
            foreach (var it in improvementsThatProvideThis)
                textList.Add(new FormattedLine(it.Name, link: it.MakeLink(), indent: 1));
        }

        var buildingsThatProvideThis = ruleset.Buildings.Values
            .Where(building => ((IHasUniques)building).UniqueObjects.Any(unique =>
                unique.Type == UniqueType.ProvidesResources && unique.Params[1] == resource.Name))
            .ToList();
        if (buildingsThatProvideThis.Count != 0)
        {
            textList.Add(new FormattedLine());
            textList.Add(new FormattedLine("{Buildings that provide this resource}:"));
            foreach (var it in buildingsThatProvideThis)
                textList.Add(new FormattedLine(it.Name, link: it.MakeLink(), indent: 1));
        }

        var technologiesThatProvideThis = ruleset.Technologies.Values
            .Where(technology => ((IHasUniques)technology).UniqueObjects.Any(unique =>
                unique.Type == UniqueType.ProvidesResources && unique.Params[1] == resource.Name))
            .ToList();
        if (technologiesThatProvideThis.Count != 0)
        {
            textList.Add(new FormattedLine());
            textList.Add(new FormattedLine("{Technologies that provide this resource}:"));
            foreach (var it in technologiesThatProvideThis)
                textList.Add(new FormattedLine(it.Name, link: it.MakeLink(), indent: 1));
        }

        var buildingsThatConsumeThis = ruleset.Buildings.Values
            .Where(it => it.GetResourceRequirementsPerTurn(GameContext.IgnoreConditionalsState).ContainsKey(resource.Name))
            .ToList();
        if (buildingsThatConsumeThis.Count != 0)
        {
            textList.Add(new FormattedLine());
            textList.Add(new FormattedLine("{Buildings that consume this resource}:"));
            foreach (var it in buildingsThatConsumeThis)
                textList.Add(new FormattedLine(it.Name, link: it.MakeLink(), indent: 1));
        }

        var unitsThatConsumeThis = ruleset.Units.Values
            .Where(it => it.GetResourceRequirementsPerTurn(GameContext.IgnoreConditionalsState).ContainsKey(resource.Name))
            .ToList();
        if (unitsThatConsumeThis.Count != 0)
        {
            textList.Add(new FormattedLine());
            textList.Add(new FormattedLine("{Units that consume this resource}: "));
            foreach (var it in unitsThatConsumeThis)
                textList.Add(new FormattedLine(it.Name, link: it.MakeLink(), indent: 1));
        }

        var buildingsRequiringThis = ruleset.Buildings.Values
            .Where(it => it.RequiredNearbyImprovedResources?.Contains(resource.Name) == true)
            .ToList();
        if (buildingsRequiringThis.Count != 0)
        {
            textList.Add(new FormattedLine());
            textList.Add(new FormattedLine("{Buildings that require this resource improved near the city}: "));
            foreach (var it in buildingsRequiringThis)
                textList.Add(new FormattedLine(it.Name, link: it.MakeLink(), indent: 1));
        }

        textList.AddRange(Belief.GetCivilopediaTextMatching(resource.Name, ruleset, true, context.GameInfo));

        return textList;
    }

    public static List<FormattedLine> GetCivilopediaTextLines(TileResource resource, Ruleset ruleset) =>
        GetCivilopediaTextLines(resource, new CivilopediaContext(ruleset));
}
