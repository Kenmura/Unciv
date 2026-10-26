using System.Globalization;
using Unciv.Commands.Empire;
using Unciv.Queries.Empire;
using Unciv.Utils;

namespace Unciv.Assets;

/// <summary>One spy of the espionage screen. <see cref="CanMove"/> is true when the engine offers the spy any destination; <see cref="Coup"/> is null unless the engine allows a coup.</summary>
public sealed record EspionageSpyRow(string Name, string Rank, string Location, string Action, bool Alive, bool CanMove, SetSpyCoup? Coup, string CoupLabel);

/// <summary>One place of the screen's location table. <see cref="CityId"/> is null for the hideout and for a city the engine offers no spy (a spy of the viewer merely sits there). <see cref="SpiesPresent"/> lists only the viewer's own spies.</summary>
public sealed record EspionageLocationRow(string? CityId, string Name, string Civ, string SpiesPresent, bool IsHideout = false);

/// <summary>The place a selected spy may be sent to, and the command that sends it.</summary>
public sealed record EspionageTarget(string Label, MoveSpy Command);

/// <summary>The whole screen: spies, locations, and whether the "assign all" picker (<see cref="DecisionPickerModel.MoveSpies"/>) has anything to offer.</summary>
public sealed record EspionageSpec(IReadOnlyList<EspionageSpyRow> Spies, IReadOnlyList<EspionageLocationRow> Locations, bool CanAssign, string Empty);

/// <summary>
/// C051: the espionage screen (Kotlin <c>EspionageScreen</c>) as plain data, Godot-free so it is unit-tested. Everything comes from <see cref="SpyDestinationsResult"/>, the
/// engine's answer about the viewer's own spies: no other civ's spy is ever described, and a city is listed only if the engine offers it as a destination or one of the
/// viewer's spies sits in it. Moving one spy is a <see cref="MoveSpy"/>; assigning several at once reuses <see cref="DecisionPickerModel.MoveSpies"/>. No game value is
/// computed here.
/// </summary>
public static class EspionageModel
{
    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static EspionageSpec Build(SpyDestinationsResult result)
    {
        var spies = result.Spies.Select(spy =>
        {
            bool staging = spy.Action == "Coup";
            string coupLabel = spy.CanDoCoup && spy.CoupChance is { } chance
                ? staging ? "Call off the coup" : $"Stage a coup ({KotlinMath.RoundToInt(chance * 100f).ToString(CultureInfo.InvariantCulture)}% chance)"
                : "";
            return new EspionageSpyRow(spy.Name, N(spy.Rank), spy.IsAlive ? spy.Location : "-", spy.IsAlive ? spy.Action : "Dead", spy.IsAlive,
                spy.IsAlive && (spy.CanMoveToHideout || spy.Destinations.Count > 0), spy.CanDoCoup ? new SetSpyCoup { Spy = spy.Name, Stage = !staging } : null, coupLabel);
        }).ToList();

        // Cities the engine offers to any spy, plus the ones a spy of the viewer is in already (the engine never offers the spy's own city)
        var cities = new Dictionary<string, (string Name, string Civ)>(StringComparer.Ordinal);
        foreach (var spy in result.Spies.Where(it => it.IsAlive))
            foreach (var destination in spy.Destinations)
                cities.TryAdd(destination.CityId, (destination.CityName, destination.Civ));
        var rows = new List<EspionageLocationRow> { new(null, Constants.SpyHideout, "", PresentAt(result, Constants.SpyHideout), IsHideout: true) };
        rows.AddRange(cities.OrderBy(it => it.Value.Name, StringComparer.Ordinal).ThenBy(it => it.Key, StringComparer.Ordinal)
            .Select(it => new EspionageLocationRow(it.Key, it.Value.Name, it.Value.Civ, PresentAt(result, it.Value.Name))));
        var offered = cities.Values.Select(it => it.Name).ToHashSet(StringComparer.Ordinal);
        foreach (string occupied in result.Spies.Where(it => it.IsAlive && it.Location != Constants.SpyHideout && !offered.Contains(it.Location)).Select(it => it.Location).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            rows.Add(new EspionageLocationRow(null, occupied, "", PresentAt(result, occupied)));

        return new EspionageSpec(spies, rows, result.Spies.Any(it => it.IsAlive && (it.CanMoveToHideout || it.Destinations.Count > 0)),
            spies.Count == 0 ? "You have no spies." : "");
    }

    private static string PresentAt(SpyDestinationsResult result, string location) =>
        string.Join(", ", result.Spies.Where(it => it.IsAlive && it.Location == location).Select(it => it.Name));

    /// <summary>Where <paramref name="spy"/> may go: the hideout when the engine allows it, then its destinations in the engine's order. Empty for an unknown or dead spy.</summary>
    public static IReadOnlyList<EspionageTarget> Targets(SpyDestinationsResult result, string spy)
    {
        var entry = result.Spies.FirstOrDefault(it => it.Name == spy && it.IsAlive);
        if (entry == null) return [];
        var targets = new List<EspionageTarget>();
        if (entry.CanMoveToHideout) targets.Add(new EspionageTarget("Hideout", new MoveSpy { Spy = spy, CityId = null }));
        foreach (var destination in entry.Destinations)
            targets.Add(new EspionageTarget(destination.CityName, new MoveSpy { Spy = spy, CityId = destination.CityId }));
        return targets;
    }

    /// <summary>The target that sends the selected spy to <paramref name="row"/>, or null when the engine does not offer that place to the spy.</summary>
    public static EspionageTarget? TargetFor(IReadOnlyList<EspionageTarget> targets, EspionageLocationRow row) =>
        targets.FirstOrDefault(it => row.IsHideout ? it.Command.CityId == null : row.CityId != null && it.Command.CityId == row.CityId);
}
