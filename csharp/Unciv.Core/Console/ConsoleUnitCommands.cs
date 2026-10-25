namespace Unciv.DevConsole;

/// <summary>Kotlin: <c>ConsoleUnitCommands</c>. <c>activatetrigger</c> is not ported (see ticket notes).</summary>
internal static class ConsoleUnitCommands
{
    public static ConsoleCommandNode Create()
    {
        var node = new ConsoleCommandNode();
        node.Subcommands["checkfilter"] = new ConsoleAction("unit checkfilter <unitFilter>", (console, parameters) =>
            ConsolePlan.Hint(console.GetSelectedUnit().MatchesFilter(parameters[0].OriginalUnquoted()).ToString().ToLowerInvariant()));
        node.Subcommands["add"] = new ConsoleAction("unit add <civName> <unitName>", (console, parameters) =>
        {
            var selectedTile = console.GetSelectedTile();
            var civ = console.GetCivByName(parameters[0]);
            var baseUnit = parameters[1].Find(console.Game.Ruleset.Units.Values, it => it.Name);
            return ConsolePlan.Do(() => civ.Units.PlaceUnitNearTile(selectedTile.Position, baseUnit));
        });
        node.Subcommands["remove"] = new ConsoleAction("unit remove [all]", (console, parameters) =>
        {
            if (parameters.Count > 0 && parameters[0].Equals("all"))
                return ConsolePlan.Do(() =>
                {
                    foreach (var civ in console.Game.Civilizations)
                        foreach (var unit in civ.Units.GetCivUnits().ToList())
                            unit.Destroy();
                });
            var selected = console.GetSelectedUnit();
            return ConsolePlan.Do(() => selected.Destroy());
        });
        node.Subcommands["addpromotion"] = new PromotionAction("unit addpromotion <promotionName>", (console, parameters) =>
        {
            var unit = console.GetSelectedUnit();
            var promotion = parameters[0].Find(console.Game.Ruleset.UnitPromotions.Values, it => it.Name);
            return ConsolePlan.Do(() => unit.Promotions.AddPromotion(promotion.Name, true));
        }, adding: true);
        node.Subcommands["removepromotion"] = new PromotionAction("unit removepromotion <promotionName>", (console, parameters) =>
        {
            var unit = console.GetSelectedUnit();
            var promotion = parameters[0].FindOrNull(unit.Promotions.GetPromotions(), it => it.Name)
                ?? throw new ConsoleErrorException("Promotion not found on unit");
            return ConsolePlan.Do(() =>
            {
                // No such action in-game so we need to manually update
                unit.Promotions.Promotions.Remove(promotion.Name);
                unit.UpdateUniques();
                unit.UpdateVisibleTiles();
            });
        }, adding: false);
        node.Subcommands["setmovement"] = new ConsoleAction("unit setmovement [amount]", (console, parameters) =>
        {
            // Note amount defaults to maxMovement, but is not limited by it - it's an arbitrary choice to allow that
            var unit = console.GetSelectedUnit();
            float movement = parameters.Count > 0 && !parameters[0].IsEmpty() ? parameters[0].ToFloat() : unit.GetMaxMovement();
            if (movement < 0f) throw new ConsoleErrorException("Number out of range");
            return ConsolePlan.Do(() => unit.CurrentMovement = movement);
        });
        node.Subcommands["sethealth"] = new ConsoleAction("unit sethealth [amount]", (console, parameters) =>
        {
            int health = parameters.Count > 0 && !parameters[0].IsEmpty() ? parameters[0].ToInt() : 100;
            if (health is < 1 or > 100) throw new ConsoleErrorException("Number out of range");
            var unit = console.GetSelectedUnit();
            return ConsolePlan.Do(() => unit.Health = health);
        });
        node.Subcommands["setxp"] = new ConsoleAction("unit setxp [amount]", (console, parameters) =>
        {
            if (parameters.Count == 0) throw new ConsoleErrorException("No XP provided");
            int xp = parameters[0].ToInt();
            if (xp < 0) throw new ConsoleErrorException("Number out of range");
            var unit = console.GetSelectedUnit();
            return ConsolePlan.Do(() => unit.Promotions.XP = xp);
        });
        return node;
    }

    /// <summary>The two promotion commands autocomplete from the selected unit.</summary>
    private sealed class PromotionAction(string format, Func<ConsoleContext, IReadOnlyList<CliInput>, ConsolePlan> action, bool adding)
        : ConsoleAction(format, action)
    {
        public override AutocompleteResult? Autocomplete(ConsoleContext console, IReadOnlyList<CliInput> parameters)
        {
            var last = parameters.Count > 0 ? parameters[^1] : CliInput.Empty;
            var owned = console.GetSelectedUnit().Promotions.Promotions;
            // Note: filtering by unit type sounds good, but would also prevent promotions that can be legally obtained like Morale
            IEnumerable<string> options = adding
                ? console.Game.Ruleset.UnitPromotions.Keys.Where(it => !owned.Contains(it)).Select(it => it.Replace("[", "", StringComparison.Ordinal).Replace("]", "", StringComparison.Ordinal))
                : owned;
            return CliInput.GetAutocompleteString(last, options);
        }
    }
}
