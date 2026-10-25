using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.StatTypes;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

// T054 (TileStatFunctions, TileImprovementFunctions, ImprovementFunctions): the members they call on Civilization, City, MapUnit and
// GameInfo, as extensions of the GameContext interfaces (GameContext.cs), which those classes implement. Member names mirror the Kotlin members.

namespace Unciv.Models.Ruleset.Unique;

public partial interface IGameContextGameInfo
{
    /// <summary><c>getAliveCityStates().forEach { it.questManager.handleObsoleteGlobalQuests() }</c></summary>
    void HandleObsoleteGlobalQuestsOfAliveCityStates();
}

public partial interface IGameContextCivilization
{
    /// <summary><c>civ.forEachMatchingUnique(uniqueType, gameContext, action)</c></summary>
    void ForEachMatchingUnique(UniqueType uniqueType, GameContext gameContext, Action<Unique> action);
    bool IsDefeated();
    void SetLastSeenImprovement(HexCoord position, string? improvementName);
    /// <summary><c>civ.cache.uniqueImprovements</c></summary>
    IEnumerable<TileImprovement> UniqueImprovements { get; }
    /// <summary><c>civ.getTriggeredUniques(trigger, gameContext, triggerFilter)</c></summary>
    IEnumerable<Unique> GetTriggeredUniques(UniqueType trigger, GameContext gameContext, Func<Unique, bool>? triggerFilter = null);
    /// <summary><c>unique.getGameProgressModifier(civ)</c></summary>
    float GetGameProgressModifier(Unique unique);
    /// <summary><c>civ.addNotification(text, locations, category, *icons)</c>, the locations being <c>LocationAction(vararg)</c>.</summary>
    void AddNotification(string text, IEnumerable<NotificationAction> actions, Notification.NotificationCategory category, params string[] notificationIcons);
}

public partial interface IGameContextCity
{
    /// <summary><c>city.forEachMatchingUnique(uniqueType, gameContext, action)</c>, including the civ's uniques; <c>null</c> is the city's own state.</summary>
    void ForEachMatchingUnique(UniqueType uniqueType, GameContext? gameContext, Action<Unique> action);
    /// <summary><c>city.getCenterTile()</c></summary>
    TileClass GetCenterTile();
    /// <summary><c>city.cityStats.update()</c></summary>
    void UpdateCityStats();
    /// <summary><c>city.addStat(stat, amount)</c></summary>
    void AddStat(Stat stat, int amount);
    /// <summary><c>city.cityConstructions.removeCreateOneImprovementConstruction(improvement)</c></summary>
    void RemoveCreateOneImprovementConstruction(string improvement);
}

public partial interface IGameContextUnit
{
    /// <summary><c>unit.getTriggeredUniques(trigger, gameContext, triggerFilter)</c></summary>
    IEnumerable<Unique> GetTriggeredUniques(UniqueType trigger, GameContext gameContext, Func<Unique, bool>? triggerFilter = null);
    /// <summary><c>unit.movement.clearPathfindingCache()</c></summary>
    void ClearPathfindingCache();
}
