using Unciv.Models.Ruleset;

namespace Unciv.DevConsole;

/// <summary>Kotlin: <c>ConsoleCityCommands</c>. <c>activatetrigger</c> is not part of this node in Kotlin either.</summary>
internal static class ConsoleCityCommands
{
    public static ConsoleCommandNode Create()
    {
        var node = new ConsoleCommandNode();
        node.Subcommands["checkfilter"] = new ConsoleAction("city checkfilter <cityFilter>", (console, parameters) =>
            ConsolePlan.Hint(console.GetSelectedCity().MatchesFilter(parameters[0].OriginalUnquoted()).ToString().ToLowerInvariant()));
        node.Subcommands["add"] = new ConsoleAction("city add <civName>", (console, parameters) =>
        {
            var civ = console.GetCivByName(parameters[0]);
            if (!civ.IsMajorCiv() && !civ.IsCityState) throw new ConsoleErrorException("Can only add cities to major civs or city states");
            var selectedTile = console.GetSelectedTile();
            if (selectedTile.IsCityCenter()) throw new ConsoleErrorException("Tile already contains a city center");
            return ConsolePlan.Do(() => civ.AddCity(selectedTile.Position));
        });
        node.Subcommands["remove"] = new ConsoleAction("city remove", (console, _) =>
        {
            var city = console.GetSelectedCity();
            return ConsolePlan.Do(() => city.DestroyCity(overrideSafeties: true));
        });
        node.Subcommands["setpop"] = new ConsoleAction("city setpop <amount>", (console, parameters) =>
        {
            var city = console.GetSelectedCity();
            int newPop = parameters[0].ToInt();
            if (newPop < 1) throw new ConsoleErrorException("Population must be at least 1");
            return ConsolePlan.Do(() => city.Population.SetPopulation(newPop));
        });
        node.Subcommands["setname"] = new ConsoleAction("city setname <\"name\">", (console, parameters) =>
        {
            var city = console.GetSelectedCity();
            string name = parameters[0].OriginalUnquoted();
            return ConsolePlan.Do(() => city.Name = name);
        });
        node.Subcommands["addtile"] = new ConsoleAction("city addtile <cityName> [radius]", (console, parameters) =>
        {
            var selectedTile = console.GetSelectedTile();
            var city = console.GetCity(parameters[0]);
            if (!selectedTile.Neighbors.Any(it => it.GetCity() == city))
                throw new ConsoleErrorException("Tile is not adjacent to any tile already owned by the city");
            if (selectedTile.IsCityCenter()) throw new ConsoleErrorException("Cannot transfer city center");
            int radius = parameters.Count > 1 ? parameters[1].ToInt() : 0;
            return ConsolePlan.Do(() => selectedTile.ForEachTileInDistance(radius, tile =>
            {
                if (tile.GetCity() != city && !tile.IsCityCenter()) city.Expansion.TakeOwnership(tile);
            }));
        });
        node.Subcommands["removetile"] = new ConsoleAction("city removetile", (console, _) =>
        {
            var selectedTile = console.GetSelectedTile();
            var city = console.GetSelectedCity();
            return ConsolePlan.Do(() => city.Expansion.RelinquishOwnership(selectedTile));
        });
        node.Subcommands["religion"] = new ConsoleAction("city religion <religionName> <±pressure>", (console, parameters) =>
        {
            var city = console.GetSelectedCity();
            string religion = parameters[0].FindOrNull(console.Game.Religions.Keys)
                ?? throw new ConsoleErrorException($"'{parameters[0]}' is not a known religion");
            int pressure = parameters[1].ToInt();
            return ConsolePlan.Do(() =>
            {
                int floor = -city.Religion.GetPressures()[religion];
                city.Religion.AddPressure(religion, Math.Max(pressure, floor));
                city.Religion.UpdatePressureOnPopulationChange(0);
            });
        });
        node.Subcommands["sethealth"] = new ConsoleAction("city sethealth [amount]", (console, parameters) =>
        {
            var city = console.GetSelectedCity();
            int maxHealth = city.GetMaxHealth();
            int health = parameters.Count > 0 ? parameters[0].ToInt() : maxHealth;
            if (health < 1 || health > maxHealth) throw new ConsoleErrorException("Number out of range");
            return ConsolePlan.Do(() => city.Health = health);
        });
        node.Subcommands["addbuilding"] = new ConsoleAction("city addbuilding <buildingName>", (console, parameters) =>
        {
            var city = console.GetSelectedCity();
            var building = FindBuilding(console, parameters[0]);
            return ConsolePlan.Do(() => city.CityConstructions.AddBuilding(building));
        });
        node.Subcommands["removebuilding"] = new ConsoleAction("city removebuilding <buildingName>", (console, parameters) =>
        {
            var city = console.GetSelectedCity();
            var building = FindBuilding(console, parameters[0]);
            return ConsolePlan.Do(() => city.CityConstructions.RemoveBuilding(building));
        });
        node.Subcommands["setpuppet"] = new ConsoleAction("city setpuppet <boolean>", (console, parameters) =>
        {
            var city = console.GetSelectedCity();
            bool puppet = parameters.Count == 0 || parameters[0].ToBoolean();
            return ConsolePlan.Do(() => city.IsPuppet = puppet);
        });
        return node;
    }

    private static Building FindBuilding(ConsoleContext console, CliInput name) =>
        name.FindOrNull(console.Game.Ruleset.Buildings.Values, it => it.Name) ?? throw new ConsoleErrorException("Unknown building");
}
