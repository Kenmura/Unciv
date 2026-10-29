using System.Globalization;
using System.Text.Json.Serialization;
using Unciv.Commands;
using Unciv.Commands.City;
using Unciv.Logic.City;
using Unciv.Models.StatTypes;
using Unciv.Queries.Cities;

namespace Unciv.Assets;

/// <summary>C092: a named build queue the player keeps in the client settings: construction names in the order they are queued.</summary>
public sealed record BuildTemplate
{
    public const int MaxTemplates = 20;
    public const int MaxItems = 20;
    public const int MaxNameLength = 40;

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("items")]
    public List<string> Items { get; init; } = [];

    /// <summary>The template made safe (a hand-edited file could hold anything): trimmed name, no empty or surplus items; null when nothing usable is left.</summary>
    public BuildTemplate? Sanitized()
    {
        string name = (Name ?? "").Trim();
        if (name.Length > MaxNameLength) name = name[..MaxNameLength].TrimEnd();
        var items = (Items ?? []).Select(it => (it ?? "").Trim()).Where(it => it.Length > 0).Take(MaxItems).ToList();
        return name.Length == 0 || items.Count == 0 ? null : new BuildTemplate { Name = name, Items = items };
    }
}

/// <summary>C092: one of the player's cities with the engine's answers the bulk planner reads: the city panel and the construction list (which must list every construction, not only the displayed ones).</summary>
public sealed record BulkCity(CityPanelResult Panel, ConstructionListResult List);

/// <summary>C092: one step of a bulk action: the command for one city, or the reason that city is skipped (empty when there is a command).</summary>
public sealed record BulkStep(string City, string What, GameCommand? Command, string Skipped);

/// <summary>C092: what a bulk action would do. Steps are in the order of the cities given; the commands are the ordinary city commands, so the engine validates each one and a refusal changes nothing.</summary>
public sealed record BulkPlan(IReadOnlyList<BulkStep> Steps)
{
    public IReadOnlyList<BulkStep> Commands => Steps.Where(it => it.Command != null).ToList();

    public IReadOnlyList<BulkStep> Skips => Steps.Where(it => it.Command == null).ToList();
}

/// <summary>C092: what a bulk action did: how many commands the engine accepted, and a line for each step that was skipped or refused.</summary>
public sealed record BulkOutcome(int Applied, IReadOnlyList<string> Problems)
{
    /// <summary>The line the screen shows: the count, then up to <paramref name="show"/> of the problems.</summary>
    public string Text(int show = 3)
    {
        string head = Applied == 1 ? "1 change made." : $"{Applied.ToString(CultureInfo.InvariantCulture)} changes made.";
        if (Problems.Count == 0) return head;
        string tail = Problems.Count > show ? $" (and {(Problems.Count - show).ToString(CultureInfo.InvariantCulture)} more)" : "";
        return $"{head} Not done: {string.Join("; ", Problems.Take(show))}{tail}";
    }
}

/// <summary>
/// C092: city management at scale as plain data, Godot-free so it is unit-tested. It plans a bulk action (set focus, queue an item, buy the current production, apply a
/// <see cref="BuildTemplate"/>) over several cities as ordinary city commands. The only decisions made here are which cities to skip and why, and they follow the validation of
/// the corresponding commands (the engine decides again when a command is submitted); prices and build rules come from the queries' answers.
/// </summary>
public static class CityBulkModel
{
    private static string I(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Reason(string why) => why == CityProductionModel.PuppetWhy ? "a puppet city chooses its own production" : why.TrimEnd('.');

    private static BulkStep Skip(CityPanelResult city, string what, string why) => new(city.Name, what, null, why);

    private static BulkStep Do(CityPanelResult city, string what, GameCommand command) => new(city.Name, what, command, "");

    /// <summary>Choose <paramref name="focus"/> in every city. A puppet city chooses its own citizens; a city that already has the focus is left alone.</summary>
    public static BulkPlan SetFocus(IReadOnlyList<BulkCity> cities, CityFocus focus, bool religionEnabled = true)
    {
        string what = CityScreenModel.FocusLabel(focus);
        var steps = new List<BulkStep>();
        foreach (var city in cities.Select(it => it.Panel))
        {
            if (!Enum.IsDefined(focus) || focus == CityFocus.FaithFocus && !religionEnabled) steps.Add(Skip(city, what, $"{what} cannot be chosen"));
            else if (city.IsPuppet) steps.Add(Skip(city, what, "a puppet city chooses its own citizens"));
            else if (city.CityFocus == focus.ToString()) steps.Add(Skip(city, what, $"already on {what}"));
            else steps.Add(Do(city, what, new SetCityFocus { CityId = city.CityId, Focus = focus }));
        }
        return new BulkPlan(steps);
    }

    /// <summary>Queue the construction <paramref name="name"/> in every city that the engine lets queue it (<see cref="CityProductionModel.QueueWhy"/>).</summary>
    public static BulkPlan Queue(IReadOnlyList<BulkCity> cities, string name, bool toTop = false)
    {
        var steps = new List<BulkStep>();
        foreach (var (city, list) in cities.Select(it => (it.Panel, it.List)))
        {
            var entry = list.Entries.FirstOrDefault(it => it.Name == name);
            string why = entry == null ? $"{name} is not known here" : CityProductionModel.QueueWhy(city, entry);
            if (why.Length > 0) steps.Add(Skip(city, name, Reason(why)));
            else steps.Add(Do(city, name, new QueueConstruction { CityId = city.CityId, Name = name, AddToTop = toTop && entry!.Category != "Perpetual" }));
        }
        return new BulkPlan(steps);
    }

    /// <summary>
    /// Buy the construction now in production (first in the queue) in every city where the engine says a purchase is allowed. Gold is spent in the order of the cities: a city
    /// whose price the remaining <paramref name="gold"/> no longer covers is skipped, so the plan does not send a command the engine would refuse for lack of gold.
    /// </summary>
    public static BulkPlan BuyCurrent(IReadOnlyList<BulkCity> cities, int gold)
    {
        var steps = new List<BulkStep>();
        int left = gold;
        foreach (var (city, list) in cities.Select(it => (it.Panel, it.List)))
        {
            if (city.Queue.Count == 0)
            {
                steps.Add(Skip(city, "Buy", "nothing in production"));
                continue;
            }
            string name = city.Queue[0].Name;
            var options = list.Entries.FirstOrDefault(it => it.Name == name)?.BuyCosts ?? [];
            if (options.Count == 0)
            {
                steps.Add(Skip(city, name, $"{name} cannot be bought"));
                continue;
            }
            var allowed = options.Where(it => it.Allowed).ToList();
            var pick = allowed.Where(it => it.Stat != nameof(Stat.Gold) || it.Cost <= left).OrderBy(it => it.Stat == nameof(Stat.Gold) ? 0 : 1).FirstOrDefault();
            if (pick == null) steps.Add(Skip(city, name, allowed.Count == 0 ? $"{name} cannot be bought now" : $"not enough gold left for {I(allowed.Min(it => it.Cost))}"));
            else
            {
                if (pick.Stat == nameof(Stat.Gold)) left -= pick.Cost;
                steps.Add(Do(city, name, CityProductionModel.Purchase(city, name, new BuyOption(pick.Stat, pick.Cost, true, "", ""))));
            }
        }
        return new BulkPlan(steps);
    }

    /// <summary>
    /// Append the items of <paramref name="template"/>, in order, to the queue of every city. An item that city cannot queue (not buildable there, already queued or built, puppet) is
    /// skipped with the engine's reason; the rest of the template still applies. An item that appears twice is queued once.
    /// </summary>
    public static BulkPlan ApplyTemplate(IReadOnlyList<BulkCity> cities, BuildTemplate template)
    {
        var steps = new List<BulkStep>();
        foreach (var (city, list) in cities.Select(it => (it.Panel, it.List)))
        {
            var queued = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in template.Items)
            {
                var entry = list.Entries.FirstOrDefault(it => it.Name == name);
                string why = entry == null ? $"{name} is not known here" : CityProductionModel.QueueWhy(city, entry);
                if (why.Length == 0 && entry!.Category != "Unit" && !queued.Add(name)) why = "already in the template";
                if (why.Length > 0) steps.Add(Skip(city, name, Reason(why)));
                else steps.Add(Do(city, name, new QueueConstruction { CityId = city.CityId, Name = name }));
            }
        }
        return new BulkPlan(steps);
    }

    /// <summary>The names offered in the "queue an item" choice: what at least one of the cities can queue, in the engine's order (units, buildings, wonders, perpetual).</summary>
    public static IReadOnlyList<string> QueueChoices(IReadOnlyList<BulkCity> cities)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (city, list) in cities.Select(it => (it.Panel, it.List)))
            foreach (var entry in list.Entries)
                if (entry.Displayed && CityProductionModel.QueueWhy(city, entry).Length == 0 && seen.Add(entry.Name)) names.Add(entry.Name);
        return names;
    }

    /// <summary>The template that holds the queue of <paramref name="city"/> as it is now; null when the queue is empty.</summary>
    public static BuildTemplate? FromQueue(CityPanelResult city, string name) =>
        new BuildTemplate { Name = name, Items = city.Queue.Select(it => it.Name).ToList() }.Sanitized();

    /// <summary>The templates with <paramref name="template"/> added, or replacing the one of the same name; nothing changes when it is not usable or the limit is reached.</summary>
    public static List<BuildTemplate> WithTemplate(IReadOnlyList<BuildTemplate> templates, BuildTemplate template)
    {
        var safe = template.Sanitized();
        var result = templates.ToList();
        if (safe == null) return result;
        int at = result.FindIndex(it => it.Name == safe.Name);
        if (at >= 0) result[at] = safe;
        else if (result.Count < BuildTemplate.MaxTemplates) result.Add(safe);
        return result;
    }

    public static List<BuildTemplate> WithoutTemplate(IReadOnlyList<BuildTemplate> templates, string name) => templates.Where(it => it.Name != name).ToList();

    /// <summary>The templates made safe: unusable ones dropped, later duplicates of a name dropped, at most <see cref="BuildTemplate.MaxTemplates"/>.</summary>
    public static List<BuildTemplate> SanitizeTemplates(IReadOnlyList<BuildTemplate>? templates)
    {
        var result = new List<BuildTemplate>();
        foreach (var safe in (templates ?? []).Select(it => it?.Sanitized()))
            if (safe != null && result.All(it => it.Name != safe.Name) && result.Count < BuildTemplate.MaxTemplates) result.Add(safe);
        return result;
    }
}
