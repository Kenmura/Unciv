using Unciv.Logic.Civilization;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Models.Translations;
using Unciv.Utils;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using NationObject = Unciv.Models.Ruleset.Nation.Nation;
using PlayerSetup = Unciv.Models.Metadata.Player;
using RulesetObject = Unciv.Models.Ruleset.Ruleset;
using StatsObject = Unciv.Models.StatTypes.Stats;

namespace Unciv.Logic;

// Ported from core/src/com/unciv/logic/GameStarter.kt (T099).
//
// Deviations from the Kotlin source:
// - Randomness and the clock are inputs. Kotlin draws the gameId from a SecureRandom (GameInfo's initializer) and every unseeded
//   shuffled()/shuffle() from Random.Default (chooseCivilizations, getCivsOrderedByAvailableLocations): here the gameId comes from
//   GameInfoPreview.RandomGameId(new GameRandom(seed)) and every shuffle draws from the GameStarter rng, so one seed gives one game.
//   currentTurnStartTime is the given clock reading; a map seed of 0 also needs the clock (MapGenerator).
// - Quirk kept: on the saved-map path chooseCivilizations reads gameInfo.tileMap before it is assigned (the empty default map), so preset starting locations are never seen there.
// - Start scores are ordered with OrderBy: NaN and -0.0 sort differently from Kotlin Float.compareTo (scores are never NaN).
// - Collections whose JVM order was identity-hash order (HashMap<Tile, Float>, HashMap<Civilization, Tile>) are insertion ordered.
// - `it.playerType === PlayerType.Human || it.chosenCiv === Constants.spectator` compared the string by reference on the JVM, which is only
//   true for the interned literal; here strings compare by value (the intended meaning).
// - Stripped as UI/client concerns: the consoleTimings/runAndMeasure debugging instrumentation, and `UncivGame.Current.settings.lastGameSetup = ...; save()`
//   (the client stores its own last setup). The StartIntro popup alert is kept; there are no multiplayer alerts in this class.
// - MapSaver.loadMap(gameSetupInfo.mapFile!!): GameSetupInfo.MapFile is a path string, opened with the openMapFile argument.

/// <summary>
/// Starts a new game.
/// Map terrain is deterministic, but game setup, including start locations, resources, and other details, are random,
/// based on <see cref="GameContext.StateBasedRandom"/>, which is based on the gameId.
/// </summary>
public sealed class GameStarter
{
    private readonly GameSetupInfo gameSetupInfo;
    private readonly GameInfo gameInfo = new();
    private readonly GameRandom rng;
    private readonly RulesetObject ruleset;
    // JUSTIFY: T099 Kotlin `lateinit var tileMap`, assigned in the constructor before any use
    private TileMap tileMap = null!;

    /// <summary>
    /// Kotlin <c>GameStarter.startNewGame(gameSetupInfo)</c>.
    /// </summary>
    /// <param name="gameSetupInfo">The setup. Like Kotlin, its game parameters are normalised in place (dead mods removed, base ruleset and speed fixed).</param>
    /// <param name="seed">Seeds the gameId (Kotlin: a random UUID), and with it everything drawn from <c>stateBasedRandom</c>.</param>
    /// <param name="currentTimeMillis">The clock reading for <c>currentTurnStartTime</c> and for a map seed of 0.</param>
    /// <param name="openMapFile">Opens <see cref="GameSetupInfo.MapFile"/> when the setup names a saved map.</param>
    public static GameInfo StartNewGame(GameSetupInfo gameSetupInfo, long seed, long currentTimeMillis,
        Func<string, IUncivFileHandle>? openMapFile = null) =>
        new GameStarter(gameSetupInfo, seed, currentTimeMillis, openMapFile).gameInfo;

    private GameStarter(GameSetupInfo gameSetupInfo, long seed, long currentTimeMillis, Func<string, IUncivFileHandle>? openMapFile)
    {
        this.gameSetupInfo = gameSetupInfo;
        gameInfo.GameId = GameInfoPreview.RandomGameId(new GameRandom(seed));
        gameInfo.CurrentTurnStartTime = currentTimeMillis;
        rng = new GameContext(gameInfo: gameInfo).StateBasedRandom("GameStarter");

        var gameParameters = gameSetupInfo.GameParameters;

        // In the case where we used to have an extension mod, and now we don't, we cannot "unselect" it in the UI.
        // We need to remove the dead mods so there aren't problems later.
        foreach (var mod in gameParameters.Mods.ToList())
            if (!RulesetCache.ContainsKey(mod)) gameParameters.Mods.Remove(mod);

        // [TEMPORARY] If we have a base ruleset in the mod list, we make that our base ruleset
        var baseRulesetInMods = gameParameters.Mods.FirstOrDefault(it => RulesetCache.Get(it).NotNull().ModOptions.IsBaseRuleset);
        if (baseRulesetInMods != null)
            gameParameters.BaseRuleset = baseRulesetInMods;

        if (!RulesetCache.ContainsKey(gameParameters.BaseRuleset))
            gameParameters.BaseRuleset = RulesetCache.GetVanillaRuleset().Name;

        gameInfo.GameParameters = gameParameters;
        ruleset = RulesetCache.GetComplexRuleset(gameInfo.GameParameters);
        var mapGen = new Map.MapGenerator.MapGenerator(ruleset, currentTimeMillis: () => currentTimeMillis);

        // Make sure that a valid game speed is loaded (catches a base ruleset not using the default game speed)
        if (!ruleset.Speeds.ContainsKey(gameParameters.Speed))
            gameParameters.Speed = ruleset.Speeds.Keys.First();

        List<PlayerSetup> phaseOneChosenCivs;
        if (gameSetupInfo.MapParameters.Name != "")
        {
            var mapFile = gameSetupInfo.MapFile ?? throw new InvalidOperationException("GameSetupInfo.MapFile is required for a saved map");
            tileMap = MapSaver.LoadMap((openMapFile ?? throw new InvalidOperationException("openMapFile is required for a saved map"))(mapFile));
            // Don't override the map parameters - this can include if we world wrap or not!
            phaseOneChosenCivs = ChooseCivilizations(existingMap: true);
        }
        else
        {
            // The MapGen needs to know what civs are in the game to generate regions, starts and resources
            phaseOneChosenCivs = ChooseCivilizations(existingMap: false);
            AddCivilizations(phaseOneChosenCivs);
            tileMap = mapGen.GenerateMap(gameSetupInfo.MapParameters, gameParameters, gameInfo);
            tileMap.MapParameters = gameSetupInfo.MapParameters;
            // Now forget them for a moment! MapGen can silently fail to place some city states, so then we'll use the old fallback method to place those.
            gameInfo.Civilizations.Clear();
        }

        gameInfo.TileMap = tileMap;
        tileMap.GameInfo = gameInfo; // need to set this transient before placing units in the map
        AddCivilizations(phaseOneChosenCivs); // this is before gameInfo.setTransients, so gameInfo doesn't yet have the gameBasics

        // Remove units for civs that aren't in this game
        foreach (var tile in tileMap.Values)
        {
            foreach (var unit in tile.GetUnits().ToList())
            {
                if (gameInfo.Civilizations.Any(it => it.CivId == unit.Owner)) continue;
                unit.CurrentTile = tile;
                unit.SetTransients(ruleset);
                unit.RemoveFromTile();
            }
        }

        if (tileMap.ContinentSizes.Count == 0) // Probably saved map without continent data
            tileMap.AssignContinents(TileMap.AssignContinentsMode.Ensure);

        // mark as no migrateToTileHistory necessary
        gameInfo.HistoryStartTurn = 0;
        tileMap.SetTransients(ruleset); // if we're starting from a map with pre-placed units, they need the civs to exist first
        tileMap.SetStartingLocationsTransients();

        gameInfo.Difficulty = gameParameters.Difficulty;

        gameInfo.SetTransients(); // needs to be before placeBarbarianUnit because it depends on the tilemap having its gameInfo set

        AddCivStartingUnits();
        AddCivPolicies();
        AddCivTechs();
        AddCivStats();

        // remove starting locations once we're done
        tileMap.ClearStartingLocations();

        // set max starting movement for units loaded from map
        foreach (var tile in tileMap.Values)
            foreach (var unit in tile.GetUnits())
                unit.CurrentMovement = unit.GetMaxMovement();

        // This triggers the one-time greeting from Nation.startIntroPart1/2
        AddPlayerIntros();
    }

    private void AddPlayerIntros()
    {
        foreach (var civ in gameInfo.Civilizations.Where(it =>
                     // isNotEmpty should also exclude a spectator
                     it.PlayerType == PlayerType.Human && it.Nation.StartIntroPart1.Length != 0).ToList())
            civ.PopupAlerts.Add(new PopupAlert(AlertType.StartIntro, ""));
    }

    private void AddTechSilently(CivilizationObject civ, string name)
    {
        // check if the technology is in the ruleset and not already researched
        if (!ruleset.Technologies.ContainsKey(name)) return;
        if (civ.Tech.IsResearched(name)) return;
        civ.Tech.AddTechnology(name, false);
    }

    private void AddCivTechs()
    {
        foreach (var civInfo in gameInfo.Civilizations)
        {
            if (civInfo.IsBarbarian) continue;

            foreach (var tech in ruleset.Technologies.Values.Where(it => ((IHasUniques)it).HasUnique(UniqueType.StartingTech)).ToList())
                AddTechSilently(civInfo, tech.Name);

            if (!civInfo.IsHuman())
                foreach (var tech in gameInfo.GetDifficulty().AiFreeTechs)
                    AddTechSilently(civInfo, tech);

            // generic start with technology unique
            civInfo.ForEachMatchingUnique(UniqueType.StartsWithTech, unique => AddTechSilently(civInfo, unique.Params[0]));

            // add all techs to spectators
            if (civInfo.IsSpectator())
                foreach (var tech in ruleset.Technologies.Values)
                    AddTechSilently(civInfo, tech.Name);

            // Add techs for advanced starting era
            var startingEraNumber = ruleset.Eras[gameSetupInfo.GameParameters.StartingEra].EraNumber;
            foreach (var tech in ruleset.Technologies.Values)
            {
                if (ruleset.Eras[tech.Era()].EraNumber >= startingEraNumber) continue;
                if (civInfo.Tech.IsUnresearchable(tech)) continue;
                AddTechSilently(civInfo, tech.Name);
            }

            // Since adding technologies generates popups (addTechnology parameter showNotification only suppresses Notifications)
            civInfo.PopupAlerts.Clear();
        }
    }

    private void AddCivPolicies()
    {
        foreach (var civInfo in gameInfo.Civilizations.Where(it => !it.IsBarbarian).ToList())
        {
            // generic start with policy unique
            civInfo.ForEachMatchingUnique(UniqueType.StartsWithPolicy, unique =>
            {
                // get the parameter from the unique
                var policyName = unique.Params[0];

                // check if the policy is in the ruleset and not already adopted
                if (!ruleset.Policies.ContainsKey(policyName) || civInfo.Policies.IsAdopted(policyName))
                    return;

                var policyToAdopt = ruleset.Policies[policyName];
                civInfo.Policies.FreePolicies++;
                civInfo.Policies.Adopt(policyToAdopt);
            });
        }
    }

    private void AddCivStats()
    {
        var startingEra = gameInfo.GameParameters.StartingEra;
        var era = gameInfo.Ruleset.Eras[startingEra];
        foreach (var civInfo in gameInfo.Civilizations.Where(it => !it.IsBarbarian && !it.IsSpectator()).ToList())
        {
            civInfo.AddGold(KotlinMath.FloatToInt(era.StartingGold * gameInfo.Speed.GoldCostModifier));
            civInfo.Policies.AddCulture(KotlinMath.FloatToInt(era.StartingCulture * gameInfo.Speed.CultureCostModifier));
        }
    }

    private List<PlayerSetup> ChooseCivilizations(bool existingMap)
    {
        var newGameParameters = gameSetupInfo.GameParameters;
        var selectedPlayerNames = newGameParameters.Players.Select(it => it.ChosenCiv).ToHashSet();
        var randomNationsPoolSource = gameSetupInfo.GameParameters.EnableRandomNationsPool
            ? ruleset.Nations.Values.Where(it => gameSetupInfo.GameParameters.RandomNationsPool.Contains(it.Name))
            : ruleset.Nations.Values.Where(it => it.IsMajorCiv && !((IHasUniques)it).HasUnique(UniqueType.WillNotBeChosenForNewGames));
        // A stack: removeLast() takes from the end
        var randomNationsPool = randomNationsPoolSource.Where(it => !selectedPlayerNames.Contains(it.Name)).Shuffled(rng);

        IReadOnlyCollection<string> civNamesWithStartingLocations = existingMap
            ? gameInfo.TileMap.StartingLocationsByNation.Keys.ToList()
            : [];
        var presetRandomNationsPool = randomNationsPool.Where(it => civNamesWithStartingLocations.Contains(it.Name)).Shuffled(rng);
        randomNationsPool.RemoveAll(presetRandomNationsPool.Contains);

        // At this point the civ names in newGameParameters.players, randomNationsPool and presetRandomNationsPool
        // are mutually exclusive. Random should **not** exist in the two random pools, but we have not explicitly guarded
        // here against the UI leaving one in gameParameters.randomNationsPool or map editor in tileMap.startingLocationsByNation.

        var extraRandomAIPlayers = 0;
        List<PlayerSetup> selectedAIToSkip = [];
        if (newGameParameters.RandomNumberOfPlayers)
        {
            // This swaps min and max if the user accidentally swapped min and max
            var min = System.Math.Min(newGameParameters.MinNumberOfPlayers, newGameParameters.MaxNumberOfPlayers);
            var max = System.Math.Max(newGameParameters.MaxNumberOfPlayers, newGameParameters.MinNumberOfPlayers);
            var nonAICount = newGameParameters.Players.Count(it => it.PlayerType == PlayerType.Human || it.ChosenCiv == Constants.Spectator);
            var desiredNumberOfPlayers = new IntRange(System.Math.Max(min, nonAICount), System.Math.Max(max, nonAICount)).Random(rng);

            var numberOfNonSpectatorPlayers = newGameParameters.Players.Count(it => it.ChosenCiv != Constants.Spectator);
            if (desiredNumberOfPlayers > numberOfNonSpectatorPlayers)
            {
                extraRandomAIPlayers = desiredNumberOfPlayers - numberOfNonSpectatorPlayers;
            }
            else if (desiredNumberOfPlayers < numberOfNonSpectatorPlayers)
            {
                var extraPlayers = newGameParameters.Players.Count - desiredNumberOfPlayers;
                selectedAIToSkip = newGameParameters.Players
                    .Where(it => it.PlayerType == PlayerType.AI)
                    .Shuffled(rng)
                    .OrderByDescending(it => it.ChosenCiv == Constants.Random)
                    .ToList()
                    .GetRange(0, extraPlayers);
            }
        }

        // Add player entries to the result

        // Join two sequences, one the explicitly chosen players...
        var chosenPlayers = newGameParameters.Players
            .Where(it => !selectedAIToSkip.Contains(it))
            .OrderBy(it => it.ChosenCiv == Constants.Random) // Nonrandom before random
            .ThenBy(it => it.PlayerType == PlayerType.AI) // Human before AI
                                                          // ...another for the extra random ones
            .Concat(Enumerable.Range(0, extraRandomAIPlayers).Select(_ => new PlayerSetup()))
            .Select(it =>
            {
                // Resolve random players
                if (it.ChosenCiv != Constants.Random) return new PlayerSetup(ruleset.Nations[it.ChosenCiv], it.PlayerType, it.PlayerId);
                if (presetRandomNationsPool.Count != 0) return new PlayerSetup(PopLast(presetRandomNationsPool), it.PlayerType, it.PlayerId);
                if (randomNationsPool.Count != 0) return new PlayerSetup(PopLast(randomNationsPool), it.PlayerType, it.PlayerId);
                return null;
            })
            .OfType<PlayerSetup>()
            .ToList();

        // ensure Spectators always first players
        var spectators = chosenPlayers.Where(it => it.ChosenCiv == Constants.Spectator).ToList();
        var otherPlayers = chosenPlayers.Where(it => it.ChosenCiv != Constants.Spectator).ToList();

        // Shuffle Major Civs
        if (newGameParameters.ShufflePlayerOrder) otherPlayers.Shuffle(rng);

        chosenPlayers.Clear();
        chosenPlayers.AddRange(spectators);
        chosenPlayers.AddRange(otherPlayers);

        // Add CityStates to result - disguised as normal AI, but addCivilizations will detect them
        int numberOfCityStates;
        if (newGameParameters.RandomNumberOfCityStates)
        {
            // This swaps min and max if the user accidentally swapped min and max
            var min = System.Math.Min(newGameParameters.MinNumberOfCityStates, newGameParameters.MaxNumberOfCityStates);
            var max = System.Math.Max(newGameParameters.MaxNumberOfCityStates, newGameParameters.MinNumberOfCityStates);
            numberOfCityStates = new IntRange(min, max).Random(rng);
        }
        else
        {
            numberOfCityStates = newGameParameters.NumberOfCityStates;
        }
        if (numberOfCityStates < 0) throw new ArgumentException($"Requested element count {numberOfCityStates} is less than zero.");

        chosenPlayers.AddRange(ruleset.Nations.Values
            .Where(it => it.IsCityState && !((IHasUniques)it).HasUnique(UniqueType.WillNotBeChosenForNewGames))
            .Shuffled(rng)
            .OrderByDescending(it => civNamesWithStartingLocations.Contains(it.Name)) // please those with location first
            .Take(numberOfCityStates)
            .Select(it => new PlayerSetup(it)));

        return chosenPlayers;
    }

    private static NationObject PopLast(List<NationObject> stack)
    {
        var last = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        return last;
    }

    private void AddCivilizations(List<PlayerSetup> chosenPlayers)
    {
        var newGameParameters = gameSetupInfo.GameParameters;
        if (!newGameParameters.NoBarbarians)
        {
            if (ruleset.Nations.TryGetValue(Constants.Barbarians, out var barbs))
            {
                var barbarianCivilization = new CivilizationObject(barbs);
                gameInfo.Civilizations.Add(barbarianCivilization);
            }
        }

        var usedCivNations = chosenPlayers.Select(it => it.ChosenNation).ToHashSet();
        var usedMajorCivs = ruleset.Nations.Values.Where(it => it.IsMajorCiv && usedCivNations.Contains(it));

        foreach (var player in chosenPlayers)
        {
            var civ = new CivilizationObject(player.ChosenNation);
            if (civ.IsMajorCiv() || civ.IsSpectator())
            {
                civ.PlayerType = player.PlayerType;
                civ.PlayerId = player.PlayerId;
                civ.PlayerMinutesBeforeForceResign = newGameParameters.MinutesUntilForceResign;
            }
            else if (!civ.CityStateFunctions.InitCityState(ruleset, newGameParameters.StartingEra, usedMajorCivs, rng))
            {
                continue;
            }
            gameInfo.Civilizations.Add(civ);
        }
    }

    private void AddCivStartingUnits()
    {
        var ruleSet = gameInfo.Ruleset;
        var tileMap = gameInfo.TileMap;

        var cityCenterMinStats = ruleSet.TileImprovements.TryGetValue(Constants.CityCenter, out var cityCenter)
            ? ((IHasUniques)cityCenter).GetMatchingUniques(UniqueType.EnsureMinimumStats, GameContext.IgnoreConditionalsState).FirstOrDefault()?.Stats
                ?? StatsObject.DefaultCityCenterMinimum
            : StatsObject.DefaultCityCenterMinimum;

        var startScores = new LinkedHashMap<Tile, float>();
        foreach (var tile in tileMap.Values)
            startScores.Put(tile, tile.Stats.GetTileStartScore(cityCenterMinStats));
        var allCivs = gameInfo.Civilizations.Where(it => !it.IsBarbarian).ToList();
        var landTilesInBigEnoughGroup = GetCandidateLand(allCivs.Count, startScores);

        // First we get start locations for the major civs, on the second pass the city states (without predetermined starts) can squeeze in wherever
        var civNamesWithStartingLocations = tileMap.StartingLocationsByNation.Keys;
        var bestCivs = allCivs.Where(it => (!it.IsCityState || civNamesWithStartingLocations.Contains(it.CivId)) && !it.IsSpectator()).ToList();
        var bestLocations = GetStartingLocations(bestCivs, landTilesInBigEnoughGroup, startScores);
        foreach (var (civ, tile) in bestLocations)
        {
            // A nation can have multiple marked starting locations, of which the first pass may have chosen one
            tileMap.RemoveStartingLocations(civ.CivId);
            // Mark the best start locations so we remember them for the second pass
            tileMap.AddStartingLocation(civ.CivId, tile);
        }

        var startingLocations = GetStartingLocations(allCivs, landTilesInBigEnoughGroup, startScores);

        // no starting units for Barbarians and Spectators
        DetermineStartingUnitsAndLocations(startingLocations, ruleSet);
    }

    private static void RemoveAncientRuinsNearStartingLocation(Tile startingLocation)
    {
        startingLocation.ForEachTileAtDistance(3, tile =>
        {
            if (tile.TileImprovement != null && tile.TileImprovement.IsAncientRuinsEquivalent())
                tile.RemoveImprovement(); // Remove ancient ruins in immediate vicinity
        });
    }

    private void DetermineStartingUnitsAndLocations(LinkedHashMap<CivilizationObject, Tile> startingLocations, RulesetObject ruleset)
    {
        var startingEra = gameInfo.GameParameters.StartingEra;
        var settlerLikeUnits = ruleset.Units.Where(it => it.Value.IsCityFounder()).ToList();
        var settlerLikeMap = new LinkedHashMap<string, BaseUnit>();
        foreach (var (name, unit) in settlerLikeUnits) settlerLikeMap.Put(name, unit);

        foreach (var civ in gameInfo.Civilizations.Where(it => !it.IsBarbarian && !it.IsSpectator()).ToList())
        {
            var startingLocation = startingLocations[civ];

            RemoveAncientRuinsNearStartingLocation(startingLocation);
            var startingUnits = GetStartingUnitsForEraAndDifficulty(civ, startingEra);
            AdjustStartingUnitsForCityStatesAndOneCityChallenge(civ, startingUnits, settlerLikeMap);
            PlaceStartingUnits(civ, startingLocation, startingUnits, ruleset.Eras[startingEra].StartingMilitaryUnit, settlerLikeMap);

            // Trigger any global or nation uniques that should be triggered.
            // We may need the starting location for some uniques, which is why we're doing it now
            // This relies on gameInfo.ruleset already being initialized
            var startingTriggers = gameInfo.GetGlobalUniques().UniqueObjects.Concat(((IHasUniques)civ.Nation).UniqueObjects).ToList();
            foreach (var unique in startingTriggers)
            {
                if (unique.HasTriggerConditional() || !unique.ConditionalsApply(civ.State)) continue;
                var repeats = unique.GetUniqueMultiplier(civ.State);
                for (var i = 0; i < repeats; i++)
                    UniqueTriggerActivation.TriggerUnique(unique, civ, tile: startingLocation);
            }
        }
    }

    private List<string> GetStartingUnitsForEraAndDifficulty(CivilizationObject civ, string startingEra)
    {
        if (!ruleset.Eras.TryGetValue(startingEra, out var era))
            throw new InvalidOperationException($"Era {startingEra} does not exist in the ruleset!");
        var startingUnits = era.GetStartingUnits(ruleset);

        // Add extra units granted by difficulty
        var difficulty = gameInfo.GetDifficulty();
        startingUnits.AddRange(
            civ.IsHuman() ? difficulty.PlayerBonusStartingUnits
            : civ.IsMajorCiv() ? difficulty.AiMajorCivBonusStartingUnits
            : difficulty.AiCityStateBonusStartingUnits);

        return startingUnits;
    }

    private BaseUnit? GetEquivalentUnit(CivilizationObject civ, string unitParam, string eraUnitReplacement, LinkedHashMap<string, BaseUnit> settlerLikeUnits)
    {
        var unit = unitParam; // We want to change it and this is the easiest way to do so
        if (unit == Constants.EraSpecificUnit) unit = eraUnitReplacement;
        if (unit == Constants.Settler && !ruleset.Units.ContainsKey(Constants.Settler))
        {
            var buildableSettlerLikeUnits = settlerLikeUnits.Where(it => it.Value.IsBuildable(civ) && it.Value.IsCivilian()).Select(it => it.Key).ToList();
            if (buildableSettlerLikeUnits.Count == 0) return null; // No settlers in this mod
            return civ.GetEquivalentUnit(buildableSettlerLikeUnits.Random(rng));
        }
        if (unit == "Worker" && !ruleset.Units.ContainsKey("Worker"))
        {
            var buildableWorkerLikeUnits = ruleset.Units
                .Where(it => ((IHasUniques)it.Value).HasUnique(UniqueType.BuildImprovements) && it.Value.IsBuildable(civ) && it.Value.IsCivilian())
                .Select(it => it.Key).ToList();
            if (buildableWorkerLikeUnits.Count == 0) return null; // No workers in this mod
            return civ.GetEquivalentUnit(buildableWorkerLikeUnits.Random(rng));
        }
        return civ.GetEquivalentUnit(unit);
    }

    private void AdjustStartingUnitsForCityStatesAndOneCityChallenge(CivilizationObject civ, List<string> startingUnits, LinkedHashMap<string, BaseUnit> settlerLikeUnits)
    {
        // Adjust starting units for city states
        if (civ.IsCityState && !((IHasUniques)gameInfo.Ruleset.ModOptions).HasUnique(UniqueType.AllowCityStatesSpawnUnits))
        {
            var startingSettlers = startingUnits.Where(settlerLikeUnits.ContainsKey).ToList();

            startingUnits.Clear();
            startingUnits.Add(startingSettlers.Random(rng));
        }

        // Adjust starting units for one city challenge
        if (civ.PlayerType == PlayerType.Human && gameInfo.GameParameters.OneCityChallenge)
        {
            var startingSettlers = startingUnits.Where(settlerLikeUnits.ContainsKey).ToList();

            startingUnits.RemoveAll(startingSettlers.Contains);
            startingUnits.Add(startingSettlers.Random(rng));
        }
    }

    private void PlaceStartingUnits(CivilizationObject civ, Tile startingLocation, List<string> startingUnits, string eraUnitReplacement, LinkedHashMap<string, BaseUnit> settlerLikeUnits)
    {
        foreach (var unit in startingUnits)
        {
            var unitToAdd = GetEquivalentUnit(civ, unit, eraUnitReplacement, settlerLikeUnits);
            if (unitToAdd != null) civ.Units.PlaceUnitNearTile(startingLocation.Position, unitToAdd);
        }
    }

    private LinkedHashMap<Tile, float> GetCandidateLand(int civCount, LinkedHashMap<Tile, float> startScores)
    {
        tileMap.AssignContinents(TileMap.AssignContinentsMode.Ensure);

        // We want to  distribute starting locations fairly, and thus not place anybody on a small island
        // - unless necessary. Old code would only consider landmasses >= 20 tiles.
        // Instead, take continents until >=90% total area or everybody can get their own island
        var totalArea = tileMap.ContinentSizes.Values.Sum();
        var candidateArea = 0;
        var candidateContinents = new HashSet<int>();
        var index = 0;
        foreach (var continentId in tileMap.ContinentsSortedBySize)
        {
            candidateArea += tileMap.ContinentSizes[continentId];
            candidateContinents.Add(continentId);
            if (candidateArea >= totalArea * 0.9f) break;
            if (index >= civCount) break;
            index++;
        }

        var result = new LinkedHashMap<Tile, float>();
        foreach (var (tile, score) in startScores)
            if (candidateContinents.Contains(tile.GetContinent())) result.Put(tile, score);
        return result;
    }

    private LinkedHashMap<CivilizationObject, Tile> GetStartingLocations(List<CivilizationObject> civs,
        LinkedHashMap<Tile, float> landTilesInBigEnoughGroup, LinkedHashMap<Tile, float> startScores)
    {
        var civsOrderedByAvailableLocations = GetCivsOrderedByAvailableLocations(civs);

        for (var minimumDistanceBetweenStartingLocations = tileMap.TileMatrix.Count / 6; minimumDistanceBetweenStartingLocations >= 0; minimumDistanceBetweenStartingLocations--)
        {
            var freeTiles = GetFreeTiles(landTilesInBigEnoughGroup, minimumDistanceBetweenStartingLocations);

            var startingLocations = GetStartingLocationsForCivs(civsOrderedByAvailableLocations, freeTiles, startScores, minimumDistanceBetweenStartingLocations);
            if (startingLocations != null) return startingLocations;
        }
        throw new InvalidOperationException("Didn't manage to get starting tiles even with distance of 1?");
    }

    private List<CivilizationObject> GetCivsOrderedByAvailableLocations(List<CivilizationObject> civs)
    {
        var noStartBias = gameSetupInfo.GameParameters.NoStartBias;
        return civs.Shuffled(rng) // Order should be random since it determines who gets best start
            .OrderBy(civ =>
            {
                var startBias = civ.Nation.GetStartBias(ruleset, civ.GetGameContextForStartBias());
                if (tileMap.StartingLocationsByNation.ContainsKey(civ.CivId)) return 1; // harshest requirements
                if (startBias.Any(it => tileMap.NaturalWonders.Contains(it)) && !noStartBias) return 2;
                if (startBias.Contains(Constants.Tundra) && !noStartBias) return 3; // Tundra starts are hard to find, so let's do them first
                if (startBias.Count != 0 && !noStartBias) return 4; // less harsh
                return 5; // no requirements
            })
            .OrderByDescending(it => it.IsHuman()) // More important for humans to get their start biases!
            .ToList();
    }

    private List<Tile> GetFreeTiles(LinkedHashMap<Tile, float> landTilesInBigEnoughGroup, int minimumDistanceBetweenStartingLocations) =>
        landTilesInBigEnoughGroup
            .Where(it => HexMath.GetDistanceFromEdge(it.Key.Position, tileMap.MapParameters) >= minimumDistanceBetweenStartingLocations * 2 / 3)
            .OrderBy(it => it.Value)
            .Select(it => it.Key)
            .ToList();

    // Mutating - updates freeTiles
    private LinkedHashMap<CivilizationObject, Tile>? GetStartingLocationsForCivs(List<CivilizationObject> civsOrderedByAvailableLocations,
        List<Tile> freeTiles, LinkedHashMap<Tile, float> startScores, int minimumDistanceBetweenStartingLocations)
    {
        var startingLocations = new LinkedHashMap<CivilizationObject, Tile>();
        foreach (var civ in civsOrderedByAvailableLocations)
        {
            var startingLocation = GetCivStartingLocation(civ, freeTiles, startScores);
            if (startingLocation == null) break;

            startingLocations.Put(civ, startingLocation);

            var distanceToNext = minimumDistanceBetweenStartingLocations /
                (civ.IsCityState ? 2 : 1); // We allow city states to squeeze in tighter
            var nearby = tileMap.GetTilesInDistance(startingLocation.Position, distanceToNext).ToHashSet();
            freeTiles.RemoveAll(nearby.Contains);
        }
        return startingLocations.Count < civsOrderedByAvailableLocations.Count ? null : startingLocations;
    }

    private Tile? GetCivStartingLocation(CivilizationObject civ, List<Tile> freeTiles, LinkedHashMap<Tile, float> startScores)
    {
        Tile? startingLocation = null;
        if (tileMap.StartingLocationsByNation.TryGetValue(civ.CivId, out var civLocations))
            startingLocation = civLocations.RandomOrNull(rng);
        if (startingLocation == null)
        {
            if (tileMap.StartingLocationsByNation.TryGetValue(Constants.Spectator, out var spectatorLocations))
            {
                startingLocation = spectatorLocations.RandomOrNull(rng);
                if (startingLocation != null)
                    spectatorLocations.Remove(startingLocation);
            }
        }
        if (startingLocation == null && freeTiles.Count != 0)
            startingLocation = GetOneStartingLocation(civ, freeTiles, startScores);
        // If startingLocation is null we failed to get all the starting tiles with this minimum distance
        return startingLocation;
    }

    private Tile GetOneStartingLocation(CivilizationObject civ, List<Tile> freeTiles, LinkedHashMap<Tile, float> startScores)
    {
        if (gameSetupInfo.GameParameters.NoStartBias)
            return freeTiles.Random(rng);

        var startBiases = civ.Nation.GetStartBias(ruleset, civ.GetGameContextForStartBias());
        if (startBiases.Any(it => tileMap.NaturalWonders.Contains(it)))
        {
            // startPref wants Natural wonder neighbor: Rare and very likely to be outside getDistanceFromEdge
            var wonderNeighbor = tileMap.Values
                .Where(it => it.IsNaturalWonder() && startBiases.Contains(it.NaturalWonder.NotNull()))
                .OrderByDescending(it => startScores[it])
                .FirstOrDefault();
            if (wonderNeighbor != null) return wonderNeighbor;
        }

        var preferredTiles = freeTiles.ToList();
        foreach (var startBias in startBiases)
        {
            if (startBias.EqualsPlaceholderText("Avoid []"))
            {
                var tileToAvoid = startBias.GetPlaceholderParameters()[0];
                preferredTiles = preferredTiles.Where(tile =>
                    !tile.GetTilesInDistance(1).Any(it => it.MatchesTerrainFilter(tileToAvoid, null))).ToList();
            }
            else if (tileMap.NaturalWonders.Contains(startBias))
            {
                // passthrough: already failed
            }
            else
            {
                preferredTiles = preferredTiles.Where(tile =>
                    tile.GetTilesInDistance(1).Any(it => it.MatchesTerrainFilter(startBias, null))).ToList();
            }
        }
        return preferredTiles.RandomOrNull(rng) ?? freeTiles.Random(rng);
    }
}
