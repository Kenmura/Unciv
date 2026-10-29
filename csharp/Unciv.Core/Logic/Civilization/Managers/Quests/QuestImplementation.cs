using System.Collections.Immutable;
using System.Globalization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Map;
using Unciv.Models.Ruleset;
using Unciv.Models.Translations;
using Unciv.Utils;
using Tile = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Logic.Civilization.Managers;

// Ported from core/src/com/unciv/logic/civilization/managers/quests/QuestImplementation.kt (T069). Stripped: the purity annotations.
// Container for IQuestImplementation Implementations per QuestName: these are instantiated and delegated to by each QuestName entry
// (see QuestNameImplementation). There should always be exactly one class here per enum entry there.
// Convention: Every member that returns false for IsComplete because completion is handled elsewhere MUST comment how completion happens.

public abstract class QuestImplementation : IQuestImplementation
{
    public virtual bool IsValid(Civilization civ, Civilization challenger) => true;
    public virtual (string, string) GetAssignedQuestData(Civilization civ, Quest quest, Civilization assignee) => ("", "");
    public virtual List<NotificationAction> GetNotificationActions(Civilization civ, AssignedQuest assignedQuest) =>
        [new DiplomacyAction(civ.CivId)];
    public abstract bool IsComplete(Civilization civ, AssignedQuest assignedQuest);
    public virtual bool IsObsolete(Civilization civ, AssignedQuest assignedQuest) => false;

    protected static bool IsData1CivDefeated(AssignedQuest quest, Civilization civ) =>
        civ.GameInfo.GetCivilizationOrNull(quest.Data1)?.IsDefeated() != false;
    protected static Tile GetTile(AssignedQuest quest, Civilization civ) =>
        civ.GameInfo.TileMap[ParseInt(quest.Data1), ParseInt(quest.Data2)];
    protected static HexCoord GetPosition(AssignedQuest quest) =>
        new(ParseInt(quest.Data1), ParseInt(quest.Data2));
    // Kotlin String.toInt(): throws NumberFormatException on bad input
    internal static int ParseInt(string text) => int.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    public sealed class None : QuestImplementation
    {
        public override bool IsValid(Civilization civ, Civilization challenger) => false;
        public override bool IsComplete(Civilization civ, AssignedQuest assignedQuest) => false;
    }

    public sealed class Route : QuestImplementation
    {
        public override bool IsValid(Civilization civ, Civilization challenger)
        {
            if (challenger.Cities.Count == 0) return false;
            if (challenger.IsCapitalConnectedToCity(civ.GetCapital().NotNull())) return false;
            var capital = civ.GetCapital();
            if (capital == null) return false;
            var capitalTile = capital.GetCenterTile();
            return challenger.Cities.Any(it =>
                it.GetCenterTile().GetContinent() == capitalTile.GetContinent() &&
                it.GetCenterTile().AerialDistanceTo(capitalTile) <= 7);
        }

        public override bool IsComplete(Civilization civ, AssignedQuest assignedQuest) =>
            assignedQuest.AssigneeCiv.IsCapitalConnectedToCity(civ.GetCapital().NotNull());
    }

    /// <summary>
    /// B021 (bnw-spec 4.10): "connect a trade route" (+MINOR_QUEST_FRIENDSHIP_TRADE_ROUTE influence). Not in Kotlin. Only offered where trade routes
    /// exist, to a civ that has a slot and no route to us yet; complete while the assignee has an active route to a city of ours.
    /// </summary>
    public sealed class TradeRoute : QuestImplementation
    {
        public override bool IsValid(Civilization civ, Civilization challenger) =>
            Trade.Routes.TradeRouteRules.IsEnabled(civ.GameInfo) && civ.Cities.Count > 0
            && Trade.Routes.TradeRouteRules.Slots(challenger) > 0
            && !HasRoute(civ, challenger);

        public override bool IsComplete(Civilization civ, AssignedQuest assignedQuest) => HasRoute(civ, assignedQuest.AssigneeCiv);

        private static bool HasRoute(Civilization civ, Civilization trader) =>
            Trade.Routes.TradeRouteRules.Routes(civ.GameInfo).Any(it => it.OriginOwner == trader.CivId && it.DestOwner == civ.CivId);
    }

    public sealed class ClearBarbarianCamp : QuestImplementation
    {
        public override bool IsValid(Civilization civ, Civilization challenger) =>
            civ.GetBarbarianEncampmentForQuest(challenger) != null;

        public override (string, string) GetAssignedQuestData(Civilization civ, Quest quest, Civilization assignee)
        {
            var position = civ.GetBarbarianEncampmentForQuest(assignee).NotNull().Position;
            return (position.x.ToString(CultureInfo.InvariantCulture), position.y.ToString(CultureInfo.InvariantCulture));
        }

        public override List<NotificationAction> GetNotificationActions(Civilization civ, AssignedQuest assignedQuest) =>
            [new LocationAction(GetPosition(assignedQuest)), .. base.GetNotificationActions(civ, assignedQuest)];

        /// <summary>Completion happens when MapUnit.ClearEncampment calls QuestManager.BarbarianCampCleared.</summary>
        public override bool IsComplete(Civilization civ, AssignedQuest assignedQuest) => false;

        public override bool IsObsolete(Civilization civ, AssignedQuest assignedQuest) =>
            !GetTile(assignedQuest, civ).IsBarbarianEncampment();
    }

    public sealed class ConstructWonder : QuestImplementation
    {
        public override bool IsValid(Civilization civ, Civilization challenger) =>
            civ.GetWonderToBuildForQuest(challenger) != null;

        public override (string, string) GetAssignedQuestData(Civilization civ, Quest quest, Civilization assignee) =>
            (civ.GetWonderToBuildForQuest(assignee).NotNull().Name, "");

        public override bool IsComplete(Civilization civ, AssignedQuest assignedQuest) =>
            assignedQuest.AssigneeCiv.Cities.Any(it => it.CityConstructions.IsBuilt(assignedQuest.Data1));

        public override bool IsObsolete(Civilization civ, AssignedQuest assignedQuest) =>
            civ.GameInfo.GetCities().Any(it => it.Civ != assignedQuest.AssigneeCiv && it.CityConstructions.IsBuilt(assignedQuest.Data1));
    }

    public sealed class ConnectResource : QuestImplementation
    {
        public override bool IsValid(Civilization civ, Civilization challenger) =>
            civ.GetResourceForQuest(challenger) != null;

        public override (string, string) GetAssignedQuestData(Civilization civ, Quest quest, Civilization assignee) =>
            (civ.GetResourceForQuest(assignee).NotNull().Name, "");

        public override bool IsComplete(Civilization civ, AssignedQuest assignedQuest)
        {
            civ.GameInfo.Ruleset.TileResources.TryGetValue(assignedQuest.Data1, out var resource);
            return assignedQuest.AssigneeCiv.DetailedCivResources.Any(it => it.Resource == resource);
        }
    }

    public sealed class GreatPerson : QuestImplementation
    {
        public override bool IsValid(Civilization civ, Civilization challenger) =>
            civ.GetGreatPersonForQuest(challenger) != null;

        public override (string, string) GetAssignedQuestData(Civilization civ, Quest quest, Civilization assignee) =>
            (civ.GetGreatPersonForQuest(assignee).NotNull().Name, "");

        public override bool IsComplete(Civilization civ, AssignedQuest assignedQuest)
        {
            var ruleset = civ.GameInfo.Ruleset;
            return assignedQuest.AssigneeCiv.Units.GetCivGreatPeople()
                .Any(it => it.BaseUnit.GetReplacedUnit(ruleset).Name == assignedQuest.Data1);
        }
    }

    public sealed class ConquerCityState : QuestImplementation
    {
        public override bool IsValid(Civilization civ, Civilization challenger) =>
            civ.GetCityStateTarget(challenger) != null && civ.CityStatePersonality != CityStatePersonality.Friendly;

        public override (string, string) GetAssignedQuestData(Civilization civ, Quest quest, Civilization assignee) =>
            (civ.GetCityStateTarget(assignee).NotNull().CivId, "");

        /// <summary>Completed by CityStateFunctions.CityStateDestroyed calling QuestManager.CityStateConquered</summary>
        public override bool IsComplete(Civilization civ, AssignedQuest assignedQuest) => false;

        public override bool IsObsolete(Civilization civ, AssignedQuest assignedQuest) =>
            IsData1CivDefeated(assignedQuest, civ);
    }

    public sealed class FindPlayer : QuestImplementation
    {
        public override bool IsValid(Civilization civ, Civilization challenger) =>
            civ.GetCivilizationToFindForQuest(challenger) != null;

        public override (string, string) GetAssignedQuestData(Civilization civ, Quest quest, Civilization assignee) =>
            (civ.GetCivilizationToFindForQuest(assignee).NotNull().CivId, "");

        public override bool IsComplete(Civilization civ, AssignedQuest assignedQuest)
        {
            var civToFind = civ.GameInfo.GetCivilizationOrNull(assignedQuest.Data1);
            if (civToFind == null) return false;
            return assignedQuest.AssigneeCiv.HasMetCivTerritory(civToFind);
        }

        public override bool IsObsolete(Civilization civ, AssignedQuest assignedQuest) =>
            IsData1CivDefeated(assignedQuest, civ);
    }

    public sealed class FindNaturalWonder : QuestImplementation
    {
        public override bool IsValid(Civilization civ, Civilization challenger) =>
            civ.GetNaturalWonderToFindForQuest(challenger) != null;

        public override (string, string) GetAssignedQuestData(Civilization civ, Quest quest, Civilization assignee) =>
            (civ.GetNaturalWonderToFindForQuest(assignee).NotNull(), "");

        public override bool IsComplete(Civilization civ, AssignedQuest assignedQuest) =>
            assignedQuest.AssigneeCiv.NaturalWonders.Contains(assignedQuest.Data1);
    }

    public sealed class GiveGold : QuestImplementation
    {
        public override bool IsValid(Civilization civ, Civilization challenger) =>
            civ.GetMostRecentBully() != null;

        public override (string, string) GetAssignedQuestData(Civilization civ, Quest quest, Civilization assignee) =>
            (civ.GetMostRecentBully().NotNull(), "");

        /// <summary>Completed by CityStateFunctions.ReceiveGoldGift calling QuestManager.ReceivedGoldGift</summary>
        public override bool IsComplete(Civilization civ, AssignedQuest assignedQuest) => false;
    }

    public sealed class PledgeToProtect : QuestImplementation
    {
        public override bool IsValid(Civilization civ, Civilization challenger) =>
            civ.GetMostRecentBully() != null && !civ.CityStateFunctions.GetProtectorCivs().Contains(challenger);

        public override (string, string) GetAssignedQuestData(Civilization civ, Quest quest, Civilization assignee) =>
            (civ.GetMostRecentBully().NotNull(), "");

        public override bool IsComplete(Civilization civ, AssignedQuest assignedQuest) =>
            civ.CityStateFunctions.GetProtectorCivs().Contains(assignedQuest.AssigneeCiv);
    }

    public sealed class ContestCulture : QuestImplementation
    {
        public override (string, string) GetAssignedQuestData(Civilization civ, Quest quest, Civilization assignee) =>
            (assignee.TotalCultureForContests.ToString(CultureInfo.InvariantCulture), "");

        /// <summary>Completed by QuestManager.HandleGlobalQuest after it expires via duration</summary>
        public override bool IsComplete(Civilization civ, AssignedQuest assignedQuest) => false;
    }

    public sealed class ContestFaith : QuestImplementation
    {
        public override bool IsValid(Civilization civ, Civilization challenger) =>
            civ.GameInfo.IsReligionEnabled();

        public override (string, string) GetAssignedQuestData(Civilization civ, Quest quest, Civilization assignee) =>
            (assignee.TotalFaithForContests.ToString(CultureInfo.InvariantCulture), "");

        /// <summary>Completed by QuestManager.HandleGlobalQuest after it expires via duration</summary>
        public override bool IsComplete(Civilization civ, AssignedQuest assignedQuest) => false;
    }

    public sealed class ContestTech : QuestImplementation
    {
        public override (string, string) GetAssignedQuestData(Civilization civ, Quest quest, Civilization assignee) =>
            (assignee.Tech.GetNumberOfTechsResearched().ToString(CultureInfo.InvariantCulture), "");

        /// <summary>Completed by QuestManager.HandleGlobalQuest after it expires via duration</summary>
        public override bool IsComplete(Civilization civ, AssignedQuest assignedQuest) => false;
    }

    public sealed class Invest : QuestImplementation
    {
        public override (string, string) GetAssignedQuestData(Civilization civ, Quest quest, Civilization assignee) =>
            (quest.Description.GetPlaceholderParameters()[0], ""); // Kotlin first(): throws when there is no placeholder

        /// <summary>Never completed, just modifies gift effect and times out</summary>
        public override bool IsComplete(Civilization civ, AssignedQuest assignedQuest) => false;
    }

    public sealed class BullyCityState : QuestImplementation
    {
        public override bool IsValid(Civilization civ, Civilization challenger) =>
            civ.GetCityStateTarget(challenger) != null;

        public override (string, string) GetAssignedQuestData(Civilization civ, Quest quest, Civilization assignee) =>
            (civ.GetCityStateTarget(assignee).NotNull().CivId, "");

        /// <summary>Completed by CityStateFunctions.CityStateBullied calling QuestManager.CityStateBullied</summary>
        public override bool IsComplete(Civilization civ, AssignedQuest assignedQuest) => false;

        public override bool IsObsolete(Civilization civ, AssignedQuest assignedQuest) =>
            IsData1CivDefeated(assignedQuest, civ);
    }

    public sealed class DenounceCiv : QuestImplementation
    {
        public override bool IsValid(Civilization civ, Civilization challenger)
        {
            var mostRecentBully = civ.GetMostRecentBully();
            if (mostRecentBully == null) return false;
            var challengerDiplomacy = challenger.GetDiplomacyManager(mostRecentBully);
            if (challengerDiplomacy == null) return false;
            return !challengerDiplomacy.HasFlag(DiplomacyFlags.Denunciation)
                && challengerDiplomacy.DiplomaticStatus != DiplomaticStatus.War
                && !(challenger.PlayerType == PlayerType.Human
                    && civ.GameInfo.GetCivilization(mostRecentBully).PlayerType == PlayerType.Human);
        }

        public override (string, string) GetAssignedQuestData(Civilization civ, Quest quest, Civilization assignee) =>
            (civ.GetMostRecentBully().NotNull(), "");

        public override bool IsComplete(Civilization civ, AssignedQuest assignedQuest) =>
            assignedQuest.AssigneeCiv.GetDiplomacyManager(assignedQuest.Data1)?.HasFlag(DiplomacyFlags.Denunciation) == true;

        public override bool IsObsolete(Civilization civ, AssignedQuest assignedQuest) =>
            IsData1CivDefeated(assignedQuest, civ);
    }

    public sealed class SpreadReligion : QuestImplementation
    {
        public override bool IsValid(Civilization civ, Civilization challenger)
        {
            var playerReligion = civ.GameInfo.Religions.Values
                .FirstOrDefault(it => ReferenceEquals(it.FoundingCiv, challenger) && it.IsMajorReligion());
            if (playerReligion == null) return false;
            return civ.GetCapital().NotNull().Religion.GetMajorityReligion()?.Name != playerReligion.Name;
        }

        public override (string, string) GetAssignedQuestData(Civilization civ, Quest quest, Civilization assignee)
        {
            var playerReligion = civ.GameInfo.Religions.Values
                .First(it => ReferenceEquals(it.FoundingCiv, assignee) && it.IsMajorReligion()); // isQuestValid must have ensured this won't throw
            var data1 = playerReligion.GetReligionDisplayName(); // For display
            var data2 = playerReligion.Name; // To check completion
            return (data1, data2);
        }

        public override bool IsComplete(Civilization civ, AssignedQuest assignedQuest)
        {
            civ.GameInfo.Religions.TryGetValue(assignedQuest.Data2, out var religion);
            return ReferenceEquals(civ.GetCapital().NotNull().Religion.GetMajorityReligion(), religion);
        }
    }
}

/// <summary>
/// Kotlin <c>QuestName : IQuestImplementation by implementation</c>: each entry delegates to one QuestImplementation instance.
/// QuestName lives in Models (Quest.cs); the delegation is here because it needs Civilization.
/// </summary>
public static class QuestNameImplementation
{
    private static readonly ImmutableArray<IQuestImplementation> Implementations = [.. Enum.GetValues<QuestName>().Select(NewImplementation)];

    private static IQuestImplementation NewImplementation(QuestName name) => name switch
    {
        QuestName.None => new QuestImplementation.None(),
        QuestName.Route => new QuestImplementation.Route(),
        QuestName.ClearBarbarianCamp => new QuestImplementation.ClearBarbarianCamp(),
        QuestName.ConstructWonder => new QuestImplementation.ConstructWonder(),
        QuestName.ConnectResource => new QuestImplementation.ConnectResource(),
        QuestName.GreatPerson => new QuestImplementation.GreatPerson(),
        QuestName.ConquerCityState => new QuestImplementation.ConquerCityState(),
        QuestName.FindPlayer => new QuestImplementation.FindPlayer(),
        QuestName.FindNaturalWonder => new QuestImplementation.FindNaturalWonder(),
        QuestName.GiveGold => new QuestImplementation.GiveGold(),
        QuestName.PledgeToProtect => new QuestImplementation.PledgeToProtect(),
        QuestName.ContestCulture => new QuestImplementation.ContestCulture(),
        QuestName.ContestFaith => new QuestImplementation.ContestFaith(),
        QuestName.ContestTech => new QuestImplementation.ContestTech(),
        QuestName.Invest => new QuestImplementation.Invest(),
        QuestName.BullyCityState => new QuestImplementation.BullyCityState(),
        QuestName.DenounceCiv => new QuestImplementation.DenounceCiv(),
        QuestName.SpreadReligion => new QuestImplementation.SpreadReligion(),
        QuestName.TradeRoute => new QuestImplementation.TradeRoute(),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    public static IQuestImplementation GetImplementation(this QuestName name) => Implementations[(int)name];

    public static bool IsValid(this QuestName name, Civilization civ, Civilization challenger) =>
        name.GetImplementation().IsValid(civ, challenger);

    public static (string, string) GetAssignedQuestData(this QuestName name, Civilization civ, Quest quest, Civilization assignee) =>
        name.GetImplementation().GetAssignedQuestData(civ, quest, assignee);

    public static List<NotificationAction> GetNotificationActions(this QuestName name, Civilization civ, AssignedQuest assignedQuest) =>
        name.GetImplementation().GetNotificationActions(civ, assignedQuest);

    public static bool IsComplete(this QuestName name, Civilization civ, AssignedQuest assignedQuest) =>
        name.GetImplementation().IsComplete(civ, assignedQuest);

    public static bool IsObsolete(this QuestName name, Civilization civ, AssignedQuest assignedQuest) =>
        name.GetImplementation().IsObsolete(civ, assignedQuest);
}
