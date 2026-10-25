using System.Globalization;
using Unciv.Commands;
using Unciv.Commands.City;
using Unciv.Models.Ruleset;
using Unciv.Models.StatTypes;
using Unciv.Queries.Cities;

namespace Unciv.Assets;

/// <summary>One line of the production queue. The raise/lower/top/remove flags mirror what the engine's commands allow, so a button is disabled exactly when the engine would refuse.</summary>
public sealed record QueueRow(int Index, string Name, string Label, bool IsPerpetual, bool CanTop, bool CanRaise, bool CanLower, bool CanRemove, IReadOnlyList<BuyOption> Buy);

/// <summary>One way to buy a construction: the engine's price and whether the engine would accept it now (<see cref="Why"/> explains a disabled one).</summary>
public sealed record BuyOption(string Stat, int Cost, bool Allowed, string Label, string Why);

/// <summary>One entry of the buildable list. <see cref="QueueWhy"/> is the reason the queue button is disabled (empty when <see cref="CanQueue"/>).</summary>
public sealed record BuildableRow(string Name, string Category, string State, string Label, string Detail, string Tooltip, bool CanQueue, string QueueWhy, IReadOnlyList<BuyOption> Buy);

/// <summary>A built building or wonder with the gold it sells for (the engine's price). Wonders cannot be sold.</summary>
public sealed record BuildingRow(string Name, string Kind, int SellPrice, bool CanSell, string Label);

/// <summary>A button of the city state area (annex, raze, stop razing) and the command it sends.</summary>
public sealed record CityStateAction(string Label, GameCommand Command, string Tooltip);

/// <summary>
/// C018: the production half of the city screen as plain data, Godot-free so it is unit-tested. It formats the engine's <see cref="CityPanelResult"/> and
/// <see cref="ConstructionListResult"/> and builds the commands a click means. Prices, turns, build rules and purchase checks all come from those answers;
/// the only thing decided here is which button is disabled and why, and that follows the validation of the corresponding command.
/// </summary>
public static class CityProductionModel
{
    public const string PuppetWhy = "A puppet city chooses its own production.";

    /// <summary>Category headings in display order.</summary>
    public static readonly IReadOnlyList<string> Categories = ["Unit", "Building", "Wonder", "NationalWonder", "Perpetual"];

    public static string CategoryHeading(string category) => category switch
    {
        "Unit" => "Units",
        "Building" => "Buildings",
        "Wonder" => "Wonders",
        "NationalWonder" => "National wonders",
        _ => "Other",
    };

    /// <summary>
    /// The queue, in order. Raise and lower follow <see cref="ReorderQueue"/>; a puppet's queue cannot be changed. Each entry carries the purchase options the
    /// engine quotes for it in <paramref name="list"/> (which must list every construction, not only the displayed ones: a queued building is not displayed).
    /// </summary>
    public static IReadOnlyList<QueueRow> Queue(CityPanelResult city, ConstructionListResult list, int gold)
    {
        var rows = new List<QueueRow>();
        int last = city.Queue.Count - 1;
        bool changeable = !city.IsPuppet;
        for (int i = 0; i < city.Queue.Count; i++)
        {
            var entry = city.Queue[i];
            bool perpetual = entry.Cost == null;
            string progress = perpetual ? "perpetual" : $"{Turns(entry.Turns)}, {I(entry.WorkDone)}/{I(entry.Cost ?? 0)}";
            rows.Add(new QueueRow(i, entry.Name, $"{I(i + 1)}. {entry.Name} ({progress})", perpetual,
                changeable && !perpetual && i > 0, changeable && i > 0, changeable && i != last, changeable, BuyOptions(city, list, entry.Name, gold)));
        }
        return rows;
    }

    public static GameCommand Reorder(string cityId, QueueRow row, QueueMove move) => new ReorderQueue { CityId = cityId, Index = row.Index, Move = move };

    public static GameCommand Remove(string cityId, QueueRow row) => new RemoveFromQueue { CityId = cityId, Index = row.Index };

    /// <summary>
    /// The listed constructions the engine says to display, in the engine's order (units, buildings, perpetual), each with its turns, cost and purchase options.
    /// <paramref name="gold"/> is the civ's gold, used only to word the reason a purchase in gold is disabled.
    /// </summary>
    public static IReadOnlyList<BuildableRow> Buildables(CityPanelResult city, ConstructionListResult list, int gold)
    {
        var rows = new List<BuildableRow>();
        foreach (var entry in list.Entries)
        {
            if (!entry.Displayed) continue;
            string why = QueueWhy(city, entry);
            string detail = entry.State == "rejected"
                ? entry.Rejections.FirstOrDefault(it => it.ShouldShow)?.Message ?? "cannot be built"
                : entry.Cost != null ? $"{Turns(entry.Turns)}, {I(entry.Cost.Value)}" : "perpetual";
            rows.Add(new BuildableRow(entry.Name, entry.Category, entry.State, entry.Name + " - " + detail, detail, Tooltip(entry), why.Length == 0, why, BuyOptions(city, list, entry.Name, gold)));
        }
        return rows;
    }

    private static IReadOnlyList<BuyOption> BuyOptions(CityPanelResult city, ConstructionListResult list, string name, int gold)
    {
        var entry = list.Entries.FirstOrDefault(it => it.Name == name);
        return entry == null ? [] : entry.BuyCosts.Select(it => Buy(city, entry, it, gold)).ToList();
    }

    private static string QueueWhy(CityPanelResult city, ConstructionEntry entry)
    {
        if (city.IsPuppet) return PuppetWhy;
        if (entry.State == "buildable") return "";
        if (entry.State == "enqueued") return entry.Category == "Unit" ? "" : "Already in the queue.";
        if (entry.State == "built") return "Already built.";
        return entry.Rejections.FirstOrDefault(it => it.ShouldShow)?.Message ?? "It cannot be built now.";
    }

    private static BuyOption Buy(CityPanelResult city, ConstructionEntry entry, ConstructionBuyCost cost, int gold)
    {
        string why = "";
        if (!cost.Allowed)
            why = cost.Stat == nameof(Stat.Gold) && cost.Cost > gold
                ? $"Need {I(cost.Cost - gold)} more gold."
                : $"{entry.Name} cannot be bought with {cost.Stat} in {city.Name} now (a unit may stand in the way, or the city is in resistance).";
        return new BuyOption(cost.Stat, cost.Cost, cost.Allowed, $"Buy {I(cost.Cost)} {cost.Stat}", why);
    }

    private static string Tooltip(ConstructionEntry entry)
    {
        var lines = new List<string> { entry.Name + " (" + CategoryHeading(entry.Category).TrimEnd('s') + ")" };
        if (entry.Cost != null) lines.Add($"Cost {I(entry.Cost.Value)} production, {Turns(entry.Turns)}");
        if (entry.WorkDone > 0) lines.Add($"Already {I(entry.WorkDone)} production invested");
        foreach (var rejection in entry.Rejections.Where(it => it.ShouldShow)) lines.Add(rejection.Message);
        foreach (var cost in entry.BuyCosts) lines.Add($"Buy: {I(cost.Cost)} {cost.Stat}");
        return string.Join("\n", lines);
    }

    /// <summary>Add to the queue (a click on a buildable row).</summary>
    public static GameCommand Enqueue(string cityId, BuildableRow row, bool toTop = false) => new QueueConstruction { CityId = cityId, Name = row.Name, AddToTop = toTop };

    /// <summary>
    /// Buy it. When the construction is already in the queue the command names that entry (the engine requires it to hold the construction); the
    /// engine itself rejects a building that still needs a tile chosen (the client has no tile picker yet).
    /// </summary>
    public static GameCommand Purchase(CityPanelResult city, string name, BuyOption option)
    {
        int index = city.Queue.Select((it, i) => (it.Name, i)).Where(it => it.Name == name).Select(it => it.i).DefaultIfEmpty(-1).First();
        return new PurchaseConstruction { CityId = city.CityId, Name = name, Stat = Enum.Parse<Stat>(option.Stat), QueueIndex = index };
    }

    /// <summary>The built buildings and wonders (wonders last), sorted by name within each kind; only a plain building is sellable.</summary>
    public static IReadOnlyList<BuildingRow> Buildings(CityPanelResult city, Ruleset ruleset)
    {
        var rows = new List<BuildingRow>();
        foreach (var building in city.Buildings)
        {
            var type = ruleset.Buildings.GetValueOrDefault(building.Name);
            string kind = type is { IsWonder: true } ? "Wonder" : type is { IsNationalWonder: true } ? "NationalWonder" : "Building";
            bool sellable = kind == "Building" && !city.IsPuppet;
            string label = kind == "Building" ? $"{building.Name} (sells for {I(building.SellPrice)} gold)" : $"{building.Name} ({(kind == "Wonder" ? "wonder" : "national wonder")})";
            rows.Add(new BuildingRow(building.Name, kind, building.SellPrice, sellable, label));
        }
        return rows.OrderBy(it => it.Kind == "Building" ? 0 : it.Kind == "NationalWonder" ? 1 : 2).ThenBy(it => it.Name, StringComparer.Ordinal).ToList();
    }

    public static GameCommand Sell(string cityId, BuildingRow row) => new SellBuilding { CityId = cityId, Building = row.Name };

    /// <summary>The state line of the city: puppet, razing and resistance (empty for an ordinary city).</summary>
    public static string StateLine(CityPanelResult city)
    {
        var parts = new List<string>();
        if (city.IsPuppet) parts.Add("Puppet city: it chooses its own production and citizens. Annex it to take control.");
        if (city.IsBeingRazed) parts.Add("This city is being razed: it loses a citizen every turn.");
        if (city.IsInResistance) parts.Add("In resistance: it produces nothing until the unrest ends.");
        return string.Join(" ", parts);
    }

    /// <summary>The state buttons: annex a puppet, raze or stop razing. The engine decides whether razing is allowed (capital, holy city, game rules) and says so when refusing.</summary>
    public static IReadOnlyList<CityStateAction> StateActions(CityPanelResult city)
    {
        var actions = new List<CityStateAction>();
        if (city.IsPuppet) actions.Add(new CityStateAction("Annex city", new AnnexCity { CityId = city.CityId }, "Take control of the puppet city."));
        if (city.IsBeingRazed) actions.Add(new CityStateAction("Stop razing", new SetRazing { CityId = city.CityId, Raze = false }, "Keep the city."));
        else if (!city.IsPuppet) actions.Add(new CityStateAction("Raze city", new SetRazing { CityId = city.CityId, Raze = true }, "Destroy the city, one citizen per turn."));
        return actions;
    }

    private static string Turns(int? turns) => turns == null ? "never" : turns == 1 ? "1 turn" : $"{I(turns.Value)} turns";

    private static string I(int value) => value.ToString(CultureInfo.InvariantCulture);
}
