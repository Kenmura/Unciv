using Unciv.Commands;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
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
    public static GameSession Create(long gameSeed = GameSeed, int mapSeed = MapSeed, bool solo = false, bool godMode = false, bool skirmish = false, bool diplomacy = false, bool rich = false, bool policies = false, bool alerts = false)
    {
        diplomacy |= alerts;
        solo |= skirmish | diplomacy | policies;
        var ruleset = LoadVanilla();
        var game = GameStarter.StartNewGame(
            new GameSetupInfo(
                new GameParameters
                {
                    BaseRuleset = ruleset.Name,
                    NumberOfCityStates = diplomacy ? 1 : 0,
                    GodMode = godMode,
                    VictoryTypes = ruleset.Victories.Keys.ToList(),
                    Players = solo
                        ? diplomacy
                            ? [new Player(HumanA, PlayerType.Human), new Player(Ai), new Player(HumanB)]
                            : [new Player(HumanA, PlayerType.Human), new Player(Ai)]
                        : [new Player(HumanA, PlayerType.Human), new Player(HumanB, PlayerType.Human), new Player(Ai)],
                },
                new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = mapSeed, MapSize = MapSize.Tiny }),
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
        return new GameSession(game);
    }

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

    /// <summary>The 64-bit canonical hash of the whole game state (serialised form), as lowercase hex.</summary>
    public static string StateHash(GameSession session) =>
        StateHasher.ComputeHash(h => h.Add(UncivFiles.GameInfoToString(session.Game, forceZip: false))).ToString("x16", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The first city of a civ, in the engine's own order.</summary>
    public static CityObject FirstCity(GameInfo game, string civName) => game.GetCivilization(civName).Cities[0];
}
