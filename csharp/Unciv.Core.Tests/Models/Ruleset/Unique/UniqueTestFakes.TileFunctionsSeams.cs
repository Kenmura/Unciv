using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.Models.Ruleset.Unique;

// The members TileStatFunctions, TileImprovementFunctions and ImprovementFunctions (T054) read from the game-state interfaces;
// tests override what they use. The unique lookups run over the fakes' own unique lists.

internal partial class FakeGameInfo
{
    public int ObsoleteQuestCalls { get; private set; }
    public virtual void HandleObsoleteGlobalQuestsOfAliveCityStates() => ObsoleteQuestCalls++;
}

internal partial class FakeCiv
{
    public virtual void ForEachMatchingUnique(UniqueType uniqueType, GameContext gameContext, Action<Unciv.Models.Ruleset.Unique.Unique> action)
    {
        foreach (var unique in CivUniques.Where(it => it.Type == uniqueType).ToList()) action(unique);
    }
    public virtual bool IsDefeated() => false;
    public Dictionary<HexCoord, string?> LastSeenImprovements { get; } = [];
    public virtual void SetLastSeenImprovement(HexCoord position, string? improvementName) => LastSeenImprovements[position] = improvementName;
    public virtual IEnumerable<TileImprovement> UniqueImprovements => [];
    public virtual IEnumerable<Unciv.Models.Ruleset.Unique.Unique> GetTriggeredUniques(UniqueType trigger, GameContext gameContext, Func<Unciv.Models.Ruleset.Unique.Unique, bool>? triggerFilter = null) => [];
    public virtual float GetGameProgressModifier(Unciv.Models.Ruleset.Unique.Unique unique) => 1f;
    public List<(string Text, Notification.NotificationCategory Category)> Notifications { get; } = [];
    public virtual void AddNotification(string text, IEnumerable<NotificationAction> actions, Notification.NotificationCategory category, params string[] notificationIcons) =>
        Notifications.Add((text, category));
}

internal partial class FakeCity
{
    public virtual void ForEachMatchingUnique(UniqueType uniqueType, GameContext? gameContext, Action<Unciv.Models.Ruleset.Unique.Unique> action)
    {
        foreach (var unique in LocalUniques.Where(it => it.Type == uniqueType).ToList()) action(unique);
    }
    public virtual TileClass GetCenterTile() => throw new NotSupportedException();
    public int CityStatsUpdates { get; private set; }
    public virtual void UpdateCityStats() => CityStatsUpdates++;
    public Dictionary<Stat, int> AddedStats { get; } = [];
    public virtual void AddStat(Stat stat, int amount) => AddedStats[stat] = AddedStats.GetValueOrDefault(stat) + amount;
    public virtual void RemoveCreateOneImprovementConstruction(string improvement) => throw new NotSupportedException();
}

internal partial class FakeUnit
{
    public virtual IEnumerable<Unciv.Models.Ruleset.Unique.Unique> GetTriggeredUniques(UniqueType trigger, GameContext gameContext, Func<Unciv.Models.Ruleset.Unique.Unique, bool>? triggerFilter = null) => [];
    public virtual void ClearPathfindingCache() { }
}
