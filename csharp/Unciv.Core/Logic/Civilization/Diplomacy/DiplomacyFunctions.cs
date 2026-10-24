using Unciv.Logic.Map;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.Utils;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;

namespace Unciv.Logic.Civilization.Diplomacy;

// core/src/com/unciv/logic/civilization/diplomacy/DiplomacyFunctions.kt
// Deviation: meetCiv does not record the "Meet another civilization" tutorial task (UncivGame.Current.settings is client/UI state;
// a headless engine has no settings), which only matters for the current player's tutorial hints.

public class DiplomacyFunctions(Civilization civInfo)
{
    public Civilization CivInfo { get; } = civInfo;

    /// <summary>A sorted Sequence of all other civs we know (excluding barbarians and spectators)</summary>
    public IEnumerable<Civilization> GetKnownCivsSorted(bool includeCityStates = true, bool includeDefeated = false) =>
        CivInfo.GameInfo.GetCivsSorted(includeCityStates, includeDefeated, null, it => it != CivInfo && CivInfo.Knows(it));

    public void MakeCivilizationsMeet(Civilization otherCiv, bool warOnContact = false)
    {
        MeetCiv(otherCiv, warOnContact);
        otherCiv.DiplomacyFunctions.MeetCiv(CivInfo, warOnContact);
    }

    private void MeetCiv(Civilization otherCiv, bool warOnContact = false)
    {
        CivInfo.Diplomacy[otherCiv.CivId] = new DiplomacyManager(CivInfo, otherCiv) { DiplomaticStatus = DiplomaticStatus.Peace };

        if (!otherCiv.IsSpectator())
            otherCiv.PopupAlerts.Add(new PopupAlert(AlertType.FirstContact, CivInfo.CivId));

        if (CivInfo.IsCityState && otherCiv.IsMajorCiv())
        {
            if (warOnContact || otherCiv.IsMinorCivAggressor()) return; // No gift if they are bad people, or we are just about to be at war

            HexCoord? cityStateLocation = CivInfo.Cities.Count == 0 ? null : CivInfo.GetCapital()!.Location;

            var giftAmount = new Stats(gold: 15f);
            var faithAmount = new Stats(faith: 4f);
            // Later, religious city-states will also gift gold, making this the better implementation
            // For now, it might be overkill though.
            string meetString = $"[{CivInfo.CivName}] has given us [{giftAmount.ToStringForNotifications()}] as a token of goodwill for meeting us";
            string religionMeetString = $"[{CivInfo.CivName}] has also given us [{faithAmount.ToStringForNotifications()}]";
            if (CivInfo.Diplomacy.Count(it => it.Value.OtherCiv.IsMajorCiv()) == 1)
            {
                giftAmount.TimesInPlace(2f);
                meetString = $"[{CivInfo.CivName}] has given us [{giftAmount.ToStringForNotifications()}] as we are the first major civ to meet them";
            }
            if (cityStateLocation != null)
                otherCiv.AddNotification(meetString, cityStateLocation.Value, NotificationCategory.Diplomacy, NotificationIcon.Gold);
            else
                otherCiv.AddNotification(meetString, NotificationCategory.Diplomacy, NotificationIcon.Gold);

            if (CivInfo.CityStateFunctions.CanProvideStat(Stat.Faith))
            {
                otherCiv.AddNotification(religionMeetString, NotificationCategory.Diplomacy, NotificationIcon.Faith);

                foreach (var (key, value) in faithAmount)
                    otherCiv.AddStat(key, KotlinMath.FloatToInt(value));
            }
            foreach (var (key, value) in giftAmount)
                otherCiv.AddStat(key, KotlinMath.FloatToInt(value));

            if (CivInfo.Cities.Count != 0)
                CivInfo.GetCapital()?.GetCenterTile().SetExplored(otherCiv, true);

            CivInfo.QuestManager.JustMet(otherCiv); // Include them in war with major pseudo-quest
        }
    }

    public bool IsAtWarWith(Civilization otherCiv)
    {
        if (otherCiv == CivInfo) return false;
        if (otherCiv.IsBarbarian || CivInfo.IsBarbarian) return true;
        var diplomacyManager = CivInfo.GetDiplomacyManager(otherCiv);
        if (diplomacyManager == null) return false; // not encountered yet
        return diplomacyManager.DiplomaticStatus == DiplomaticStatus.War;
    }

    /// <summary>If denounciation happened this turn from either side, establishing embassy again is possible only from next turn.</summary>
    private static bool IsDenouncedThisTurn(DiplomacyManager diploManager) =>
        diploManager.GetFlag(DiplomacyFlags.Denunciation) == 30
        || diploManager.OtherCivDiplomacy().GetFlag(DiplomacyFlags.Denunciation) == 30;

    /// <summary>Basic check if we can trade embassies, does not check all prerequisities. Use <see cref="CanOfferEmbassyTo"/> and <see cref="CanEstablishEmbassyWith"/> instead.</summary>
    private bool CanTradeEmbassies() => CivInfo.IsMajorCiv() && CivInfo.HasUnique(UniqueType.EnablesEmbassies);

    /// <summary>Test if we can offer our embassy to <paramref name="otherCiv"/></summary>
    public bool CanOfferEmbassyTo(Civilization otherCiv)
    {
        if (!CanTradeEmbassies() || !otherCiv.IsMajorCiv() || CivInfo.GetCapital() == null)
            return false;

        var theirDiploManager = otherCiv.GetDiplomacyManager(CivInfo)!;
        return !CivInfo.IsAtWarWith(otherCiv) && !IsDenouncedThisTurn(theirDiploManager)
            && !theirDiploManager.HasModifier(DiplomaticModifiers.EstablishedEmbassy)
            && !theirDiploManager.HasModifier(DiplomaticModifiers.SharedEmbassies);
    }

    /// <summary>Test if we can establish embassy in <paramref name="otherCiv"/> capital</summary>
    public bool CanEstablishEmbassyWith(Civilization otherCiv)
    {
        if (!CanTradeEmbassies() || !otherCiv.IsMajorCiv() || otherCiv.GetCapital() == null)
            return false;

        var ourDiploManager = CivInfo.GetDiplomacyManager(otherCiv)!;
        return !CivInfo.IsAtWarWith(otherCiv) && !IsDenouncedThisTurn(ourDiploManager)
            && !ourDiploManager.HasModifier(DiplomaticModifiers.EstablishedEmbassy)
            && !ourDiploManager.HasModifier(DiplomaticModifiers.SharedEmbassies);
    }

    public bool MeetsEmbassyRequirementFor(Civilization otherCiv) =>
        !CivInfo.HasUnique(UniqueType.RequiresEmbassiesForDiplomacy) ||
            CivInfo.GetDiplomacyManager(otherCiv)!.HasModifier(DiplomaticModifiers.SharedEmbassies);

    /// <summary>Remove mutual embassies from both civs</summary>
    public void RemoveEmbassies(Civilization otherCiv)
    {
        var ourDiploManager = CivInfo.GetDiplomacyManager(otherCiv)!;
        ourDiploManager.RemoveModifier(DiplomaticModifiers.EstablishedEmbassy);
        ourDiploManager.RemoveModifier(DiplomaticModifiers.ReceivedEmbassy);
        ourDiploManager.RemoveModifier(DiplomaticModifiers.SharedEmbassies);

        var theirDiploManager = ourDiploManager.OtherCivDiplomacy();
        theirDiploManager.RemoveModifier(DiplomaticModifiers.EstablishedEmbassy);
        theirDiploManager.RemoveModifier(DiplomaticModifiers.ReceivedEmbassy);
        theirDiploManager.RemoveModifier(DiplomaticModifiers.SharedEmbassies);
    }

    public bool CanSignDeclarationOfFriendshipWith(Civilization otherCiv) =>
        otherCiv.IsMajorCiv() && !otherCiv.IsAtWarWith(CivInfo)
            && !CivInfo.GetDiplomacyManager(otherCiv)!.HasFlag(DiplomacyFlags.Denunciation)
            && !CivInfo.GetDiplomacyManager(otherCiv)!.HasFlag(DiplomacyFlags.DeclarationOfFriendship);

    public bool CanSignResearchAgreement()
    {
        if (!CivInfo.IsMajorCiv()) return false;
        if (!CivInfo.HasUnique(UniqueType.EnablesResearchAgreements)) return false;
        if (CivInfo.Tech.AllTechsAreResearched()) return false;
        return true;
    }

    public bool CanSignResearchAgreementNoCostWith(Civilization otherCiv)
    {
        var ourDiploManager = CivInfo.GetDiplomacyManager(otherCiv)!;
        return CanSignResearchAgreement()
            && otherCiv.DiplomacyFunctions.CanSignResearchAgreement()
            && MeetsEmbassyRequirementFor(otherCiv)
            && ourDiploManager.HasFlag(DiplomacyFlags.DeclarationOfFriendship)
            && !ourDiploManager.HasFlag(DiplomacyFlags.ResearchAgreement)
            && !ourDiploManager.OtherCivDiplomacy().HasFlag(DiplomacyFlags.ResearchAgreement);
    }

    public bool CanSignResearchAgreementWith(Civilization otherCiv)
    {
        int cost = GetResearchAgreementCost(otherCiv);
        return CanSignResearchAgreementNoCostWith(otherCiv)
            && CivInfo.Gold >= cost && otherCiv.Gold >= cost;
    }

    public int GetResearchAgreementCost(Civilization otherCiv) =>
        // https://forums.civfanatics.com/resources/research-agreements-bnw.25568/
        KotlinMath.FloatToInt(Math.Max(CivInfo.GetEra().ResearchAgreementCost, otherCiv.GetEra().ResearchAgreementCost)
            * CivInfo.GameInfo.Speed.GoldCostModifier);

    public bool CanSignDefensivePact()
    {
        if (!CivInfo.IsMajorCiv()) return false;
        if (!CivInfo.HasUnique(UniqueType.EnablesDefensivePacts)) return false;
        return true;
    }

    public bool CanSignDefensivePactWith(Civilization otherCiv)
    {
        var ourDiplomacyManager = CivInfo.GetDiplomacyManager(otherCiv)!;
        return CanSignDefensivePact()
            && otherCiv.DiplomacyFunctions.CanSignDefensivePact()
            && MeetsEmbassyRequirementFor(otherCiv)
            && ourDiplomacyManager.HasFlag(DiplomacyFlags.DeclarationOfFriendship)
            && !ourDiplomacyManager.HasFlag(DiplomacyFlags.DefensivePact)
            && !ourDiplomacyManager.OtherCivDiplomacy().HasFlag(DiplomacyFlags.DefensivePact)
            && ourDiplomacyManager.DiplomaticStatus != DiplomaticStatus.DefensivePact;
    }

    /// <returns>
    /// whether units of this civilization can pass through the tiles owned by <paramref name="otherCiv"/>,
    /// considering only civ-wide filters.
    /// Use <c>Tile.canCivPassThrough</c> to check whether units of a civilization can pass through
    /// a specific tile, considering only civ-wide filters.
    /// Use <c>UnitMovement.canPassThrough</c> to check whether a specific unit can pass through a specific tile.
    /// </returns>
    public bool CanPassThroughTiles(Civilization otherCiv)
    {
        if (otherCiv == CivInfo) return true;
        if (otherCiv.IsBarbarian) return true;
        if (CivInfo.IsBarbarian && CivInfo.GameInfo.Turns >= CivInfo.GameInfo.GetDifficulty().TurnBarbariansCanEnterPlayerTiles)
            return true;
        var diplomacyManager = CivInfo.GetDiplomacyManager(otherCiv);
        if (diplomacyManager != null && (diplomacyManager.HasOpenBorders || diplomacyManager.DiplomaticStatus == DiplomaticStatus.War))
            return true;
        // Players can always pass through city-state tiles
        if (!CivInfo.IsAIOrAutoPlaying() && otherCiv.IsCityState) return true;
        return false;
    }
}
