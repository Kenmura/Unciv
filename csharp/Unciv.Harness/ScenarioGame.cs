using Unciv.Commands;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Models;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Harness;

/// <summary>
/// The fixed game every client scenario starts from (C008): Vanilla ruleset, Tiny map, two human civs (Babylon moves first) and
/// one AI civ, each human with a city, all from fixed seeds. The Godot scenario runner and the headless replay both build their
/// session here, so "same seed + same commands" is a statement about exactly one factory. Engine-only; no Godot types.
/// </summary>
public static class ScenarioGame
{
    public const long GameSeed = 7L;
    public const int MapSeed = 3;
    public const long FixedClockMillis = 1000L;
    public const string HumanA = "Babylon";
    public const string HumanB = "Greece";
    public const string Ai = "Rome";
    public const int RichGold = 500;
    public const int PolicyCulture = 50;

    /// <summary>C052 <c>religion</c> game: a pantheon, two great prophets in two cities (found, then enhance), spies to move, a World Congress vote and two notifications that open pickers.</summary>
    public const int ReligionFoundEnhance = 1;

    /// <summary>C052 <c>religion</c> game: a founded religion with a free follower belief to choose (the reform picker).</summary>
    public const int ReligionReform = 2;
    public const int CrowdAiCount = 10;

    /// <summary>Loads the Vanilla ruleset into <see cref="RulesetCache"/> (from the repository's jsons, no mods) unless it is already there.</summary>
    public static RulesetClass LoadVanilla()
    {
        if (RulesetCache.ContainsKey(BaseRuleset.Civ_V_Vanilla.FullName())) return RulesetCache.GetVanillaRuleset();
        var jsons = FindJsonsPath();
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
        return RulesetCache.GetVanillaRuleset();
    }

    public static string FindJsonsPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "android", "assets", "jsons");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("android/assets/jsons not found above " + AppContext.BaseDirectory);
    }

    /// <summary>
    /// <paramref name="solo"/> (C010): one human (Babylon) and the AI, no city founded: the game the client's QuickStart builds; otherwise the two-human game above.
    /// <paramref name="rich"/> (C018): every human civ starts with <see cref="RichGold"/> gold, so a scenario can buy for real (God mode makes purchases free), and Babylon owns a puppet city (<see cref="SetUpRich"/>).
    /// <paramref name="godMode"/> (C041): the game setup's God mode switch, so a scenario can use the debug commands (free purchases, DebugAddTechnology) to reach a state quickly. The switch is part of the setup, not a command, so the replay passes it too.
    /// </summary>
    public static GameSession Create(long gameSeed = GameSeed, int mapSeed = MapSeed, bool solo = false, bool godMode = false, bool skirmish = false, bool diplomacy = false, bool rich = false, bool policies = false, bool alerts = false, bool victory = false, bool defeat = false, bool trade = false, bool decisions = false, bool crowd = false, bool espionage = false, bool proposals = false, int religion = 0, bool voteResult = false, bool tutorialTasks = false)
    {
        espionage |= religion == ReligionFoundEnhance;
        diplomacy |= alerts | trade | espionage | proposals | voteResult;
        solo |= skirmish | diplomacy | policies | victory | defeat | decisions | crowd | religion > 0 | tutorialTasks;
        var vanilla = LoadVanilla();
        // C038: religion needs Gods & Kings (Vanilla has no beliefs); C054: so do the tutorial task Events; every other scenario plays Vanilla
        var ruleset = decisions || religion > 0 || tutorialTasks ? RulesetCache.Get(BaseRuleset.Civ_V_GnK.FullName()).NotNull().Clone() : vanilla;
        var game = GameStarter.StartNewGame(
            new GameSetupInfo(
                new GameParameters
                {
                    BaseRuleset = ruleset.Name,
                    NumberOfCityStates = diplomacy ? 1 : 0,
                    GodMode = godMode,
                    VictoryTypes = ruleset.Victories.Keys.ToList(),
                    Players = crowd
                        ? CrowdPlayers(ruleset)
                        : solo
                        ? diplomacy
                            ? [new Player(HumanA, PlayerType.Human), new Player(Ai), new Player(HumanB)]
                            : [new Player(HumanA, PlayerType.Human), new Player(Ai)]
                        : [new Player(HumanA, PlayerType.Human), new Player(HumanB, PlayerType.Human), new Player(Ai)],
                },
                new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = mapSeed, MapSize = crowd ? MapSize.Medium : MapSize.Tiny }),
            gameSeed, FixedClockMillis);
        foreach (string name in solo ? Array.Empty<string>() : new[] { HumanA, HumanB })
        {
            var civ = game.GetCivilization(name);
            var settler = civ.Units.GetCivUnits().First(it => it.BaseUnit.IsCityFounder());
            civ.AddCity(settler.CurrentTile.Position, settler);
        }
        if (rich) SetUpRich(game);
        if (policies) SetUpPolicies(game);
        if (skirmish) SetUpSkirmish(game);
        if (diplomacy) SetUpDiplomacy(game);
        if (alerts) SetUpAlerts(game);
        if (victory) SetUpVictory(game);
        if (defeat) SetUpDefeat(game);
        if (trade) SetUpTrade(game);
        if (decisions) SetUpDecisions(game);
        if (espionage) SetUpEspionage(game);
        if (proposals) SetUpProposals(game);
        if (religion == ReligionFoundEnhance) SetUpReligion(game);
        if (religion == ReligionReform) SetUpReform(game);
        if (voteResult) SetUpVoteResult(game);
        return new GameSession(game);
    }

    /// <summary>C040: the human and <see cref="CrowdAiCount"/> AI civs (Rome and the first other major nations in name order), so ending the turn has many civs to play.</summary>
    private static List<Player> CrowdPlayers(RulesetClass ruleset) =>
    [
        new Player(HumanA, PlayerType.Human),
        new Player(Ai),
        .. ruleset.Nations.Values.Where(it => it.IsMajorCiv && it.Name != HumanA && it.Name != Ai).Select(it => it.Name).Order(StringComparer.Ordinal).Take(CrowdAiCount - 1).Select(it => new Player(it)),
    ];

    /// <summary>
    /// C018: every human civ has <see cref="RichGold"/> gold and Babylon owns a second, puppet city (the first land tile at least 5 tiles from its capital, in coordinate
    /// order), so the city screen can annex it. Set up directly on the state so a headless replay builds the same game.
    /// </summary>
    private static void SetUpRich(GameInfo game)
    {
        foreach (var civ in game.Civilizations.Where(it => it.IsHuman())) civ.AddGold(RichGold - civ.Gold);
        var human = game.GetCivilization(HumanA);
        var capital = human.Cities[0].GetCenterTile();
        var site = game.TileMap.Values
            .Where(it => it.IsLand && !it.IsImpassible() && !it.IsCityCenter() && it.GetOwner() == null && it.AerialDistanceTo(capital) >= 5)
            .OrderBy(it => it.Position.x).ThenBy(it => it.Position.y).First();
        human.AddCity(site.Position).IsPuppet = true;
    }

    /// <summary>
    /// C020: Babylon founds its capital (building a Warrior, researching Agriculture) and has <see cref="PolicyCulture"/> stored culture, so it can adopt a few policies (the engine then offers the policy picker). Set up
    /// directly on the state, so a headless replay builds the same game.
    /// </summary>
    private static void SetUpPolicies(GameInfo game)
    {
        var human = game.GetCivilization(HumanA);
        var settler = human.Units.GetCivUnits().First(it => it.BaseUnit.IsCityFounder());
        var capital = human.AddCity(settler.CurrentTile.Position, settler);
        capital.CityConstructions.AddToQueue("Warrior"); // production and research already chosen, so the policy pick is the first thing the HUD asks for
        human.Tech.TechsToResearch.Add("Agriculture");
        human.Policies.AddCulture(PolicyCulture);
    }

    /// <summary>
    /// C012: the solo game plus an AI warrior on a tile next to the human warrior, at war with the human, and a human worker on another neighbouring tile: a
    /// battle, a worker and a settler to give orders to. Set up directly on the state (like the founded cities above), so a headless replay builds the same game.
    /// </summary>
    private static void SetUpSkirmish(GameInfo game)
    {
        var human = game.GetCivilization(HumanA);
        var ai = game.GetCivilization(Ai);
        var warrior = human.Units.GetCivUnits().OrderBy(it => it.Id).First(it => it.BaseUnit.Name == "Warrior");
        var free = warrior.CurrentTile.Neighbors
            .Where(it => it.IsLand && !it.IsImpassible() && !it.GetUnits().Any())
            .OrderBy(it => it.Position.x).ThenBy(it => it.Position.y).ToList();
        ai.Units.PlaceUnitNearTile(free[0].Position, "Warrior");
        human.Units.PlaceUnitNearTile(free[1].Position, "Worker");
        human.GetDiplomacyManagerOrMeet(ai).DeclareWar();
    }


    /// <summary>
    /// C021: the solo game plus a city-state and a second AI civ (Greece). The human has met Rome and the city-state (peace) and has 1000 gold; Greece is never met,
    /// so the diplomacy screen must not list it. Set up directly on the state, so a headless replay builds the same game.
    /// </summary>
    private static void SetUpDiplomacy(GameInfo game)
    {
        var human = game.GetCivilization(HumanA);
        foreach (var civ in game.Civilizations.Where(it => it.CivName != HumanA && it.CivName != HumanB && !it.IsBarbarian).OrderBy(it => it.CivName, StringComparer.Ordinal))
            human.DiplomacyFunctions.MakeCivilizationsMeet(civ);
        human.AddGold(1000 - human.Gold); // after the meetings: meeting a city-state pays a bonus
    }

    /// <summary>
    /// C013: the diplomacy game plus a founded capital, four notifications (a city, a tech, a tile the human cannot see, one without a place) and four pending
    /// alerts after the welcome alert: Rome's demand, Rome's friendship offer, the capital as a just-conquered city (blocking), a built wonder. Set up directly on the
    /// state, so a headless replay builds the same game.
    /// </summary>
    private static void SetUpAlerts(GameInfo game)
    {
        var human = game.GetCivilization(HumanA);
        var settler = human.Units.GetCivUnits().First(it => it.BaseUnit.IsCityFounder());
        var city = human.AddCity(settler.CurrentTile.Position, settler);
        var capital = city.GetCenterTile();
        var unseen = game.TileMap.Values.Where(it => !it.IsVisible(human)).OrderByDescending(it => it.AerialDistanceTo(capital)).ThenBy(it => it.Position.x).ThenBy(it => it.Position.y).First();
        human.AddNotification("Your city [" + city.Name + "] has been founded", capital.Position, NotificationCategory.Cities);
        human.AddNotification("You have discovered [Agriculture]!", new TechAction("Agriculture"), NotificationCategory.General);
        human.AddNotification("An enemy scout was seen far away", unseen.Position, NotificationCategory.War);
        human.AddNotification("[Rome] wishes to speak with you", NotificationCategory.Diplomacy);
        human.AddPopupAlert(new PopupAlert(AlertType.DemandToStopSpreadingReligion, Ai));
        human.AddPopupAlert(new PopupAlert(AlertType.DeclarationOfFriendship, Ai));
        human.AddPopupAlert(new PopupAlert(AlertType.CityConquered, city.Id));
        human.AddPopupAlert(new PopupAlert(AlertType.WonderBuilt, "Pyramids"));
    }

    /// <summary>
    /// C024: the solo game with Babylon's capital founded and every Rome unit gone, so Rome is defeated (it never owned a capital, and a civ without units is destroyed) and
    /// Babylon is the last major civ alive: ending the turn makes the engine record a Domination victory for Babylon. Set up directly on the state, so a headless replay builds the same game.
    /// </summary>
    private static void SetUpVictory(GameInfo game)
    {
        var human = game.GetCivilization(HumanA);
        var settler = human.Units.GetCivUnits().First(it => it.BaseUnit.IsCityFounder());
        var capital = human.AddCity(settler.CurrentTile.Position, settler);
        capital.CityConstructions.AddToQueue("Warrior"); // production and research already chosen, so ending the turn is not refused for a pending decision
        human.Tech.TechsToResearch.Add("Agriculture");
        foreach (var civ in game.Civilizations.Where(it => it.IsMajorCiv())) civ.StatsHistory.RecordRankingStats(civ); // the turn-0 record the game would hold, so the history graph has data
        foreach (var unit in game.GetCivilization(Ai).Units.GetCivUnits().ToList()) unit.Destroy();
    }

    /// <summary>C024: the solo game with every Babylon unit gone and no city: the human civ is destroyed while Rome plays on, so the game is not over but lost for the viewer.</summary>
    private static void SetUpDefeat(GameInfo game)
    {
        foreach (var unit in game.GetCivilization(HumanA).Units.GetCivUnits().ToList()) unit.Destroy();
    }

    /// <summary>
    /// C022: the diplomacy game plus a trade table. Trade hints are on (the screen shows the AI's likely answer), Rome has 500 gold and has sent the human a trade
    /// (100 gold for 30 gold) and a demand (stop spreading its religion). Set up directly on the state, so a headless replay builds the same game.
    /// </summary>
    private static void SetUpTrade(GameInfo game)
    {
        var human = game.GetCivilization(HumanA);
        var rome = game.GetCivilization(Ai);
        game.GameParameters.TradeHintsEnabled = true;
        rome.AddGold(500 - rome.Gold);
        var offer = new Unciv.Logic.Trade.Trade();
        offer.OurOffers.Add(new Unciv.Logic.Trade.TradeOffer(Constants.FlatGold, Unciv.Logic.Trade.TradeOfferType.Gold, 30, game.Speed));
        offer.TheirOffers.Add(new Unciv.Logic.Trade.TradeOffer(Constants.FlatGold, Unciv.Logic.Trade.TradeOfferType.Gold, 100, game.Speed));
        human.TradeRequests.Add(new Unciv.Logic.Trade.TradeRequest(rome.CivId, offer));
        human.AddPopupAlert(new Unciv.Logic.Civilization.PopupAlert(Unciv.Logic.Civilization.AlertType.DemandToStopSpreadingReligion, rome.CivId));
    }

    /// <summary>
    /// C053: the diplomacy game where the World Congress has voted: Babylon (capital founded, production and research chosen) has the result to see. Rome owns the United
    /// Nations; Babylon voted for Rome, Rome for Greece (a civ Babylon has not met) and Greece for Babylon. Set up directly on the state, so a headless replay builds the same game.
    /// </summary>
    private static void SetUpVoteResult(GameInfo game)
    {
        var human = game.GetCivilization(HumanA);
        var capital = FoundWithSettler(human);
        capital.CityConstructions.AddToQueue("Warrior");
        human.Tech.TechsToResearch.Add("Agriculture");
        var rome = game.GetCivilization(Ai);
        var greece = game.GetCivilization(HumanB);
        var romeCapital = FoundWithSettler(rome);
        var building = game.Ruleset.Buildings.Values.First(it => ((IHasUniques)it).HasUnique(UniqueType.OneTimeTriggerVoting, GameContext.IgnoreConditionalsState));
        romeCapital.CityConstructions.AddBuilding(building.Name);
        game.DiplomaticVictoryVotesCast[human.CivId] = rome.CivId;
        game.DiplomaticVictoryVotesCast[rome.CivId] = greece.CivId;
        game.DiplomaticVictoryVotesCast[greece.CivId] = human.CivId;
        human.AddFlag(nameof(Logic.Civilization.CivFlags.ShowDiplomaticVotingResults), 0);
    }

    /// <summary>
    /// C039: the diplomacy game where the human has also met Greece, Rome (at war with the human) has sent a peace proposal (the peace treaty on both sides) and Greece
    /// a trade (50 gold for 20 gold); both are rich enough to honour it. Set up directly on the state, so a headless replay builds the same game.
    /// </summary>
    private static void SetUpProposals(GameInfo game)
    {
        var human = game.GetCivilization(HumanA);
        var rome = game.GetCivilization(Ai);
        var greece = game.GetCivilization(HumanB);
        human.DiplomacyFunctions.MakeCivilizationsMeet(greece);
        human.AddGold(1000 - human.Gold);
        greece.AddGold(500 - greece.Gold);
        human.GetDiplomacyManagerOrMeet(rome).DeclareWar();
        var peace = new Unciv.Logic.Trade.Trade();
        peace.OurOffers.Add(new Unciv.Logic.Trade.TradeOffer(Constants.PeaceTreaty, Unciv.Logic.Trade.TradeOfferType.Treaty, game.Speed));
        peace.TheirOffers.Add(new Unciv.Logic.Trade.TradeOffer(Constants.PeaceTreaty, Unciv.Logic.Trade.TradeOfferType.Treaty, game.Speed));
        human.TradeRequests.Add(new Unciv.Logic.Trade.TradeRequest(rome.CivId, peace));
        var gift = new Unciv.Logic.Trade.Trade();
        gift.OurOffers.Add(new Unciv.Logic.Trade.TradeOffer(Constants.FlatGold, Unciv.Logic.Trade.TradeOfferType.Gold, 20, game.Speed));
        gift.TheirOffers.Add(new Unciv.Logic.Trade.TradeOffer(Constants.FlatGold, Unciv.Logic.Trade.TradeOfferType.Gold, 50, game.Speed));
        human.TradeRequests.Add(new Unciv.Logic.Trade.TradeRequest(greece.CivId, gift));
    }

    /// <summary>
    /// C038: Babylon founds its capital (production and research chosen) and has a free great person to choose, a free technology to pick, enough faith for a pantheon, and a
    /// warrior with enough experience to be promoted: every blocking picker is asked for in turn. Set up directly on the state, so a headless replay builds the same game.
    /// </summary>
    private static void SetUpDecisions(GameInfo game)
    {
        var human = game.GetCivilization(HumanA);
        var settler = human.Units.GetCivUnits().First(it => it.BaseUnit.IsCityFounder());
        var capital = human.AddCity(settler.CurrentTile.Position, settler);
        capital.CityConstructions.AddToQueue("Warrior");
        human.Tech.TechsToResearch.Add("Agriculture");
        human.GreatPeople.FreeGreatPeople = 1;
        human.Tech.FreeTechs = 1;
        human.ReligionManager.StoredFaith = human.ReligionManager.FaithForPantheon();
        human.Units.GetCivUnits().OrderBy(it => it.Id).First(it => it.BaseUnit.Name == "Warrior").Promotions.XP = 30;
    }

    /// <summary>
    /// C050: the diplomacy game with espionage switched on, Babylon's capital founded (production and research chosen), Rome's capital founded and the city-state's capital
    /// (if it has none) explored by Babylon, and two Babylon spies: one idle in the hideout, one set up (surveillance) in the city-state, so it can stage a coup. Set up
    /// directly on the state, so a headless replay builds the same game.
    /// </summary>
    private static void SetUpEspionage(GameInfo game)
    {
        game.GameParameters.EspionageEnabled = true;
        var human = game.GetCivilization(HumanA);
        var settler = human.Units.GetCivUnits().First(it => it.BaseUnit.IsCityFounder());
        var capital = human.AddCity(settler.CurrentTile.Position, settler);
        capital.CityConstructions.AddToQueue("Warrior");
        human.Tech.TechsToResearch.Add("Agriculture");
        var rome = game.GetCivilization(Ai);
        var romeCapital = FoundWithSettler(rome);
        var cityState = game.Civilizations.First(it => it.IsCityState);
        var cityStateCity = cityState.Cities.FirstOrDefault() ?? FoundWithSettler(cityState);
        romeCapital.GetCenterTile().SetExplored(human, true);
        cityStateCity.GetCenterTile().SetExplored(human, true);
        human.EspionageManager.AddSpy();
        var resident = human.EspionageManager.AddSpy();
        resident.MoveTo(cityStateCity);
        resident.SetAction(SpyAction.Surveillance);
    }

    /// <summary>
    /// C052: on top of the espionage game (G&amp;K, so there are beliefs): Babylon has a pantheon and a second city, a Great Prophet stands in each city (found a religion with
    /// one, enhance it with the other), the World Congress votes now (as when the United Nations was built and the countdown ran out), and two notifications open pickers: the
    /// warrior (30 experience) can be promoted, and a spy report opens the move-spies picker. Set up directly on the state, so a headless replay builds the same game.
    /// </summary>
    private static void SetUpReligion(GameInfo game)
    {
        var human = game.GetCivilization(HumanA);
        var capital = human.Cities[0];
        GivePantheon(human);
        foreach (var settler in human.Units.GetCivUnits().Where(it => it.BaseUnit.IsCityFounder()).ToList()) settler.Destroy(); // the capital is founded, so its tile is free for a prophet
        var site = game.TileMap.Values
            .Where(it => it.IsLand && !it.IsImpassible() && !it.IsCityCenter() && it.GetOwner() == null && it.AerialDistanceTo(capital.GetCenterTile()) >= 4)
            .OrderBy(it => it.Position.x).ThenBy(it => it.Position.y).First();
        var second = human.AddCity(site.Position);
        second.CityConstructions.AddToQueue("Warrior");
        human.Units.PlaceUnitNearTile(capital.Location, "Great Prophet");
        human.Units.PlaceUnitNearTile(second.Location, "Great Prophet");
        human.AddFlag(CivFlags.TurnsTillNextDiplomaticVote.ToString(), 0);
        var warrior = human.Units.GetCivUnits().OrderBy(it => it.Id).First(it => it.BaseUnit.Name == "Warrior");
        warrior.Promotions.XP = 30;
        human.AddNotification("[Warrior] can be promoted!", [new MapUnitAction(warrior.CurrentTile.Position, warrior.Id), new PromoteUnitAction(warrior.Name, warrior.CurrentTile.Position, warrior.Id)],
            NotificationCategory.Units, warrior.Name);
        human.AddNotification("Your spies are waiting for orders", new EspionageAction(), NotificationCategory.Espionage, NotificationIcon.Spy);
    }

    /// <summary>
    /// C052: Babylon (solo, G&amp;K) has founded a religion (a pantheon, then a Great Prophet used in the capital, a founder and a follower belief) and has one free follower belief
    /// to choose, as after a one-time free belief: the engine asks to reform the religion. The founding goes through the real commands on a throwaway session (its log is not
    /// the scenario's), so a headless replay builds the same game.
    /// </summary>
    private static void SetUpReform(GameInfo game)
    {
        var human = game.GetCivilization(HumanA);
        var settler = human.Units.GetCivUnits().First(it => it.BaseUnit.IsCityFounder());
        var capital = human.AddCity(settler.CurrentTile.Position, settler);
        settler.Destroy(); // the city is founded, so the tile is free for the prophet
        capital.CityConstructions.AddToQueue("Warrior");
        human.Tech.TechsToResearch.Add("Agriculture");
        GivePantheon(human);
        var prophet = human.Units.PlaceUnitNearTile(capital.Location, "Great Prophet").NotNull();
        human.ReligionManager.FoundReligion(prophet);
        prophet.Destroy(); // used up, as the unit action does
        var ruleset = game.Ruleset;
        var taken = new List<string>();
        foreach (var (type, count) in human.ReligionManager.GetBeliefsToChooseAtFounding())
            taken.AddRange(ruleset.Beliefs.Values.Where(it => it.Type == type && !game.Religions.Values.Any(r => r.HasBelief(it.Name))).Select(it => it.Name).Order(StringComparer.Ordinal).Take(count));
        string icon = ruleset.Religions.First(it => !game.Religions.ContainsKey(it));
        var result = new GameSession(game).Submit(HumanA, new Commands.Empire.FoundReligion { ReligionName = icon, DisplayName = icon, Beliefs = taken });
        if (!result.Accepted) throw new InvalidOperationException("Setting up the founded religion failed: " + result.Validation.Message);
        human.ReligionManager.FreeBeliefs.Add(BeliefType.Follower.ToString(), 1);
    }

    /// <summary>The first pantheon belief by name, taken as a free belief (no faith spent).</summary>
    private static void GivePantheon(Unciv.Logic.Civilization.Civilization human) =>
        human.ReligionManager.ChooseBeliefs([human.GameInfo.Ruleset.Beliefs.Values.Where(it => it.Type == BeliefType.Pantheon).OrderBy(it => it.Name, StringComparer.Ordinal).First()], useFreeBeliefs: true);

    private static CityObject FoundWithSettler(Logic.Civilization.Civilization civ)
    {
        var settler = civ.Units.GetCivUnits().First(it => it.BaseUnit.IsCityFounder());
        return civ.AddCity(settler.CurrentTile.Position, settler);
    }

    /// <summary>The 64-bit canonical hash of the whole game state (serialised form), as lowercase hex.</summary>
    public static string StateHash(GameSession session) =>
        StateHasher.ComputeHash(h => h.Add(UncivFiles.GameInfoToString(session.Game, forceZip: false))).ToString("x16", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The first city of a civ, in the engine's own order.</summary>
    public static CityObject FirstCity(GameInfo game, string civName) => game.GetCivilization(civName).Cities[0];
}
