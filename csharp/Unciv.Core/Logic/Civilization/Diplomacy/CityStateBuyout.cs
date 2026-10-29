using Unciv.Logic.Map.MapUnits;
using Unciv.Models.Ruleset.Unique;

namespace Unciv.Logic.Civilization.Diplomacy;

/// <summary>
/// B021 (bnw-spec 4.10), not in Kotlin: the Merchant of Venice buys a city-state, and a dead city-state keeps its friendships.
/// The take-over itself is <see cref="CityStateFunctions.TakeOver"/>, shared with the diplomatic marriage.
/// </summary>
public static class CityStateBuyout
{
    private static CityStateFunctions Functions(Civilization cityState) => (CityStateFunctions)cityState.CityStateFunctions;

    /// <summary>
    /// Why <paramref name="unit"/> (a Merchant of Venice: unique <c>Can buy a City-State</c>) cannot buy <paramref name="cityState"/> now, or null.
    /// Unlike the diplomatic marriage there is no ally requirement and no marriage cooldown. [?] The price is the marriage price (the same Gedemon DLL formula,
    /// <see cref="CityStateFunctions.GetDiplomaticMarriageCost"/>) and the unit is spent; the BNW engine's own price is not decoded.
    /// </summary>
    public static string? WhyCannotBeBoughtBy(Civilization cityState, MapUnit unit)
    {
        var buyer = unit.Civ;
        if (!unit.HasUnique(UniqueType.CanBuyCityState)) return $"{unit.Name} cannot buy city-states";
        if (!cityState.IsCityState || cityState.IsDefeated() || !cityState.Cities.Any() || cityState.GetCapital() == null) return "There is no city-state to buy";
        if (!buyer.Knows(cityState)) return $"You have not met {cityState.CivName}";
        if (buyer.IsAtWarWith(cityState)) return $"You are at war with {cityState.CivName}";
        if (!unit.HasMovement()) return "The unit has no movement left";
        var tile = unit.GetTile();
        if (!cityState.Cities.Any(it => it.GetCenterTile().AerialDistanceTo(tile) <= 1))
            return $"The unit must be next to a city of {cityState.CivName}";
        int cost = Functions(cityState).GetDiplomaticMarriageCost();
        return buyer.Gold < cost ? $"Buying {cityState.CivName} costs {cost} Gold" : null;
    }

    /// <summary>The buyer pays, the unit is spent, and every city of the city-state becomes a puppet of the buyer. <see cref="WhyCannotBeBoughtBy"/> must be null.</summary>
    public static void BuyCityState(Civilization cityState, MapUnit unit)
    {
        var buyer = unit.Civ;
        buyer.AddGold(-Functions(cityState).GetDiplomaticMarriageCost());
        unit.Consume();
        Functions(cityState).TakeOver(buyer, "bought");
    }

    /// <summary>
    /// FRIENDSHIP_*_ON_DEATH: when a city-state dies each major civ keeps the influence of its relationship at death (Ally 45, Friend 15,
    /// Neutral 0 in BNW), (stored on the city-state's side; nothing reads it back yet, liberation still sets MinorLiberationFriendship). The civ that destroyed it is not excluded [?]. Rulesets that state none of the constants (G&amp;K: all 0) keep the
    /// influence untouched. Worse relationships than Neutral are left alone [?].
    /// </summary>
    public static void KeepFriendshipsOnDeath(Civilization cityState)
    {
        var c = cityState.GameInfo.Ruleset.ModOptions.Constants;
        if (!cityState.IsCityState || (c.FriendshipFriendsOnDeath <= 0 && c.FriendshipAlliesOnDeath <= 0)) return;
        foreach (var diplomacyManager in cityState.Diplomacy.Values)
        {
            if (!diplomacyManager.OtherCiv.IsMajorCiv()) continue;
            float kept = diplomacyManager.RelationshipIgnoreAfraid() switch
            {
                RelationshipLevel.Ally => c.FriendshipAlliesOnDeath,
                RelationshipLevel.Friend => c.FriendshipFriendsOnDeath,
                RelationshipLevel.Neutral => c.FriendshipNeutralOnDeath,
                _ => diplomacyManager.Influence,
            };
            diplomacyManager.SetInfluenceWithoutSideEffects(kept);
        }
    }
}
