using System.Text.Json.Serialization;
using Unciv.Json;
using Unciv.Logic.Map.MapUnits;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;

namespace Unciv.Logic.Civilization.Managers;

// Ported from core/src/com/unciv/logic/civilization/managers/RuinsManager.kt (T057).
// The triggering unit is typed by the MapUnit seam, which is the MapUnit.
public class RuinsManager : IsPartOfGameInfoSerialization
{
    [JsonPropertyName("lastChosenRewards")]
    [JsonInclude]
    private List<string> lastChosenRewards;

    public RuinsManager(List<string>? lastChosenRewards = null)
    {
        this.lastChosenRewards = lastChosenRewards ?? ["", ""];
    }

    private Civilization? civInfo;
    /// <summary>Transient. Kotlin <c>lateinit var</c>: reading before assignment throws.</summary>
    [JsonIgnore]
    public Civilization CivInfo
    {
        get => civInfo ?? throw new InvalidOperationException("lateinit property civInfo has not been initialized");
        set => civInfo = value;
    }

    private IEnumerable<RuinReward>? validRewards;
    /// <summary>Transient. Kotlin <c>lateinit var</c>: reading before assignment throws.</summary>
    [JsonIgnore]
    public IEnumerable<RuinReward> ValidRewards
    {
        get => validRewards ?? throw new InvalidOperationException("lateinit property validRewards has not been initialized");
        set => validRewards = value;
    }

    /// <summary>needs to deep-clone (the List, not the Strings) so undo works</summary>
    public RuinsManager Clone() => new(new List<string>(lastChosenRewards));

    public void SetTransients(Civilization civInfo)
    {
        CivInfo = civInfo;
        ValidRewards = civInfo.GameInfo.Ruleset.RuinRewards.Values;
    }

    private void RememberReward(string reward)
    {
        lastChosenRewards[0] = lastChosenRewards[1];
        lastChosenRewards[1] = reward;
    }

    private List<RuinReward> GetShuffledPossibleRewards(MapUnit triggeringUnit)
    {
        var candidates =
            ValidRewards.Where(it => IsPossibleReward(it, triggeringUnit))
            // This might be a dirty way to do this, but it works (we do have randomWeighted in CollectionExtensions, but below we
            // need to choose another when the first choice's TriggerActivations report failure, and that's simpler this way)
            // For each possible reward, this feeds (reward.weight) copies of this reward to the overall Sequence to implement 'weight'.
            .SelectMany(reward => Enumerable.Repeat(reward, reward.Weight))
            // Convert to List since Sequence.shuffled would do one anyway, Mutable so shuffle doesn't need to pull a copy
            .ToList();
        // The resulting List now gets shuffled, using a tile-based random to thwart save-scumming.
        // Note both Sequence.shuffled and Iterable.shuffled (with a 'd') always pull an extra copy of a MutableList internally, even if you feed them one.
        candidates.Shuffle(triggeringUnit.Civ.GameInfo.SeededRandom(triggeringUnit.GetTile().Position.ToVector2().GetHashCode()));
        return candidates;
    }

    private bool IsPossibleReward(RuinReward ruinReward, MapUnit unit)
    {
        if (lastChosenRewards.Contains(ruinReward.Name)) return false;
        if (ruinReward.IsUnavailableBySettings(CivInfo.GameInfo)) return false;
        var gameContext = new GameContext(CivInfo, unit: unit, tile: unit.GetTile());
        var rewardUniques = (IHasUniques)ruinReward;
        if (rewardUniques.HasUnique(UniqueType.Unavailable, gameContext)) return false;
        if (rewardUniques.GetMatchingUniques(UniqueType.OnlyAvailable, GameContext.IgnoreConditionalsState)
            .Any(it => !it.ConditionalsApply(gameContext))) return false;
        return true;
    }

    public void SelectNextRuinsReward(MapUnit triggeringUnit)
    {
        foreach (var possibleReward in GetShuffledPossibleRewards(triggeringUnit))
        {
            bool atLeastOneUniqueHadEffect = false;
            foreach (var unique in possibleReward.UniqueObjects)
            {
                if (!unique.ConditionalsApply(triggeringUnit.Cache.State)) continue;
                bool uniqueTriggered = false;
                int repeats = unique.GetUniqueMultiplier(triggeringUnit.Cache.State);
                for (int i = 0; i < repeats; i++)
                {
                    uniqueTriggered =
                        UniqueTriggerActivation.TriggerUnique(
                            unique,
                            triggeringUnit,
                            notification: possibleReward.Notification,
                            triggerNotificationText: "from the ruins"
                        ) || uniqueTriggered;
                }
                atLeastOneUniqueHadEffect = atLeastOneUniqueHadEffect || uniqueTriggered;
            }
            if (atLeastOneUniqueHadEffect)
            {
                RememberReward(possibleReward.Name);
                break;
            }
        }
    }
}
