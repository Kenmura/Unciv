using System.Text.Json.Serialization;
using Unciv.Json;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.Utils;

namespace Unciv.Models;

// Ported from core/src/com/unciv/models/Religion.kt (T057).
// - The GameInfo is held as its IGameInfoState seam (GameInfo is ported by T046, which retypes it).
// - founderBeliefs/followerBeliefs are java.util.HashSet<String>: JavaHashSet keeps their iteration order, which reaches the
//   order of the belief unique maps and of getBeliefs/getAllBeliefsOrdered.
// - Kotlin Sequences are returned as IEnumerable (lazy, like the Sequence).

/// <summary>Data object for Religions</summary>
public class Religion : INamed, IsPartOfGameInfoSerialization, IGameContextReligion
{
    private string? name;
    /// <summary>Kotlin <c>lateinit var</c>: reading before assignment throws.</summary>
    [JsonPropertyName("name")]
    public string Name
    {
        get => name ?? throw new InvalidOperationException("lateinit property name has not been initialized");
        set => name = value;
    }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }
    [JsonPropertyName("foundingTurn")]
    public int FoundingTurn { get; set; }

    [JsonPropertyName("founderBeliefs")]
    [JsonInclude]
    private JavaHashSet<string> founderBeliefs = new();
    [JsonPropertyName("followerBeliefs")]
    [JsonInclude]
    private JavaHashSet<string> followerBeliefs = new();

    /// <summary>T219: rebuild the belief sets like a load does, see <see cref="JavaHashMap{TKey,TValue}.Canonicalize"/>.</summary>
    internal void Canonicalize()
    {
        founderBeliefs.Canonicalize();
        followerBeliefs.Canonicalize();
    }

    /// <summary>Transient.</summary>
    [JsonIgnore]
    public UniqueMap FounderBeliefUniqueMap { get; private set; } = new();
    /// <summary>Transient.</summary>
    [JsonIgnore]
    public UniqueMap FollowerBeliefUniqueMap { get; private set; } = new();

    private IGameInfoState? gameInfo;
    /// <summary>Transient. Kotlin <c>lateinit var</c>: reading before assignment throws.</summary>
    [JsonIgnore]
    public IGameInfoState GameInfo
    {
        get => gameInfo ?? throw new InvalidOperationException("lateinit property gameInfo has not been initialized");
        set => gameInfo = value;
    }

    private string? foundingCivName;
    /// <summary>Kotlin <c>lateinit var</c> with a private setter: reading before assignment throws.</summary>
    [JsonPropertyName("foundingCivName")]
    [JsonInclude]
    public string FoundingCivName
    {
        get => foundingCivName ?? throw new InvalidOperationException("lateinit property foundingCivName has not been initialized");
        private set => foundingCivName = value;
    }

    private Civilization? foundingCivCache;
    /// <summary>Transient cache of <see cref="FoundingCiv"/>.</summary>
    [JsonIgnore]
    public Civilization FoundingCiv
    {
        get
        {
            foundingCivCache ??= GameInfo.GetCivilization(FoundingCivName);
            return foundingCivCache;
        }
    }

    private readonly KotlinLazy<List<string>> buildingsPurchasableByBeliefs;
    [JsonIgnore]
    public List<string> BuildingsPurchasableByBeliefs => buildingsPurchasableByBeliefs.Value;

    public Religion()
    {
        buildingsPurchasableByBeliefs = new KotlinLazy<List<string>>(UnlockedBuildingsPurchasable);
    }

    public Religion(string name, IGameInfoState gameInfo, Civilization foundingCiv, int foundingTurn) : this()
    {
        Name = name;
        FoundingTurn = foundingTurn;
        GameInfo = gameInfo;
        foundingCivCache = foundingCiv;
        FoundingCivName = foundingCiv.CivId;
    }

    public Religion(string name, IGameInfoState gameInfo, string foundingCivName, int foundingTurn) : this()
    {
        Name = name;
        FoundingTurn = foundingTurn;
        FoundingCivName = foundingCivName;
        GameInfo = gameInfo;
    }

    public Religion Clone()
    {
        var toReturn = new Religion(Name, GameInfo, FoundingCivName, FoundingTurn)
        {
            DisplayName = DisplayName,
        };
        foreach (var belief in founderBeliefs) toReturn.founderBeliefs.Add(belief);
        foreach (var belief in followerBeliefs) toReturn.followerBeliefs.Add(belief);
        return toReturn;
    }

    public void SetTransients(IGameInfoState gameInfo)
    {
        GameInfo = gameInfo;
        UpdateUniqueMaps();
    }

    private void UpdateUniqueMaps()
    {
        FollowerBeliefUniqueMap = new UniqueMap(MapToExistingBeliefs(followerBeliefs).SelectMany(it => it.UniqueObjects));
        FounderBeliefUniqueMap = new UniqueMap(MapToExistingBeliefs(founderBeliefs).SelectMany(it => it.UniqueObjects));
    }

    public void AddBeliefs(IEnumerable<Belief> beliefs)
    {
        foreach (var belief in beliefs)
        {
            switch (belief.Type)
            {
                case BeliefType.Founder:
                case BeliefType.Enhancer:
                case BeliefType.Reformation:
                    founderBeliefs.Add(belief.Name);
                    break;
                case BeliefType.Pantheon:
                case BeliefType.Follower:
                    followerBeliefs.Add(belief.Name);
                    break;
                default:
                    continue; // 'None' and 'Any' are not valid for beliefs, they're used for internal purposes
            }
        }
        UpdateUniqueMaps();
    }

    public void AddBelief(Belief belief) => AddBeliefs([belief]);

    public void AddBelief(string beliefName)
    {
        if (GameInfo.Ruleset.Beliefs.TryGetValue(beliefName, out var belief))
            AddBelief(belief);
    }

    public string GetIconName() => IsPantheon() ? "Pantheon" : Name;

    public string GetReligionDisplayName() => DisplayName ?? Name;

    private IEnumerable<Belief> MapToExistingBeliefs(IEnumerable<string> beliefs)
    {
        var rulesetBeliefs = GameInfo.Ruleset.Beliefs;
        return beliefs.Select(it => rulesetBeliefs.TryGetValue(it, out var belief) ? belief : null).OfType<Belief>();
    }

    public IEnumerable<Belief> GetBeliefs(BeliefType beliefType)
    {
        if (beliefType == BeliefType.Any)
        {
            // (founderBeliefs + followerBeliefs).toHashSet(): Set.plus is a LinkedHashSet, so the hash set gets the distinct elements
            var distinct = founderBeliefs.Concat(followerBeliefs).Distinct().ToList();
            return MapToExistingBeliefs(JavaHashSet<string>.ToHashSet(distinct));
        }

        JavaHashSet<string> beliefs;
        if (beliefType.IsFollower()) beliefs = followerBeliefs;
        else if (beliefType.IsFounder()) beliefs = founderBeliefs;
        else throw new NullReferenceException("null!! on a belief type that is neither follower nor founder"); // Kotlin: null!! // This is fine...

        return MapToExistingBeliefs(beliefs).Where(it => it.Type == beliefType);
    }

    public IEnumerable<Belief> GetAllBeliefsOrdered() =>
        MapToExistingBeliefs(followerBeliefs).Where(it => it.Type == BeliefType.Pantheon)
            .Concat(MapToExistingBeliefs(founderBeliefs).Where(it => it.Type == BeliefType.Founder))
            .Concat(MapToExistingBeliefs(followerBeliefs).Where(it => it.Type == BeliefType.Follower))
            .Concat(MapToExistingBeliefs(founderBeliefs).Where(it => it.Type == BeliefType.Enhancer))
            .Concat(MapToExistingBeliefs(founderBeliefs).Where(it => it.Type == BeliefType.Reformation));

    public bool HasBelief(string belief) => followerBeliefs.Contains(belief) || founderBeliefs.Contains(belief);

    public bool IsPantheon() => GetBeliefs(BeliefType.Pantheon).Any() && !IsMajorReligion();
    public bool IsMajorReligion() => GetBeliefs(BeliefType.Founder).Any();
    public bool IsEnhancedReligion() => GetBeliefs(BeliefType.Enhancer).Any();

    public bool MatchesFilter(string filter, GameContext? state = null, Civilization? civ = null)
    {
        var gameContext = state ?? GameContext.IgnoreConditionalsState;
        return MultiFilter.Filter(filter, it => MatchesSingleFilter(it, gameContext, civ));
    }

    bool IGameContextReligion.MatchesFilter(string filter, GameContext state, IGameContextCivilization? civ) =>
        // The only implementer of the civilization seam in the engine is Civilization (the same assumption Civilization.cs makes for its state)
        MatchesFilter(filter, state, (Civilization?)civ);

    private bool MatchesSingleFilter(string filter, GameContext state, Civilization? civ)
    {
        var foundingCiv = FoundingCiv;
        switch (filter)
        {
            case "any": return true;
            case "major": return IsMajorReligion();
            case "enhanced": return IsEnhancedReligion();
            case "your": return ReferenceEquals(civ, foundingCiv);
            case "foreign": return civ != null && !ReferenceEquals(civ, foundingCiv);
            case "enemy":
                if (civ == null) return false;
                return civ.Knows(foundingCiv) && civ.IsAtWarWith(foundingCiv);
            default:
                if (filter == Name) return true;
                if (GetBeliefs(BeliefType.Any).Select(it => it.Name).Contains(filter)) return true;
                if (FounderBeliefUniqueMap.HasMatchingTagUnique(filter, state)) return true;
                if (FollowerBeliefUniqueMap.HasMatchingTagUnique(filter, state)) return true;
                return false;
        }
    }

    private List<string> UnlockedBuildingsPurchasable() =>
        GetAllBeliefsOrdered().SelectMany(belief =>
            ((IHasUniques)belief).GetMatchingUniques(UniqueType.BuyBuildingsWithStat).Select(it => it.Params[0])
                .Concat(((IHasUniques)belief).GetMatchingUniques(UniqueType.BuyBuildingsForAmountStat).Select(it => it.Params[0]))
                .Concat(((IHasUniques)belief).GetMatchingUniques(UniqueType.BuyBuildingsIncreasingCost).Select(it => it.Params[0])))
            .Where(it => GameInfo.Ruleset.Buildings.ContainsKey(it)).ToList();
}
