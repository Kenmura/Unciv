using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Transients;
using Unciv.Logic.Map.Tiles;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Translations;
using Unciv.UI.Components.Extensions;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;

namespace Unciv.Logic.Map.MapUnit.Actions;

// Ported from core/src/com/unciv/logic/map/mapunit/actions/UnitActionsGreatPerson.kt (T079).
// The tile parameter is unused by some of these: they need the signature expected by UnitActions.actionTypeToFunctions (T080).
// Tile.GetCity() is typed by its seam; the owning city is always a City, hence the casts.
public static class UnitActionsGreatPerson
{
    internal static IEnumerable<UnitAction> GetHurryResearchActions(MapUnitClass unit, Tile tile)
    {
        _ = tile; // the signature expected by UnitActions.actionTypeToFunctions
        foreach (var unique in unit.GetMatchingUniques(UniqueType.CanHurryResearch))
        {
            yield return new UnitAction(
                UnitActionType.HurryResearch,
                associatedUnique: unique,
                action: unit.HasMovement()
                    && unit.Civ.Tech.CurrentTechnologyName() != null
                    && !((IHasUniques)unit.Civ.Tech.CurrentTechnology().NotNull()).HasUnique(UniqueType.CannotBeHurried)
                    ? () =>
                    {
                        unit.Civ.Tech.AddScience(unit.Civ.Tech.GetScienceFromGreatScientist());
                        unit.Consume();
                    }
            : null);
        }
    }

    internal static IEnumerable<UnitAction> GetHurryPolicyActions(MapUnitClass unit, Tile tile)
    {
        _ = tile; // the signature expected by UnitActions.actionTypeToFunctions
        foreach (var unique in unit.GetMatchingUniques(UniqueType.CanHurryPolicy))
        {
            yield return new UnitAction(
                UnitActionType.HurryPolicy,
                associatedUnique: unique,
                action: unit.HasMovement()
                    ? () =>
                    {
                        unit.Civ.Policies.AddCulture(unit.Civ.Policies.GetCultureFromGreatWriter());
                        unit.Consume();
                    }
            : null);
        }
    }

    internal static IEnumerable<UnitAction> GetHurryWonderActions(MapUnitClass unit, Tile tile)
    {
        foreach (var unique in unit.GetMatchingUniques(UniqueType.CanSpeedupWonderConstruction))
        {
            bool canHurryWonder;
            if (!tile.IsCityCenter()) canHurryWonder = false;
            else
            {
                var constructions = ((CityObject)tile.GetCity().NotNull()).CityConstructions;
                // T242: a wonder another civ finished meanwhile is still the current construction until the queue is validated at turn processing:
                // without the buildability check a second human's Great Engineer would be consumed for nothing (two humans race for a wonder in the same round).
                canHurryWonder = constructions.IsBuildingWonder() && constructions.CanBeHurried() && constructions.GetCurrentConstruction().IsBuildable(constructions);
            }

            yield return new UnitAction(
                UnitActionType.HurryWonder,
                associatedUnique: unique,
                action: unit.HasMovement() && canHurryWonder
                    ? () =>
                    {
                        var city = (CityObject)tile.GetCity().NotNull();
                        var cityConstructions = city.CityConstructions;
                        //http://civilization.wikia.com/wiki/Great_engineer_(Civ5)
                        cityConstructions.AddProductionPoints(KotlinMath.FloatToInt((300 + 30 * city.Population.Population) * unit.Civ.GameInfo.Speed.ProductionCostModifier));
                        cityConstructions.ConstructIfEnough();

                        unit.Consume();
                    }
            : null);
        }
    }

    internal static IEnumerable<UnitAction> GetHurryBuildingActions(MapUnitClass unit, Tile tile)
    {
        foreach (var unique in unit.GetMatchingUniques(UniqueType.CanSpeedupConstruction))
        {
            if (!tile.IsCityCenter())
            {
                yield return new UnitAction(UnitActionType.HurryBuilding, associatedUnique: unique, action: null);
                continue;
            }

            var city = (CityObject)tile.GetCity().NotNull();
            var cityConstructions = city.CityConstructions;
            bool canHurryConstruction = cityConstructions.GetCurrentConstruction() is Building
                && cityConstructions.CanBeHurried();

            //http://civilization.wikia.com/wiki/Great_engineer_(Civ5)
            int productionPointsToAdd = KotlinMath.FloatToInt(MathF.Min(
                (300 + 30 * city.Population.Population) * unit.Civ.GameInfo.Speed.ProductionCostModifier,
                cityConstructions.GetRemainingWork(cityConstructions.CurrentConstructionName()) - 1f));
            if (productionPointsToAdd <= 0) continue;

            yield return new UnitAction(
                UnitActionType.HurryBuilding,
                title: $"Hurry Construction (+[{productionPointsToAdd}]⚙)",
                associatedUnique: unique,
                action: unit.HasMovement() && canHurryConstruction
                    ? () =>
                    {
                        cityConstructions.AddProductionPoints(productionPointsToAdd);
                        cityConstructions.ConstructIfEnough();

                        unit.Consume();
                    }
            : null);
        }
    }

    internal static IEnumerable<UnitAction> GetConductTradeMissionActions(MapUnitClass unit, Tile tile)
    {
        var owningCiv = ((CityObject?)tile.OwningCity)?.Civ;
        bool canConductTradeMission = owningCiv?.IsCityState == true
            && !ReferenceEquals(owningCiv, unit.Civ)
            && !owningCiv.IsAtWarWith(unit.Civ);
        foreach (var unique in unit.GetMatchingUniques(UniqueType.CanTradeWithCityStateForGoldAndInfluence))
        {
            float influenceEarned = unique.Params[0].ToFloat();

            yield return new UnitAction(
                UnitActionType.ConductTradeMission,
                associatedUnique: unique,
                action: unit.HasMovement() && canConductTradeMission
                    ? () =>
                    {
                        // http://civilization.wikia.com/wiki/Great_Merchant_(Civ5)
                        float goldEarned = (350 + 50 * unit.Civ.GetEraNumber()) * unit.Civ.GameInfo.Speed.GoldCostModifier;

                        // Apply the gold trade mission modifier
                        foreach (var goldUnique in unit.GetMatchingUniques(UniqueType.PercentGoldFromTradeMissions, checkCivInfoUniques: true))
                            goldEarned *= goldUnique.Params[0].ToPercent();

                        int goldEarnedInt = KotlinMath.FloatToInt(goldEarned);
                        unit.Civ.AddGold(goldEarnedInt);
                        var tileOwningCiv = ((CityObject)tile.OwningCity.NotNull()).Civ;

                        tileOwningCiv.GetDiplomacyManager(unit.Civ).NotNull().AddInfluence(influenceEarned);
                        unit.Civ.AddNotification($"Your trade mission to [{tileOwningCiv}] has earned you [{goldEarnedInt.Tr()}] gold and [{influenceEarned.Tr()}] influence!",
                            NotificationCategory.General, tileOwningCiv.CivName, NotificationIcon.Gold, NotificationIcon.Culture);
                        unit.Consume();
                    }
            : null);
        }
    }
}
