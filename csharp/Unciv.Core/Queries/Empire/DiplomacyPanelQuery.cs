using Unciv.Utils;
using Unciv.View;

namespace Unciv.Queries.Empire;

// Name: Modifier or flag name
// Value: Opinion modifier amount, or turns left of the flag
public sealed record DiplomacyValue(string Name, float Value);

// Status: Peace, Protector, War or DefensivePact. For a city-state it is the city-state's side (Kotlin: a protector is recorded on the city-state's manager toward us).
// Relationship: How the other civ sees the viewer (RelationshipLevel name), the colour of the diplomacy screen
// Modifiers: Reasons for the other civ's opinion of the viewer (not for city states, which use Influence)
// Flags: Active flags between the two, with turns left
// Influence, IsAlly: City states only: the viewer's influence there and whether the viewer is its ally
// TurnsToPeaceTreaty: Turns the current peace treaty still forbids war (0: none)
public sealed record DiplomacyPanelResult(string CivName, bool IsCityState, bool IsHuman, string Status, string Relationship,
    IReadOnlyList<DiplomacyValue> Modifiers, IReadOnlyList<DiplomacyValue> Flags, float? Influence, bool IsAlly, int TurnsToPeaceTreaty,
    bool CanDeclareWar, bool HasOpenBorders, bool TheyHaveOpenBordersWithUs, bool CanSignDeclarationOfFriendship, bool CanSignResearchAgreement,
    bool CanSignDefensivePact, bool CanOfferEmbassy, bool HasPendingOfferFromUs, int PendingOffersFromThem);

// T197: the per-civ diplomacy panel. Answers only about civs the viewer has met, like the diplomacy screen.
[QueryType("empire.DiplomacyPanel")]
public sealed record DiplomacyPanelQuery(string CivName) : IQuery<DiplomacyPanelResult>
{
    public string Type => "empire.DiplomacyPanel";

    public DiplomacyPanelResult Run(GameView view)
    {
        var civView = view.CivView;
        var civ = civView.Unwrap();
        var other = EmpireQueryHelpers.KnownCiv(view, CivName);
        var ours = civ.GetDiplomacyManager(other).NotNull();
        var theirs = other.GetDiplomacyManager(civ).NotNull();

        var modifiers = other.IsCityState ? [] : theirs.DiplomaticModifiers
            .Select(it => new DiplomacyValue(it.Key, it.Value)).OrderBy(it => it.Name, StringComparer.Ordinal).ToList();
        var flags = ours.FlagsCountdown
            .Select(it => new DiplomacyValue(it.Key, it.Value)).OrderBy(it => it.Name, StringComparer.Ordinal).ToList();
        var functions = civ.DiplomacyFunctions;
        return new DiplomacyPanelResult(other.CivName, other.IsCityState, other.IsHuman(), (other.IsCityState ? theirs : ours).DiplomaticStatus.ToString(),
            theirs.RelationshipLevel().ToString(), modifiers, flags,
            other.IsCityState ? theirs.GetInfluence() : null, other.IsCityState && other.AllyCiv == civ,
            ours.TurnsToPeaceTreaty(), ours.CanDeclareWar(), ours.HasOpenBorders, theirs.HasOpenBorders,
            functions.CanSignDeclarationOfFriendshipWith(other), functions.CanSignResearchAgreementWith(other),
            functions.CanSignDefensivePactWith(other), functions.CanOfferEmbassyTo(other),
            civView.GetTradeView(view.GetForeignCivView(other)).HasPendingOfferFromUs(),
            civ.TradeRequests.Count(it => it.RequestingCiv == other.CivId));
    }
}
