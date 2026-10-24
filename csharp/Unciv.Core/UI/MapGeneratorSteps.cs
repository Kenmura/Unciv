using Unciv.Logic.Map;

namespace Unciv.UI.Screens.MapEditorScreen;

// core/src/com/unciv/ui/screens/mapeditorscreen/MapGeneratorSteps.kt
public enum MapGeneratorSteps
{
    None,
    All,
    Landmass,
    HumidityAndTemperature,
    Elevation,
    LakesAndCoast,
    Vegetation,
    RareFeatures,
    Ice,
    Continents,
    NaturalWonders,
    Rivers,
    Resources,
    AncientRuins,
}

public static class MapGeneratorStepsExtensions
{
    public static string Label(this MapGeneratorSteps step) => step switch
    {
        MapGeneratorSteps.None => "",
        MapGeneratorSteps.All => "All",
        MapGeneratorSteps.Landmass => "Generate landmass",
        MapGeneratorSteps.HumidityAndTemperature => "Humidity and temperature",
        MapGeneratorSteps.Elevation => "Raise mountains and hills",
        MapGeneratorSteps.LakesAndCoast => "Lakes and coastline",
        MapGeneratorSteps.Vegetation => "Sprout vegetation",
        MapGeneratorSteps.RareFeatures => "Spawn rare features",
        MapGeneratorSteps.Ice => "Distribute ice",
        MapGeneratorSteps.Continents => "Assign continent IDs",
        MapGeneratorSteps.NaturalWonders => "Place Natural Wonders",
        MapGeneratorSteps.Rivers => "Let the rivers flow",
        MapGeneratorSteps.Resources => "Spread Resources",
        MapGeneratorSteps.AncientRuins => "Create ancient ruins",
        _ => throw new ArgumentOutOfRangeException(nameof(step)),
    };

    /// <summary>Copies the parameters belonging to this step from <c>newParameters</c> to <c>actualParameters</c>; null where Kotlin had no copier.</summary>
    public static Action<MapParameters, MapParameters>? CopyParameters(this MapGeneratorSteps step) => step switch
    {
        MapGeneratorSteps.Landmass => (n, a) =>
        {
            a.Type = n.Type;
            a.WaterThreshold = n.WaterThreshold;
            a.Seed = n.Seed;
        }
        ,
        MapGeneratorSteps.HumidityAndTemperature => (n, a) =>
        {
            a.TemperatureIntensity = n.TemperatureIntensity;
            a.TemperatureShift = n.TemperatureShift;
        }
        ,
        MapGeneratorSteps.Elevation => (n, a) => a.ElevationExponent = n.ElevationExponent,
        MapGeneratorSteps.LakesAndCoast => (n, a) => a.MaxCoastExtension = n.MaxCoastExtension,
        MapGeneratorSteps.Vegetation => (n, a) => a.VegetationRichness = n.VegetationRichness,
        MapGeneratorSteps.RareFeatures => (n, a) => a.RareFeaturesRichness = n.RareFeaturesRichness,
        MapGeneratorSteps.Resources => (n, a) => a.ResourceRichness = n.ResourceRichness,
        _ => null,
    };
}
