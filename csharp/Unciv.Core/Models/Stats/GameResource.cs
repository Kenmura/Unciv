namespace Unciv.Models.StatTypes;

// Kotlin: `interface GameResource { val name: String }`, implemented by the enums Stat and SubStat and the class TileResource.
// C# enums cannot implement interfaces, so Stat and SubStat do NOT implement this. Code needing the polymorphic
// "Stat | SubStat | TileResource" value (Civilization.addGameResource etc.) must be solved by the ticket that ports it
// (e.g. pattern-match over `object`). TileResource can implement this interface.
public interface IGameResource
{
    string Name { get; }
}
