using Unciv.View;

namespace Unciv.Queries.Empire;

// Name: Great person unit name (the nation's equivalent unit)
// Points: Points gathered towards the next one
// Required: Points needed for the next one, at the current game speed
// PerTurn: Points the cities add next turn
public sealed record GreatPersonEntry(string Name, int Points, int Required, int PerTurn);

// GreatGeneralPoints: Points towards the next great general
public sealed record GreatPeopleResult(IReadOnlyList<GreatPersonEntry> GreatPeople, int GreatGeneralPoints, int FreeGreatPeople,
    IReadOnlyList<string> FreeOptions);

// T197: the great people panel and the free great person picker. Required uses GreatPersonManager.PeekPointsRequiredForGreatPerson:
// the Kotlin getter writes the initial 100 into the saved counter, which a query must not do.
[QueryType("empire.GreatPeople")]
public sealed record GreatPeopleQuery : IQuery<GreatPeopleResult>
{
    public string Type => "empire.GreatPeople";

    public GreatPeopleResult Run(GameView view)
    {
        var civView = view.CivView;
        var civ = civView.Unwrap();
        var manager = civ.GreatPeople;
        var perTurn = civView.GetGreatPersonPointsForNextTurn();
        var people = manager.GetGreatPeople()
            .Select(it => new GreatPersonEntry(it.Name, civView.GetGreatPersonPoints(it.Name), manager.PeekPointsRequiredForGreatPerson(it.Name), perTurn[it.Name]))
            .OrderBy(it => it.Name, StringComparer.Ordinal).ToList();
        return new GreatPeopleResult(people, manager.GreatGeneralPoints, manager.FreeGreatPeople,
            manager.GetFreeGreatPersonOptions().Select(it => it.Name).OrderBy(it => it, StringComparer.Ordinal).ToList());
    }
}
