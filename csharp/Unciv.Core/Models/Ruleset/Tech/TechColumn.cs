
namespace Unciv.Models.Ruleset.Tech;

// core/src/com/unciv/models/ruleset/tech/TechColumn.kt
public class TechColumn
{
    private string? era;

    public int ColumnNumber { get; set; }

    /// <summary>Kotlin `lateinit var`</summary>
    public string Era
    {
        get => era ?? throw new InvalidOperationException("lateinit property era has not been initialized");
        set => era = value;
    }

    public List<Technology> Techs { get; set; } = [];
    public int TechCost { get; set; }
    public int BuildingCost { get; set; } = -1;
    public int WonderCost { get; set; } = -1;
}
