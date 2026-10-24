using Unciv.Models.StatTypes;

namespace Unciv.Models.Ruleset;

// core/src/com/unciv/models/ruleset/Specialist.kt
// Not ported yet: colorObject (colorFromRGB needs the engine colour type, T050).
public class Specialist : NamedStats
{
    public List<int> Color { get; set; } = [];
    public Counter<string> GreatPersonPoints { get; set; } = new();
}
