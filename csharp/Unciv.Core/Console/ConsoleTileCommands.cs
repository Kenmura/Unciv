using Unciv.Logic;
using Unciv.Logic.Map.MapGenerator;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset.Tile;
using CityObject = Unciv.Logic.City.City;

namespace Unciv.DevConsole;

/// <summary>
/// Kotlin: <c>ConsoleTileCommands</c>. We deliberately do not normalize the tile to the ruleset: the console may create invalid tile
/// configurations. <c>tile find</c> is not ported (it opens a client notification); <c>activatetrigger</c> is not part of this node in Kotlin.
/// </summary>
internal static class ConsoleTileCommands
{
    public static ConsoleCommandNode Create()
    {
        var node = new ConsoleCommandNode();
        node.Subcommands["checkfilter"] = new ConsoleAction("tile checkfilter <tileFilter>", (console, parameters) =>
            ConsolePlan.Hint(console.GetSelectedTile().MatchesFilter(parameters[0].OriginalUnquoted()).ToString().ToLowerInvariant()));
        node.Subcommands["setimprovement"] = new ConsoleAction("tile setimprovement <improvementName> [civName]", (console, parameters) =>
        {
            var tile = console.GetSelectedTile();
            var improvement = parameters[0].Find(console.Game.Ruleset.TileImprovements.Values, it => it.Name);
            var civ = parameters.Count > 1 ? console.GetCivByName(parameters[1]) : null;
            return ConsolePlan.Do(() =>
            {
                tile.SetImprovement(improvement, civ);
                tile.GetCity()?.ReassignPopulation();
            });
        });
        node.Subcommands["removeimprovement"] = new ConsoleAction("tile removeimprovement", (console, _) =>
        {
            var tile = console.GetSelectedTile();
            return ConsolePlan.Do(() =>
            {
                tile.SetImprovement((TileImprovement?)null);
                tile.GetCity()?.ReassignPopulation();
            });
        });
        node.Subcommands["setpillaged"] = new ConsoleAction("tile setpillaged <boolean>", (console, parameters) =>
        {
            var tile = console.GetSelectedTile();
            bool pillaged = parameters[0].ToBoolean();
            return ConsolePlan.Do(() =>
            {
                if (pillaged) tile.SetPillaged();
                else tile.SetRepaired();
            });
        });
        node.Subcommands["removeroad"] = new ConsoleAction("tile removeroad", (console, _) =>
        {
            var tile = console.GetSelectedTile();
            return ConsolePlan.Do(() =>
            {
                tile.RemoveRoad();
                // Kotlin todo: this covers many cases but not all
                (tile.GetOwner() as Logic.Civilization.Civilization)?.Cache.UpdateCitiesConnectedToCapital();
            });
        });
        AddTerrainCommands(node);
        node.Subcommands["setowner"] = new ConsoleAction("tile setowner [civName|cityName]", (console, parameters) =>
        {
            var tile = console.GetSelectedTile();
            var oldOwner = tile.GetCity();
            var newOwner = GetOwnerCity(console, parameters, tile);
            return ConsolePlan.Do(() =>
            {
                // for simplicity, treat assign to civ without cities same as un-assign
                oldOwner?.Expansion.RelinquishOwnership(tile); // redundant if new owner is not null, but simpler for un-assign
                newOwner?.Expansion.TakeOwnership(tile);
            });
        });
        node.Subcommands["spawn-barbarian-encampment"] = new ConsoleAction("tile spawn-barbarian-encampment", (console, _) =>
        {
            var tile = console.GetSelectedTile();
            return ConsolePlan.Do(() => console.Game.Barbarians.CreateNewCamp(tile));
        });
        return node;
    }

    private static void AddTerrainCommands(ConsoleCommandNode node)
    {
        node.Subcommands["addfeature"] = new ConsoleAction("tile addfeature <featureName>", (console, parameters) =>
        {
            var tile = console.GetSelectedTile();
            var feature = GetTerrainFeature(console, parameters[0]);
            return ConsolePlan.Do(() =>
            {
                if (feature.Name == Constants.River) RiverGenerator.ContinueRiverOn(tile);
                else tile.AddTerrainFeature(feature.Name);
                tile.GetCity()?.ReassignPopulation();
            });
        });
        node.Subcommands["removefeature"] = new ConsoleAction("tile removefeature <featureName>", (console, parameters) =>
        {
            var tile = console.GetSelectedTile();
            var feature = GetTerrainFeature(console, parameters[0]);
            if (feature.Name == Constants.River)
                throw new ConsoleHintException("Rivers cannot be removed like a terrain feature - use tile removeriver <direction>");
            return ConsolePlan.Do(() =>
            {
                tile.RemoveTerrainFeature(feature.Name);
                tile.GetCity()?.ReassignPopulation();
            });
        });
        node.Subcommands["setterrain"] = new ConsoleAction("tile setterrain <terrainName>", (console, parameters) =>
        {
            var tile = console.GetSelectedTile();
            var terrain = parameters[0].Find(console.Game.Ruleset.Terrains.Values, it => it.Name);
            if (terrain.Type == TerrainType.NaturalWonder) return ConsolePlan.Do(() => SetNaturalWonder(console.Game, tile, terrain));
            if (terrain.Type != tile.GetBaseTerrain().Type) throw new ConsoleErrorException("Changing terrain type is not allowed");
            return ConsolePlan.Do(() => SetBaseTerrain(tile, terrain.Name));
        });
        node.Subcommands["setresource"] = new ConsoleAction("tile setresource <resourceName> [amount]", (console, parameters) =>
        {
            var tile = console.GetSelectedTile();
            var resource = parameters[0].Find(console.Game.Ruleset.TileResources.Values, it => it.Name);
            int? amount = parameters.Count > 1 && !parameters[1].IsEmpty() ? parameters[1].ToInt() : null;
            if (amount != null && resource.ResourceType != ResourceType.Strategic)
                return ConsolePlan.Error($"Can't set resource amount for {resource.ResourceType} resource");
            if (amount != null && amount <= 0) return ConsolePlan.Error("Invalid resource amount");
            return ConsolePlan.Do(() =>
            {
                // Deviation: Kotlin picks the deposit size with an unseeded Random; here it comes from the tile's state-based generator.
                tile.SetTileResource(resource, rng: tile.StateThisTile.StateBasedRandom("DevConsole tile setresource"));
                if (amount != null) tile.ResourceAmount = amount.Value;
                tile.SetTerrainTransients();
                tile.GetCity()?.ReassignPopulation();
            });
        });
        node.Subcommands["removeresource"] = new ConsoleAction("tile removeresource", (console, _) =>
        {
            var tile = console.GetSelectedTile();
            return ConsolePlan.Do(() =>
            {
                tile.TileResource = null;
                tile.SetTerrainTransients();
                tile.GetCity()?.ReassignPopulation();
            });
        });
        node.Subcommands["addriver"] = RiverAction("tile addriver <direction>", true);
        node.Subcommands["removeriver"] = RiverAction("tile removeriver <direction>", false);
    }

    private static void SetBaseTerrain(Tile tile, string terrainName)
    {
        tile.BaseTerrain = terrainName;
        tile.SetTerrainTransients();
        tile.GetCity()?.ReassignPopulation();
    }

    private static void SetNaturalWonder(GameInfo game, Tile tile, Terrain wonder)
    {
        tile.RemoveTerrainFeatures();
        tile.NaturalWonder = wonder.Name;
        SetBaseTerrain(tile, wonder.TurnsInto ?? tile.BaseTerrain);
        foreach (var civ in game.Civilizations)
        {
            if (civ.NaturalWonders.Contains(wonder.Name)) continue;
            if (civ.IsDefeated() || civ.IsBarbarian || civ.IsSpectator()) continue;
            if (!civ.HasExplored(tile)) continue;
            civ.Cache.DiscoverNaturalWonders();
            civ.UpdateStatsForNextTurn();
        }
    }

    private static Terrain GetTerrainFeature(ConsoleContext console, CliInput param) =>
        param.Find(console.Game.Ruleset.Terrains.Values.Where(it => it.Type == TerrainType.TerrainFeature), it => it.Name);

    private static ConsoleAction RiverAction(string format, bool newValue) => new(format, (console, parameters) =>
    {
        var tile = console.GetSelectedTile();
        var direction = parameters[0].EnumValue<RiverGenerator.RiverDirections>();
        var otherTile = RiverGenerator.GetNeighborTile(direction, tile)
            ?? throw new ConsoleErrorException("tile has no neighbor to the " + direction);
        if (!otherTile.IsLand) throw new ConsoleErrorException("there's no land to the " + direction);
        return ConsolePlan.Do(() => tile.SetConnectedByRiver(otherTile, newValue));
    });

    private static CityObject? GetOwnerCity(ConsoleContext console, IReadOnlyList<CliInput> parameters, Tile selectedTile)
    {
        if (parameters.Count == 0 || parameters[0].IsEmpty()) return null;
        var param = parameters[0];
        // Look for a city by name to assign the Tile to
        var namedCity = param.FindOrNull(console.Game.Civilizations.SelectMany(civ => civ.Cities), it => it.Name);
        if (namedCity != null) return namedCity;
        // If the user didn't specify a City, they must have given us a Civilization instead
        var namedCiv = console.GetCivByNameOrNull(param) ?? throw new ConsoleErrorException($"{param} is neither a city nor a civilization");
        return namedCiv.Cities.MinBy(it => it.GetCenterTile().AerialDistanceTo(selectedTile) + (it.IsBeingRazed ? 5 : 0));
    }
}
