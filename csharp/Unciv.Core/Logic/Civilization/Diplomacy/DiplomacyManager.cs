using System.Text.Json.Serialization;
using Unciv.Json;
using Unciv.Logic.Trade;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Translations;
using Unciv.UI.Components.Extensions;
using Unciv.Utils;
using Level = Unciv.Logic.Civilization.Diplomacy.RelationshipLevel;
using Modifier = Unciv.Logic.Civilization.Diplomacy.DiplomaticModifiers;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;
using TradeObject = Unciv.Logic.Trade.Trade;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Logic.Civilization.Diplomacy;

// core/src/com/unciv/logic/civilization/diplomacy/DiplomacyManager.kt (the enums are in their own files)
//
// Deviations / deferred (each names its ticket):
// - UniqueTriggerActivation.triggerUnique (TriggerUponSigningPeace / DeclaringFriendship / SigningDefensivePact) is wired through
//   TriggerUnique() below.
// - makePeaceOneSide builds a plain Trade where Kotlin builds `TradeLogic(thirdCiv, otherCiv).currentTrade`: TradeLogic's constructor
//   only computes the available offers and has no side effect, so the Trade is the same (TradeLogic: T086).
// - recieveGoldGifts / getGoldInflation: TradeEvaluation().getGoldInflation is TradeEvaluation.Default.GetGoldInflation (T089).
// - Kotlin HashMaps (flagsCountdown, diplomaticModifiers) are JavaHashMaps: opinionOfOtherCiv sums the values in HashMap order.

public class DiplomacyManager : IsPartOfGameInfoSerialization
{
    /// <summary>The value city-state influence can't go below</summary>
    public const float MinimumInfluence = -60f;

    /// <summary>
    /// Smoothing period used in denounce automation, before adjusting for game speed etc.
    /// Higher values make denunciations more likely.
    /// </summary>
    public const float EmaPeriod = 30f;

    /// <summary>In old savefiles, if we have already met a civ, but the smoothed opinion value has not been set, apply a very low initial value to prevent sudden mass denunciations</summary>
    public const float OldSavefileInitialSmoothedOpinion = -250f;

    /// <summary>How upset should the AI be when they are denounced for the first time?</summary>
    public const float InitialOpinionChangeWhenDenounced = -35f;

    private Civilization? civInfo;
    /// <summary>Transient. Kotlin <c>lateinit var</c>: reading before assignment throws.</summary>
    [JsonIgnore]
    public Civilization CivInfo
    {
        get => civInfo ?? throw new InvalidOperationException("lateinit property civInfo has not been initialized");
        set => civInfo = value;
    }

    private string? otherCivName;
    /// <summary>Kotlin <c>lateinit var</c> with a private setter: reading before assignment throws.</summary>
    [JsonPropertyName("otherCivName")]
    [JsonInclude]
    public string OtherCivName
    {
        get => otherCivName ?? throw new InvalidOperationException("lateinit property otherCivName has not been initialized");
        private set => otherCivName = value;
    }

    private Civilization? otherCivCache;
    [JsonIgnore]
    public Civilization OtherCiv => otherCivCache ??= (Civilization)CivInfo.GameInfo.GetCivilization(OtherCivName);

    private GameContext? context;
    [JsonIgnore]
    public GameContext State => context ??= GameContext.ForCiv(CivInfo, OtherCiv);

    // since this needs to be checked a lot during travel, putting it in a transient is a good performance booster
    /// <summary>Transient. Can civInfo enter otherCivInfo's tiles?</summary>
    [JsonIgnore]
    public bool HasOpenBorders { get; set; }

    [JsonPropertyName("trades")]
    public List<TradeObject> Trades { get; set; } = [];

    [JsonPropertyName("diplomaticStatus")]
    public DiplomaticStatus DiplomaticStatus { get; set; } = DiplomaticStatus.War;

    /// <summary>
    /// Contains various flags (declared war, promised to not settle, declined luxury trade) and the number of turns in which they will expire.
    /// The JSON serialize/deserialize REFUSES to deserialize hashmap keys as Enums, so I'm forced to use strings instead =(
    /// This is so sad Alexa play Despacito
    /// </summary>
    [JsonPropertyName("flagsCountdown")]
    [JsonInclude]
    internal JavaHashMap<string, int> FlagsCountdown { get; set; } = new();

    /// <summary>
    /// For AI. Positive is good relations, negative is bad.
    /// Baseline is 1 point for each turn of peace - so declaring a war upends 40 years of peace, and e.g. capturing a city can be another 30 or 40.
    /// As for why it's String and not DiplomaticModifier see FlagsCountdown comment
    /// </summary>
    [JsonPropertyName("diplomaticModifiers")]
    public JavaHashMap<string, float> DiplomaticModifiers { get; set; } = new();

    /// <summary>
    /// For city-states. Influence is saved in the CITY STATE -> major civ Diplomacy, NOT in the major civ -> city state diplomacy.
    /// Access via getInfluence() and setInfluence() unless you know what you're doing.
    /// Note that not using the setter skips recalculating the ally and bounds checks,
    /// and skipping the getter bypasses the modified value when at war
    /// </summary>
    [JsonPropertyName("influence")]
    [JsonInclude]
    internal float Influence { get; set; }

    /// <summary>Total of each turn Science during Research Agreement</summary>
    [JsonPropertyName("totalOfScienceDuringRA")]
    [JsonInclude]
    internal int TotalOfScienceDuringRA { get; set; }

    /// <summary>
    /// How quickly do we forget past relationships?
    ///
    /// Used in AI denounce logic (values updated for all major civs, including humans, incase player resigns).
    ///
    /// A period of 1 is equivalent to only remembering our opinion from the last turn - very sudden drop in opinion is required to denounce.
    ///
    /// A period of >1 means we keep some memory of opinions from previous turns.
    ///
    /// If the old average is 10, new value is 25, and period is 3, then the new average will be 15, because (25-10)/3 = +5
    ///
    /// (ema = exponential moving average)
    /// </summary>
    private float SmoothedOpinionEmaPeriod() => EmaPeriod * CivInfo.GameInfo.Speed.Modifier; // setting all this to 1 is useful for debugging

    /// <summary>The smoothed opinion perpetually converges with the actual opinion, updated each turn</summary>
    [JsonPropertyName("smoothedOpinionOfOtherCiv")]
    [JsonInclude]
    public float SmoothedOpinionOfOtherCiv { get; private set; } = OldSavefileInitialSmoothedOpinion;

    /// <summary>Needed as a cache because the opinion value is saved right before the automation logic runs - but we want to use the one from the previous turn</summary>
    [JsonPropertyName("cachedSmoothedOpinionOfOtherCiv")]
    [JsonInclude]
    private float cachedSmoothedOpinionOfOtherCiv = OldSavefileInitialSmoothedOpinion;

    /// <summary>Smoothing formula = (newValue + oldAverage * (period - 1)) / period</summary>
    internal void SaveSmoothedOpinionOfOtherCiv()
    {
        SmoothedOpinionOfOtherCiv = cachedSmoothedOpinionOfOtherCiv;
        cachedSmoothedOpinionOfOtherCiv = (OpinionOfOtherCiv() + SmoothedOpinionOfOtherCiv * (SmoothedOpinionEmaPeriod() - 1)) / SmoothedOpinionEmaPeriod();
    }

    /// <summary>How much has our opinion of the other civ changed recently?</summary>
    public float SmoothedOpinionDelta() => OpinionOfOtherCiv() - SmoothedOpinionOfOtherCiv;

    public DiplomacyManager Clone()
    {
        var toReturn = new DiplomacyManager
        {
            OtherCivName = OtherCivName,
            DiplomaticStatus = DiplomaticStatus,
            Influence = Influence,
            TotalOfScienceDuringRA = TotalOfScienceDuringRA,
            SmoothedOpinionOfOtherCiv = SmoothedOpinionOfOtherCiv,
            cachedSmoothedOpinionOfOtherCiv = cachedSmoothedOpinionOfOtherCiv,
        };
        toReturn.Trades.AddRange(Trades.Select(it => it.Clone()));
        toReturn.FlagsCountdown.PutAll(FlagsCountdown);
        toReturn.DiplomaticModifiers.PutAll(DiplomaticModifiers);
        return toReturn;
    }

    /// <summary>For json serialization.</summary>
    public DiplomacyManager()
    {
    }

    /// <summary>Called when we meet a new civ.</summary>
    public DiplomacyManager(Civilization civilization, string otherCivName)
    {
        CivInfo = civilization;
        OtherCivName = otherCivName;
        UpdateHasOpenBorders();
        SmoothedOpinionOfOtherCiv = 0f;
        cachedSmoothedOpinionOfOtherCiv = 0f;
    }

    public DiplomacyManager(Civilization civilization, Civilization otherCiv)
    {
        CivInfo = civilization;
        otherCivCache = otherCiv;
        OtherCivName = otherCiv.CivId;
        UpdateHasOpenBorders();
        SmoothedOpinionOfOtherCiv = 0f;
        cachedSmoothedOpinionOfOtherCiv = 0f;
    }

    private static int ParseInt(string text) => int.Parse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture);

    private static float ParseFloat(string text) => float.Parse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);

    #region pure functions
    public DiplomacyManager OtherCivDiplomacy() => OtherCiv.GetDiplomacyManager(CivInfo).NotNull();

    public int TurnsToPeaceTreaty()
    {
        foreach (var trade in Trades)
            foreach (var offer in trade.OurOffers)
                if (offer.Name == Constants.PeaceTreaty && offer.Duration > 0) return offer.Duration;
        return 0;
    }

    /// <summary>
    /// Reflects how we perceive the other civ, and affects willingness to trade, declare war, etc..
    /// The other civ can view this number in the diplimacy screen.
    /// </summary>
    public float OpinionOfOtherCiv()
    {
        float modifierSum = 0f; // Iterable<Float>.sum(): a float accumulator in iteration order
        foreach (var value in DiplomaticModifiers.Values) modifierSum += value;
        // Angry about attacked CS and destroyed CS do not stack
        if (HasModifier(Modifier.DestroyedProtectedMinor) && HasModifier(Modifier.AttackedProtectedMinor))
            modifierSum -= GetModifier(Modifier.AttackedProtectedMinor);
        return modifierSum;
    }

    /// <summary>
    /// Related to <see cref="RelationshipLevel"/>, this compares with a specific outcome.
    ///
    /// It is cheap unless you ask such that Neutral / Afraid on a CityState need to be distinguished and influence is currently in 0 until 30.
    /// Thus it can be far cheaper than first retrieving <see cref="RelationshipLevel"/> and then comparing.
    ///
    /// Readability shortcuts: <see cref="IsRelationshipLevelEQ"/>, <see cref="IsRelationshipLevelGE"/>, <see cref="IsRelationshipLevelGT"/>, <see cref="IsRelationshipLevelLE"/>, <see cref="IsRelationshipLevelLT"/>
    /// </summary>
    /// <param name="level">the level to compare with</param>
    /// <param name="comparesAs">same as <c>RelationshipLevel.compareTo</c>'s sign</param>
    /// <returns><c>true</c> if <c>relationshipLevel().compareTo(level) == comparesAs</c> - or: when <paramref name="comparesAs"/> &gt; 0 only if relationshipLevel &gt; <paramref name="level"/> and so on.</returns>
    private bool CompareRelationshipLevel(RelationshipLevel level, int comparesAs)
    {
        if (!CivInfo.IsCityState)
            return Math.Sign((int)RelationshipLevel() - (int)level) == comparesAs;
        switch (level)
        {
            case Level.Afraid:
                if (comparesAs < 0) return GetInfluence() < 0;
                if (comparesAs > 0) return GetInfluence() >= 30 || RelationshipLevel() > level;
                return IsInfluenceBelowThirty() && RelationshipLevel() == level;
            case Level.Neutral:
                if (comparesAs < 0) return GetInfluence() < 0 || RelationshipLevel() < level;
                if (comparesAs > 0) return GetInfluence() >= 30;
                return IsInfluenceBelowThirty() && RelationshipLevel() == level;
            default:
                // Outside the potentially expensive questions, do it the easy way
                // Except - Enum.compareTo does not behave quite like other compareTo's
                // or like kotlinlang.org says, thus the `sign`
                return Math.Sign((int)RelationshipLevel() - (int)level) == comparesAs;
        }
    }

    private bool IsInfluenceBelowThirty()
    {
        float influence = GetInfluence();
        return influence >= 0 && influence < 30;
    }

    /// <seealso cref="CompareRelationshipLevel"/>
    public bool IsRelationshipLevelEQ(RelationshipLevel level) => CompareRelationshipLevel(level, 0);
    /// <seealso cref="CompareRelationshipLevel"/>
    public bool IsRelationshipLevelLT(RelationshipLevel level) => CompareRelationshipLevel(level, -1);
    /// <seealso cref="CompareRelationshipLevel"/>
    public bool IsRelationshipLevelGT(RelationshipLevel level) => CompareRelationshipLevel(level, 1);
    /// <seealso cref="CompareRelationshipLevel"/>
    public bool IsRelationshipLevelLE(RelationshipLevel level) =>
        level == Level.Ally || CompareRelationshipLevel(level.Plus(1), -1);
    /// <seealso cref="CompareRelationshipLevel"/>
    public bool IsRelationshipLevelGE(RelationshipLevel level) =>
        level == Level.Unforgivable || CompareRelationshipLevel(level.Plus(-1), 1);

    /// <summary>
    /// Careful: Cheap unless this is a CityState and influence is in 0 until 30,
    /// where the distinction Neutral/Afraid gets expensive.
    /// </summary>
    /// <seealso cref="CompareRelationshipLevel"/>
    /// <seealso cref="RelationshipIgnoreAfraid"/>
    public RelationshipLevel RelationshipLevel()
    {
        var level = RelationshipIgnoreAfraid();
        if (level != Level.Neutral || !CivInfo.IsCityState) return level;
        return CivInfo.CityStateFunctions.GetTributeWillingness(OtherCiv) > 0 ? Level.Afraid : Level.Neutral;
    }

    /// <summary>Same as <see cref="RelationshipLevel"/> but omits the distinction Neutral/Afraid, which can be _much_ cheaper</summary>
    public RelationshipLevel RelationshipIgnoreAfraid()
    {
        if (CivInfo.IsHuman() && OtherCiv.IsHuman())
            return Level.Neutral; // People make their own choices.

        if (CivInfo.IsHuman())
            return OtherCivDiplomacy().RelationshipLevel();

        if (CivInfo.IsCityState)
        {
            float influence = GetInfluence(); // getInfluence tests isAtWarWith
            if (influence <= -30) return Level.Unforgivable;
            if (influence < 0) return Level.Enemy;
            if (influence >= 60 && CivInfo.AllyCiv == OtherCiv) return Level.Ally;
            if (influence >= 30) return Level.Friend;
            return Level.Neutral;
        }

        // not entirely sure what to do between AI civs, because they probably have different views of each other,
        // maybe we need to average their views of each other? That makes sense to me.

        float opinion = OpinionOfOtherCiv();
        if (opinion <= -80) return Level.Unforgivable;
        if (opinion <= -40 || CivInfo.IsAtWarWith(OtherCiv)) return Level.Enemy; /* During wartime, the estimation in which you are held may be enemy OR unforgivable */
        if (opinion <= -15) return Level.Competitor;

        if (opinion >= 80) return Level.Ally;
        if (opinion >= 40) return Level.Friend;
        if (opinion >= 15) return Level.Favorable;
        return Level.Neutral;
    }

    private bool BelievesSameReligion()
    {
        // what is the majorityReligion of civInfo? If it is null, we immediately return false
        var civMajorityReligion = CivInfo.ReligionManager.GetMajorityReligion();
        if (civMajorityReligion == null) return false;
        // if not yet returned false from previous line, return the Boolean isMajorityReligionForCiv
        // true if majorityReligion of civInfo is also majorityReligion of otherCiv, false otherwise
        return OtherCiv.ReligionManager.IsMajorityReligionForCiv(civMajorityReligion);
    }

    /// <summary>Returns the number of turns to degrade from Ally or from Friend</summary>
    public int GetTurnsToRelationshipChange()
    {
        if (OtherCiv.IsCityState)
            return OtherCivDiplomacy().GetTurnsToRelationshipChange();

        if (CivInfo.IsCityState && !OtherCiv.IsCityState)
        {
            float dropPerTurn = GetCityStateInfluenceDegrade();
            if (dropPerTurn == 0f) return 0;
            if (IsRelationshipLevelEQ(Level.Ally)) return KotlinMath.FloatToInt(MathF.Ceiling((GetInfluence() - 60f) / dropPerTurn)) + 1;
            if (IsRelationshipLevelEQ(Level.Friend)) return KotlinMath.FloatToInt(MathF.Ceiling((GetInfluence() - 30f) / dropPerTurn)) + 1;
            return 0;
        }
        return 0;
    }

    public void AddInfluence(float amount) => SetInfluence(Influence + amount);

    /// <summary>Reduces the influence to zero, or if they have negative influence does nothing</summary>
    /// <param name="amount">A positive value to subtract from the influecne</param>
    public void ReduceInfluence(float amount)
    {
        if (Influence <= 0) return;
        float reduced = Influence - amount;
        Influence = reduced < 0f ? 0f : reduced; // coerceAtLeast(0f)
    }

    public void SetInfluence(float amount)
    {
        Influence = Math.Max(amount, MinimumInfluence);
        CivInfo.CityStateFunctions.UpdateAllyCivForCityState();
    }

    /// <summary>Kotlin <c>@VisibleForTesting</c>.</summary>
    public void SetInfluenceWithoutSideEffects(float amount) => Influence = amount;

    public float GetInfluence() => CivInfo.IsAtWarWith(OtherCiv) ? MinimumInfluence : Influence;

    // To be run from City-State DiplomacyManager, which holds the influence. Resting point for every major civ can be different.
    internal float GetCityStateInfluenceRestingPoint()
    {
        // B021: the anchors are ModConstants (BNW 0 / +5 protected / -20 wary of; G&K 0 / +10 / -20).
        var constants = CivInfo.GameInfo.Ruleset.ModOptions.Constants;
        float restingPoint = constants.MinorFriendshipAnchorDefault;

        OtherCiv.ForEachMatchingUnique(UniqueType.CityStateRestingPoint, unique =>
        {
            restingPoint += ParseInt(unique.Params[0]);
        });

        if (CivInfo.Cities.Count != 0 && CivInfo.GetCapital() != null)
            OtherCiv.ForEachMatchingUnique(UniqueType.RestingPointOfCityStatesFollowingReligionChange, unique =>
            {
                if (OtherCiv.ReligionManager.Religion?.Name == CivInfo.GetCapital().NotNull().Religion.GetMajorityReligionName())
                    restingPoint += ParseInt(unique.Params[0]);
            });

        if (DiplomaticStatus == DiplomaticStatus.Protector) restingPoint += constants.MinorFriendshipAnchorModProtected;

        if (HasFlag(DiplomacyFlags.WaryOf)) restingPoint += constants.MinorFriendshipAnchorModWaryOf;

        return restingPoint;
    }

    internal float GetCityStateInfluenceDegrade()
    {
        if (GetInfluence() <= GetCityStateInfluenceRestingPoint())
            return 0f;

        float decrement =
            CivInfo.CityStatePersonality == CityStatePersonality.Hostile ? 1.5f
            : OtherCiv.IsMinorCivAggressor() ? 2f
            : 1f;

        float modifierPercent = 0f;
        OtherCiv.ForEachMatchingUnique(UniqueType.CityStateInfluenceDegradation, unique =>
        {
            modifierPercent += ParseFloat(unique.Params[0]);
        });

        string? religion = CivInfo.Cities.Count == 0 || CivInfo.GetCapital() == null ? null
            : CivInfo.GetCapital().NotNull().Religion.GetMajorityReligionName();
        if (religion != null && religion == OtherCiv.ReligionManager.Religion?.Name)
            modifierPercent -= 25f; // 25% slower degrade when sharing a religion

        foreach (var civ in CivInfo.GameInfo.Civilizations.Where(it => it.IsMajorCiv() && it != OtherCiv).ToList())
        {
            civ.ForEachMatchingUnique(UniqueType.OtherCivsCityStateRelationsDegradeFaster, unique =>
            {
                modifierPercent += ParseFloat(unique.Params[0]);
            });
        }

        return Math.Max(0f, decrement) * Math.Max(-100f, modifierPercent).ToPercent();
    }

    public bool CanDeclareWar() => !CivInfo.IsDefeated() && !OtherCiv.IsDefeated()
        && TurnsToPeaceTreaty() == 0 && DiplomaticStatus != DiplomaticStatus.War;

    /// <summary>Kotlin <c>declareWar(declareWarReason = DeclareWarReason(WarType.DirectWar))</c> = <c>DeclareWar.declareWar(this, reason)</c> (T068).</summary>
    public void DeclareWar(DeclareWarReason? declareWarReason = null) =>
        Diplomacy.DeclareWar.Declare(this, declareWarReason ?? new DeclareWarReason(WarType.DirectWar));

    //Used for nuke
    public bool CanAttack() => TurnsToPeaceTreaty() == 0;

    public int GoldPerTurn()
    {
        int goldPerTurnForUs = 0;
        foreach (var trade in Trades)
        {
            foreach (var offer in trade.OurOffers.Where(it => it.Type == TradeOfferType.Gold_Per_Turn))
                goldPerTurnForUs -= offer.Amount;
            foreach (var offer in trade.TheirOffers.Where(it => it.Type == TradeOfferType.Gold_Per_Turn))
                goldPerTurnForUs += offer.Amount;
        }
        return goldPerTurnForUs;
    }

    public ResourceSupplyList ResourcesFromTrade()
    {
        var newResourceSupplyList = new ResourceSupplyList();
        var resourcesMap = CivInfo.GameInfo.Ruleset.TileResources;
        bool IsResourceFilter(TradeOffer it) =>
            (it.Type == TradeOfferType.Strategic_Resource || it.Type == TradeOfferType.Luxury_Resource)
                && resourcesMap.ContainsKey(it.Name)
                && !resourcesMap[it.Name].IsStockpiled;
        foreach (var trade in Trades)
        {
            foreach (var offer in trade.OurOffers.Where(IsResourceFilter))
                newResourceSupplyList.Add(resourcesMap[offer.Name], "Trade", -offer.Amount);
            foreach (var offer in trade.TheirOffers.Where(IsResourceFilter))
                newResourceSupplyList.Add(resourcesMap[offer.Name], "Trade", offer.Amount);
        }

        return newResourceSupplyList;
    }

    /// <summary>Returns the <see cref="Civilization"/>s that know about both sides (<see cref="CivInfo"/> and <see cref="OtherCiv"/>), in the order of <see cref="CivInfo"/>'s known civs</summary>
    public IReadOnlyList<Civilization> GetCommonKnownCivs() => Intersect(CivInfo.GetKnownCivs(), OtherCiv.GetKnownCivs());

    public IReadOnlyList<Civilization> GetCommonKnownCivsWithSpectators() => Intersect(CivInfo.GetKnownCivsWithSpectators(), OtherCiv.GetKnownCivsWithSpectators());

    // Iterable.intersect: a LinkedHashSet of the receiver (first occurrence order), minus everything not in the other collection
    private static List<Civilization> Intersect(IEnumerable<Civilization> ours, IEnumerable<Civilization> theirs)
    {
        var theirSet = theirs.ToHashSet();
        return ours.Distinct().Where(theirSet.Contains).ToList();
    }

    /// <summary>
    /// Returns true when the <see cref="CivInfo"/>'s territory is considered allied for <see cref="OtherCiv"/>.
    /// This includes friendly and allied city-states and the open border treaties.
    /// </summary>
    public bool IsConsideredFriendlyTerritory()
    {
        if (CivInfo.IsCityState &&
            (IsRelationshipLevelGE(Level.Friend) || OtherCiv.HasUnique(UniqueType.CityStateTerritoryAlwaysFriendly)))
            return true;

        return OtherCivDiplomacy().HasOpenBorders; // if THEY can enter US then WE are considered friendly territory for THEM
    }
    #endregion

    #region state-changing functions
    // for performance reasons we don't want to call this every time we want to see if a unit can move through a tile
    public void UpdateHasOpenBorders()
    {
        // City-states can enter ally's territory (the opposite is true anyway even without open borders)
        bool newHasOpenBorders = CivInfo.AllyCiv == OtherCiv
                || Trades.SelectMany(it => it.TheirOffers).Any(it => it.Name == Constants.OpenBorders && it.Duration > 0);

        bool bordersWereClosed = HasOpenBorders && !newHasOpenBorders;
        HasOpenBorders = newHasOpenBorders;

        if (bordersWereClosed)
        { // borders were closed, get out!
            foreach (var unit in CivInfo.Units.GetCivUnits()
                .Where(it => ReferenceEquals(it.CurrentTile.GetOwner(), OtherCiv)).ToList())
            {
                unit.Movement.TeleportToClosestMoveableTile();
            }
        }
    }

    /// <summary>Should only be called from makePeace</summary>
    private void MakePeaceOneSide()
    {
        DiplomaticStatus = DiplomaticStatus.Peace;
        var otherCiv = OtherCiv;
        // Get out of others' territory
        foreach (var unit in CivInfo.Units.GetCivUnits().Where(it => ReferenceEquals(it.CurrentTile.GetOwner(), otherCiv)).ToList())
            unit.Movement.TeleportToClosestMoveableTile();

        foreach (var thirdCiv in CivInfo.GetKnownCivs())
        {
            // Our ally city states make peace with us
            if (thirdCiv.AllyCiv == CivInfo && thirdCiv.IsAtWarWith(otherCiv))
            {
                var thirdCivDiplo = thirdCiv.GetDiplomacyManager(otherCiv).NotNull();
                thirdCivDiplo.MakePeace();

                // Make the peace treaty so that the civ can't declare war immedietly
                var currentTrade = new TradeObject(); // TradeLogic(thirdCiv, otherCiv).currentTrade, see the header (T086)
                currentTrade.OurOffers.Add(new TradeOffer(Constants.PeaceTreaty, TradeOfferType.Treaty, CivInfo.GameInfo.Speed));
                currentTrade.TheirOffers.Add(new TradeOffer(Constants.PeaceTreaty, TradeOfferType.Treaty, CivInfo.GameInfo.Speed));
                thirdCivDiplo.Trades.Add(currentTrade);
                thirdCivDiplo.OtherCivDiplomacy().Trades.Add(currentTrade.Reverse());
            }
            // Other City-States that are not our ally don't like the fact that we made peace with their enemy
            if (thirdCiv.AllyCiv != CivInfo && thirdCiv.IsAtWarWith(otherCiv))
                thirdCiv.GetDiplomacyManager(CivInfo).NotNull().AddInfluence(-10f);
        }
    }

    public void MakePeace()
    {
        MakePeaceOneSide();
        OtherCivDiplomacy().MakePeaceOneSide();
        GameEventHooks.PeaceMade(CivInfo, OtherCiv);

        foreach (var civ in GetCommonKnownCivsWithSpectators())
        {
            civ.AddNotification(
                $"[{CivInfo.CivName}] and [{OtherCiv.CivName}] have signed a Peace Treaty!",
                NotificationCategory.Diplomacy, CivInfo.CivName, NotificationIcon.Diplomacy, OtherCiv.CivName
            );
        }

        CivInfo.ForEachTriggeredUnique(UniqueType.TriggerUponSigningPeace, CivInfo.State, _ => true, false, unique =>
        {
            if (OtherCiv.MatchesFilter(unique.Params[0]))
                TriggerUnique(unique, CivInfo);
        });

        OtherCiv.ForEachTriggeredUnique(UniqueType.TriggerUponSigningPeace, OtherCiv.State, _ => true, false, unique =>
        {
            if (CivInfo.MatchesFilter(unique.Params[0]))
                TriggerUnique(unique, OtherCiv);
        });
    }

    /// <summary>Kotlin <c>UniqueTriggerActivation.triggerUnique(unique, civ)</c>.</summary>
    private static void TriggerUnique(UniqueObject unique, Civilization civ) =>
        UniqueTriggerActivation.TriggerUnique(unique, civ);

    public bool HasFlag(DiplomacyFlags flag) => FlagsCountdown.ContainsKey(flag.ToString());

    public void SetFlag(DiplomacyFlags flag, int amount, bool adjustWithGameSpeed = false) =>
        FlagsCountdown[flag.ToString()] =
            adjustWithGameSpeed ? KotlinMath.RoundToInt(amount * CivInfo.GameInfo.Speed.Modifier)
            : amount;

    /// <summary>0 indicates 'flag does not exist'</summary>
    public int GetFlag(DiplomacyFlags flag) => FlagsCountdown.TryGetValue(flag.ToString(), out var value) ? value : 0;

    public void RemoveFlag(DiplomacyFlags flag) => FlagsCountdown.Remove(flag.ToString());

    public void AddModifier(DiplomaticModifiers modifier, float amount, float? limit = null)
    {
        string modifierString = modifier.ToString();
        if (!HasModifier(modifier)) SetModifier(modifier, 0f);
        float newValue = DiplomaticModifiers[modifierString] + amount;
        if (limit != null)
        {
            if (limit < 0) newValue = newValue < limit ? limit.Value : newValue; // coerceAtLeast
            else newValue = newValue > limit ? limit.Value : newValue; // coerceAtMost
        }
        DiplomaticModifiers[modifierString] = newValue;
        if (DiplomaticModifiers[modifierString] == 0f) DiplomaticModifiers.Remove(modifierString);
    }

    public void SetModifier(DiplomaticModifiers modifier, float amount) => DiplomaticModifiers[modifier.ToString()] = amount;

    internal float GetModifier(DiplomaticModifiers modifier)
    {
        if (!HasModifier(modifier)) return 0f;
        return DiplomaticModifiers[modifier.ToString()];
    }

    internal bool RemoveModifier(DiplomaticModifiers modifier) => DiplomaticModifiers.Remove(modifier.ToString());

    public bool HasModifier(DiplomaticModifiers modifier) => DiplomaticModifiers.ContainsKey(modifier.ToString());

    public void ReplaceModifier(DiplomaticModifiers oldModifier, DiplomaticModifiers newModifier, float amount)
    {
        RemoveModifier(oldModifier);
        AddModifier(newModifier, amount);
    }

    public void SignDeclarationOfFriendship()
    {
        SetModifier(Modifier.DeclarationOfFriendship, 35f);
        OtherCivDiplomacy().SetModifier(Modifier.DeclarationOfFriendship, 35f);
        // before adjusting with game speed - consider side effects
        SetFlag(DiplomacyFlags.DeclarationOfFriendship, 30);
        OtherCivDiplomacy().SetFlag(DiplomacyFlags.DeclarationOfFriendship, 30);

        foreach (var thirdCiv in GetCommonKnownCivsWithSpectators())
        {
            thirdCiv.AddNotification($"[{CivInfo.CivName}] and [{OtherCiv.CivName}] have signed a Declaration of Friendship!",
                NotificationCategory.Diplomacy, CivInfo.CivName, NotificationIcon.Diplomacy, OtherCiv.CivName);
            if (thirdCiv.IsSpectator()) continue;
            thirdCiv.GetDiplomacyManager(CivInfo).NotNull().SetFriendshipBasedModifier();
            thirdCiv.GetDiplomacyManager(OtherCiv).NotNull().SetFriendshipBasedModifier();
        }

        // Ignore contitionals as triggerUnique will check again, and that would break
        // UniqueType.ConditionalChance - 25% declared chance would work as 6% actual chance
        CivInfo.ForEachTriggeredUnique(UniqueType.TriggerUponDeclaringFriendship, GameContext.IgnoreConditionalsState, _ => true, false, unique =>
        {
            TriggerUnique(unique, CivInfo);
        });
        OtherCiv.ForEachTriggeredUnique(UniqueType.TriggerUponDeclaringFriendship, GameContext.IgnoreConditionalsState, _ => true, false, unique =>
        {
            TriggerUnique(unique, OtherCiv);
        });
    }

    internal void SetFriendshipBasedModifier()
    {
        RemoveModifier(Modifier.DeclaredFriendshipWithOurAllies);
        RemoveModifier(Modifier.DeclaredFriendshipWithOurEnemies);
        var civsOtherCivHasDeclaredFriendshipWith = GetCommonKnownCivs()
            .Where(it => it.GetDiplomacyManager(OtherCiv).NotNull().HasFlag(DiplomacyFlags.DeclarationOfFriendship)).ToList();

        foreach (var thirdCiv in civsOtherCivHasDeclaredFriendshipWith)
        {
            // What do we (A) think about the otherCiv() (B) being friends with the third Civ (C)?
            var ourRelationshipWithThirdCiv = CivInfo.GetDiplomacyManager(thirdCiv).NotNull().RelationshipIgnoreAfraid();

            var modifierType = ourRelationshipWithThirdCiv switch
            {
                Level.Unforgivable or Level.Enemy => Modifier.DeclaredFriendshipWithOurEnemies,
                _ => Modifier.DeclaredFriendshipWithOurAllies,
            };
            float modifierValue = ourRelationshipWithThirdCiv switch
            {
                Level.Unforgivable => -15f,
                Level.Enemy => -5f,
                Level.Friend => 5f,
                Level.Ally => 15f,
                _ => 0f,
            };
            AddModifier(modifierType, modifierValue);
        }
    }

    public void SignDefensivePact(int duration)
    {
        //Note: These modifiers are additive to the friendship modifiers
        SetModifier(Modifier.DefensivePact, 10f);
        OtherCivDiplomacy().SetModifier(Modifier.DefensivePact, 10f);
        SetFlag(DiplomacyFlags.DefensivePact, duration);
        OtherCivDiplomacy().SetFlag(DiplomacyFlags.DefensivePact, duration);
        DiplomaticStatus = DiplomaticStatus.DefensivePact;
        OtherCivDiplomacy().DiplomaticStatus = DiplomaticStatus.DefensivePact;

        foreach (var thirdCiv in GetCommonKnownCivsWithSpectators())
        {
            thirdCiv.AddNotification($"[{CivInfo.CivName}] and [{OtherCiv.CivName}] have signed a Defensive Pact!",
                NotificationCategory.Diplomacy, CivInfo.CivName, NotificationIcon.Diplomacy, OtherCiv.CivName);
            if (thirdCiv.IsSpectator()) return; // sic: Kotlin returns here, skipping the triggered uniques below
            thirdCiv.GetDiplomacyManager(CivInfo).NotNull().SetDefensivePactBasedModifier();
        }

        // Ignore contitionals as triggerUnique will check again, and that would break
        // UniqueType.ConditionalChance - 25% declared chance would work as 6% actual chance
        CivInfo.ForEachTriggeredUnique(UniqueType.TriggerUponSigningDefensivePact, GameContext.IgnoreConditionalsState, _ => true, false, unique =>
        {
            TriggerUnique(unique, CivInfo);
        });
        OtherCiv.ForEachTriggeredUnique(UniqueType.TriggerUponSigningDefensivePact, GameContext.IgnoreConditionalsState, _ => true, false, unique =>
        {
            TriggerUnique(unique, OtherCiv);
        });
    }

    internal void SetDefensivePactBasedModifier()
    {
        RemoveModifier(Modifier.SignedDefensivePactWithOurAllies);
        RemoveModifier(Modifier.SignedDefensivePactWithOurEnemies);

        var civsTheyHavePactWith = GetCommonKnownCivs()
            .Where(it => OtherCiv.GetDiplomacyManager(it).NotNull().HasFlag(DiplomacyFlags.DefensivePact)).ToList();

        foreach (var thirdCiv in civsTheyHavePactWith)
        {
            // what do we (A) think about the other civ (B) having a defensive pact with the third civ (C)?
            var ourRelationshipWithThirdCiv = CivInfo.GetDiplomacyManager(thirdCiv).NotNull().RelationshipIgnoreAfraid();
            var modifierType = ourRelationshipWithThirdCiv switch
            {
                Level.Unforgivable or Level.Enemy => Modifier.SignedDefensivePactWithOurEnemies,
                _ => Modifier.SignedDefensivePactWithOurAllies,
            };
            float modifierValue = ourRelationshipWithThirdCiv switch
            {
                Level.Unforgivable => -15f,
                Level.Enemy => -10f,
                Level.Friend => 2f,
                Level.Ally => 5f,
                _ => 0f,
            };
            AddModifier(modifierType, modifierValue);
        }
    }

    internal void SetReligionBasedModifier()
    {
        if (OtherCivDiplomacy().BelievesSameReligion())
            // they share same majority religion
            SetModifier(Modifier.BelieveSameReligion, 5f);
        else
            // their majority religions differ or one or both don't have a majority religion at all
            RemoveModifier(Modifier.BelieveSameReligion);
    }

    public void Denounce()
    {
        ActivateDenounceEffects();
        // legacy code for AI to counter-denounce
        /*
        if (otherCiv.isAI())
            otherCivDiplomacy().activateDenounceEffects()
        */
    }

    private void ActivateDenounceEffects()
    {
        // This shows the relationship at the point of denouncing
        Log.Debug("%s (opinion: %.1f, smoothed opinion change: %.1f) decided to denounce %s because...", CivInfo.CivName,
            (Func<object?>)(() => OpinionOfOtherCiv()), (Func<object?>)(() => SmoothedOpinionDelta()), OtherCivName);
        foreach (var (modifier, value) in DiplomaticModifiers)
            Log.Debug("- %s: %.1f", modifier, value);

        // the other civ's opinion of us will drop by 35 if we are the only civ to denounce them
        // if they have already been denounced by many others, our denouncement will not surprise / anger them quite as much
        // -35, -25, -18, -12, ...
        int activeDenunciationsAgainstOtherCiv = OtherCiv.GetKnownCivs().Count(
            it => it.GetDiplomacyManager(OtherCiv).NotNull().HasFlag(DiplomacyFlags.Denunciation));
        float otherCivOpinionChangeFromBeingDenounced = InitialOpinionChangeWhenDenounced * KotlinMath.Pow(2f, -0.5f * activeDenunciationsAgainstOtherCiv);
        Log.Debug("%s is upset that %s denounced them: %.1f opinion", OtherCivName, CivInfo.CivName, otherCivOpinionChangeFromBeingDenounced);
        OtherCivDiplomacy().SetModifier(Modifier.Denunciation, otherCivOpinionChangeFromBeingDenounced);

        // denouncements are active for 30 turns
        // before adjusting with game speed - consider possible side effects of this
        SetFlag(DiplomacyFlags.Denunciation, 30);

        // the denounced civ will get a popup
        OtherCiv.AddPopupAlert(new PopupAlert(AlertType.Denounced, CivInfo.CivId));
        // ...and a notification as a reminder for the rest of the turn
        OtherCiv.AddNotification(
            $"[{CivInfo.CivName}] has denounced us!",
            NotificationCategory.Diplomacy,
            NotificationIcon.Diplomacy, CivInfo.CivName
        );

        // Denounciation results in removal of embasies for both sides
        CivInfo.DiplomacyFunctions.RemoveEmbassies(OtherCiv);

        // We, A, are denouncing B. What do other major civs (C,D, etc) think of this?
        foreach (var thirdCiv in GetCommonKnownCivsWithSpectators())
        {
            thirdCiv.AddNotification(
                $"[{CivInfo.CivName}] has denounced [{OtherCiv.CivName}]!",
                NotificationCategory.Diplomacy,
                CivInfo.CivName, NotificationIcon.Diplomacy, OtherCiv.CivName
            );

            if (thirdCiv.IsSpectator())
                continue;

            // their diplomacy with us
            var thirdCivDiplomacyWithUs = thirdCiv.GetDiplomacyManager(CivInfo).NotNull();
            // their relationship with the civ we are denouncing
            var thirdCivRelationshipWithTarget = thirdCiv.GetDiplomacyManager(OtherCiv).NotNull().RelationshipIgnoreAfraid();
            switch (thirdCivRelationshipWithTarget)
            {
                case Level.Unforgivable: thirdCivDiplomacyWithUs.AddModifier(Modifier.DenouncedOurEnemies, 15f); break;
                case Level.Enemy: thirdCivDiplomacyWithUs.AddModifier(Modifier.DenouncedOurEnemies, 5f); break;
                case Level.Friend: thirdCivDiplomacyWithUs.AddModifier(Modifier.DenouncedOurAllies, -5f); break;
                case Level.Ally: thirdCivDiplomacyWithUs.AddModifier(Modifier.DenouncedOurAllies, -15f); break;
                default: break;
            }
        }
    }

    /// <summary>
    /// Queues a PopupAlert to be displayed on <see cref="OtherCiv"/>'s turn
    /// If a popup of the same type is already queued, do nothing.
    /// </summary>
    private void QueueOtherCivPopupIfUnique(PopupAlert popup)
    {
        if (!OtherCiv.PopupAlerts.Any(it => it.Type == popup.Type && it.Value == popup.Value))
            OtherCiv.AddPopupAlert(popup);
    }

    public void AgreeToDemand(Demand demand)
    {
        OtherCivDiplomacy().SetFlag(demand.AgreedToDemand, 100, true);
        AddModifier(Modifier.UnacceptableDemands, -10f);
        string text = demand.AgreedToDemandText.FillPlaceholders(CivInfo.CivName);
        OtherCiv.AddNotification(text, NotificationCategory.Diplomacy, NotificationIcon.Diplomacy, CivInfo.CivName);
        QueueOtherCivPopupIfUnique(new PopupAlert(AlertType.AcceptingDemand, CivInfo.CivId));
    }

    public void RefuseDemand(Demand demand)
    {
        AddModifier(Modifier.UnacceptableDemands, -20f);
        OtherCivDiplomacy().SetFlag(demand.WillIgnoreViolation, 100, true);
        string text = demand.RefusedDemandText.FillPlaceholders(CivInfo.CivName);
        OtherCiv.AddNotification(text, NotificationCategory.Diplomacy, NotificationIcon.Diplomacy, CivInfo.CivName);
        QueueOtherCivPopupIfUnique(new PopupAlert(AlertType.RejectingDemand, CivInfo.CivId));
        if (demand == Demand.DoNotAttackUs)
            // no modifier penalty for refusal, they respect our honesty
            DeclareWar();
        else
            OtherCivDiplomacy().AddModifier(demand.RefusedDiplomaticModifier, -15f);
    }

    public void SideWithCityState()
    {
        OtherCivDiplomacy().SetModifier(Modifier.SidedWithProtectedMinor, -5f);
        OtherCivDiplomacy().SetFlag(DiplomacyFlags.RememberSidedWithProtectedMinor, 25);
    }

    public void BecomeWary()
    {
        if (HasFlag(DiplomacyFlags.WaryOf)) return; // once is enough
        SetFlag(DiplomacyFlags.WaryOf, -1); // Never expires
        OtherCiv.AddNotification("City-States grow wary of your aggression. " +
                "The resting point for Influence has decreased by [20] for [" + CivInfo.CivName + "].",
            NotificationCategory.Diplomacy, CivInfo.CivName);
    }

    /// <summary>
    /// Resolves the otherCiv giving gifts to this civ with negative gold values.
    /// Prioritises reducing gifts given to the other civ before increasing our gift value.
    /// Does not take the gold from either civ's stockpile
    /// </summary>
    /// <param name="gold">the amount of gold without inflation, can be negative</param>
    /// <param name="isPureGift">should be true if the gift was one-sided, or false if it wasn't</param>
    public void GiftGold(int gold, bool isPureGift)
    {
        int currentGold = isPureGift
            ? KotlinMath.RoundToInt(gold * CivInfo.GameInfo.Ruleset.ModOptions.Constants.GoldGiftMultiplier)
            : KotlinMath.RoundToInt(gold * CivInfo.GameInfo.Ruleset.ModOptions.Constants.GoldGiftTradeMultiplier);

        int otherGold = OtherCivDiplomacy().GetGoldGifts();
        if (otherGold > currentGold)
        {
            OtherCivDiplomacy().RecieveGoldGifts(-currentGold);
        }
        else
        {
            OtherCivDiplomacy().RemoveModifier(Modifier.GaveUsGifts);
            RecieveGoldGifts(currentGold - otherGold);
        }
    }

    /// <summary>
    /// Adds a gift from the other civilization of the value of <paramref name="gold"/> that will deteriate over time.
    /// Does not take into account how much gold we have given to the other civ. Use <see cref="GiftGold"/> for that.
    /// Does not take the gold from either civ's stockpile.
    /// </summary>
    /// <param name="gold">the amount of gold without inflation, cannot be negative</param>
    public void RecieveGoldGifts(int gold)
    {
        double diplomaticValueOfTrade = gold * GetGoldInflation(CivInfo) / (CivInfo.GameInfo.Speed.GoldGiftModifier * 100);
        AddModifier(Modifier.GaveUsGifts, (float)diplomaticValueOfTrade);
    }

    /// <returns>the total value of the gold gifts the other civilization has given us</returns>
    public int GetGoldGifts()
    {
        // The inverse of howe we calculate GaveUsGifts in TradeLogic.acceptTrade gives us how much gold it is worth
        float giftAmount = GetModifier(Modifier.GaveUsGifts);
        return KotlinMath.DoubleToInt(giftAmount * CivInfo.GameInfo.Speed.GoldGiftModifier * 100 / GetGoldInflation(CivInfo));
    }

    private static double GetGoldInflation(Civilization civInfo) => Trade.TradeEvaluation.Default.GetGoldInflation(civInfo);
    #endregion
}
