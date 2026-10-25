namespace Unciv.DevConsole;

public enum ConsoleResponseKind
{
    Ok,
    Hint,
    Error,
}

/// <summary>Kotlin: <c>DevConsoleResponse</c>; the colors (green, gold, red) are the client's choice per <see cref="Kind"/>.</summary>
public sealed record ConsoleResponse(ConsoleResponseKind Kind, string? Message = null)
{
    public static ConsoleResponse OK { get; } = new(ConsoleResponseKind.Ok);

    public bool IsOK => Kind == ConsoleResponseKind.Ok;

    public static ConsoleResponse Ok(string message) => new(ConsoleResponseKind.Ok, message);

    public static ConsoleResponse Error(string message) => new(ConsoleResponseKind.Error, message);

    public static ConsoleResponse Hint(string message) => new(ConsoleResponseKind.Hint, message);
}

/// <summary>
/// What a console line would do: the response to show plus the state change. Handlers are pure: they validate and resolve everything
/// while building the plan and put the mutation into <see cref="Effect"/>, so a line can be checked (<c>Validate</c>) without running it
/// and applied (<c>Apply</c>) as a logged command. An effect exists only with an OK response.
/// </summary>
public sealed record ConsolePlan(ConsoleResponse Response, Action? Effect = null)
{
    public static ConsolePlan Do(Action effect) => new(ConsoleResponse.OK, effect);

    public static ConsolePlan Hint(string message) => new(ConsoleResponse.Hint(message));

    public static ConsolePlan Error(string message) => new(ConsoleResponse.Error(message));
}
