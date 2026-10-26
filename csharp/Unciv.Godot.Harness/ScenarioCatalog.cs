namespace Unciv.GodotHarness;

/// <summary>Every scenario by name. A client slice adds its scenario here and its name to <c>scenarios.txt</c> (read by review-sheet.sh).</summary>
public static class ScenarioCatalog
{
    public static IReadOnlyList<Scenario> All { get; } = [new RenameCityScenario(), new MapV1Scenario(), new V1VerticalScenario(), new MinimapScenario(), new UiKitScenario(), new Terrain3DScenario(), new SettingsScenario(), new Props3DScenario(), new HudScenario(), new FlatCityScenario(), new SaveLoadScenario(), new CityAScenario(), new UnitOrdersScenario(), new DiplomacyScenario(), new TradeScenario(), new SetupScenario(), new CityBScenario(), new UnitVisualsScenario(), new TechScenario(), new PolicyScenario(), new L10nScenario(), new AlertsScenario(), new VictoryScenario(), new DefeatScenario(), new OverviewScenario(), new OptionsScenario(), new ImprovementsScenario(), new MenuScenario(), new DecisionsScenario(), new TutorialScenario(), new TooltipsScenario(), new CombatPresentationScenario(), new CityRenameScenario()];

    public static Scenario? Find(string name) => All.FirstOrDefault(it => it.Name == name);
}
