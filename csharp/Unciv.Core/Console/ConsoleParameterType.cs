using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Map.MapGenerator;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.StatTypes;

namespace Unciv.DevConsole;

/// <summary>
/// Kotlin: <c>ConsoleParameterType</c>. Knowledge about console command parameter types, limited to supplying autocomplete possibilities.
/// The names are the ones used in the command format strings; an unknown name has no options. Triggered-unique templates are not ported
/// (see the ticket notes: <c>activatetrigger</c> needs the Unique validator).
/// </summary>
public static class ConsoleParameterType
{
    public static IReadOnlyList<string> Options(string name, ConsoleContext console)
    {
        var game = console.Game;
        var ruleset = game.Ruleset;
        return name switch
        {
            "civName" => [.. game.Civilizations.Select(it => it.CivId)],
            "unitName" => [.. ruleset.Units.Keys],
            "promotionName" => [.. ruleset.UnitPromotions.Keys],
            "improvementName" => [.. ruleset.TileImprovements.Keys],
            "featureName" => [.. ruleset.Terrains.Values.Where(it => it.Type == TerrainType.TerrainFeature).Select(it => it.Name)],
            "terrainName" => [.. ruleset.Terrains.Values.Where(it => it.Type.IsBaseTerrain() || it.Type == TerrainType.NaturalWonder).Select(it => it.Name)],
            "resourceName" => [.. ruleset.TileResources.Keys],
            "stat" => [.. StatExtensions.Names()],
            "religionName" => [.. game.Religions.Keys],
            "buildingName" => [.. ruleset.Buildings.Keys],
            "direction" => RiverGenerator.RiverDirectionNames,
            "policyName" => [.. ruleset.PolicyBranches.Keys, .. ruleset.Policies.Keys],
            "techName" => [.. ruleset.Technologies.Keys],
            "cityName" => [.. game.Civilizations.SelectMany(civ => civ.Cities).Select(it => it.Name)],
            "difficulty" => [.. ruleset.Difficulties.Keys],
            "boolean" => ["true", "false"],
            "diplomacyFlag" => [.. Enum.GetNames<DiplomacyFlags>()],
            "diplomaticModifier" => [.. Enum.GetNames<DiplomaticModifiers>()],
            "newCivName" => [.. ruleset.Nations.Keys.Where(it => !game.Civilizations.Any(civ => civ.Nation.Name == it))],
            _ => [],
        };
    }

    public static IReadOnlyList<CliInput> GetOptions(string name, ConsoleContext console) =>
        [.. Options(name, console).Select(it => new CliInput(it))];

    /// <summary>Options of a <c>|</c>-separated list of types.</summary>
    public static IReadOnlyList<CliInput> MultiOptions(string name, ConsoleContext console) =>
        [.. name.Split('|').SelectMany(it => GetOptions(it, console))];
}
