using Unciv.Models.Ruleset;
using Unciv.Models.StatTypes;
using Unciv.View;

namespace Unciv.Queries.Cities;

// Reason: Rejection type name
// Message: The engine's text for it
// ShouldShow: Whether the city screen shows this reason
public sealed record ConstructionRejection(string Reason, string Message, bool ShouldShow);

// Stat: The currency
// Cost: Price in that currency
// Allowed: Whether the viewer could pay it right now (the engine's purchase check)
public sealed record ConstructionBuyCost(string Stat, int Cost, bool Allowed);

// Category: Building, Wonder, NationalWonder, Unit or Perpetual
// State: buildable, enqueued, built or rejected
// Displayed: Whether the city screen lists it (ShouldBeDisplayed)
// Turns: Turns to finish at the current production, null when it cannot be built or is perpetual
// Cost: Production cost, null for perpetual constructions
public sealed record ConstructionEntry(
    string Name, string Category, string State, bool Displayed, int? Turns, int? Cost, int WorkDone,
    IReadOnlyList<ConstructionRejection> Rejections, IReadOnlyList<ConstructionBuyCost> BuyCosts);

public sealed record ConstructionListResult(string CityId, IReadOnlyList<ConstructionEntry> Entries);

// T196: everything the construction list of the city screen is built from. Reuses ShouldBeDisplayed, IsBuildable,
// GetRejectionReasons and the purchase checks, so a client shows what the engine decides. Units first, then buildings, then
// the perpetual constructions, each in ruleset order (as ConstructionsTable). OnlyDisplayed=false lists the whole ruleset.
[QueryType("cities.ConstructionList")]
public sealed record ConstructionListQuery(string CityId, bool OnlyDisplayed = true) : IQuery<ConstructionListResult>
{
    public string Type => "cities.ConstructionList";

    public ConstructionListResult Run(GameView view)
    {
        var city = Units.UnitQueryHelpers.OwnCity(view, CityId);
        var constructions = city.Constructions;
        var ruleset = city.GetRuleset();
        var all = new List<IConstruction>();
        all.AddRange(ruleset.Units.Values);
        all.AddRange(ruleset.Buildings.Values);
        all.AddRange(PerpetualConstruction.PerpetualConstructionsMap.Values);
        if (Logic.WorldCongress.LeagueRules.IsEnabled(ruleset)) all.AddRange(PerpetualConstruction.LeagueProcesses.Values); // B017

        var entries = new List<ConstructionEntry>();
        foreach (var construction in all)
        {
            bool displayed = constructions.ShouldBeDisplayed(construction);
            if (OnlyDisplayed && !displayed) continue;
            entries.Add(Describe(city, construction, displayed));
        }
        return new ConstructionListResult(CityId, entries);
    }

    private static ConstructionEntry Describe(CityView city, IConstruction construction, bool displayed)
    {
        var constructions = city.Constructions;
        string name = construction.Name;
        string category = construction switch
        {
            Building { IsWonder: true } => "Wonder",
            Building { IsNationalWonder: true } => "NationalWonder",
            Building => "Building",
            PerpetualConstruction => "Perpetual",
            _ => "Unit"
        };
        bool enqueued = constructions.IsBeingConstructedOrEnqueued(name);
        if (construction is not INonPerpetualConstruction nonPerpetual)
        {
            string perpetualState = constructions.IsBuildable(construction) ? (enqueued ? "enqueued" : "buildable") : "rejected";
            return new ConstructionEntry(name, category, perpetualState, displayed, null, null, 0, [], []);
        }

        bool built = construction is Building && constructions.IsBuilt(name);
        var rejections = constructions.GetRejectionReasons(nonPerpetual)
            .Select(it => new ConstructionRejection(it.Type.ToString(), it.ErrorMessage, it.ShouldShow)).ToList();
        bool buildable = constructions.IsBuildable(construction);
        string state = built ? "built" : buildable ? (enqueued ? "enqueued" : "buildable") : "rejected";

        // Like ConstructionsTable: a building uses stored production, a unit only if it is not already in the queue.
        bool useStored = construction is Building || !enqueued;
        int? turns = buildable ? constructions.TurnsToConstruction(name, useStored) : null;
        var buyCosts = new List<ConstructionBuyCost>();
        if (buildable)
        {
            foreach (var stat in StatExtensions.StatsUsableToBuy.OrderBy(it => (int)it))
            {
                if (!city.CanBePurchasedWithStat(nonPerpetual, stat)) continue;
                int? cost = constructions.GetStatBuyCost(nonPerpetual, stat);
                if (cost == null) continue;
                buyCosts.Add(new ConstructionBuyCost(stat.ToString(), cost.Value, constructions.IsConstructionPurchaseAllowed(nonPerpetual, stat, cost.Value)));
            }
        }
        return new ConstructionEntry(name, category, state, displayed, turns, city.GetConstructionProductionCost(nonPerpetual),
            constructions.GetWorkDone(name), rejections, buyCosts);
    }
}
