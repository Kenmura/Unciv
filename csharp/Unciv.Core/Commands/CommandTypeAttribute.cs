namespace Unciv.Commands;

/// <summary>
/// Marks a <see cref="GameCommand"/> class and gives it its stable wire name (kebab-case, e.g. <c>rename-city</c>).
/// <see cref="GameCommandRegistry"/> finds every marked class by scanning the assembly, so domain tickets add
/// commands without editing a shared registry file.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CommandTypeAttribute(string name) : Attribute
{
    public string Name { get; } = name;

    public static string NameOf(Type commandClass) =>
        ((CommandTypeAttribute?)GetCustomAttribute(commandClass, typeof(CommandTypeAttribute), inherit: false))?.Name
        ?? throw new InvalidOperationException($"{commandClass.Name} has no [CommandType] attribute");
}
