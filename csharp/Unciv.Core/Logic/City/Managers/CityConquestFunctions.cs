using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Civilization.Transients;
using Unciv.Logic.Map.MapUnits;
using Unciv.Logic.Trade;
using Unciv.Models.Ruleset.Unique;
using Unciv.UI.Components.Extensions;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;
using TradeObject = Unciv.Logic.Trade.Trade;

namespace Unciv.Logic.City.Managers;

// Ported from core/src/com/unciv/logic/city/managers/CityConquestFunctions.kt (T064).
//
// Stripped: the purity annotations; GUI.setUpdateWorldOnNextRender() in annexCity (a client redraw request, not part of the engine).
// Seams: Battle.destroyIfDefeated and TradeLogic.acceptTrade go through City.ConquestExternals (CityManagerHooks).
// Quirks kept: the tileBasedRandom is seeded by the Kotlin data class hashCode of the center tile's position (HexCoord.GetHashCode is
// that hash) and is consumed in call order (gold, then destroyBuildingsOnCapture); the `max(0, min(50, ..)) / 50f` turn modifier is float.

/// <summary>Helper class for containing 200 lines of "how to move cities between civs"</summary>
public class CityConquestFunctions(CityObject city)
{
    public const float MinorFriendshipAtWar = -60f;
    public const float MinorLiberationFriendship = 105f;

    private readonly CityObject city = city;
    private readonly GameRandom tileBasedRandom = new(city.GetCenterTile().Position.GetHashCode());

    private int GetGoldForCapturingCity(CivilizationObject conqueringCiv)
    {
        var baseGold = 20 + 10 * city.Population.Population + tileBasedRandom.NextInt(40);
        var turnModifier = Math.Max(0, Math.Min(50, city.Civ.GameInfo.Turns - city.TurnAcquired)) / 50f;

        var cityModifier = 1f;
        city.ForEachMatchingUnique(UniqueType.GoldFromCapturingCity, city.State, unique =>
        {
            cityModifier *= unique.Params[0].ToPercent();
        });

        var conqueringCivModifier = 1f;
        conqueringCiv.ForEachMatchingUnique(UniqueType.GoldFromEncampmentsAndCities, conqueringCiv.State, unique =>
        {
            conqueringCivModifier *= unique.Params[0].ToPercent();
        });

        var goldPlundered = baseGold * turnModifier * cityModifier * conqueringCivModifier;
        return KotlinMath.FloatToInt(goldPlundered);
    }

    private void DestroyBuildingsOnCapture()
    {
        // Possibly remove other buildings
        foreach (var building in city.CityConstructions.GetBuiltBuildings().ToList())
        {
            if (((IHasUniques)building).HasUnique(UniqueType.NotDestroyedWhenCityCaptured) || building.IsWonder) continue;
            if (((IHasUniques)building).HasUnique(UniqueType.IndicatesCapital, city.State)) continue; // Palace needs to stay a just a bit longer so moveToCiv isn't confused
            if (((IHasUniques)building).HasUnique(UniqueType.MovesToNewCapital, city.State)) continue; // Will move to the civ's new capital
            if (((IHasUniques)building).HasUnique(UniqueType.DestroyedWhenCityCaptured))
                city.CityConstructions.RemoveBuilding(building);
            // Regular buildings have a 34% chance of removal
            else if (tileBasedRandom.NextInt(100) < 34) city.CityConstructions.RemoveBuilding(building);
        }
    }

    private void RemoveAutoPromotion()
    {
        city.UnitShouldUseSavedPromotion = new JavaHashMap<string, bool>();
        city.UnitToPromotions = new JavaHashMap<string, UnitPromotions>();
    }

    private void RemoveBuildingsOnMoveToCiv()
    {
        // Remove all buildings provided for free to this city
        // At this point, the city has *not* yet moved to the new civ
        foreach (var building in city.Civ.CivConstructions.GetFreeBuildingNames(city))
        {
            city.CityConstructions.RemoveBuilding(building);
        }
        city.CityConstructions.FreeBuildingsProvidedFromThisCity.Clear();

        foreach (var building in city.CityConstructions.GetBuiltBuildings().ToList())
        {
            // Remove national wonders
            if (building.IsNationalWonder && !((IHasUniques)building).HasUnique(UniqueType.NotDestroyedWhenCityCaptured))
                city.CityConstructions.RemoveBuilding(building);

            // Check if we exceed MaxNumberBuildable for any buildings
            ((IHasUniques)building).ForEachMatchingUnique(UniqueType.MaxNumberBuildable, GameContext.EmptyState, unique =>
            {
                if (city.Civ.Cities
                        .Count(it =>
                            it.CityConstructions.ContainsBuildingOrEquivalent(building.Name)
                            || it.CityConstructions.IsBeingConstructedOrEnqueued(building.Name)
                        ) >= unique.Params[0].ToInt()
                )
                {
                    // For now, just destroy in new city. Even if constructing in own cities
                    city.CityConstructions.RemoveBuilding(building);
                }
            });
        }
    }

    /// <summary>
    /// Function for stuff that should happen on any capture, be it puppet, annex or liberate.
    /// Stuff that should happen any time a city is moved between civs, so also when trading,
    /// should go in <see cref="MoveToCiv"/>, which is called by this function.
    /// </summary>
    private void ConquerCity(CivilizationObject conqueringCiv, CivilizationObject conqueredCiv, CivilizationObject receivingCiv)
    {
        city.Espionage.RemoveAllPresentSpies(SpyFleeReason.CityCaptured);

        // Gain gold for plundering city
        var goldPlundered = GetGoldForCapturingCity(conqueringCiv);
        conqueringCiv.AddGold(goldPlundered);
        conqueringCiv.AddNotification($"Received [{goldPlundered}] Gold for capturing [{city.Name}]",
            city.GetCenterTile().Position, NotificationCategory.General, NotificationIcon.Gold);

        var reconqueredCityWhileStillInResistance = city.PreviousOwner == receivingCiv.CivId && city.IsInResistance();

        DestroyBuildingsOnCapture();

        city.MoveToCiv(receivingCiv);
        GameEventHooks.CityCaptured(city, conqueringCiv, conqueredCiv, receivingCiv);

        city.ConquestExternals.DestroyIfDefeated(conqueredCiv, conqueringCiv, city.Location.ToHexCoord());

        city.Health = city.GetMaxHealth() / 2; // I think that cities recover to half health when conquered?
        city.AvoidGrowth = false; // reset settings
        city.SetCityFocus(CityFocus.NoFocus); // reset settings
        if (city.Population.Population > 1)
            city.Population.AddPopulation(-1 - city.Population.Population / 4); // so from 2-4 population, remove 1, from 5-8, remove 2, etc.
        city.ReassignAllPopulation();

        if (!reconqueredCityWhileStillInResistance && city.FoundingCivObject != receivingCiv)
        {
            // add resistance
            // I checked, and even if you puppet there's resistance for conquering
            city.SetFlag(CityFlags.Resistance, city.Population.Population);
        }
        else
        {
            // reconquering or liberating city in resistance so eliminate it
            city.RemoveFlag(CityFlags.Resistance);
        }

        conqueredCiv.ForEachTriggeredUnique(UniqueType.TriggerUponLosingCity, new GameContext(civInfo: conqueredCiv), _ => true, false, unique =>
        {
            UniqueTriggerActivation.TriggerUnique(unique, civInfo: conqueredCiv);
        });
    }

    /// <summary>This happens when we either puppet OR annex, basically whenever we conquer a city and don't liberate it</summary>
    public void PuppetCity(CivilizationObject conqueringCiv)
    {
        var oldCiv = city.Civ;

        // must be before moving the city to the conquering civ,
        // so the repercussions are properly checked
        DiplomaticRepercussionsForConqueringCity(oldCiv, conqueringCiv);

        ConquerCity(conqueringCiv, oldCiv, conqueringCiv);
        MakePuppet();
        city.CityStats.Update();
    }

    private void MakePuppet()
    {
        city.IsPuppet = true;
        // The city could be producing something that puppets shouldn't, like units
        city.CityConstructions.RemoveAll();
    }

    public void AnnexCity()
    {
        city.IsPuppet = false;
        if (!city.IsInResistance()) city.ShouldReassignPopulation = true;
        city.AvoidGrowth = false;
        city.SetCityFocus(CityFocus.NoFocus);
        city.CityStats.Update();
        // GUI.setUpdateWorldOnNextRender(): the client redraw request is not part of the engine.
    }

    private void DiplomaticRepercussionsForConqueringCity(CivilizationObject oldCiv, CivilizationObject conqueringCiv)
    {
        // Deviation from Kotlin (T238): a civ "conquering" its own city (only reachable from a fabricated
        // CityConquered alert) has nobody to anger and must not meet itself.
        if (oldCiv == conqueringCiv) return;

        var currentPopulation = city.Population.Population;
        var percentageOfCivPopulationInThatCity = currentPopulation * 100f /
                oldCiv.Cities.Sum(it => it.Population.Population);
        var aggroGenerated = 10f + KotlinMath.RoundToInt(percentageOfCivPopulationInThatCity);

        // How can you conquer a city but not know the civ you conquered it from?!
        // I don't know either, but some of our players have managed this, and crashed their game!
        if (!conqueringCiv.Knows(oldCiv))
            conqueringCiv.DiplomacyFunctions.MakeCivilizationsMeet(oldCiv);

        oldCiv.GetDiplomacyManager(conqueringCiv).NotNull()
                .AddModifier(DiplomaticModifiers.CapturedOurCities, -aggroGenerated);

        foreach (var thirdPartyCiv in conqueringCiv.GetKnownCivs().Where(it => it.IsMajorCiv()).ToList())
        {
            var aggroGeneratedForOtherCivs = (float)KotlinMath.RoundToInt(aggroGenerated / 10);
            if (thirdPartyCiv.IsAtWarWith(oldCiv)) // Shared Enemies should like us more
                thirdPartyCiv.GetDiplomacyManager(conqueringCiv).NotNull()
                        .AddModifier(DiplomaticModifiers.SharedEnemy, aggroGeneratedForOtherCivs); // Cool, keep at it! =D
            else thirdPartyCiv.GetDiplomacyManager(conqueringCiv).NotNull()
                    .AddModifier(DiplomaticModifiers.WarMongerer, -aggroGeneratedForOtherCivs); // Uncool bro.
        }
    }

    public void LiberateCity(CivilizationObject conqueringCiv)
    {
        if (city.FoundingCivObject == null)
        { // this should never happen but just in case...
            PuppetCity(conqueringCiv);
            AnnexCity();
            return;
        }

        var foundingCiv = city.FoundingCivObject;
        var oldCiv = city.Civ;

        DiplomaticRepercussionsForLiberatingCity(conqueringCiv, oldCiv);

        ConquerCity(conqueringCiv, oldCiv, foundingCiv);

        if (foundingCiv.Cities.Count == 1)
        {
            // Resurrection!
            var capitalCityIndicator = conqueringCiv.CapitalCityIndicator(city);
            if (capitalCityIndicator != null) city.CityConstructions.AddBuilding(capitalCityIndicator);
            foreach (var civ in city.Civ.GameInfo.Civilizations)
            {
                if (civ == foundingCiv || civ == conqueringCiv) continue; // don't need to notify these civs
                if (civ.Knows(conqueringCiv) && civ.Knows(foundingCiv))
                    civ.AddNotification($"[{conqueringCiv}] has liberated [{foundingCiv}]", NotificationCategory.Diplomacy, foundingCiv.CivName, NotificationIcon.Diplomacy, conqueringCiv.CivName);
                else if (civ.Knows(conqueringCiv) && !civ.Knows(foundingCiv))
                    civ.AddNotification($"[{conqueringCiv}] has liberated [an unknown civilization]", NotificationCategory.Diplomacy, NotificationIcon.Diplomacy, conqueringCiv.CivName);
                else if (!civ.Knows(conqueringCiv) && civ.Knows(foundingCiv))
                    civ.AddNotification($"[An unknown civilization] has liberated [{foundingCiv}]", NotificationCategory.Diplomacy, NotificationIcon.Diplomacy, foundingCiv.CivName);
            }
        }
        city.IsPuppet = false;
        city.CityStats.Update();

        // Move units out of the city when liberated
        foreach (var unit in city.GetCenterTile().GetUnits().ToList())
            unit.Movement.TeleportToClosestMoveableTile();
        foreach (var unit in city.GetTiles().SelectMany(it => it.GetUnits()).ToList())
            if (!unit.Movement.CanPassThrough(unit.CurrentTile))
                unit.Movement.TeleportToClosestMoveableTile();
    }

    private void DiplomaticRepercussionsForLiberatingCity(CivilizationObject conqueringCiv, CivilizationObject conqueredCiv)
    {
        var foundingCiv = city.FoundingCivObject.NotNull();
        var percentageOfCivPopulationInThatCity = city.Population.Population *
            100f / (foundingCiv.Cities.Sum(it => it.Population.Population) + city.Population.Population);
        var respectForLiberatingOurCity = 10f + KotlinMath.RoundToInt(percentageOfCivPopulationInThatCity);

        if (foundingCiv.IsMajorCiv())
        {
            if (foundingCiv.IsDefeated())
            { // resurrected civ
                foreach (var diploManager in foundingCiv.Diplomacy.Values)
                {
                    if (diploManager.DiplomaticStatus == DiplomaticStatus.War)
                        diploManager.MakePeace();

                    // Clear all diplomatic flags and modifiers to prevent asymmetry after resurrection
                    // The defeated civ's flags were frozen while other civs' flags continued to expire
                    // Perhaps some of this needs more dedicated treatment but it's a pretty rare case anyway
                    //   so I think starting from scratch is a good way to go
                    diploManager.FlagsCountdown.Clear();
                    diploManager.OtherCivDiplomacy().FlagsCountdown.Clear();
                    diploManager.DiplomaticModifiers.Clear();
                    diploManager.OtherCivDiplomacy().DiplomaticModifiers.Clear();
                }
            }

            // In order to get "plus points" in Diplomacy, you have to establish diplomatic relations if you haven't yet
            foundingCiv.GetDiplomacyManagerOrMeet(conqueringCiv)
                .AddModifier(DiplomaticModifiers.CapturedOurCities, respectForLiberatingOurCity);
            var openBordersTrade = new TradeObject();
            openBordersTrade.OurOffers.Add(new TradeOffer(Constants.OpenBorders, TradeOfferType.Agreement, speed: conqueringCiv.GameInfo.Speed));
            city.ConquestExternals.AcceptTrade(foundingCiv, conqueringCiv, openBordersTrade);
        }
        else
        {
            // Liberating a city state gives a large amount of influence, and peace
            // as per discussion in #15226
            var maxOtherInfluence = foundingCiv.Diplomacy.Values
                .Where(it => it.OtherCiv != conqueringCiv && it.OtherCiv.IsMajorCiv() && it.OtherCiv.IsAlive())
                .Aggregate(MinorFriendshipAtWar, (a, b) => Math.Max(a, b.GetInfluence()));
            var diplomacy = foundingCiv.GetDiplomacyManagerOrMeet(conqueringCiv);
            var liberatorOldInfluence = diplomacy.GetInfluence();
            var liberatorNewInfluence = Math.Max(maxOtherInfluence, liberatorOldInfluence)
                + MinorLiberationFriendship;
            liberatorNewInfluence = Math.Max(liberatorNewInfluence, 60f); // (Kotlin notes: should be a const val, hardcoded in several places)
            diplomacy.SetInfluence(liberatorNewInfluence);
            // Peace with anyone else the city state is stuck at war with
            // Note that currently, a liberated city state does NOT automatically declare war against other civs
            //   the liberator may be at war with - if it just returned to life it's weird to have it immediately
            //   be at war, IMO, even if it will autojoin any wars declared from here on as per regular ally rules
            var civsToMakePeaceWith = foundingCiv.Diplomacy.Values
                .Where(it => it.DiplomaticStatus == DiplomaticStatus.War && it.OtherCiv.IsAlive())
                .Select(it => it.OtherCiv)
                .ToList();
            foreach (var civ in civsToMakePeaceWith)
            {
                var trade = new TradeObject();
                trade.OurOffers.Add(new TradeOffer(Constants.PeaceTreaty, TradeOfferType.Treaty, speed: conqueringCiv.GameInfo.Speed));
                trade.TheirOffers.Add(new TradeOffer(Constants.PeaceTreaty, TradeOfferType.Treaty, speed: conqueringCiv.GameInfo.Speed));
                city.ConquestExternals.AcceptTrade(foundingCiv, civ, trade);
            }
        }

        var otherCivsRespectForLiberating = (float)KotlinMath.RoundToInt(respectForLiberatingOurCity / 10);
        foreach (var thirdPartyCiv in conqueringCiv.GetKnownCivs().Where(it => it.IsMajorCiv() && it != conqueredCiv).ToList())
        {
            thirdPartyCiv.GetDiplomacyManager(conqueringCiv).NotNull()
                .AddModifier(DiplomaticModifiers.LiberatedCity, otherCivsRespectForLiberating); // Cool, keep at it! =D
        }
    }

    public void MoveToCiv(CivilizationObject newCiv)
    {
        var oldCiv = city.Civ;

        // Remove/relocate palace for old Civ - need to do this BEFORE we move the cities between
        //  civs so the capitalCityIndicator recognizes the unique buildings of the conquered civ
        if (city.IsCapital()) oldCiv.MoveCapitalToNextLargest(city);

        oldCiv.Cities = oldCiv.Cities.WithoutItem(city);
        newCiv.Cities = newCiv.Cities.WithItem(city);
        city.Civ = newCiv;
        GameEventHooks.CityMoved(city, oldCiv, newCiv);
        city.Id = city.Id != CityObject.NoId ? city.Id : CityObject.PseudoRandomId(newCiv);
        city.State = GameContext.ForCity(city);
        city.HasJustBeenConquered = false;
        city.TurnAcquired = city.Civ.GameInfo.Turns;
        city.PreviousOwner = oldCiv.CivId;

        // now that the tiles have changed, we need to reassign population
        foreach (var tile in city.GetWorkedTiles().Where(it => !city.Tiles.Contains(it.Position)).ToList())
        {
            city.StopWorkingTile(tile);
            city.Population.AutoAssignPopulation();
        }

        // Stop WLTKD if it's still going
        city.ResetWLTKD();

        // Remove their free buildings from this city and remove free buildings provided by the city from their cities
        RemoveBuildingsOnMoveToCiv();

        // Remove auto promotion from city that is being moved
        RemoveAutoPromotion();

        // catch-all - should ideally not happen as we catch the individual cases with an appropriate notification
        city.Espionage.RemoveAllPresentSpies(SpyFleeReason.Other);

        // Place palace for newCiv if this is the only city they have.
        if (newCiv.Cities.Count == 1) newCiv.MoveCapitalTo(city, null);

        // Add our free buildings to this city and add free buildings provided by the city to other cities
        city.Civ.CivConstructions.TryAddFreeBuildings();

        city.IsBeingRazed = false;

        // Transfer unique buildings
        foreach (var building in city.CityConstructions.GetBuiltBuildings().ToList())
        {
            var civEquivalentBuilding = newCiv.GetEquivalentBuilding(building);
            if (building != civEquivalentBuilding)
            {
                city.CityConstructions.RemoveBuilding(building);
                city.CityConstructions.AddBuilding(civEquivalentBuilding);
            }
        }

        if (city.Civ.GameInfo.IsReligionEnabled()) city.Religion.RemoveUnknownPantheons();

        if (newCiv.HasUnique(UniqueType.MayNotAnnexCities)) MakePuppet();

        city.TryUpdateRoadStatus();
        city.CityStats.Update();

        // Update proximity rankings
        city.Civ.UpdateProximity(oldCiv, oldCiv.UpdateProximity(city.Civ));

        // Update history
        foreach (var tile in city.GetTiles().ToList())
        {
            tile.History.RecordTakeOwnership(tile);
        }

        city.ResetDisabledConstructions();
        city.ResetSpecialistsControl(city.Civ.GameInfo.GetPlayerSettings(city.Civ.CivId).AutoAssignSpecialistsInNewCities);

        newCiv.Cache.UpdateOurTiles();
        oldCiv.Cache.UpdateOurTiles();
    }
}
