using Unciv.Models.Translations;

namespace Unciv.Logic.Map.MapUnits;

// Ported from core/src/com/unciv/logic/map/mapunit/MapUnit.kt (getMovementString, T139). Its own partial file so that MapUnit.cs
// does not take on a dependency on the translation namespace (CouplingRatchetTests).
public partial class MapUnit
{
    /// <summary>
    /// Current/max movement formatted with the language's number symbols, 0 to 1 fraction digits and no grouping
    /// (<c>getAndModifyCurrentNumberFormat</c>; the Kotlin DecimalFormat default HALF_EVEN rounding).
    /// </summary>
    public string GetMovementString()
    {
        var format = TranslationCache.GetLangData().NumberFormatter;
        // Kotlin boxes the Float as a Number, which DecimalFormat formats through doubleValue(); getMaxMovement is an Int, formatted as a Long
        return format.Format((double)CurrentMovement, 1, false) + "/" + format.Format((long)GetMaxMovement(), false);
    }
}
