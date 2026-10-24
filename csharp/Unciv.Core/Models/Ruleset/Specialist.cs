using System.Text.Json.Serialization;
using Unciv.Models.StatTypes;
using Unciv.UI.Components.Extensions;
using Unciv.Utils;

namespace Unciv.Models.Ruleset;

// core/src/com/unciv/models/ruleset/Specialist.kt
public class Specialist : NamedStats
{
    private KotlinLazy<EngineColor>? colorObject;

    /// <summary>Kotlin <c>val colorObject by lazy { colorFromRGB(color) }</c></summary>
    [JsonIgnore]
    public EngineColor ColorObject => (colorObject ??= new KotlinLazy<EngineColor>(() => ColorExtensions.ColorFromRGB(Color))).Value;

    public List<int> Color { get; set; } = [];
    public Counter<string> GreatPersonPoints { get; set; } = new();
}
