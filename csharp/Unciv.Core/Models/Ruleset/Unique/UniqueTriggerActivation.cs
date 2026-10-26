using System.Globalization;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Civilization.Transients;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapGenerator;
using Unciv.Logic.Map.MapUnits;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unit;
using Unciv.Models.StatTypes;
using Unciv.Models.Translations;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Models.Ruleset.Unique;

// core/src/com/unciv/models/ruleset/unique/UniqueTriggerActivation.kt
// Stripped: the purity annotations. Client calls (sound, music, settings) go through GameClientState (IClientAudio, AddCompletedTutorialTask).
// Buildings, techs, policies, ancient ruins and promotions can have 'triggered' effects
public static class UniqueTriggerActivation
{
    public static bool TriggerUnique(Unique unique, CityObject city, string? notification = null, string? triggerNotificationText = null) =>
        TriggerUnique(unique, city.Civ, city, tile: city.GetCenterTile(),
            notification: notification, triggerNotificationText: triggerNotificationText);

    public static bool TriggerUnique(Unique unique, MapUnit unit, string? notification = null, string? triggerNotificationText = null) =>
        TriggerUnique(unique, unit.Civ, unit: unit, tile: unit.CurrentTile,
            notification: notification, triggerNotificationText: triggerNotificationText);

    /// <returns>whether an action was successfully performed. Assumes that conditional check has already been performed</returns>
    public static bool TriggerUnique(Unique unique, Civilization civInfo, CityObject? city = null, MapUnit? unit = null, TileClass? tile = null,
        string? notification = null, string? triggerNotificationText = null)
    {
        var function = GetTriggerFunction(unique, civInfo, city, unit, tile, notification, triggerNotificationText);
        return function != null && function();
    }

    /// <returns>The action to be performed if possible, else null
    /// This is so the unit actions can be displayed as "disabled" if they won't actually do anything
    /// Even if the action itself is performable, there are still cases where it can fail -
    ///   for example unit placement - which is why the action itself needs to return Boolean to indicate success</returns>
    public static Func<bool>? GetTriggerFunction(Unique unique, Civilization civInfo, CityObject? city = null, MapUnit? unit = null, TileClass? tile = null,
        string? notification = null, string? triggerNotificationText = null)
    {
        // Kotlin default parameter: tile = city?.getCenterTile() ?: unit?.currentTile (C# cannot tell an explicit null from "not given")
        tile ??= city?.GetCenterTile() ?? unit?.CurrentTile;

        var relevantCityLazy = new KotlinLazy<CityObject?>(() => city ?? tile?.GetCity());
        CityObject? RelevantCity() => relevantCityLazy.Value;
        IEnumerable<CityObject> GetApplicableCities(string cityFilter) =>
            cityFilter == "in this city"
                ? new[] { RelevantCity() }.OfType<CityObject>()
                : civInfo.Cities.Where(it => it.MatchesFilter(cityFilter));

        var timingConditional = unique.GetModifiers(UniqueType.ConditionalTimedUnique).FirstOrDefault();
        if (timingConditional != null)
        {
            return () =>
            {
                civInfo.TemporaryUniques.Add(new TemporaryUnique(unique, timingConditional.Params[0].ToInt()));
                if (unique.Type is UniqueType.ProvidesResources or UniqueType.ConsumesResources or UniqueType.StatPercentFromObjectToResource)
                    civInfo.Cache.UpdateCivResources();
                return true;
            };
        }

        var gameContext = new GameContext(civInfo, city, unit, tile);
        var rng = gameContext.StateBasedRandom("UniqueTriggerActivation.getTriggerFunction", JavaHash.HashCode(unique.Text));

        var chosenCity = RelevantCity() ?? civInfo.Cities.FirstOrDefault(it => it.IsCapital());

        var tileBasedRandom = tile != null
            ? civInfo.GameInfo.SeededRandom(tile.Position.GetHashCode())
            : civInfo.GameInfo.SeededRandom(-550); // Very random indeed
        var ruleset = civInfo.GameInfo.Ruleset;

        switch (unique.Type)
        {
            case UniqueType.TriggerEvent:
                {
                    var eventObj = ruleset.Events.GetValueOrDefault(unique.Params[0]);
                    if (eventObj == null) return null;
                    var choices = eventObj.GetMatchingChoices(gameContext);
                    if (choices == null) return null;
                    if (choices.Count == 0) return null;
                    if (civInfo.IsAI() || eventObj.Presentation == Event.PresentationType.None)
                        return () =>
                        {
                            var choice = choices.ToList().RandomWeighted(rng, it => ((IHasUniques)it).GetWeightForAiDecision(gameContext));
                            return choice.TriggerChoice(civInfo, unit);
                        };
                    if (eventObj.Presentation is Event.PresentationType.Alert or Event.PresentationType.Floating)
                        return () =>
                        {
                            // See AlertPopup.addEvent for the deserializing of this string to the context
                            var eventText = eventObj.Name;
                            if (unit != null) eventText += Constants.StringSplitCharacter + "unitId=" + unit.Id;
                            civInfo.AddPopupAlert(new PopupAlert(AlertType.Event, eventText));
                            return true;
                        };
                    // Deviation (T171): Kotlin throws NotImplementedError for Floating (todo: park them in a queue in GameInfo).
                    // Engine: Floating is queued as an Event PopupAlert like Alert; the client decides how to show it (see EventPresentation).
                    // Only Presentation None/Alert/Floating exist, so everything is handled above.
                    return null;
                }

            case UniqueType.MarkTutorialComplete:
                return () =>
                {
                    civInfo.GameInfo.ClientState.AddCompletedTutorialTask(unique.Params[0]);
                    return true;
                };

            case UniqueType.PlaySound:
                {
                    if (civInfo.IsAIOrAutoPlaying()) return null;
                    var soundName = unique.Params[0];
                    if (soundName.Length == 0) return null;
                    var audio = civInfo.GameInfo.ClientState.Audio;
                    if (audio == null || !audio.HasSound(soundName)) return null;
                    return () =>
                    {
                        audio.PlaySound(civInfo, soundName);
                        return true;
                    };
                }
            case UniqueType.ChooseMusic:
                {
                    if (civInfo.IsAIOrAutoPlaying()) return null;
                    var prefix = string.Equals(unique.Params[0], "this civ", StringComparison.OrdinalIgnoreCase) ? civInfo.CivId : unique.Params[0];
                    var suffixes = unique.Params[1].Split(',').Select(it => it.KotlinTrim()).ToList();
                    var flags = unique.Params[1].Split(',')
                        .Select(param => MusicTrackChooserFlagName(param.KotlinTrim()))
                        .OfType<string>()
                        .ToList();
                    var audio = civInfo.GameInfo.ClientState.Audio;
                    if (audio == null) return null;
                    return () =>
                    {
                        audio.ChooseMusic(prefix, suffixes, flags);
                        return true;
                    };
                }

            case UniqueType.GetLeaderTitle:
                if (!civInfo.IsMajorCiv()) return null;
                return () =>
                {
                    civInfo.LeaderTitle = unique.Params[0];
                    if (notification != null)
                        civInfo.AddNotification(notification, NotificationCategory.Diplomacy, NotificationIcon.Diplomacy);
                    return true;
                };

            case UniqueType.OneTimeFreeUnit:
                {
                    var unitName = unique.Params[0];
                    var baseUnit = ruleset.Units.GetValueOrDefault(unitName);
                    if (baseUnit == null) return null;
                    var civUnit = civInfo.GetEquivalentUnit(baseUnit);
                    if (civUnit.IsCityFounder() && civInfo.IsOneCityChallenger())
                        return null;

                    int? limit = MinOrNull(((IHasUniques)civUnit).GetMatchingUniques(UniqueType.MaxNumberBuildable).Select(it => it.Params[0].ToInt()));
                    if (limit != null && limit <= civInfo.Units.GetCivUnits().Count(it => it.Name == civUnit.Name))
                        return null;

                    bool PlaceUnit()
                    {
                        MapUnit? placedUnit;
                        // Set unit at city if there's an explict city or if there's no tile to set at
                        if (RelevantCity() != null || (tile == null && civInfo.Cities.Count > 0))
                            placedUnit = (MapUnit?)civInfo.Units.AddUnit(civUnit, chosenCity);
                        // Else set the unit at the given tile
                        else if (tile != null)
                            placedUnit = civInfo.Units.PlaceUnitNearTile(tile.Position, civUnit);
                        // Else set unit unit near other units if we have no cities
                        else if (civInfo.Units.GetCivUnits().Any())
                            placedUnit = civInfo.Units.PlaceUnitNearTile(civInfo.Units.GetCivUnits().First().CurrentTile.Position, civUnit);
                        else
                            return false;
                        if (placedUnit == null) return false;

                        var notificationText = GetNotificationText(
                            notification, triggerNotificationText,
                            $"Gained [1] [{civUnit.Name}] unit(s)");
                        if (notificationText != null)
                            civInfo.AddNotification(notificationText, MapUnitAction.Of(placedUnit), NotificationCategory.Units, placedUnit.Name);
                        return true;
                    }
                    return PlaceUnit;
                }

            case UniqueType.OneTimeAmountFreeUnits:
                {
                    var unitName = unique.Params[1];
                    var baseUnit = ruleset.Units.GetValueOrDefault(unitName);
                    if (baseUnit == null) return null;
                    var civUnit = civInfo.GetEquivalentUnit(baseUnit);
                    if (civUnit.IsCityFounder() && civInfo.IsOneCityChallenger())
                        return null;

                    int? limit = MinOrNull(((IHasUniques)civUnit).GetMatchingUniques(UniqueType.MaxNumberBuildable).Select(it => it.Params[0].ToInt()));
                    int unitCount = civInfo.Units.GetCivUnits().Count(it => it.Name == civUnit.Name);
                    int amountFromTriggerable = unique.Params[0].ToInt();
                    int actualAmount =
                        limit == null ? amountFromTriggerable
                        : unchecked(amountFromTriggerable + unitCount) > limit ? limit.Value - unitCount
                        : amountFromTriggerable;

                    if (actualAmount <= 0) return null;

                    bool PlaceUnits()
                    {
                        var placedUnits = new List<MapUnit>();
                        for (int i = 0; i < actualAmount; i++)
                        {
                            MapUnit? placedUnit;
                            if (RelevantCity() != null || (tile == null && civInfo.Cities.Count > 0))
                                placedUnit = (MapUnit?)civInfo.Units.AddUnit(civUnit, chosenCity);
                            else if (tile != null)
                                placedUnit = civInfo.Units.PlaceUnitNearTile(tile.Position, civUnit);
                            else if (civInfo.Units.GetCivUnits().Any())
                                placedUnit = civInfo.Units.PlaceUnitNearTile(civInfo.Units.GetCivUnits().First().CurrentTile.Position, civUnit);
                            else
                                placedUnit = null;
                            if (placedUnit != null) placedUnits.Add(placedUnit);
                        }
                        if (placedUnits.Count == 0) return false;

                        var notificationText = GetNotificationText(
                            notification, triggerNotificationText,
                            $"Gained [{placedUnits.Count}] [{civUnit.Name}] unit(s)");

                        if (notificationText != null)
                            civInfo.AddNotification(notificationText, MapUnitAction.Of(placedUnits), NotificationCategory.Units, civUnit.Name);
                        return true;
                    }
                    return PlaceUnits;
                }

            case UniqueType.OneTimeRebel:
                {
                    var barbarians = civInfo.GameInfo.GetCivilizationOrNull(Constants.Barbarians);
                    if (barbarians == null) return null; // Barbs can be deselected as new-game option
                    var unitName = unique.Params[0];
                    var baseUnit = ruleset.Units.GetValueOrDefault(unitName);
                    if (baseUnit == null) return null;
                    var civUnit = civInfo.GetEquivalentUnit(baseUnit);
                    if (civUnit.IsCityFounder() && civInfo.IsOneCityChallenger())
                        return null;

                    // Choose a city as the placement anchor: use the relevant city if available, otherwise the first city
                    var placementCity = RelevantCity() ?? civInfo.Cities.FirstOrDefault();
                    if (placementCity == null) return null;

                    bool PlaceUnit()
                    {
                        var placedUnit = (MapUnit?)civInfo.GameInfo.TileMap.PlaceUnitNearTile(
                            placementCity.GetCenterTile().Position, baseUnit, barbarians);
                        if (placedUnit == null) return false;

                        var notificationText = GetNotificationText(
                            notification, triggerNotificationText,
                            $"[1] [{civUnit.Name}] has rebelled against us!");
                        if (notificationText != null)
                            civInfo.AddNotification(notificationText, MapUnitAction.Of(placedUnit), NotificationCategory.Units, placedUnit.Name);
                        return true;
                    }
                    return PlaceUnit;
                }
            case UniqueType.OneTimeAmountRebels:
                {
                    var barbarians = civInfo.GameInfo.GetCivilizationOrNull(Constants.Barbarians);
                    if (barbarians == null) return null; // Barbs can be deselected as new-game option
                    var unitName = unique.Params[1];
                    var baseUnit = ruleset.Units.GetValueOrDefault(unitName);
                    if (baseUnit == null) return null;
                    var civUnit = civInfo.GetEquivalentUnit(baseUnit);
                    if (civUnit.IsCityFounder() && civInfo.IsOneCityChallenger())
                        return null;

                    // Existing limit logic (based on the number of similar units the civ already owns)
                    int? limit = MinOrNull(((IHasUniques)civUnit).GetMatchingUniques(UniqueType.MaxNumberBuildable).Select(it => it.Params[0].ToInt()));
                    int unitCount = civInfo.Units.GetCivUnits().Count(it => it.Name == civUnit.Name);
                    int amountFromTriggerable = unique.Params[0].ToInt();
                    int actualAmount =
                        limit == null ? amountFromTriggerable
                        : unchecked(amountFromTriggerable + unitCount) > limit ? limit.Value - unitCount
                        : amountFromTriggerable;

                    if (actualAmount <= 0) return null;

                    // Choose a city as the placement anchor
                    var placementCity = RelevantCity() ?? civInfo.Cities.FirstOrDefault();
                    if (placementCity == null) return null;

                    bool PlaceUnits()
                    {
                        var placedUnits = new List<MapUnit>();
                        for (int i = 0; i < actualAmount; i++)
                        {
                            var placedUnit = (MapUnit?)civInfo.GameInfo.TileMap.PlaceUnitNearTile(
                                placementCity.GetCenterTile().Position, baseUnit, barbarians);
                            if (placedUnit != null) placedUnits.Add(placedUnit);
                        }
                        if (placedUnits.Count == 0) return false;

                        var notificationText = GetNotificationText(
                            notification, triggerNotificationText,
                            $"[{placedUnits.Count}] [{civUnit.Name}] unit(s) have rebelled against us!");

                        if (notificationText != null)
                            civInfo.AddNotification(notificationText, MapUnitAction.Of(placedUnits), NotificationCategory.Units, civUnit.Name);
                        return true;
                    }
                    return PlaceUnits;
                }

            case UniqueType.OneTimeFreeUnitRuins:
                {
                    var unitName = unique.Params[0];
                    var baseUnit = ruleset.Units.GetValueOrDefault(unitName);
                    if (baseUnit == null) return null;
                    var civUnit = civInfo.GetEquivalentUnit(baseUnit);
                    if (civUnit.IsCityFounder() && civInfo.IsOneCityChallenger())
                    {
                        var replacementUnit = ruleset.Units.Values
                            .FirstOrDefault(it => ((IHasUniques)it).GetMatchingUniques(UniqueType.BuildImprovements, GameContext.IgnoreConditionalsState)
                                .Any(u => u.Params[0] == "Land"));
                        if (replacementUnit == null) return null;
                        civUnit = civInfo.GetEquivalentUnit(replacementUnit.Name);
                    }

                    bool PlaceUnit()
                    {
                        var placeRng = (unit?.Cache.State ?? civInfo.State).StateBasedRandom("UniqueTriggerActivation.getTriggerFunction");
                        var placingTile = tile ?? civInfo.Cities.Random(placeRng).GetCenterTile();
                        var placedUnit = civInfo.Units.PlaceUnitNearTile(placingTile.Position, civUnit.Name);
                        if (notification != null && placedUnit != null)
                        {
                            var notificationText = notification.HasPlaceholderParameters()
                                ? notification.FillPlaceholders(unique.Params[0])
                                : notification;
                            civInfo.AddNotification(
                                notificationText,
                                new NotificationAction[] { MapUnitAction.Of(placedUnit) }.Concat(LocationAction.Of(tile?.Position)),
                                NotificationCategory.Units,
                                placedUnit.Name);
                        }
                        return placedUnit != null;
                    }

                    return PlaceUnit;
                }

            case UniqueType.OneTimeFreePolicy:
                // spectators get all techs at start of game, and if (in a mod) a tech gives a free policy, the game gets stuck on the policy picker screen
                if (civInfo.IsSpectator()) return null;

                return () =>
                {
                    civInfo.Policies.FreePolicies++;

                    var notificationText = GetNotificationText(notification, triggerNotificationText,
                        "You may choose a free Policy");
                    if (notificationText != null)
                        civInfo.AddNotification(notificationText, new PolicyAction(), NotificationCategory.General, NotificationIcon.Culture);
                    return true;
                };
            case UniqueType.OneTimeAmountFreePolicies:
                {
                    if (civInfo.IsSpectator()) return null;
                    int newFreePolicies = unique.Params[0].ToInt();

                    return () =>
                    {
                        civInfo.Policies.FreePolicies += newFreePolicies;

                        var notificationText = GetNotificationText(
                            notification, triggerNotificationText,
                            $"You may choose [{newFreePolicies}] free Policies");
                        if (notificationText != null)
                            civInfo.AddNotification(notificationText, new PolicyAction(), NotificationCategory.General, NotificationIcon.Culture);
                        return true;
                    };
                }
            case UniqueType.OneTimeAdoptPolicyOrBelief:
                {
                    var name = unique.Params[0];
                    var policy = civInfo.GameInfo.Ruleset.Policies.GetValueOrDefault(name);
                    var belief = civInfo.GameInfo.Ruleset.Beliefs.GetValueOrDefault(name);
                    if (policy != null && !civInfo.Policies.IsAdopted(name))
                        return () =>
                        {
                            civInfo.Policies.FreePolicies++;
                            civInfo.Policies.Adopt(policy);
                            var text = GetNotificationText(notification, triggerNotificationText, $"You gain the [{name}] Policy");
                            if (text != null)
                                civInfo.AddNotification(text, new PolicyAction(name), NotificationCategory.General, NotificationIcon.Culture);
                            return true;
                        };
                    if (belief != null && civInfo.ReligionManager.Religion?.HasBelief(name) == false)
                        return () =>
                        {
                            civInfo.ReligionManager.ChooseBeliefs([belief]);
                            var text = GetNotificationText(notification, triggerNotificationText, $"You gain the [{name}] Belief");
                            if (text != null)
                                civInfo.AddNotification(text, NotificationCategory.Religion, NotificationIcon.Faith);
                            return true;
                        };
                    return null;
                }

            case UniqueType.OneTimeRemovePolicy:
                {
                    var policyFilter = unique.Params[0];
                    var policiesToRemove = civInfo.Policies
                        .GetAdoptedPoliciesMatching(policyFilter, gameContext, forRemoval: true);
                    if (!policiesToRemove.Any()) return null;

                    return () =>
                    {
                        foreach (var policy in policiesToRemove)
                        {
                            civInfo.Policies.RemovePolicy(policy);

                            var notificationText = GetNotificationText(
                                notification, triggerNotificationText,
                                $"You lose the [{policy.Name}] Policy");
                            if (notificationText != null)
                                civInfo.AddNotification(notificationText, new PolicyAction(policy.Name), NotificationCategory.General, NotificationIcon.Culture);
                        }
                        return true;
                    };
                }
            case UniqueType.OneTimeRemovePolicyRefund:
                {
                    var policyFilter = unique.Params[0];
                    var policiesToRemove = civInfo.Policies
                        .GetAdoptedPoliciesMatching(policyFilter, gameContext, forRemoval: true);
                    if (!policiesToRemove.Any()) return null;

                    int refundPercentage = unique.Params[1].ToInt();
                    var policiesToRemoveMap = civInfo.Policies.GetCultureRefundMap(policiesToRemove, refundPercentage);

                    return () =>
                    {
                        foreach (var policy in policiesToRemoveMap)
                        {
                            string effectNotificationText;
                            if (policy.Value == PolicyManager.FreePolicyMarker)
                            {
                                civInfo.Policies.RemovePolicy(policy.Key, assumeWasFree: true);
                                civInfo.Policies.FreePolicies++;
                                effectNotificationText = $"You lose the [{policy.Key.Name}] Policy. A free policy has been refunded";
                            }
                            else
                            {
                                civInfo.Policies.RemovePolicy(policy.Key);
                                civInfo.Policies.AddCulture(policy.Value);
                                effectNotificationText = $"You lose the [{policy.Key.Name}] Policy. [{policy.Value}] Culture has been refunded";
                            }
                            var notificationText =
                                GetNotificationText(notification, triggerNotificationText, effectNotificationText);
                            if (notificationText != null)
                                civInfo.AddNotification(notificationText, new PolicyAction(policy.Key.Name), NotificationCategory.General, NotificationIcon.Culture);
                        }
                        return true;
                    };
                }

            case UniqueType.OneTimeEnterGoldenAge:
            case UniqueType.OneTimeEnterGoldenAgeTurns:
                return () =>
                {
                    if (unique.Type == UniqueType.OneTimeEnterGoldenAgeTurns) civInfo.GoldenAges.EnterGoldenAge(unique.Params[0].ToInt());
                    else civInfo.GoldenAges.EnterGoldenAge();

                    var notificationText = GetNotificationText(
                        notification, triggerNotificationText,
                        "You enter a Golden Age");
                    if (notificationText != null)
                        civInfo.AddNotification(notificationText, NotificationCategory.General, NotificationIcon.Happiness);
                    return true;
                };

            case UniqueType.OneTimeFreeGreatPerson:
                if (civInfo.IsSpectator()) return null;
                return () =>
                {
                    civInfo.GreatPeople.FreeGreatPeople++;
                    // Anyone an idea for a good icon?
                    if (notification != null)
                        civInfo.AddNotification(notification, NotificationCategory.General);

                    // NextTurnAutomation.chooseGreatPerson is reached through GameClientState.NextTurnAutomation
                    if (civInfo.IsAI() || civInfo.GameInfo.ClientState.IsFullAutoPlayAI)
                        civInfo.GameInfo.ClientState.NextTurnAutomation?.ChooseGreatPerson(civInfo);
                    return true;
                };

            case UniqueType.OneTimeGainPopulation:
                {
                    var applicableCities = GetApplicableCities(unique.Params[1]);
                    if (!applicableCities.Any()) return null;
                    return () =>
                    {
                        foreach (var applicableCity in applicableCities)
                            applicableCity.Population.AddPopulation(unique.Params[0].ToInt());
                        if (notification != null)
                            civInfo.AddNotification(
                                notification,
                                LocationAction.Of(applicableCities.Select(it => it.Location)),
                                NotificationCategory.Cities,
                                NotificationIcon.Population);
                        return true;
                    };
                }
            case UniqueType.OneTimeGainPopulationRandomCity:
                if (civInfo.Cities.Count == 0) return null;
                return () =>
                {
                    var randomCity = civInfo.Cities.Random(tileBasedRandom);
                    randomCity.Population.AddPopulation(unique.Params[0].ToInt());
                    if (notification != null)
                    {
                        var notificationText = notification.HasPlaceholderParameters()
                            ? notification.FillPlaceholders(randomCity.Name)
                            : notification;
                        civInfo.AddNotification(
                            notificationText,
                            LocationAction.Of(randomCity.Location, tile?.Position),
                            NotificationCategory.Cities,
                            NotificationIcon.Population);
                    }
                    return true;
                };

            case UniqueType.OneTimeFreeTech:
                if (civInfo.IsSpectator()) return null;
                return () =>
                {
                    civInfo.Tech.FreeTechs += 1;
                    if (notification != null)
                        civInfo.AddNotification(notification, NotificationCategory.General, NotificationIcon.Science);
                    return true;
                };
            case UniqueType.OneTimeAmountFreeTechs:
                if (civInfo.IsSpectator()) return null;
                return () =>
                {
                    civInfo.Tech.FreeTechs += unique.Params[0].ToInt();
                    if (notification != null)
                        civInfo.AddNotification(notification, NotificationCategory.General, NotificationIcon.Science);
                    return true;
                };
            case UniqueType.OneTimeFreeTechRuins:
                {
                    var researchableTechsFromThatEra = ruleset.Technologies.Values
                        .Where(it => ((IHasUniques)it).Era(ruleset)?.MatchesFilter(unique.Params[1], gameContext) == true
                                     && civInfo.Tech.CanBeResearched(it.Name))
                        .ToList();
                    if (researchableTechsFromThatEra.Count == 0) return null;

                    return () =>
                    {
                        var techsToResearch = researchableTechsFromThatEra.Shuffled(tileBasedRandom)
                            .Take(unique.Params[0].ToInt()).ToList();
                        foreach (var tech in techsToResearch)
                            civInfo.Tech.AddTechnology(tech.Name);

                        if (notification != null)
                        {
                            var notificationText = notification.HasPlaceholderParameters()
                                ? notification.FillPlaceholders(techsToResearch.Select(it => it.Name).ToArray())
                                : notification;
                            // Notification click for first tech only, supporting multiple adds little value.
                            // Relies on RulesetValidator catching <= 0!
                            var notificationActions = LocationAction.Of(tile?.Position).Cast<NotificationAction>()
                                .Append(new TechAction(techsToResearch.First().Name));
                            civInfo.AddNotification(
                                notificationText, notificationActions,
                                NotificationCategory.General, NotificationIcon.Science);
                        }
                        return true;
                    };
                }
            case UniqueType.OneTimeDiscoverTech:
                {
                    var techName = unique.Params[0];
                    if (civInfo.Tech.IsResearched(techName)) return null;

                    return () =>
                    {
                        civInfo.Tech.AddTechnology(techName);
                        var notificationText = GetNotificationText(
                            notification, triggerNotificationText,
                            $"You have discovered the secrets of [{techName}]");
                        if (notificationText != null)
                            civInfo.AddNotification(notificationText, new TechAction(techName), NotificationCategory.General, NotificationIcon.Science);
                        return true;
                    };
                }

            case UniqueType.OneTimeProvideResources:
                {
                    var resourceName = unique.Params[1];
                    var resource = ruleset.TileResources.GetValueOrDefault(resourceName);
                    if (resource == null) return null;
                    if (!resource.IsStockpiled) return null;

                    return () =>
                    {
                        int amount = unique.Params[0].ToInt();
                        if (city != null) city.GainStockpiledResource(resource, amount);
                        else civInfo.GainStockpiledResource(resource, amount);

                        var notificationText = GetNotificationText(
                            notification, triggerNotificationText,
                            $"You have gained [{amount}] [{resourceName}]");
                        if (notificationText != null)
                            civInfo.AddNotification(notificationText, NotificationCategory.General, resourceName);
                        return true;
                    };
                }

            case UniqueType.OneTimeConsumeResources:
                {
                    var resourceName = unique.Params[1];
                    var resource = ruleset.TileResources.GetValueOrDefault(resourceName);
                    if (resource == null) return null;
                    if (!resource.IsStockpiled) return null;

                    return () =>
                    {
                        int amount = unique.Params[0].ToInt();
                        if (city != null) city.GainStockpiledResource(resource, -amount);
                        else civInfo.GainStockpiledResource(resource, -amount);

                        var notificationText = GetNotificationText(
                            notification, triggerNotificationText,
                            $"You have lost [{amount}] [{resourceName}]");
                        if (notificationText != null)
                            civInfo.AddNotification(notificationText, NotificationCategory.General, resourceName);
                        return true;
                    };
                }

            case UniqueType.OneTimeGainResource:
                {
                    var resourceName = unique.Params[1];
                    var resource = ruleset.GetGameResource(resourceName);
                    if (resource == null) return null;
                    if (resource is TileResource { IsStockpiled: false }) return null;

                    return () =>
                    {
                        int amount = unique.Params[0].ToInt();
                        if (unique.IsModifiedByGameSpeed())
                        {
                            amount = resource is Stat stat
                                ? KotlinMath.RoundToInt(amount * civInfo.GameInfo.Speed.StatCostModifiers[stat])
                                : KotlinMath.RoundToInt(amount * civInfo.GameInfo.Speed.Modifier);
                        }
                        if (city != null) city.AddGameResource(resource, amount);
                        else civInfo.AddGameResource(resource, amount);

                        var notificationText = GetNotificationText(
                            notification, triggerNotificationText,
                            $"You have gained [{amount}] [{resourceName}]");
                        if (notificationText != null)
                            civInfo.AddNotification(notificationText, NotificationCategory.General, resourceName);
                        return true;
                    };
                }

            case UniqueType.OneTimeSetStockpile:
                {
                    var resourceName = unique.Params[0];
                    var resource = ruleset.GetGameResource(resourceName);
                    if (resource == null) return null;
                    if (resource is TileResource { IsStockpiled: false }) return null;

                    var countableContext = new GameContext(civInfo, city);
                    int? countableResult = Countables.GetCountableAmount(unique.Params[1], countableContext);
                    if (countableResult == null) return null;

                    return () =>
                    {
                        int amountRequired = countableResult.Value;
                        if (unique.IsModifiedByGameSpeed())
                        {
                            amountRequired = resource is Stat stat
                                ? KotlinMath.RoundToInt(amountRequired * civInfo.GameInfo.Speed.StatCostModifiers[stat])
                                : KotlinMath.RoundToInt(amountRequired * civInfo.GameInfo.Speed.Modifier);
                        }
                        if (city != null)
                        {
                            int currentAmount = city.GetGameResource(resource);
                            int missingAmount = amountRequired - currentAmount;
                            city.AddGameResource(resource, missingAmount);
                        }
                        else
                        {
                            int currentAmount = civInfo.GetGameResource(resource);
                            int missingAmount = amountRequired - currentAmount;
                            civInfo.AddGameResource(resource, missingAmount);
                        }

                        var notificationText = GetNotificationText(
                            notification, triggerNotificationText,
                            $"[{resourceName}] has been set to [{amountRequired}]");
                        if (notificationText != null)
                            civInfo.AddNotification(notificationText, NotificationCategory.General, resourceName);
                        return true;
                    };
                }
            case UniqueType.UnitsGainPromotion:
                {
                    var filter = unique.Params[0];
                    var promotionName = unique.Params[1];
                    var promotion = ruleset.UnitPromotions.GetValueOrDefault(promotionName);
                    if (promotion == null) return null;

                    var unitsToPromote = civInfo.Units.GetCivUnits()
                        .Where(it => it.MatchesFilter(filter) && (promotion.UnitTypes.Contains(it.Type.Name) || promotion.UnitTypes.Count == 0))
                        .ToList();
                    if (unitsToPromote.Count == 0) return null;

                    return () =>
                    {
                        var promotedUnits = new List<MapUnit>();
                        foreach (var civUnit in unitsToPromote)
                        {
                            if (civUnit.Promotions.Promotions.Contains(promotionName)) continue;
                            civUnit.Promotions.AddPromotion(promotionName, isFree: true);
                            promotedUnits.Add(civUnit);
                        }

                        if (notification != null)
                        {
                            civInfo.AddNotification(
                                notification,
                                MapUnitAction.Of(promotedUnits),
                                NotificationCategory.Units,
                                promotionName);
                        }
                        return true;
                    };
                }

            // The mechanics for granting great people are wonky, but basically the following happens:
            // Based on the game speed, a timer with some amount of turns is set, 40 on regular speed
            // Every turn, 1 is subtracted from this timer, as long as you have at least 1 city state ally
            // So no, the number of city-state allies does not matter for this. You have a global timer for all of them combined.
            // If the timer reaches the amount of city-state allies you have (or 10, whichever is lower), it is reset.
            // You will then receive a random great person from a random city-state you are allied to
            // The very first time after acquiring this policy, the timer is set to half of its normal value
            // Note that the way this is implemented now, this unique does NOT stack
            case UniqueType.CityStateCanGiftGreatPeople:
                return () =>
                {
                    civInfo.AddFlag(
                        CivFlags.CityStateGreatPersonGift.ToString(),
                        civInfo.CityStateFunctions.TurnsForGreatPersonFromCityState() / 2);
                    if (notification != null)
                        civInfo.AddNotification(notification, NotificationCategory.Diplomacy, NotificationIcon.CityState);
                    return true;
                };

            case UniqueType.OneTimeGainStat:
                {
                    var stat = StatExtensions.SafeValueOf(unique.Params[1]);
                    if (stat == null) return null;

                    if (!StatExtensions.StatsWithCivWideField.Contains(stat.Value)
                        || unique.Params[0].ToIntOrNull() == null)
                        return null;
                    var statValue = stat.Value;

                    return () =>
                    {
                        int statAmount = unique.Params[0].ToInt();
                        if (unique.IsModifiedByGameSpeed()) statAmount = KotlinMath.RoundToInt(statAmount * civInfo.GameInfo.Speed.StatCostModifiers[statValue]);

                        var stats = new Stats().Add(statValue, statAmount);
                        civInfo.AddStats(stats);

                        var filledNotification = notification != null && notification.HasPlaceholderParameters()
                            ? notification.FillPlaceholders(statAmount.Tr())
                            : notification;

                        var notificationText = GetNotificationText(
                            filledNotification, triggerNotificationText,
                            $"Gained [{stats.ToStringForNotifications()}]");
                        if (notificationText != null)
                            civInfo.AddNotification(notificationText, LocationAction.Of(tile?.Position), NotificationCategory.General, NotificationIconOf(statValue));
                        return true;
                    };
                }

            case UniqueType.OneTimeGainStatRange:
                {
                    var stat = StatExtensions.SafeValueOf(unique.Params[2]);
                    if (stat == null) return null;

                    if (!StatExtensions.StatsWithCivWideField.Contains(stat.Value)
                        || unique.Params[0].ToIntOrNull() == null
                        || unique.Params[1].ToIntOrNull() == null)
                        return null;
                    var statValue = stat.Value;

                    int firstValue = unique.Params[0].ToInt();
                    int secondValue = unique.Params[1].ToInt();
                    int randomValue = new IntRange(Math.Min(firstValue, secondValue), Math.Max(firstValue, secondValue)).Random(tileBasedRandom);
                    int finalStatAmount = unique.IsModifiedByGameSpeed()
                        ? KotlinMath.RoundToInt(randomValue * civInfo.GameInfo.Speed.StatCostModifiers[statValue])
                        : randomValue;

                    return () =>
                    {
                        var stats = new Stats().Add(statValue, finalStatAmount);
                        civInfo.AddStats(stats);

                        var filledNotification = notification != null && notification.HasPlaceholderParameters()
                            ? notification.FillPlaceholders(finalStatAmount.Tr())
                            : notification;

                        var notificationText = GetNotificationText(
                            filledNotification, triggerNotificationText,
                            $"Gained [{stats.ToStringForNotifications()}]");

                        if (notificationText != null)
                            civInfo.AddNotification(notificationText, LocationAction.Of(tile?.Position), NotificationCategory.General, NotificationIconOf(statValue));
                        return true;
                    };
                }
            case UniqueType.OneTimeGainPantheon:
                {
                    if (civInfo.ReligionManager.ReligionState != ReligionState.None) return null;
                    int gainedFaith = civInfo.ReligionManager.FaithForPantheon(2);
                    if (gainedFaith == 0) return null;

                    return () =>
                    {
                        civInfo.AddStat(Stat.Faith, gainedFaith);

                        if (notification != null)
                        {
                            var notificationText = notification.HasPlaceholderParameters()
                                ? notification.FillPlaceholders(gainedFaith.Tr())
                                : notification;
                            civInfo.AddNotification(notificationText, LocationAction.Of(tile?.Position), NotificationCategory.Religion, NotificationIcon.Faith);
                        }
                        return true;
                    };
                }
            case UniqueType.OneTimeGainProphet:
                {
                    if (civInfo.ReligionManager.GetGreatProphetEquivalent() == null) return null;
                    int gainedFaith =
                        (int)(civInfo.ReligionManager.FaithForNextGreatProphet() * (unique.Params[0].ToFloat() / 100f));
                    if (gainedFaith == 0) return null;

                    return () =>
                    {
                        civInfo.AddStat(Stat.Faith, gainedFaith);

                        if (notification != null)
                        {
                            var notificationText = notification.HasPlaceholderParameters()
                                ? notification.FillPlaceholders(gainedFaith.Tr())
                                : notification;
                            civInfo.AddNotification(notificationText, LocationAction.Of(tile?.Position), NotificationCategory.Religion, NotificationIcon.Faith);
                        }
                        return true;
                    };
                }

            case UniqueType.OneTimeGainTechPercent:
                {
                    var tech = unique.Params[1];
                    float? amount = unique.Params[0].ToFloatOrNull();
                    if (amount == null || !civInfo.GameInfo.Ruleset.Technologies.ContainsKey(tech) || civInfo.Tech.IsResearched(tech)) return null;
                    int scienceGain = KotlinMath.RoundToInt(civInfo.Tech.CostOfTech(tech) * amount.Value / 100);
                    if (scienceGain == 0) return null;

                    return () =>
                    {
                        if (!civInfo.Tech.TechsInProgress.TryGetValue(tech, out int inProgress))
                            civInfo.Tech.TechsInProgress[tech] = scienceGain;
                        else civInfo.Tech.TechsInProgress[tech] = unchecked(inProgress + scienceGain);

                        var filledNotification = notification != null && notification.HasPlaceholderParameters()
                            ? notification.FillPlaceholders(scienceGain.Tr())
                            : notification;

                        var notificationText = GetNotificationText(
                            filledNotification, triggerNotificationText,
                            $"You have gained [{scienceGain}] Science towards researching [{tech}]");

                        civInfo.AddNotification(notificationText.NotNull(), LocationAction.Of(tile?.Position), NotificationCategory.General, NotificationIcon.Science);

                        return true;
                    };
                }

            case UniqueType.OneTimeFreeBelief:
                {
                    if (!civInfo.IsMajorCiv()) return null;
                    var beliefType = Enum.Parse<BeliefType>(unique.Params[0]);
                    var religionManager = civInfo.ReligionManager;
                    if (beliefType != BeliefType.Pantheon && beliefType != BeliefType.Any
                        && religionManager.ReligionState <= ReligionState.Pantheon)
                        return null; // situation where we're trying to add a formal religion belief to a civ that hasn't founded a religion
                    if (religionManager.NumberOfBeliefsAvailable(beliefType) == 0)
                        return null; // no more available beliefs of this type

                    return () =>
                    {
                        var religionIcon = beliefType.ToString();
                        if (beliefType == BeliefType.Any && religionManager.ReligionState <= ReligionState.Pantheon)
                        {
                            religionManager.FreeBeliefs.Add(BeliefType.Pantheon.ToString(), 1); // add pantheon instead of any type
                            religionIcon = BeliefType.Pantheon.ToString();
                        }
                        else
                            religionManager.FreeBeliefs.Add(beliefType.ToString(), 1);

                        if (notification != null)
                        {
                            civInfo.AddNotification(
                                notification,
                                NotificationCategory.Religion,
                                NotificationIcon.Faith,
                                $"ReligionIcons/{religionIcon}");
                        }
                        return true;
                    };
                }

            case UniqueType.OneTimeRevealEntireMap:
                return () =>
                {
                    if (notification != null)
                        civInfo.AddNotification(notification, LocationAction.Of(tile?.Position), NotificationCategory.General, NotificationIcon.Scout);
                    var tiles = civInfo.GameInfo.TileMap.Values;
                    foreach (var t in tiles) t.SetExplored(civInfo, true);
                    if (!civInfo.IsBarbarian)
                        civInfo.Cache.DiscoverNaturalWonders(tiles);
                    return true;
                };
            case UniqueType.OneTimeRevealSpecificMapTiles:
                {
                    if (tile == null) return null;

                    // "Reveal up to [amount/'all'] [tileFilter] within a [amount] tile radius"
                    var amount = unique.Params[0];
                    var filter = unique.Params[1];
                    int radius = unique.Params[2].ToInt();

                    bool isAll = Constants.All.Contains(amount);
                    var positions = new List<HexCoord>();

                    IEnumerable<TileClass> explorableTiles = tile.GetTilesInDistance(radius)
                        .Where(it => !it.IsExplored(civInfo) && it.MatchesFilter(filter));

                    if (!explorableTiles.Any())
                        return null;

                    if (!isAll)
                    {
                        // Kotlin Sequence.shuffled(...).take(n) is lazy: the draw and the explored filter run when the function executes
                        var candidates = explorableTiles;
                        int takeCount = amount.ToInt();
                        explorableTiles = DeferredShuffleTake(candidates, tileBasedRandom, takeCount);
                    }

                    return () =>
                    {
                        foreach (var explorableTile in explorableTiles)
                        {
                            explorableTile.SetExplored(civInfo, true);
                            civInfo.SetLastSeenImprovement(explorableTile.Position, explorableTile.Improvement);
                            positions.Add(explorableTile.Position);
                        }

                        if (notification != null)
                        {
                            civInfo.AddNotification(
                                notification,
                                LocationAction.Of(positions),
                                NotificationCategory.War,
                                unique.Params[1] == Constants.BarbarianEncampment
                                    ? NotificationIcon.Barbarians : NotificationIcon.Scout);
                        }
                        return true;
                    };
                }
            case UniqueType.OneTimeRevealCrudeMap:
                {
                    if (tile == null) return null;

                    // "From a randomly chosen tile [amount] tiles away from the ruins,
                    // reveal tiles up to [amount] tiles away with [amount]% chance"
                    int distance = unique.Params[0].ToInt();
                    int radius = unique.Params[1].ToInt();
                    float chance = unique.Params[2].ToFloat() / 100f;

                    var revealCenter = tile.GetTilesAtDistance(distance)
                        .Where(it => !it.IsExplored(civInfo))
                        .ToList()
                        .RandomOrNull(tileBasedRandom);
                    if (revealCenter == null) return null;

                    return () =>
                    {
                        revealCenter.ForEachTileInDistance(radius, _ => tileBasedRandom.NextFloat() < chance, it => it.SetExplored(civInfo, true));
                        civInfo.Cache.UpdateViewableTiles();
                        if (notification != null)
                            civInfo.AddNotification(
                                notification,
                                tile.Position,
                                NotificationCategory.General,
                                NotificationIcon.Ruins);
                        return true;
                    };
                }

            case UniqueType.OneTimeTriggerVoting:
                return () =>
                {
                    foreach (var civ in civInfo.GameInfo.Civilizations)
                        if (!civ.IsBarbarian && !civ.IsSpectator())
                            civ.AddFlag(
                                CivFlags.TurnsTillNextDiplomaticVote.ToString(),
                                civInfo.GetTurnsBetweenDiplomaticVotes());
                    if (notification != null)
                        civInfo.AddNotification(notification, NotificationCategory.General, NotificationIcon.Diplomacy);
                    return true;
                };

            case UniqueType.OneTimeGlobalSpiesWhenEnteringEra:
                if (!civInfo.IsMajorCiv()) return null;
                if (!civInfo.GameInfo.IsEspionageEnabled()) return null;

                return () =>
                {
                    var currentEra = civInfo.GetEra().Name;
                    foreach (var otherCiv in civInfo.GameInfo.GetAliveMajorCivs())
                    {
                        if (!otherCiv.EspionageManager.ErasSpyEarnedFor.Contains(currentEra))
                        {
                            var spy = otherCiv.EspionageManager.AddSpy();
                            otherCiv.EspionageManager.ErasSpyEarnedFor.Add(currentEra);
                            if (otherCiv == civInfo || otherCiv.Knows(civInfo))
                                // We don't tell which civilization entered the new era, as that is done in the notification directly above this one
                                spy.AddNotification($"We have recruited [{spy.Name}] as a spy!");
                            else
                                spy.AddNotification($"After [an unknown civilization] entered the [{currentEra}], we have recruited [{spy.Name}] as a spy!");
                        }
                    }
                    return true;
                };

            case UniqueType.OneTimeSpiesLevelUp:
                if (!civInfo.IsMajorCiv()) return null;
                if (!civInfo.GameInfo.IsEspionageEnabled()) return null;

                return () =>
                {
                    foreach (var spy in civInfo.EspionageManager.SpyList.ToList())
                        spy.LevelUpSpy(unique.Params[0].ToInt());
                    return true;
                };

            case UniqueType.OneTimeGainSpy:
                if (!civInfo.IsMajorCiv()) return null;
                if (!civInfo.GameInfo.IsEspionageEnabled()) return null;

                return () =>
                {
                    var spy = civInfo.EspionageManager.AddSpy();
                    spy.AddNotification($"We have recruited [{spy.Name}] as a spy!");
                    return true;
                };

            case UniqueType.OneTimeTakeOverTilesInCity:
                {
                    var applicableCities = GetApplicableCities(unique.Params[1]);
                    if (!applicableCities.Any()) return null;
                    if (!applicableCities.Any(it => it.Expansion.ChooseNewTileToOwn() != null)) return null;

                    return () =>
                    {
                        int positiveAmount = unique.Params[0].ToInt();
                        foreach (var applicableCity in applicableCities)
                        {
                            for (int i = 1; i <= positiveAmount; i++)
                            {
                                var tileToOwn = applicableCity.Expansion.ChooseNewTileToOwn();
                                if (tileToOwn == null) break;
                                applicableCity.Expansion.TakeOwnership(tileToOwn);
                            }
                        }
                        if (notification != null)
                            civInfo.AddNotification(notification, LocationAction.Of(applicableCities.Select(it => it.Location)), NotificationCategory.Cities, NotificationIcon.City);
                        return true;
                    };
                }

            case UniqueType.GainFreeBuildings:
                {
                    var freeBuilding = civInfo.GetEquivalentBuilding(unique.Params[0]);
                    var applicableCities = GetApplicableCities(unique.Params[1]);
                    if (!applicableCities.Any()) return null;

                    return () =>
                    {
                        foreach (var applicableCity in applicableCities)
                        {
                            applicableCity.CityConstructions.FreeBuildingsProvidedFromThisCity.AddToMapOfSets(applicableCity.Id, freeBuilding.Name);

                            if (applicableCity.CityConstructions.ContainsBuildingOrEquivalent(freeBuilding.Name)) continue;
                            applicableCity.CityConstructions.CompleteConstruction(freeBuilding);
                        }
                        return true;
                    };
                }
            case UniqueType.FreeStatBuildings:
                {
                    var stat = StatExtensions.SafeValueOf(unique.Params[0]);
                    if (stat == null) return null;
                    return () =>
                    {
                        civInfo.CivConstructions.AddFreeStatBuildings(stat.Value, unique.Params[1].ToInt());
                        return true;
                    };
                }
            case UniqueType.FreeSpecificBuildings:
                {
                    var building = ruleset.Buildings.GetValueOrDefault(unique.Params[0]);
                    if (building == null) return null;
                    return () =>
                    {
                        civInfo.CivConstructions.AddFreeBuildings(building, unique.Params[1].ToInt());
                        return true;
                    };
                }

            case UniqueType.RemoveBuilding:
                {
                    var applicableCities = GetApplicableCities(unique.Params[1]);
                    if (!applicableCities.Any()) return null;

                    return () =>
                    {
                        foreach (var applicableCity in applicableCities)
                        {
                            var buildingsToRemove = applicableCity.CityConstructions.GetBuiltBuildings()
                                .Where(it => it.MatchesFilter(unique.Params[0], applicableCity.State)).ToList();
                            foreach (var building in buildingsToRemove) applicableCity.CityConstructions.RemoveBuilding(building);
                        }
                        if (notification != null)
                            civInfo.AddNotification(
                                notification,
                                LocationAction.Of(applicableCities.Select(it => it.Location)),
                                NotificationCategory.Cities,
                                NotificationIcon.Construction);
                        return true;
                    };
                }

            case UniqueType.OneTimeSellBuilding:
                {
                    var applicableCities = GetApplicableCities(unique.Params[1]);
                    if (!applicableCities.Any()) return null;

                    return () =>
                    {
                        foreach (var applicableCity in applicableCities)
                        {
                            var buildingsToSell = applicableCity.CityConstructions.GetBuiltBuildings()
                                .Where(it => it.MatchesFilter(unique.Params[0], applicableCity.State) && it.IsSellable()).ToList();

                            foreach (var building in buildingsToSell) applicableCity.SellBuilding(building);
                        }
                        if (notification != null)
                            civInfo.AddNotification(
                                notification,
                                LocationAction.Of(applicableCities.Select(it => it.Location)),
                                NotificationCategory.Cities,
                                NotificationIcon.Gold);
                        return true;
                    };
                }

            case UniqueType.OneTimeUnitHeal:
                if (unit == null) return null;
                if (unit.Health == 100) return null;
                return () =>
                {
                    unit.HealBy(unique.Params[1].ToInt());
                    if (notification != null)
                        unit.Civ.AddNotification(notification, MapUnitAction.Of(unit), NotificationCategory.Units, unit.Name, "Heal Instantly");
                    return true;
                };
            case UniqueType.OneTimeUnitDamage:
                if (unit == null) return null;
                return () =>
                {
                    unit.TakeDamage(unique.Params[1].ToInt());
                    if (notification != null)
                        unit.Civ.AddNotification(notification, MapUnitAction.Of(unit), NotificationCategory.Units, unit.Name);
                    return true;
                };
            case UniqueType.OneTimeUnitGainXP:
                if (unit == null) return null;
                return () =>
                {
                    unit.Promotions.XP += unique.Params[1].ToInt();
                    if (notification != null)
                        unit.Civ.AddNotification(notification, MapUnitAction.Of(unit), NotificationCategory.Units, unit.Name, "UnitActionIcons/Promote");
                    return true;
                };
            case UniqueType.OneTimeUnitGainMovement:
            case UniqueType.OneTimeUnitLoseMovement:
                if (unit == null) return null;
                return () =>
                {
                    float movementToUse =
                        unique.Type == UniqueType.OneTimeUnitLoseMovement
                            ? unique.Params[1].ToFloat()
                            : -unique.Params[1].ToFloat();
                    unit.UseMovementPoints(movementToUse);
                    if (notification != null)
                        unit.Civ.AddNotification(notification, MapUnitAction.Of(unit), NotificationCategory.Units, unit.Name);
                    return true;
                };
            case UniqueType.OneTimeUnitGainStatus:
                if (unit == null) return null;
                if (!unit.Civ.GameInfo.Ruleset.UnitPromotions.ContainsKey(unique.Params[1])) return null;
                return () =>
                {
                    unit.SetStatus(unique.Params[1], unique.Params[2].ToInt());
                    if (notification != null)
                        unit.Civ.AddNotification(notification, MapUnitAction.Of(unit), NotificationCategory.Units, unit.Name, unique.Params[1]);
                    return true;
                };
            case UniqueType.OneTimeUnitLoseStatus:
                if (unit == null) return null;
                if (!unit.HasStatus(unique.Params[1])) return null;
                return () =>
                {
                    unit.RemoveStatus(unique.Params[1]);
                    if (notification != null)
                        unit.Civ.AddNotification(notification, MapUnitAction.Of(unit), NotificationCategory.Units, unit.Name, unique.Params[1]);
                    return true;
                };
            case UniqueType.OneTimeUnitDestroyed:
                if (unit == null) return null;
                return () =>
                {
                    if (notification != null)
                        unit.Civ.AddNotification(notification, LocationAction.Of(unit.GetTile().Position), NotificationCategory.Units, unit.Name, "OtherIcons/DisbandUnit");
                    unit.Destroy();
                    return true;
                };
            case UniqueType.OneTimeUnitUpgrade:
            case UniqueType.OneTimeUnitSpecialUpgrade:
                {
                    if (unit == null) return null;
                    var upgrade = MinByOrNull(
                        unit.Upgrade.GetUpgradesOfKind(isSpecial: unique.Type == UniqueType.OneTimeUnitSpecialUpgrade)
                            .Where(it => unit.Upgrade.CanPerformUpgrade(it, isFree: true)),
                        it => it.UpgradedUnit.Cost);
                    if (upgrade == null) return null;
                    return () =>
                    {
                        unit.Upgrade.PerformUpgrade(upgrade, isFree: true);
                        if (notification != null)
                            unit.Civ.AddNotification(notification, MapUnitAction.Of(unit), NotificationCategory.Units, unit.Name, "UnitActionIcons/Upgrade");
                        return true;
                    };
                }
            case UniqueType.OneTimeUnitGainPromotion:
                {
                    if (unit == null) return null;
                    var promotion = unit.Civ.GameInfo.Ruleset.UnitPromotions.Keys
                        .FirstOrDefault(it => it == unique.Params[1]);
                    if (promotion == null) return null;
                    return () =>
                    {
                        unit.Promotions.AddPromotion(promotion, true);
                        if (notification != null)
                            unit.Civ.AddNotification(notification, MapUnitAction.Of(unit), NotificationCategory.Units, unit.Name, promotion);
                        return true;
                    };
                }
            case UniqueType.OneTimeUnitRemovePromotion:
                {
                    if (unit == null) return null;
                    var promotion = unit.Civ.GameInfo.Ruleset.UnitPromotions.Keys
                        .FirstOrDefault(it => it == unique.Params[1]);
                    if (promotion == null) return null;
                    return () =>
                    {
                        unit.Promotions.RemovePromotion(promotion);
                        if (notification != null)
                            unit.Civ.AddNotification(notification, MapUnitAction.Of(unit), NotificationCategory.Units, unit.Name, promotion);
                        return true;
                    };
                }

            case UniqueType.OneTimeRemoveResourcesFromTile:
                {
                    if (tile == null) return null;
                    var resource = tile.TileResource;
                    if (resource == null) return null;
                    var resourceFilter = unique.Params[0];
                    if (!resource.MatchesFilter(resourceFilter)) return null;
                    return () =>
                    {
                        tile.TileResource = null;
                        tile.ResourceAmount = 0;
                        return true;
                    };
                }

            case UniqueType.OneTimeRemoveImprovementsFromTile:
                {
                    if (tile == null) return null;
                    var tileImprovement = tile.TileImprovement;
                    if (tileImprovement == null) return null;
                    var improvementFilter = unique.Params[0];
                    if (!tileImprovement.MatchesFilter(improvementFilter)) return null;
                    return () =>
                    {
                        // Don't remove the improvement if we're just removing the roads
                        if (improvementFilter != Constants.AllRoad)
                            tile.RemoveImprovement();

                        // Remove the roads if desired
                        if (improvementFilter == "All" || improvementFilter == Constants.AllRoad)
                            tile.RemoveRoad();
                        return true;
                    };
                }

            case UniqueType.OneTimeChangeTerrain:
                {
                    if (tile == null) return null;
                    var terrain = ruleset.Terrains.GetValueOrDefault(unique.Params[0]);
                    if (terrain == null) return null;
                    if (terrain.Name == Constants.River)
                        return GetOneTimeChangeRiverTriggerFunction(tile);
                    if (terrain.Type.IsBaseTerrain() && tile.BaseTerrain == terrain.Name) return null;
                    if (tile.IsCityCenter() && terrain.Type != TerrainType.Land) return null;
                    if (tile.TerrainFeatures.Contains(terrain.Name)) return null;
                    if (terrain.Type == TerrainType.TerrainFeature && !terrain.OccursOn.Contains(tile.LastTerrain.Name))
                        return null;

                    return () =>
                    {
                        switch (terrain.Type)
                        {
                            case TerrainType.Land:
                            case TerrainType.Water:
                                tile.SetBaseTerrain(terrain);
                                break;
                            case TerrainType.TerrainFeature:
                                tile.AddTerrainFeature(terrain.Name);
                                break;
                            case TerrainType.NaturalWonder:
                                NaturalWonderGenerator.PlaceNaturalWonder(terrain, tile);
                                break;
                        }
                        TileNormalizer.NormalizeToRuleset(tile, ruleset);
                        foreach (var tileUnit in tile.GetUnits().Where(it => !it.Movement.CanPassThrough(tile)).ToList())
                            tileUnit.Movement.TeleportToClosestMoveableTile();
                        return true;
                    };
                }
            case UniqueType.OneTimeAddResource:
                {
                    if (tile == null) return null;
                    var resourceName = unique.Params[0];
                    var resource = ruleset.TileResources.GetValueOrDefault(resourceName);
                    if (resource == null) return null;
                    if (!resource.TerrainsCanBeFoundOn.Any(it => it == tile.BaseTerrain || tile.TerrainFeatures.Contains(it))) return null;
                    return () =>
                    {
                        // Remove the original resources
                        if (tile.TileResource != null)
                        {
                            tile.TileResource = null;
                            tile.ResourceAmount = 0;
                        }

                        // Same deal as the place resource command
                        tile.SetTileResource(resource, majorDeposit: false);
                        (tile.GetOwner() as Civilization)?.Cache.UpdateCivResources();
                        TileNormalizer.NormalizeToRuleset(tile, ruleset);
                        return true;
                    };
                }

            case UniqueType.OneTimeTakeOverTilesInRadius:
                {
                    if (tile == null) return null;
                    if (civInfo.Cities.Count == 0) return null;
                    var tileFilter = unique.Params[0];
                    int radius = unique.Params[1].ToInt();
                    if (radius < 0) return null;
                    var tilesToTakeOver = tile.GetTilesInDistance(radius)
                        .Where(it => !it.IsCityCenter() && it.MatchesFilter(tileFilter) && !ReferenceEquals(it.GetOwner(), civInfo))
                        .ToList();
                    if (tilesToTakeOver.Count == 0) return null;

                    // Lower is better
                    int CityPriority(CityObject c) => c.GetCenterTile().AerialDistanceTo(tile) + (c.IsBeingRazed ? 5 : 0);

                    var citiesWithAdjacentTiles = tilesToTakeOver
                        .SelectMany(it => it.Neighbors.Append(it))
                        .Select(it => it.OwningCity)
                        .OfType<CityObject>()
                        .Where(it => it.Civ == civInfo)
                        .Distinct()
                        .ToList();

                    var cityToAddTo = MinByOrNull(citiesWithAdjacentTiles, CityPriority)
                        ?? MinByOrNull(civInfo.Cities, CityPriority).NotNull();

                    return () =>
                    {
                        var civsToNotify = new List<Civilization>();
                        foreach (var tileToTakeOver in tilesToTakeOver)
                        {
                            var otherCiv = tileToTakeOver.GetOwner() as Civilization;
                            if (otherCiv != null)
                            {
                                // decrease relations for -10 pt/tile
                                otherCiv.GetDiplomacyManagerOrMeet(civInfo).AddModifier(DiplomaticModifiers.StealingTerritory, -10f);
                                if (!civsToNotify.Contains(otherCiv)) civsToNotify.Add(otherCiv);
                            }
                            // check if civ has stolen a tile from a citystate
                            if (otherCiv != null && otherCiv.IsCityState)
                            {
                                // create this varibale diplomacyCityState for more readability
                                var diplomacyCityState = otherCiv.GetDiplomacyManagerOrMeet(civInfo);
                                diplomacyCityState.AddInfluence(-15f);

                                if (!diplomacyCityState.HasFlag(DiplomacyFlags.TilesStolen))
                                {
                                    civInfo.AddPopupAlert(new PopupAlert(AlertType.TilesStolen, otherCiv.CivId));
                                    diplomacyCityState.SetFlag(DiplomacyFlags.TilesStolen, 1);
                                }
                            }
                            cityToAddTo.Expansion.TakeOwnership(tileToTakeOver);
                        }

                        foreach (var otherCiv in civsToNotify)
                            otherCiv.AddNotification($"Your territory has been stolen by [{civInfo}]!",
                                tile.Position, NotificationCategory.Cities, civInfo.CivName, NotificationIcon.War);

                        return true;
                    };
                }

            case UniqueType.OneTimeUnitGetsName:
                {
                    if (unit == null) return null;
                    var unitNameGroup = unique.Params[1];
                    // Get both the name and associated unique for the unit name
                    var pair = unit.Civ.GameInfo.Ruleset.UnitNameGroups.Values.Where(it =>
                            // Get all unit name groups that match the name or a tag
                            it.Name == unitNameGroup || ((IHasUniques)it).HasTagUnique(unitNameGroup, unit.Cache.State))
                        .Where(it =>
                            // Validate that it's available
                            !((IHasUniques)it).GetMatchingUniques(UniqueType.OnlyAvailable, GameContext.IgnoreConditionalsState)
                                .Any(u => !u.ConditionalsApply(unit.Cache.State))
                            && !((IHasUniques)it).GetMatchingUniques(UniqueType.Unavailable, GameContext.IgnoreConditionalsState)
                                .Any(u => u.ConditionalsApply(unit.Cache.State)))
                        .SelectMany(group =>
                            // Grab only names that haven't been taken
                            group.UnitNames.Where(name => !civInfo.GameInfo.UnitNamesTaken.Contains(name))
                                // Make a pair of the name and the group instance
                                .Select(it => (Name: it, Group: group)))
                        // JUSTIFY: T052 Kotlin shuffles with the unseeded Random.Default; the engine has no unseeded RNG, so the state-based rng is used
                        .ToList().Shuffled(rng).Select(it => ((string Name, UnitNameGroup Group)?)it).FirstOrDefault();
                    if (pair == null) return null;

                    return () =>
                    {
                        unit.InstanceName = pair.Value.Name;
                        civInfo.GameInfo.UnitNamesTaken.Add(pair.Value.Name);
                        unit.Promotions.AddPromotion(pair.Value.Name, true);
                        foreach (var groupUnique in pair.Value.Group.UniqueObjects)
                        {
                            if (groupUnique.IsTriggerable && !groupUnique.HasTriggerConditional() && groupUnique.ConditionalsApply(unit.Cache.State))
                                TriggerUnique(groupUnique, unit);
                        }
                        return true;
                    };
                }

            case UniqueType.OneTimeGlobalAlert:
                {
                    if (triggerNotificationText == null) return null;
                    var alertText = unique.Params[0];
                    return () => UniqueTriggerExecutors.TriggerGlobalAlerts(civInfo, alertText, triggerNotificationText);
                }

            default:
                return null;
        }
    }

    private static string NotificationIconOf(Stat stat) => stat switch
    {
        Stat.Production => NotificationIcon.Production,
        Stat.Food => NotificationIcon.Food,
        Stat.Gold => NotificationIcon.Gold,
        Stat.Science => NotificationIcon.Science,
        Stat.Culture => NotificationIcon.Culture,
        Stat.Happiness => NotificationIcon.Happiness,
        Stat.Faith => NotificationIcon.Faith,
        _ => throw new ArgumentOutOfRangeException(nameof(stat)),
    };

    private static IEnumerable<T> DeferredShuffleTake<T>(IEnumerable<T> source, GameRandom random, int count)
    {
        foreach (var item in source.Shuffled(random).Take(count)) yield return item;
    }

    /// <summary>The <c>MusicTrackChooserFlags</c> entry whose name equals <paramref name="name"/> ignoring case, or null.</summary>
    private static string? MusicTrackChooserFlagName(string name)
    {
        foreach (var flag in (string[])["PrefixMustMatch", "SuffixMustMatch", "SlowFade", "PlaySingle", "PlayDefaultFile"])
            if (string.Equals(flag, name, StringComparison.OrdinalIgnoreCase)) return flag;
        return null;
    }

    /// <summary>Kotlin <c>minOrNull()</c> on an Int sequence.</summary>
    private static int? MinOrNull(IEnumerable<int> values)
    {
        int? result = null;
        foreach (var value in values)
            if (result == null || value < result) result = value;
        return result;
    }

    /// <summary>Kotlin <c>minByOrNull</c>: the first element with the smallest selector value.</summary>
    private static T? MinByOrNull<T>(IEnumerable<T> source, Func<T, int> selector) where T : class
    {
        T? best = null;
        int bestValue = 0;
        foreach (var item in source)
        {
            int value = selector(item);
            if (best == null || value < bestValue)
            {
                best = item;
                bestValue = value;
            }
        }
        return best;
    }

    private static string? GetNotificationText(string? notification, string? triggerNotificationText, string effectNotificationText)
    {
        if (!string.IsNullOrEmpty(notification)) return notification;
        if (triggerNotificationText == null) return null;
        var translations = Translations.Translations.Current;
        return translations.TriggerNotificationEffectBeforeCause(translations.CurrentLanguage)
            ? $"{{{effectNotificationText}}}{{ }}{{{triggerNotificationText}}}"
            : $"{{{triggerNotificationText}}}{{ }}{{{effectNotificationText}}}";
    }

    private static Func<bool>? GetOneTimeChangeRiverTriggerFunction(TileClass tile)
    {
        if (!tile.Neighbors.Any(it => it.IsLand && !tile.IsConnectedByRiver(it)))
            return null; // no place for another river
        return () => RiverGenerator.ContinueRiverOn(tile);
    }
}

/// <summary>Kotlin <c>String.toInt()/toIntOrNull()/toFloat()/toFloatOrNull()</c> for the trigger parameters.</summary>
internal static class UniqueTriggerParsing
{
    public static int? ToIntOrNull(this string text) =>
        int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : null;
    public static float? ToFloatOrNull(this string text) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
}
