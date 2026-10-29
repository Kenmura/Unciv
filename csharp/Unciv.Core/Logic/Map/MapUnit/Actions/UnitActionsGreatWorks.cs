using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Transients;
using Unciv.Logic.GreatWorks;
using Unciv.Logic.Map.Tiles;
using Unciv.Logic.Tourism;
using Unciv.Models;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Translations;
using Unciv.UI.Components;
using Unciv.Utils;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;

namespace Unciv.Logic.Map.MapUnit.Actions;

// Brave New World (B011, bnw-spec/SPEC.md 4.2.3, 4.3.6, 4.5). Not part of the Kotlin source.
// The unit actions of the Great Writer (Political Treatise), Great Artist, Writer and Musician (Create Great Work) and Great Musician (Concert Tour).
public static class UnitActionsGreatWorks
{
    /// <summary>The Kotlin hurry-policy actions plus the BNW Political Treatise: the Great Writer gives the culture of N turns at the current rate.</summary>
    internal static IEnumerable<UnitAction> GetHurryPolicyActions(MapUnitClass unit, Tile tile)
    {
        foreach (var action in UnitActionsGreatPerson.GetHurryPolicyActions(unit, tile)) yield return action;
        foreach (var unique in unit.GetMatchingUniques(UniqueType.GreatWorkTurnsOfCulture))
        {
            yield return new UnitAction(
                UnitActionType.HurryPolicy,
                title: $"{{Political Treatise}} ({Fonts.Death})",
                associatedUnique: unique,
                action: unit.HasMovement()
                    ? () =>
                    {
                        unit.Civ.Policies.AddCulture(GreatWorkRules.PoliticalTreatiseCulture(unit.Civ, unique.Params[0].ToInt()));
                        unit.Consume();
                    }
            : null);
        }
    }

    /// <summary>Brave New World (B011): a Great Writer, Artist or Musician in one of its owner's cities creates a Great Work (bnw-spec/SPEC.md 4.2.3).</summary>
    internal static IEnumerable<UnitAction> GetCreateGreatWorkActions(MapUnitClass unit, Tile tile)
    {
        var ruleset = unit.Civ.GameInfo.Ruleset;
        if (!GreatWorkRules.IsEnabled(ruleset) || GreatWorkRules.ClassCreatedBy(unit, ruleset) == null) yield break;
        yield return new UnitAction(
            UnitActionType.CreateGreatWork,
            action: unit.HasMovement() && GreatWorkRules.CanCreate(unit.Civ, unit, tile)
                ? () => { GreatWorkRules.Create(unit.Civ, unit); }
        : null);
    }

    /// <summary>Brave New World (B011): a Great Musician's Concert Tour in the territory of a civ it can influence (bnw-spec/SPEC.md 4.3.6).</summary>
    internal static IEnumerable<UnitAction> GetConcertTourActions(MapUnitClass unit, Tile tile)
    {
        if (!TourismRules.IsEnabled(unit.Civ)) yield break;
        var target = tile.GetOwner() as CivilizationObject;
        bool canTour = target != null && InfluenceRules.CanInfluence(unit.Civ, target);
        foreach (var unique in unit.GetMatchingUniques(UniqueType.TourismBlast))
        {
            yield return new UnitAction(
                UnitActionType.ConcertTour,
                associatedUnique: unique,
                action: unit.HasMovement() && canTour
                    ? () =>
                    {
                        int strength = InfluenceRules.ApplyBlast(unit.Civ, target.NotNull(), unique.Params[0].ToInt(), unique.Params[1].ToInt());
                        unit.Civ.AddNotification($"Your concert tour in [{target}] has made your culture known to the world (+[{strength.Tr()}] influence)!",
                            NotificationCategory.General, target.CivName, NotificationIcon.Culture);
                        unit.Consume();
                    }
            : null);
        }
    }
}
