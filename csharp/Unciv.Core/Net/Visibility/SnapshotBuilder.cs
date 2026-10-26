using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Logic.Trade;
using Unciv.Utils;
using Unciv.View;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Net.Visibility;

// T174 (design/SERVER.md sections 2.3, 3). Builds the ClientSnapshot of one civ by walking the View classes, which are the single
// source of truth for what that civ may know (never reimplement IsExplored/IsVisible/isInvisible here). Output order is
// deterministic: civilization list order, tile list order, tile unit order.

/// <summary>Visibility filter: GameInfo plus viewer to <see cref="ClientSnapshot"/>.</summary>
public static class SnapshotBuilder
{
    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static ClientSnapshot Build(GameInfo gameInfo, CivilizationClass viewer, long seq, TurnStatusDto turnStatus)
    {
        var gameView = new GameView(gameInfo, viewer);
        var tiles = new List<TileDto>();
        var units = new List<UnitDto>();
        var emittedUnitIds = new HashSet<int>();
        var referencedCivs = new HashSet<string> { viewer.CivId };
        foreach (var tile in gameInfo.TileMap.TileList)
        {
            var tileView = gameView.TileMapView.GetTile(tile);
            if (!tileView.IsExplored()) continue;
            var tileDto = BuildTile(tileView, gameView);
            tiles.Add(tileDto);
            if (tileDto.Owner != null) referencedCivs.Add(tileDto.Owner);
            // Defense in depth: GetVisibleUnits already filters per unit, but fog must never depend on that alone.
            if (!tileView.IsVisible()) continue;
            foreach (var unit in tileView.GetVisibleUnits())
            {
                if (!emittedUnitIds.Add(unit.Id)) continue;
                units.Add(BuildUnit(unit, tileView));
                referencedCivs.Add(unit.Civ().CivId);
            }
        }

        var cities = new List<CityDto>();
        foreach (var civ in gameInfo.Civilizations)
        {
            var civView = gameView.GetForeignCivView(civ);
            foreach (var city in civ == viewer ? gameView.CivView.Cities().Cast<ForeignCityView>() : civView.Cities())
            {
                cities.Add(BuildCity(city));
                referencedCivs.Add(civ.CivId);
            }
        }

        var known = gameView.CivView.GetKnownCivs().Select(it => it.CivId).ToHashSet();
        referencedCivs.UnionWith(known);
        var civs = new List<CivDto>();
        foreach (var civ in gameInfo.Civilizations)
            if (referencedCivs.Contains(civ.CivId)) civs.Add(BuildCiv(gameView, civ));

        var snapshot = new ClientSnapshot
        {
            Seq = seq,
            Turn = gameView.GetTurn(),
            Phase = turnStatus.Phase,
            TurnStatus = turnStatus,
            You = viewer.CivId,
            Civs = civs,
            Tiles = tiles,
            Cities = cities,
            Units = units,
            Diplomacy = BuildDiplomacy(gameView, gameInfo, viewer, known),
            PendingTrades = BuildPendingTrades(gameView, gameInfo, viewer, known),
            Notifications = viewer.Notifications.Select(it => BuildNotification(it, gameInfo, gameView)).ToList()
        };
        return snapshot with { ViewHash = ComputeViewHash(snapshot) };
    }

    /// <summary>Canonical JSON of the snapshot with <see cref="ClientSnapshot.ViewHash"/> unset (property order is declaration order, camelCase, nulls omitted).</summary>
    public static string ToCanonicalJson(ClientSnapshot snapshot) => JsonSerializer.Serialize(snapshot with { ViewHash = null }, CanonicalOptions);

    /// <summary>FNV-1a (T011 <see cref="StateHasher"/>) over the canonical JSON; an integrity and bug detector, not a security control.</summary>
    public static string ComputeViewHash(ClientSnapshot snapshot) =>
        StateHasher.ComputeHexString(hasher => hasher.Add(Encoding.UTF8.GetBytes(ToCanonicalJson(snapshot))));

    /// <summary>The wire message for this snapshot (the Snapshot message carries each DTO as an opaque JSON element).</summary>
    public static Snapshot ToMessage(ClientSnapshot snapshot)
    {
        List<JsonElement> Elements<T>(List<T> items) => items.Select(it => JsonSerializer.SerializeToElement(it, CanonicalOptions)).ToList();
        return new Snapshot
        {
            Seq = snapshot.Seq,
            Turn = snapshot.Turn,
            Phase = snapshot.Phase,
            TurnStatus = new TurnStatus { Turn = snapshot.Turn, Ended = snapshot.TurnStatus.Ended, Deadline = snapshot.TurnStatus.Deadline, Phase = snapshot.TurnStatus.Phase },
            You = snapshot.You,
            Civs = Elements(snapshot.Civs),
            Tiles = Elements(snapshot.Tiles),
            Cities = Elements(snapshot.Cities),
            Units = Elements(snapshot.Units),
            Diplomacy = Elements(snapshot.Diplomacy),
            PendingTrades = Elements(snapshot.PendingTrades),
            Notifications = Elements(snapshot.Notifications),
            ViewHash = snapshot.ViewHash ?? ComputeViewHash(snapshot)
        };
    }

    internal static CivDto BuildCiv(GameView gameView, CivilizationClass civ)
    {
        var view = gameView.GetForeignCivView(civ);
        OwnCivDto? own = null;
        if (view.IsOwnedByViewer())
        {
            var ownView = gameView.CivView;
            own = new OwnCivDto
            {
                Gold = ownView.Gold,
                Happiness = ownView.GetHappiness(),
                CurrentTech = ownView.CurrentTechnologyName(),
                Techs = view.Ruleset.Technologies.Keys.Where(ownView.IsResearched).Order(StringComparer.Ordinal).ToList(),
                Policies = ownView.GetAdoptedPolicies().Order(StringComparer.Ordinal).ToList(),
                Spies = PrivateSpies.Build(gameView)
            };
        }
        return new CivDto
        {
            CivId = view.CivId,
            Name = view.CivName,
            IsCityState = view.IsCityState,
            IsMajor = view.IsMajorCiv(),
            IsBarbarian = view.IsBarbarian(),
            IsDefeated = view.IsDefeated(),
            Own = own
        };
    }

    internal static TileDto BuildTile(TileView tile, GameView gameView)
    {
        var position = tile.Position();
        var visible = tile.IsVisible();
        return new TileDto
        {
            X = position.X,
            Y = position.Y,
            Visible = visible,
            BaseTerrain = tile.BaseTerrain,
            Features = [.. tile.TerrainFeatures],
            NaturalWonder = tile.NaturalWonder,
            Resource = tile.GetViewableResource(gameView.CivView)?.Name,
            Improvement = tile.GetShownImprovement(),
            RiverBottom = tile.HasBottomRiver,
            RiverBottomLeft = tile.HasBottomLeftRiver,
            RiverBottomRight = tile.HasBottomRightRiver,
            Owner = visible ? tile.GetOwner()?.CivId : null,
            Road = visible ? tile.RoadStatus.ToString() : null,
            RoadPillaged = visible ? tile.RoadIsPillaged : null,
            ImprovementPillaged = visible ? tile.ImprovementIsPillaged : null
        };
    }

    internal static UnitDto BuildUnit(ForeignMapUnitView unit, TileView tile)
    {
        var position = tile.Position();
        var ownView = unit.TryGetMapUnitView();
        return new UnitDto
        {
            Id = unit.Id,
            Name = unit.Name,
            Owner = unit.Civ().CivId,
            X = position.X,
            Y = position.Y,
            HealthBucket = Math.Clamp((unit.UnitHealth + 24) / 25, 1, 4),
            IsEmbarked = unit.IsEmbarked(),
            IsFortified = unit.IsFortified(),
            // IsOwnedByViewer is defense in depth: TryGetMapUnitView also yields a view for spectator viewers, who never receive Own detail.
            Own = ownView == null || !unit.IsOwnedByViewer() ? null : new OwnUnitDto
            {
                Health = unit.UnitHealth,
                CurrentMovement = unit.CurrentMovement,
                Xp = ownView.Xp,
                Action = ownView.Action,
                Promotions = ownView.GetPromotionNames().Order(StringComparer.Ordinal).ToList(),
                Automated = ownView.IsAutomated(),
                CanBePromoted = ownView.CanBePromoted()
            }
        };
    }

    private static CityDto BuildCity(ForeignCityView city)
    {
        var position = city.Location();
        OwnCityDto? own = null;
        if (city.IsOwnedByViewer() && city.TryGetCityView() is { } cityView)
        {
            own = new OwnCityDto
            {
                Id = cityView.Unwrap().Id,
                FoodStored = cityView.GetFoodStored(),
                CultureStored = cityView.GetCultureStored(),
                Buildings = cityView.GetBuiltBuildings().Select(it => it.Name).ToList(),
                ConstructionQueue = [.. cityView.Constructions.ConstructionQueue]
            };
        }
        return new CityDto
        {
            Name = city.Name,
            Owner = city.OwningCiv().CivId,
            X = position.X,
            Y = position.Y,
            Population = city.GetPopulationCount(),
            IsCapital = city.IsCapital(),
            IsPuppet = city.IsPuppet(),
            IsBeingRazed = city.IsBeingRazed(),
            Health = city.GetHealth(),
            MaxHealth = city.GetMaxHealth(),
            VisibleBuildings = city.GetVisibleBuildings(),
            Own = own
        };
    }

    private static List<DiplomacyDto> BuildDiplomacy(GameView gameView, GameInfo gameInfo, CivilizationClass viewer, HashSet<string> known)
    {
        var members = gameInfo.Civilizations.Where(it => it == viewer || known.Contains(it.CivId)).Select(gameView.GetForeignCivView).ToList();
        var result = new List<DiplomacyDto>();
        for (var i = 0; i < members.Count; i++)
        {
            for (var j = i + 1; j < members.Count; j++)
            {
                var involvesViewer = members[i].IsOwnedByViewer() || members[j].IsOwnedByViewer();
                var atWar = members[i].IsAtWarWith(members[j]);
                // C021: a city-state's protector status lives on its side of the pair (it is the city-state's manager that says Protector), so a pair with a
                // city-state reports that side, whichever order the civs are listed in.
                var (from, to) = members[j].IsCityState && !members[i].IsCityState ? (members[j], members[i]) : (members[i], members[j]);
                // Peace between two third parties is not a public fact the viewer needs; wars are.
                if (involvesViewer || atWar)
                    result.Add(new DiplomacyDto { A = members[i].CivId, B = members[j].CivId, AtWar = atWar, Status = from.GetDiplomacyManagerWith(to)?.Status.ToString() });
            }
        }
        return result;
    }

    private static List<PendingTradeDto> BuildPendingTrades(GameView gameView, GameInfo gameInfo, CivilizationClass viewer, HashSet<string> known)
    {
        var result = new List<PendingTradeDto>();
        // Only requests the viewer sent or received: the holder of a request is always one of the two parties.
        foreach (var civ in gameInfo.Civilizations)
        {
            if (civ != viewer && !known.Contains(civ.CivId)) continue;
            foreach (var request in gameView.GetForeignCivView(civ).GetTradeRequestsVisibleToViewer())
            {
                // The trade is stored from the holder's side: TheirOffers are what the requester offers, OurOffers what it asks of the holder.
                result.Add(new PendingTradeDto
                {
                    From = request.RequestingCiv,
                    To = civ.CivId,
                    Offered = request.Trade.TheirOffers.Select(BuildOffer).ToList(),
                    Requested = request.Trade.OurOffers.Select(BuildOffer).ToList()
                });
            }
        }
        return result;
    }

    private static TradeOfferDto BuildOffer(TradeOffer offer) =>
        new() { Name = offer.Name, Type = offer.Type.ToString(), Amount = offer.Amount, Duration = offer.Duration };

    private static NotificationDto BuildNotification(Notification notification, GameInfo gameInfo, GameView gameView)
    {
        var actions = new List<NotificationActionDto>();
        foreach (var action in notification.Actions)
        {
            var dto = action switch
            {
                LocationAction a => Located("location", a.Location),
                CityAction a => Located("city", a.City),
                MapUnitAction a => Located("unit", a.Location),
                TechAction a => new NotificationActionDto { Kind = "tech", Name = a.TechName },
                // C052: both belong to the recipient's own civ (its unit id; its spies), so they carry nothing another civ's view could not hold
                PromoteUnitAction a => new NotificationActionDto { Kind = "promote", Id = a.Id },
                EspionageAction => new NotificationActionDto { Kind = "espionage" },
                _ => null
            };
            if (dto != null) actions.Add(dto);
        }
        return new NotificationDto
        {
            Category = notification.Category.ToString(),
            Text = notification.Text,
            Icons = [.. notification.Icons],
            Actions = actions
        };

        NotificationActionDto? Located(string kind, HexCoord location)
        {
            // A coordinate the recipient cannot currently see would leak it; drop the action instead.
            if (!gameInfo.TileMap.Contains(location) || gameView.TileMapView.GetTile(location) is not { } tile || !tile.IsVisible()) return null;
            return new NotificationActionDto { Kind = kind, X = location.X, Y = location.Y };
        }
    }
}
