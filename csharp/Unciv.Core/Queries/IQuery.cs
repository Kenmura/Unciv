namespace Unciv.Queries;

// A question about the game from one civ's point of view (T185, see migration/design/COMMANDS-IMPLEMENTATION.md section 8).
//
// Contract: Run is pure (no mutation of game state, no RNG draws, no clock, no lazy cache that changes the state hash) and
// deterministic. It sees the game only through the GameView of the viewer, so it cannot learn more than the viewer may know.
// The query itself is the request payload (a plain record, json serializable); TResult is a plain record too: no engine
// objects, ruleset objects by name. Queries never read a "current player"; the viewer is always explicit.
public interface IQuery
{
    string Type { get; }

    object RunBoxed(View.GameView view);
}

public interface IQuery<out TResult> : IQuery where TResult : notnull
{
    TResult Run(View.GameView view);

    object IQuery.RunBoxed(View.GameView view) => Run(view);
}

// Marks a query class for assembly scanning by QueryRegistry; the name is the wire type.
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class QueryTypeAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
