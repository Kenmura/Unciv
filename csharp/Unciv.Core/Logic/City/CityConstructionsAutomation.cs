using Unciv.Logic.Automation.City;

namespace Unciv.Logic.City;

// T158: keeps CityConstructions free of a dependency on Unciv.Logic.Automation.City (coupling ratchet); the Kotlin call is
// `ConstructionAutomation(this).chooseNextConstruction()`.
internal static class CityConstructionsAutomation
{
    internal static void ChooseNextConstruction(CityConstructions cityConstructions) =>
        new ConstructionAutomation(cityConstructions).ChooseNextConstruction();
}
