using System.Globalization;
using Unciv.Commands.City;

namespace Unciv.Assets;

/// <summary>
/// C048: the rules of the "Rename city" popup as plain data, Godot-free so they are unit-tested. Kotlin <c>CityRenamePopup</c> only refused the empty name; the client also
/// trims the text, caps the length and refuses the characters that the translation layer and the label markup treat specially (a name like <c>[Ur]</c> would be read as a
/// placeholder), a deliberate deviation recorded in C048 Notes. The engine still validates (empty, owner, turn) when the command arrives.
/// </summary>
public static class CityRenameModel
{
    /// <summary>The longest name the popup accepts, after trimming.</summary>
    public const int MaxLength = 32;

    private const string ForbiddenChars = "[]{}\"\\<>";

    /// <summary>The name that would be sent for what was typed: the text without leading and trailing white space.</summary>
    public static string Normalize(string typed) => typed.Trim();

    /// <summary>Why <paramref name="typed"/> cannot be the city's new name (null = it can). <paramref name="current"/> is the name now, which is refused as "no change".</summary>
    public static string? Problem(string typed, string current)
    {
        string name = Normalize(typed);
        if (name.Length == 0) return "Enter a name";
        if (name.Length > MaxLength) return $"The name can have at most {MaxLength.ToString(CultureInfo.InvariantCulture)} characters";
        if (name.Any(it => char.IsControl(it) || ForbiddenChars.Contains(it))) return "The name cannot contain any of [ ] { } \" \\ < >";
        if (name == current) return "That is already the city's name";
        return null;
    }

    /// <summary>The command a confirmed name sends, or null while <see cref="Problem"/> says something.</summary>
    public static RenameCity? Command(string cityId, string current, string typed) =>
        Problem(typed, current) == null ? new RenameCity { CityId = cityId, Name = Normalize(typed) } : null;
}
