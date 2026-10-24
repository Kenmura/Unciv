using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Translations;
using Unciv.UI.Screens.Civilopedia;
using Unciv.Utils;
using NationObject = Unciv.Models.Ruleset.Nation.Nation;

namespace Unciv.UI.ObjectDescriptions;

// core/src/com/unciv/ui/objectdescriptions/NationDescriptions.kt: Nation.getNationCivilopediaTextLines and its helpers
// (Personality.getShortDescription is Personality.ToString, the two Personality Civilopedia members are in PersonalityDescriptions.cs).

public static class NationDescriptions
{
    public static List<FormattedLine> GetNationCivilopediaTextLines(this NationObject nation, CivilopediaContext context)
    {
        var ruleset = context.Ruleset;
        return FormattedLineListBuilder.BuildCivilopediaText(context, b =>
        {
            var personalityObj = nation.Personality == null ? null : ruleset.Personalities.GetValueOrDefault(nation.Personality);

            if (nation.IsCityState) AddCityStateInfo(b, nation, ruleset);

            if (nation.LeaderName.Length != 0)
            {
                b.Add(extraImage: $"LeaderIcons/{nation.LeaderName}", imageSize: 200f);
                b.Add(nation.GetLeaderDisplayName(), centered: true, header: 3);
            }
            if (personalityObj != null)
                b.Add($"{{Personality}}: {{{personalityObj}}}", link: personalityObj.MakeLink(), centered: true);
            if (nation.LeaderName.Length != 0 || personalityObj != null)
                b.Space();

            if (nation.UniqueName != "")
                b.Add($"{{{nation.UniqueName}}}:", header: 4);
            if (nation.UniqueText != "")
                b.Add(nation.UniqueText, indent: 1);
            else
                b.AddUniques(nation, SeparatorType.None);
            b.Space();

            var effectiveStartBias = nation.GetStartBias(ruleset);
            if (effectiveStartBias.Count != 0)
            {
                var index = 0;
                foreach (var bias in effectiveStartBias)
                {
                    // can be "Avoid []"
                    var link = !bias.Contains('[') ? bias : bias.GetPlaceholderParameters()[0];
                    b.Add(
                        (index == 0 ? "[Start bias:] " : "") + bias.Tr(), // extra tr because tr cannot nest {[]}
                        link: $"Terrain/{link}",
                        indent: index == 0 ? 0 : 1,
                        iconCrossed: bias.StartsWith("Avoid ", StringComparison.Ordinal));
                    index++;
                }
                b.Space();
            }
            AddUniqueBuildingsText(b, nation, context);
            AddUniqueUnitsText(b, nation, context);
            AddUniqueImprovementsText(b, nation, context);
        });
    }

    private static void AddCityStateInfo(IFormattedLineListBuilder b, NationObject nation, Ruleset ruleset)
    {
        var cityStateType = ruleset.CityStateTypes[nation.CityStateType.NotNull()];
        b.Add($"{{Type}}: {{{cityStateType.Name}}}", header: 4, color: "#" + cityStateType.GetColor());

        var showResources = false;

        void AddBonusLines(string header, UniqueMap uniqueMap)
        {
            // Note: Using getCityStateBonuses would be nice, but it's bound to a CityStateFunctions instance without even using `this`.
            // Too convoluted to reuse that here - but feel free to refactor that into a static.
            var bonuses = uniqueMap.GetAllUniques().Where(it => !it.IsHiddenToUsers()).ToList();
            if (bonuses.Count == 0) return;
            b.Space();
            b.Add($"{{{header}}} ");
            foreach (var unique in bonuses)
            {
                b.Add(unique, indent: 1);
                if (unique.Type == UniqueType.CityStateUniqueLuxury) showResources = true;
            }
        }

        AddBonusLines("When Friends:", cityStateType.FriendBonusUniqueMap);
        AddBonusLines("When Allies:", cityStateType.AllyBonusUniqueMap);

        if (showResources)
        {
            var allMercantileResources = ruleset.TileResources.Values
                .Where(it => ((IHasUniques)it).HasUnique(UniqueType.CityStateOnlyResource))
                .ToList();

            if (allMercantileResources.Count != 0)
            {
                b.Space();
                b.Add("The unique luxury is one of:");
                foreach (var resource in allMercantileResources)
                    b.Add(resource.Name, resource.MakeLink(), indent: 1);
            }
        }
        b.Separator();
    }

    private static void AddUniqueBuildingsText(IFormattedLineListBuilder b, NationObject nation, CivilopediaContext context)
    {
        var ruleset = context.Ruleset;
        foreach (var building in ruleset.Buildings.Values)
        {
            if (building.UniqueTo == null) continue;
            if (!nation.MatchesFilter(building.UniqueTo)) continue;
            if (((IHasUniques)building).IsHiddenFromCivilopedia(context.GameInfo, ruleset)) continue;
            b.Separator();
            b.Add($"{{{building.Name}}} -", link: building.MakeLink());
            if (building.Replaces != null && ruleset.Buildings.TryGetValue(building.Replaces, out var originalBuilding))
            {
                b.Add($"Replaces [{originalBuilding.Name}]", link: originalBuilding.MakeLink(), indent: 1);
                b.AddBuildingDifferences(originalBuilding, building);
                b.Space();
            }
            else if (building.Replaces != null)
            {
                b.Add($"Replaces [{building.Replaces}], which is not found in the ruleset!", indent: 1);
            }
            else
            {
                b.Add(BuildingDescriptions.GetShortDescription(building, true), indent: 1);
            }
        }
    }

    private static void AddUniqueUnitsText(IFormattedLineListBuilder b, NationObject nation, CivilopediaContext context)
    {
        var ruleset = context.Ruleset;
        foreach (var unit in ruleset.Units.Values)
        {
            if (((IHasUniques)unit).IsHiddenFromCivilopedia(context.GameInfo, ruleset)) continue;
            if (unit.UniqueTo == null || !nation.MatchesFilter(unit.UniqueTo)) continue;

            b.Separator();
            b.Add($"{{{unit.Name}}} -", link: $"Unit/{unit.Name}");
            if (unit.Replaces != null && ruleset.Units.TryGetValue(unit.Replaces, out var originalUnit))
            {
                b.Add($"Replaces [{originalUnit.Name}]", link: $"Unit/{originalUnit.Name}", indent: 1);
                if (unit.Cost != originalUnit.Cost)
                    b.Add("{Cost} ".Tr() + $"[{KotlinNumberText.Int(unit.Cost)}] vs [{KotlinNumberText.Int(originalUnit.Cost)}]".Tr(), indent: 1);
                b.AddUnitDifferences(ruleset, originalUnit, unit, true, 1);
            }
            else if (unit.Replaces != null)
            {
                b.Add($"Replaces [{unit.Replaces}], which is not found in the ruleset!", indent: 1);
            }
            else
            {
                b.Add(unit.GetCivilopediaTextLines(context).Select(it => it.Copy(indent: it.Indent + 1)));
            }
            b.Space();
        }
    }

    private static void AddUniqueImprovementsText(IFormattedLineListBuilder b, NationObject nation, CivilopediaContext context)
    {
        var ruleset = context.Ruleset;
        foreach (var improvement in ruleset.TileImprovements.Values)
        {
            if (((IHasUniques)improvement).IsHiddenFromCivilopedia(context.GameInfo, ruleset)) continue;
            if (improvement.UniqueTo == null || !nation.MatchesFilter(improvement.UniqueTo)) continue;

            b.Separator();
            b.Add(improvement.Name, link: $"Improvement/{improvement.Name}");
            b.Add(improvement.CloneStats().ToString(), indent: 1); // = (improvement as Stats).toString minus import plus copy overhead
            if (improvement.Replaces != null && ruleset.TileImprovements.TryGetValue(improvement.Replaces, out var originalImprovement))
            {
                b.Add($"Replaces [{originalImprovement.Name}]", link: originalImprovement.MakeLink(), indent: 1);
                b.Add(ImprovementDescriptions.GetDifferences(ruleset, originalImprovement, improvement));
                b.Space();
            }
            else if (improvement.Replaces != null)
            {
                b.Add($"Replaces [{improvement.Replaces}], which is not found in the ruleset!", indent: 1);
            }
            else
            {
                b.Add(ImprovementDescriptions.GetShortDescription(improvement));
            }
        }
    }
}
