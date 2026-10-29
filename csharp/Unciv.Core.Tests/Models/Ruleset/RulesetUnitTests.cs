using Unciv.Core.Tests.Models.Ruleset.Unique;
using Unciv.Json;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Nation;
using Unciv.Models.Ruleset.Tech;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Models.StatTypes;
using StatsObject = Unciv.Models.StatTypes.Stats;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Core.Tests.Models.Ruleset;

/// <summary>
/// The unit and construction objects of T033. Kotlin's UnitUniquesTests needs a whole game (TestGame, T035); the expectations here are derived
/// by hand from the Kotlin source. Methods that need City/Civilization are tested through the seam fakes below.
/// </summary>
public class RulesetUnitTests
{
    private static T Read<T>(string json) => UncivJson.Deserialize<T>(json)!;

    // region fakes

    private sealed class ConstructionGameInfo : FakeGameInfo, IConstructionGameInfo
    {
        public Difficulty Difficulty { get; } = new();
        public Difficulty GetDifficulty() => Difficulty;
    }

    private sealed class ConstructionCiv(string civId, ConstructionGameInfo gameInfo) : FakeCiv(civId, gameInfo), IConstructionCivilization
    {
        public bool Human { get; set; } = true;
        public Difficulty CivDifficulty { get; } = new();
        public Era Era { get; } = new();
        public Counter<string> BuiltItems { get; } = new();
        public Counter<string> BoughtItems { get; } = new();
        IConstructionGameInfo IConstructionCivilization.GameInfo => gameInfo;
        public bool IsHuman() => Human;
        public Difficulty GetDifficulty() => CivDifficulty;
        public Era GetEra() => Era;
        public TileImprovement GetEquivalentTileImprovement(TileImprovement improvement) => improvement;
        public int GetBuiltItemsWithIncreasingCost(string name) => BuiltItems[name];
        public int GetBoughtItemsWithIncreasingPrice(string name) => BoughtItems[name];
    }

    private sealed class ConstructionCity(string id, ConstructionCiv civ) : FakeCity(id, civ), IConstructionCity
    {
        IConstructionCivilization IConstructionCity.Civ => civ;

        public IEnumerable<UniqueObject> GetMatchingUniques(UniqueType uniqueType, GameContext? gameContext = null)
        {
            var state = gameContext ?? State;
            return LocalUniques.Concat(civ.CivUniques).Where(it => it.Type == uniqueType && it.ConditionalsApply(state));
        }

        public override void ForEachMatchingUnique(UniqueType uniqueType, GameContext? gameContext, Action<UniqueObject> op)
        {
            foreach (var unique in GetMatchingUniques(uniqueType, gameContext)) op(unique);
        }
    }

    private sealed class FakeConstructionRuleset : IRulesetConstructionView
    {
        public GlobalUniques GlobalUniques { get; } = new();
        public Dictionary<string, UnitType> UnitTypes { get; } = [];
        public Dictionary<string, BaseUnit> Units { get; } = [];
        public Dictionary<string, Promotion> Promotions { get; } = [];
        public Dictionary<string, TileImprovement> Improvements { get; } = [];
        public Dictionary<string, HashSet<string>> TechFilters { get; } = [];
        public UnitType? GetUnitType(string name) => UnitTypes.GetValueOrDefault(name);
        public BaseUnit? GetUnit(string name) => Units.GetValueOrDefault(name);
        public IEnumerable<BaseUnit> GetAllUnits() => Units.Values;
        public IEnumerable<UnitType> GetAllUnitTypes() => UnitTypes.Values;
        public Promotion? GetPromotion(string name) => Promotions.GetValueOrDefault(name);
        public TileImprovement? GetTileImprovement(string name) => Improvements.GetValueOrDefault(name);
        public bool? TechnologyMatchesFilter(string techName, string filter, GameContext? state, bool multiFilter) =>
            TechFilters.TryGetValue(techName, out var filters) ? filters.Contains(filter) : null;
    }

    private static (ConstructionGameInfo Game, ConstructionCiv Civ, ConstructionCity City) MakeWorld()
    {
        var game = new ConstructionGameInfo();
        var civ = new ConstructionCiv("civ", game);
        game.FakeCivs.Add(civ);
        var city = new ConstructionCity("city", civ);
        civ.FakeCities.Add(city);
        foreach (var stat in Enum.GetValues<Stat>()) game.FakeSpeedData.StatCostModifiers[stat] = 1f;
        return (game, civ, city);
    }

    private static UniqueObject U(string text) => new(text);

    private static FakeConstructionRuleset MakeRuleset()
    {
        var ruleset = new FakeConstructionRuleset();
        ruleset.UnitTypes["Mounted"] = new UnitType("Mounted", "Land");
        ruleset.UnitTypes["Naval"] = new UnitType("Naval", "Water");
        ruleset.UnitTypes["Fighter"] = new UnitType("Fighter", "Air");
        return ruleset;
    }

    private static BaseUnit MakeUnit(FakeConstructionRuleset ruleset, string name, string type = "Mounted", int strength = 10, int ranged = 0, int movement = 2)
    {
        var unit = new BaseUnit { Name = name, UnitType = type, Strength = strength, RangedStrength = ranged, Movement = movement, Cost = 100 };
        ruleset.Units[name] = unit;
        unit.SetRuleset(ruleset);
        return unit;
    }

    // endregion

    // region IConstruction

    [Fact]
    public void PerpetualConstructionsKeepKotlinNamesAndOrder()
    {
        Assert.Equal(["Science", "Gold", "Culture", "Faith", "Food", "Nothing"], PerpetualConstruction.PerpetualConstructionsMap.Keys);
        Assert.Equal("Convert production to [Gold] at a rate of [rate] to 1", PerpetualConstruction.Gold.Description);
        Assert.Equal("The city will not produce anything.", PerpetualConstruction.Idle.Description);
        Assert.Equal(Stat.Faith, PerpetualConstruction.Faith.Stat);
        Assert.True(PerpetualConstruction.IsNamePerpetual(""));
        Assert.True(PerpetualConstruction.IsNamePerpetual("Nothing"));
        Assert.True(PerpetualConstruction.IsNamePerpetual("Food"));
        Assert.False(PerpetualConstruction.IsNamePerpetual("Library"));
        Assert.Same(Counter.ZERO, PerpetualConstruction.Science.GetResourceRequirementsPerTurn());
        Assert.Same(Counter.ZERO, PerpetualConstruction.Science.GetStockpiledResourceRequirements(GameContext.EmptyState));
        Assert.Empty(PerpetualConstruction.Science.RequiredResources());
    }

    [Fact]
    public void PurchaseReasonPurchasableMatchesKotlin()
    {
        Assert.Equal(["Allowed", "Invalid", "Unpurchasable", "UniqueAllowed", "Other", "OtherAllowed", "NotAllowed"], Enum.GetNames<PurchaseReason>());
        Assert.Equal(
            [PurchaseReason.Allowed, PurchaseReason.UniqueAllowed, PurchaseReason.OtherAllowed],
            Enum.GetValues<PurchaseReason>().Where(it => it.Purchasable()));
    }

    [Fact]
    public void RejectionReasonDefaultsComeFromTheType()
    {
        var reason = RejectionReasonType.RequiresBuildingInThisCity.ToInstance();
        Assert.Equal("Requires a specific building in this city!", reason.ErrorMessage);
        Assert.True(reason.ShouldShow);
        Assert.Same(reason, RejectionReasonType.RequiresBuildingInThisCity.ToInstance());

        var custom = RejectionReasonType.Obsoleted.ToInstance("Obsolete by Gunpowder");
        Assert.Equal("Obsolete by Gunpowder", custom.ErrorMessage);
        Assert.False(custom.ShouldShow);
        Assert.NotSame(custom, RejectionReasonType.Obsoleted.ToInstance());
        Assert.True(RejectionReasonType.NoPlaceToPutUnit.ToInstance("x", false) is { ShouldShow: false });
    }

    [Fact]
    public void RejectionReasonClassification()
    {
        Assert.True(RejectionReasonType.RequiresTech.ToInstance().TechPolicyEraWonderRequirements());
        Assert.False(RejectionReasonType.AlreadyBuilt.ToInstance().TechPolicyEraWonderRequirements());
        Assert.True(RejectionReasonType.MaxNumberBuildable.ToInstance().HasAReasonToBeRemovedFromQueue());
        Assert.True(RejectionReasonType.CanOnlyBePurchased.ToInstance().IsImportantRejection());
        Assert.True(RejectionReasonType.Unbuildable.ToInstance().IsConstructionRejection());
        Assert.True(RejectionReasonType.WonderBeingBuiltElsewhere.ToInstance().IsNeverVisible());
        Assert.False(RejectionReasonType.ConsumesResources.ToInstance().IsNeverVisible());
        Assert.Equal(0, RejectionReasonType.ShouldNotBeDisplayed.ToInstance().GetRejectionPrecedence());
        Assert.Equal(11, RejectionReasonType.NoPlaceToPutUnit.ToInstance().GetRejectionPrecedence());
        Assert.Equal(-1, RejectionReasonType.AlreadyBuilt.ToInstance().GetRejectionPrecedence());
        Assert.Equal(41, Enum.GetValues<RejectionReasonType>().Length);
    }

    // endregion

    // region UnitType, Promotion, UnitNameGroup

    [Fact]
    public void UnitTypeMovementTypeAndFilters()
    {
        var land = Read<UnitType>("""{ "name": "Melee", "movementType": "Land", "uniques": ["Tagged unit type"] }""");
        Assert.Equal(UnitMovementType.Land, land.GetMovementType());
        Assert.True(land.IsLandUnit());
        Assert.False(land.IsWaterUnit());
        Assert.False(land.IsAirUnit());
        Assert.Equal(UniqueTarget.UnitType, land.GetUniqueTarget());
        Assert.True(land.MatchesFilter("Land"));
        Assert.False(land.MatchesFilter("Water"));
        Assert.True(land.MatchesFilter("Melee"));
        Assert.True(land.MatchesFilter("Tagged unit type"));
        Assert.False(land.MatchesFilter("Other"));

        Assert.Null(new UnitType().GetMovementType());
        Assert.False(new UnitType().IsLandUnit());
        Assert.True(UnitType.City.IsLandUnit());
        Assert.Equal("City", UnitType.City.Name);
    }

    [Fact]
    public void UnitTypeWithUnknownMovementTypeThrowsOnUse()
    {
        var bad = new UnitType("Weird", "Space");
        Assert.Throws<ArgumentException>(() => bad.GetMovementType());
        Assert.Throws<ArgumentException>(() => bad.MatchesFilter("Land"));
        Assert.Throws<ArgumentException>(() => new UnitType("Weird", "land").IsLandUnit()); // valueOf is case-sensitive
    }

    [Fact]
    public void UnitTypeCivilopediaIteratorListsDomainsThenUsedTypes()
    {
        var ruleset = MakeRuleset();
        MakeUnit(ruleset, "Horseman");
        var names = UnitType.GetCivilopediaIterator(ruleset).Select(it => it.Name).ToList();
        Assert.Equal(["Domain: [Land]", "Domain: [Water]", "Domain: [Air]", "Mounted"], names);
        Assert.True(ruleset.UnitTypes["Mounted"].IsUsed(ruleset));
        Assert.False(ruleset.UnitTypes["Naval"].IsUsed(ruleset));
    }

    [Theory]
    [InlineData("Drill", 0, "Drill")]
    [InlineData("Drill I", 1, "Drill")]
    [InlineData("Drill II", 2, "Drill")]
    [InlineData("[Drill] III", 3, "Drill")]
    [InlineData("Drill IV", 0, "Drill IV")]
    [InlineData("DrillI", 0, "DrillI")]
    [InlineData(" I", 1, "")]
    [InlineData("I", 0, "I")]
    [InlineData("", 0, "")]
    public void PromotionBaseNameAndLevel(string name, int level, string baseName)
    {
        var result = Promotion.GetBaseNameAndLevel(name);
        Assert.Equal(name.Replace("[", "").Replace("]", ""), result.NameWithoutBrackets);
        Assert.Equal(level, result.Level);
        Assert.Equal(baseName, result.BasePromotionName);
    }

    [Fact]
    public void PromotionReadsJsonAndClonesSharedLists()
    {
        var promotion = Read<Promotion>("""{ "name": "Shock I", "prerequisites": ["Drill I"], "unitTypes": ["Melee"], "innerColor": [255, 0, 0], "row": 2, "column": 3, "uniques": ["[+25]% Strength"] }""");
        Assert.Equal(UniqueTarget.Promotion, promotion.GetUniqueTarget());
        Assert.Equal(["Drill I"], promotion.Prerequisites);
        Assert.Equal(1f, promotion.GetInnerColorObject()!.R);
        Assert.Null(promotion.GetOuterColorObject());
        var clone = promotion.Clone();
        Assert.Equal((2, 3), (clone.Row, clone.Column));
        Assert.Same(promotion.Uniques, clone.Uniques);
        Assert.Null(clone.InnerColor); // not copied by Kotlin's clone
        var defaults = new Promotion();
        Assert.Equal((-1, 0), (defaults.Row, defaults.Column));
    }

    [Fact]
    public void UnitNameGroupFindsUnitsByNameOrTag()
    {
        var ruleset = MakeRuleset();
        var hero = MakeUnit(ruleset, "Great General");
        hero.Uniques = ["[This Unit] gets a name from the [Greek Heroes] group"];
        hero.SetRuleset(ruleset);
        var tagged = MakeUnit(ruleset, "Great Admiral");
        tagged.Uniques = ["[This Unit] gets a name from the [Hero Tag] group"];
        tagged.SetRuleset(ruleset);
        MakeUnit(ruleset, "Other");

        var group = new UnitNameGroup { Name = "Greek Heroes", Uniques = ["Hero Tag"], UnitNames = ["Achilles"] };
        Assert.Equal(["Great General", "Great Admiral"], group.GetUnits(ruleset).Select(it => it.Name));
        var plain = new UnitNameGroup { Name = "Greek Heroes" };
        Assert.Equal(["Great General"], plain.GetUnits(ruleset).Select(it => it.Name));
        Assert.Equal(UniqueTarget.EventChoice, group.GetUniqueTarget());

        var clone = group.Clone();
        Assert.Equal("Greek Heroes", clone.Name);
        Assert.Same(group.UnitNames, clone.UnitNames);
        Assert.Same(group.Uniques, clone.Uniques);
    }

    // endregion

    // region BaseUnit

    [Fact]
    public void BaseUnitReadsJsonAndHasKotlinDefaults()
    {
        var unit = Read<BaseUnit>("""{ "name": "Horseman", "cost": 40, "movement": 4, "strength": 12, "unitType": "Mounted", "requiredTech": "Horseback Riding", "obsoleteTech": "Chivalry", "upgradesTo": "Knight", "promotions": ["Shock I", "Drill I"], "hurryCostModifier": 20 }""");
        Assert.Equal((40, 4, 12, 20), (unit.Cost, unit.Movement, unit.Strength, unit.HurryCostModifier));
        Assert.Equal(["Shock I", "Drill I"], unit.Promotions);
        Assert.Equal(["Chivalry"], unit.TechsThatObsoleteThis());
        Assert.Equal(UniqueTarget.Unit, unit.GetUniqueTarget());
        Assert.Equal(["Horseback Riding"], ((IHasUniques)unit).RequiredTechs());

        var defaults = new BaseUnit();
        Assert.Equal((-1, 2, 0, 0), (defaults.Cost, defaults.Range, defaults.RangedStrength, defaults.InterceptRange));
        Assert.Empty(defaults.TechsAtWhichNoLongerAvailable());
        Assert.Throws<InvalidOperationException>(() => defaults.Ruleset);
    }

    [Fact]
    public void SetRulesetAddsGlobalAndUnitTypeUniques()
    {
        var ruleset = MakeRuleset();
        ruleset.GlobalUniques.UnitUniques = ["Must set up to ranged attack"];
        ruleset.UnitTypes["Mounted"].Uniques = ["May Paradrop to [Land] tiles up to [5] tiles away"];
        var unit = new BaseUnit { Name = "Horseman", UnitType = "Mounted", Uniques = ["Can upgrade to [Knight]", "Great Person - [Engineer]"] };
        var self = (IHasUniques)unit;

        Assert.False(self.HasUnique(UniqueType.MayParadrop));
        Assert.True(self.HasUnique(UniqueType.CanUpgrade));
        unit.SetRuleset(ruleset);
        Assert.True(self.HasUnique(UniqueType.MayParadrop));
        Assert.True(self.HasUnique(UniqueType.MustSetUp));
        Assert.True(self.HasTagUnique("Must set up to ranged attack"));
        Assert.Single(self.GetMatchingUniques(UniqueType.MayParadrop));
        Assert.Single(self.GetMatchingTagUniques("Must set up to ranged attack", null));
        Assert.Equal(4, unit.RulesetUniqueObjects.Count);
        var seen = new List<string>();
        self.ForEachMatchingUnique(UniqueType.MustSetUp, GameContext.EmptyState, it => seen.Add(it.Text));
        Assert.Equal(["Must set up to ranged attack"], seen);
        // the own uniques stay what json set
        Assert.Equal(2, unit.UniqueObjects.Count);
        Assert.True(((IRulesetUnitParameterView)unit).HasGreatPersonUnique);
        Assert.True(unit.IsGreatPerson);
        Assert.True(unit.IsGreatPersonOfType("Engineer"));
        Assert.False(unit.IsGreatPersonOfType("Scientist"));
        Assert.Same(ruleset.UnitTypes["Mounted"], unit.Type);
    }

    [Fact]
    public void LazyMembersRetryAfterAnException()
    {
        // Kotlin `by lazy` does not cache an exception
        var ruleset = MakeRuleset();
        var unit = new BaseUnit { Name = "Horseman", UnitType = "Mounted" };
        Assert.Throws<InvalidOperationException>(() => unit.IsLandUnit);
        unit.SetRuleset(ruleset);
        Assert.True(unit.IsLandUnit);
        Assert.False(unit.IsWaterUnit);
    }

    [Fact]
    public void PromotionsIterateInJvmHashOrderAndHideNothingFromTheView()
    {
        var unit = Read<BaseUnit>("""{ "name": "U", "promotions": ["b", "a", "c"] }""");
        Assert.Equal(["a", "b", "c"], unit.Promotions); // String.hashCode order, not insertion order
        var ruleset = MakeRuleset();
        ruleset.GlobalUniques.UnitUniques = ["Must set up to ranged attack"];
        var v = MakeUnit(ruleset, "V");
        Assert.True(((IRulesetObjectParameterView)v).HasTagUniqueIgnoringConditionals("Must set up to ranged attack"));
    }

    [Fact]
    public void UnitWithUnknownUnitTypeFailsOnSetRuleset()
    {
        var unit = new BaseUnit { Name = "Ghost", UnitType = "Nope" };
        var error = Assert.Throws<InvalidOperationException>(() => unit.SetRuleset(MakeRuleset()));
        Assert.Equal("Unit Ghost has unit type Nope which is not present in ruleset!", error.Message);
    }

    [Fact]
    public void BaseUnitMatchesFilters()
    {
        var ruleset = MakeRuleset();
        ruleset.TechFilters["Horseback Riding"] = ["Ancient era"];
        var horseman = MakeUnit(ruleset, "Horseman", strength: 12, movement: 4);
        horseman.RequiredTech = "Horseback Riding";
        horseman.Replaces = "Scout";
        var archer = MakeUnit(ruleset, "Archer", ranged: 6, strength: 3);
        var worker = MakeUnit(ruleset, "Worker", strength: 0);
        var ship = MakeUnit(ruleset, "Galley", "Naval");
        var plane = MakeUnit(ruleset, "Fighter", "Fighter", ranged: 20);

        Assert.True(horseman.IsMelee());
        Assert.False(horseman.IsRanged());
        Assert.True(archer.IsRanged());
        Assert.False(archer.IsMelee()); // ranged wins over strength
        Assert.True(worker.IsCivilian());
        Assert.False(worker.IsMilitary);
        Assert.True(ship.IsWaterUnit);
        Assert.True(plane.IsAirUnit());

        foreach (var filter in new[] { "all", "All", "Melee", "Military", "Land", "non-air", "Mounted", "Horseman", "Scout", "Ancient era",
                     "Mounted units", "Military units", "{Land} {Melee}", "non-[Ranged]" })
            Assert.True(horseman.MatchesFilter(filter), filter);
        foreach (var filter in new[] { "Ranged", "Civilian", "Water", "Air", "Nuclear Weapon", "Great Person", "Religious", "Foo units", "Modern era", "units" })
            Assert.False(horseman.MatchesFilter(filter), filter);
        Assert.True(horseman.MatchesFilter("Ranged", multiFilter: false) == false);
        Assert.True(worker.MatchesFilter("Civilian units"));
        Assert.True(ship.MatchesFilter("Water"));
        Assert.True(plane.MatchesFilter("Air"));
        Assert.False(plane.MatchesFilter("non-air"));
        Assert.False(plane.MatchesFilter("non-[Air]"));
    }

    [Fact]
    public void BaseUnitFilterCacheIsKeyedByFilterAlone()
    {
        // Kotlin quirk: cachedMatchesFilterResult.getOrPut(filter) ignores the state, so the first answer sticks
        var ruleset = MakeRuleset();
        ruleset.TechFilters["Horseback Riding"] = ["Ancient era"];
        var unit = MakeUnit(ruleset, "Horseman");
        unit.RequiredTech = "Horseback Riding";
        Assert.True(unit.MatchesFilter("Ancient era", GameContext.EmptyState));
        ruleset.TechFilters["Horseback Riding"] = [];
        Assert.True(unit.MatchesFilter("Ancient era"));
    }

    [Fact]
    public void BaseUnitUpgradeUnitsAndAutoUpgrade()
    {
        var ruleset = MakeRuleset();
        var knight = MakeUnit(ruleset, "Knight");
        var lancer = MakeUnit(ruleset, "Lancer");
        var unit = MakeUnit(ruleset, "Horseman");
        unit.UpgradesTo = "Knight";
        unit.ObsoleteTech = "Chivalry";
        unit.Uniques = ["Can upgrade to [Lancer]", "Can upgrade to [Ghost]"];
        unit.SetRuleset(ruleset);
        Assert.Equal(["Knight", "Lancer", "Ghost"], unit.GetUpgradeUnits());
        Assert.Equal([knight, lancer], unit.GetRulesetUpgradeUnits());
        Assert.Equal("Knight", unit.AutomaticallyUpgradedInProductionToUnitByTech("Chivalry"));
        Assert.Null(unit.AutomaticallyUpgradedInProductionToUnitByTech("Steel"));
        Assert.Same(unit, unit.GetReplacedUnit(ruleset));
        unit.Replaces = "Knight";
        Assert.Same(knight, unit.GetReplacedUnit(ruleset));
    }

    [Fact]
    public void BaseUnitResourceRequirements()
    {
        var ruleset = MakeRuleset();
        var unit = MakeUnit(ruleset, "Horseman");
        unit.RequiredResource = "Horses";
        unit.Uniques = ["Consumes [2] [Iron]", "Consumes [1] [Horses]"];
        unit.SetRuleset(ruleset);
        var requirements = unit.GetResourceRequirementsPerTurn();
        Assert.Equal(["Horses", "Iron"], requirements.Keys);
        Assert.Equal(2, requirements["Horses"]); // Counter.add on top of the requiredResource
        Assert.Equal(2, requirements["Iron"]);
        Assert.Equal(["Horses", "Iron"], ((IConstruction)unit).RequiredResources());
    }

    [Fact]
    public void BaseUnitUnavailableWithoutNukes()
    {
        var ruleset = MakeRuleset();
        var nuke = MakeUnit(ruleset, "Nuke");
        nuke.Uniques = ["Nuclear weapon of Strength [2]"];
        nuke.SetRuleset(ruleset);
        var game = new FakeGameInfo();
        game.GameParameters.NuclearWeaponsEnabled = false;
        Assert.True(nuke.IsUnavailableBySettings(game));
        game.GameParameters.NuclearWeaponsEnabled = true;
        Assert.False(nuke.IsUnavailableBySettings(game));
        Assert.False(MakeUnit(ruleset, "Horseman").IsUnavailableBySettings(new FakeGameInfo()));
    }

    [Theory]
    [InlineData(10, 0, 2, "Mounted", 38)] // 10^1.5 * 2^0.3 = 38.93
    [InlineData(6, 12, 2, "Mounted", 45)] // ranged power replaces melee: 12^1.45 * 2^0.3 = 45.19
    [InlineData(6, 12, 2, "Naval", 22)] // ranged naval power is halved: 22.59
    [InlineData(0, 0, 5, "Mounted", 0)]
    [InlineData(10, 0, 0, "Mounted", 0)] // movement 0 -> 0^0.3 = 0
    public void BaseUnitForceEvaluation(int strength, int ranged, int movement, string type, int expected)
    {
        var ruleset = MakeRuleset();
        var unit = MakeUnit(ruleset, "Unit", type, strength, ranged, movement);
        Assert.Equal(expected, unit.GetForceEvaluation());
    }

    [Theory]
    [InlineData("[+50]% Strength", 58)] // static bonus: 38.93 * 1.5
    [InlineData("[+50]% Strength <vs cities>", 48)] // half the bonus: 38.93 * 1.25
    [InlineData("[+50]% Strength <vs [Mounted] units>", 43)] // a quarter of the bonus: 38.93 * 1.125
    [InlineData("[-50]% Strength", 38)] // non-positive bonuses are skipped
    [InlineData("May Paradrop to [Land] tiles up to [5] tiles away", 48)] // 38.93 * 1.25
    [InlineData("Must set up to ranged attack", 32)] // 38.93 / 1.2
    [InlineData("[3] additional attacks per turn", 46)] // 38.93 * (20 * min(3, movement - 1)).toPercent() = 38.93 * 1.2
    public void BaseUnitForceEvaluationUniques(string unique, int expected)
    {
        var ruleset = MakeRuleset();
        var unit = new BaseUnit { Name = "Unit", UnitType = "Mounted", Strength = 10, Movement = 2, Uniques = [unique] };
        unit.SetRuleset(ruleset);
        Assert.Equal(expected, unit.GetForceEvaluation());
    }

    [Fact]
    public void BaseUnitForceEvaluationSeesPromotionUniquesAndCachesTheResult()
    {
        var ruleset = MakeRuleset();
        ruleset.Promotions["Shock I"] = new Promotion { Name = "Shock I", Uniques = ["[+50]% Strength"] };
        var unit = MakeUnit(ruleset, "Horseman");
        unit.Promotions.Add("Shock I");
        unit.Promotions.Add("Unknown");
        Assert.Equal(58, unit.GetForceEvaluation());
        unit.Strength = 100;
        Assert.Equal(58, unit.GetForceEvaluation()); // cached
    }

    [Fact]
    public void BaseUnitSiegeAndCityFounder()
    {
        var ruleset = MakeRuleset();
        var catapult = MakeUnit(ruleset, "Catapult", ranged: 8);
        catapult.Uniques = ["[+100]% Strength <vs cities>"];
        catapult.SetRuleset(ruleset);
        Assert.True(catapult.IsProbablySiegeUnit());
        Assert.False(MakeUnit(ruleset, "Archer", ranged: 8).IsProbablySiegeUnit());
        var settler = MakeUnit(ruleset, "Settler", strength: 0);
        settler.Uniques = ["Founds a new city"];
        settler.SetRuleset(ruleset);
        Assert.True(settler.IsCityFounder());
    }

    // endregion

    // region BaseUnit cost math

    [Fact]
    public void BaseUnitProductionCostFollowsKotlinFloatMath()
    {
        var ruleset = MakeRuleset();
        var unit = MakeUnit(ruleset, "Horseman");
        unit.Uniques = ["Cost increases by [10] per owned city", "Cost increases by [5] when built", "[50]% production cost"];
        unit.SetRuleset(ruleset);
        var (game, civ, city) = MakeWorld();
        civ.FakeCities.Add(new ConstructionCity("c2", civ));
        civ.FakeCities.Add(new ConstructionCity("c3", civ));
        civ.BuiltItems["Horseman"] = 2;
        civ.CivDifficulty.UnitCostModifier = 0.5f;
        game.Difficulty.AiUnitCostModifier = 2f;

        // (100 + 3 * 10 + 2 * 5) * 1.5 = 210; human: * 0.5 = 105
        Assert.Equal(105, unit.GetProductionCost(civ, null));
        Assert.Equal(105, unit.GetProductionCost(civ, city));
        civ.Human = false;
        Assert.Equal(420, unit.GetProductionCost(civ, null)); // AI uses the game difficulty
        civ.IsCityState = true;
        Assert.Equal(315, unit.GetProductionCost(civ, null)); // city states * 1.5
        game.FakeSpeedData.ProductionCostModifier = 0.5f;
        Assert.Equal(157, unit.GetProductionCost(civ, null)); // 157.5 truncates
    }

    [Fact]
    public void BaseUnitDisbandGoldTruncatesBeforeDividing()
    {
        var ruleset = MakeRuleset();
        var unit = MakeUnit(ruleset, "Horseman");
        var (_, civ, _) = MakeWorld();
        // (30 * 100)^0.75 = 405.36 -> 405 / 20
        Assert.Equal(20, unit.GetDisbandGold(civ));
    }

    [Fact]
    public void BaseUnitPurchaseWithStatAndBuyCosts()
    {
        var ruleset = MakeRuleset();
        var unit = MakeUnit(ruleset, "Horseman");
        var (game, civ, city) = MakeWorld();
        game.FakeSpeedData.StatCostModifiers[Stat.Culture] = 2f;
        city.CityFilters.Add("in all cities");
        city.LocalUniques.Add(U("May buy [Military] units for [100] [Culture] [in all cities]"));
        city.LocalUniques.Add(U("May buy [Civilian] units with [Faith] [in all cities]"));

        Assert.True(unit.CostFunctions.CanBePurchasedWithStat(city, Stat.Culture));
        Assert.False(unit.CostFunctions.CanBePurchasedWithStat(city, Stat.Faith)); // Military, not Civilian
        Assert.False(unit.CostFunctions.CanBePurchasedWithStat(city, Stat.Gold));
        Assert.Equal([200f], unit.CostFunctions.GetBaseBuyCosts(city, Stat.Culture));
        Assert.Equal(200, unit.GetStatBuyCost(city, Stat.Culture));
        Assert.Equal(200f, unit.GetBaseBuyCost(city, Stat.Culture));

        // No specific cost for Gold: the generic gold cost (30 * 100)^0.75 = 405.36, then truncated to a multiple of 10
        Assert.Empty(unit.CostFunctions.GetBaseBuyCosts(city, Stat.Gold));
        Assert.Equal(405.36f, unit.GetBaseBuyCost(city, Stat.Gold)!.Value, 0.01f);
        Assert.Equal(400, unit.GetStatBuyCost(city, Stat.Gold));
        city.LocalUniques.Add(U("[Gold] cost of purchasing [Military] units [-50]%"));
        city.LocalUniques.Add(U("[Gold] cost of purchasing [Civilian] units [-50]%"));
        city.LocalUniques.Add(U("[Gold] cost of purchasing items in cities [-10]%"));
        Assert.Equal(180, unit.GetStatBuyCost(city, Stat.Gold)); // 405.36 * 0.5 * 0.9 = 182.4
    }

    [Fact]
    public void BaseUnitCanBePurchasedWithStatWithoutCityUsesTheConstructionDefault()
    {
        var ruleset = MakeRuleset();
        var unit = MakeUnit(ruleset, "Horseman");
        var construction = (INonPerpetualConstruction)unit;
        Assert.True(unit.CanBePurchasedWithStat(null, Stat.Gold));
        Assert.False(unit.CanBePurchasedWithStat(null, Stat.Faith));
        Assert.False(unit.CanBePurchasedWithStat(null, Stat.Production));
        Assert.Equal(PurchaseReason.Invalid, construction.CanBePurchasedWithStatReasons(null, Stat.Happiness));
    }

    // endregion

    // region Building

    [Fact]
    public void BuildingReadsJsonAndHasKotlinDefaults()
    {
        var library = Read<Building>("""{ "name": "Library", "cost": 75, "science": 2, "maintenance": 1, "percentStatBonus": { "science": 50 }, "requiredTech": "Writing", "specialistSlots": { "Scientist": 1 }, "greatPersonPoints": { "Great Scientist": 3 }, "requiredNearbyImprovedResources": ["Iron"], "cityStrength": 2.5, "uniques": ["Consumes [1] [Coal]"] }""");
        Assert.Equal((75, 1, 2.5, 0), (library.Cost, library.Maintenance, library.CityStrength, library.CityHealth));
        Assert.Equal(2f, library.Science);
        Assert.Equal(50f, library.PercentStatBonus!.Science);
        Assert.Equal(1, library.NewSpecialists()["Scientist"]);
        Assert.Equal(3, library.GreatPersonPoints["Great Scientist"]);
        Assert.Equal(["Iron"], library.RequiredNearbyImprovedResources);
        Assert.Equal(["Writing"], ((IHasUniques)library).RequiredTechs());
        Assert.Equal(UniqueTarget.Building, library.GetUniqueTarget());
        Assert.Equal(UniqueTarget.Wonder, new Building { Name = "B", IsWonder = true }.GetUniqueTarget());
        Assert.Equal(UniqueTarget.Wonder, new Building { Name = "B", IsNationalWonder = true }.GetUniqueTarget());

        var defaults = new Building { Name = "B" };
        Assert.Equal((-1, 0, 0), (defaults.Cost, defaults.HurryCostModifier, defaults.Maintenance));
        Assert.Null(defaults.PercentStatBonus);
        Assert.True(defaults.IsSellable());
        Assert.False(new Building { Name = "B", IsWonder = true }.IsSellable());
        Assert.False(new Building { Name = "B", Uniques = ["Unsellable"] }.IsSellable());
    }

    [Fact]
    public void BuildingPercentageBonusesAreCopiedForTheInitialStats()
    {
        var building = new Building { Name = "Library", PercentStatBonus = new StatsObject { Science = 50 } };
        var bonuses = building.GetStatPercentageBonuses(null);
        bonuses.Science = 99;
        Assert.Equal(50f, building.PercentStatBonus.Science);
        Assert.Equal(0f, new Building { Name = "B" }.GetStatPercentageBonuses(null).Science);
    }

    [Fact]
    public void BuildingMatchesFilters()
    {
        var library = new Building { Name = "Library", Science = 2, Replaces = "Scriptorium", RequiredTech = "Writing" };
        var wonder = new Building { Name = "Pyramids", IsWonder = true };
        var national = new Building { Name = "Heroic Epic", IsNationalWonder = true, Uniques = ["Tagged building"] };

        foreach (var filter in new[] { "all", "Building", "Buildings", "Library", "Scriptorium", "Science", "non-[Wonder]", "{Building} {Science}" })
            Assert.True(library.MatchesFilter(filter), filter);
        foreach (var filter in new[] { "Wonder", "National", "World", "Gold", "Production", "Pyramids" })
            Assert.False(library.MatchesFilter(filter), filter);
        Assert.True(wonder.MatchesFilter("Wonders"));
        Assert.True(wonder.MatchesFilter("World Wonder"));
        Assert.False(wonder.MatchesFilter("National Wonder"));
        Assert.False(wonder.MatchesFilter("Building"));
        Assert.True(national.MatchesFilter("National Wonder"));
        Assert.True(national.MatchesFilter("Wonder"));
        Assert.False(national.MatchesFilter("World"));
        Assert.True(national.MatchesFilter("Tagged building"));
        Assert.True(national.MatchesFilter("Tagged building", GameContext.EmptyState));
    }

    [Fact]
    public void BuildingFilterUsesRequiredTechOnceRulesetIsSet()
    {
        var ruleset = MakeRuleset();
        ruleset.TechFilters["Writing"] = ["Ancient era"];
        var library = new Building { Name = "Library", RequiredTech = "Writing" };
        Assert.False(library.MatchesFilter("Ancient era"));
        Assert.Throws<InvalidOperationException>(() => library.Ruleset);
        var other = new Building { Name = "Other", RequiredTech = "Writing", Ruleset = ruleset };
        Assert.True(other.MatchesFilter("Ancient era"));
    }

    [Fact]
    public void BuildingIsStatRelatedLooksAtUniquesToo()
    {
        Assert.True(new Building { Name = "B", Gold = 1 }.IsStatRelated(Stat.Gold));
        Assert.True(new Building { Name = "B", PercentStatBonus = new StatsObject { Culture = 10 } }.IsStatRelated(Stat.Culture));
        Assert.True(new Building { Name = "B", Uniques = ["[+2 Gold]"] }.IsStatRelated(Stat.Gold));
        Assert.False(new Building { Name = "B", Uniques = ["[+2 Gold]"] }.IsStatRelated(Stat.Food));
        Assert.True(new Building { Name = "B", Uniques = ["Removes extra unhappiness from annexed cities"] }.IsStatRelated(Stat.Happiness));
        Assert.False(new Building { Name = "B" }.IsStatRelated(Stat.Happiness));
        Assert.True(new Building { Name = "B", Uniques = ["[+1 Food] from [Plains] tiles [in all cities]"] }.IsStatRelated(Stat.Food));
    }

    [Fact]
    public void BuildingStatsIncludeUniquesAndCityBonuses()
    {
        var (_, civ, city) = MakeWorld();
        var library = new Building { Name = "Library", Science = 2, Uniques = ["[+3 Culture]"] };
        city.LocalUniques.Add(U("[+1 Science] from every [Library]"));
        city.LocalUniques.Add(U("[+5 Gold] from every [Wonder]"));
        city.LocalUniques.Add(U("[+1 Faith] from all [Building] buildings"));
        city.LocalUniques.Add(U("[+25]% [Science] from every [Library]"));
        city.LocalUniques.Add(U("[+10]% [Gold] from every [Wonder]"));
        civ.CivUniques.Add(U("[+100]% [Production] from every [Building]"));
        var stats = library.GetStats(city);
        Assert.Equal((3f, 3f, 0f, 1f), (stats.Science, stats.Culture, stats.Gold, stats.Faith));
        Assert.Equal(2f, library.Science); // the building itself is untouched
        var bonuses = library.GetStatPercentageBonuses(city);
        Assert.Equal(25f, bonuses.Science);
        Assert.Equal(100f, bonuses.Production);
        Assert.Equal(0f, bonuses.Gold);
        Assert.True(library.IsStatRelated(Stat.Faith, city));
        Assert.False(library.IsStatRelated(Stat.Food, city));

        var wonder = new Building { Name = "Pyramids", IsWonder = true };
        Assert.Equal(5f, wonder.GetStats(city).Gold);
        Assert.Equal(0f, wonder.GetStats(city).Faith); // wonders do not get StatsFromBuildings
    }

    [Fact]
    public void BuildingProductionCostFollowsKotlinFloatMath()
    {
        var (game, civ, city) = MakeWorld();
        civ.FakeCities.Add(new ConstructionCity("c2", civ));
        civ.CivDifficulty.BuildingCostModifier = 0.75f;
        game.Difficulty.AiBuildingCostModifier = 1.25f;
        game.Difficulty.AiWonderCostModifier = 1.5f;
        var building = new Building { Name = "Library", Cost = 100 };
        var wonder = new Building { Name = "Pyramids", Cost = 100, IsWonder = true };

        Assert.Equal(75, building.GetProductionCost(civ, city));
        Assert.Equal(100, wonder.GetProductionCost(civ, null)); // human wonders ignore the building modifier
        civ.Human = false;
        Assert.Equal(125, building.GetProductionCost(civ, null));
        Assert.Equal(150, wonder.GetProductionCost(civ, null));
        civ.IsCityState = true;
        Assert.Equal(150, building.GetProductionCost(civ, null));

        civ.IsCityState = false;
        civ.Human = true;
        civ.BuiltItems["Library"] = 3;
        game.FakeSpeedData.ProductionCostModifier = 1.5f;
        var costly = new Building { Name = "Library", Cost = 100, Uniques = ["Cost increases by [10] when built", "Cost increases by [20] per owned city", "[-30]% production cost"] };
        // (100 + 3 * 10 + 2 * 20) = 170; * 0.7 = 119; * 0.75 = 89.25; * 1.5 = 133.875
        Assert.Equal(133, costly.GetProductionCost(civ, city));
    }

    [Fact]
    public void BuildingGoldCostFormulas()
    {
        var building = new Building { Name = "Library", Cost = 100 };
        var construction = (INonPerpetualConstruction)building;
        Assert.Equal(400, construction.GetCivilopediaGoldCost()); // (30 * 100)^0.75 = 405.36
        building.HurryCostModifier = 20;
        Assert.Equal(480, construction.GetCivilopediaGoldCost()); // 486.4
        Assert.Equal(160, construction.GetCostForConstructionsIncreasingInPrice(100, 10, 3)); // 100 + 5 * 12
        Assert.Equal(100, construction.GetCostForConstructionsIncreasingInPrice(100, 10, 0));
        Assert.Equal(107, construction.GetCostForConstructionsIncreasingInPrice(100, 7, 1)); // 100 + 3.5 * 2 = 107
        var (_, civ, _) = MakeWorld();
        Assert.Equal(486.43, construction.GetBaseGoldCost(civ, null), 0.01);
    }

    [Fact]
    public void BuildingPurchaseRules()
    {
        var (game, _, city) = MakeWorld();
        city.CityFilters.Add("in all cities");
        game.FakeSpeedData.StatCostModifiers[Stat.Faith] = 1.5f;
        game.FakeSpeedData.StatCostModifiers[Stat.Culture] = 2f;
        var building = new Building { Name = "Library", Cost = 100, Uniques = ["Can be purchased with [Faith] [in all cities]"] };
        var construction = (INonPerpetualConstruction)building;

        Assert.True(building.CanBePurchasedWithStat(null, Stat.Gold));
        Assert.True(building.CanBePurchasedWithStat(null, Stat.Faith));
        Assert.False(building.CanBePurchasedWithStat(null, Stat.Culture));
        Assert.False(building.CanBePurchasedWithStat(city, Stat.Culture));
        Assert.True(building.CanBePurchasedWithStat(city, Stat.Faith));
        Assert.Equal(PurchaseReason.UniqueAllowed, construction.CanBePurchasedWithStatReasons(city, Stat.Faith));
        Assert.Equal(PurchaseReason.Invalid, construction.CanBePurchasedWithStatReasons(city, Stat.Production));
        Assert.True(construction.CanBePurchasedWithAnyStat(city));
        Assert.Equal(300, building.GetStatBuyCost(city, Stat.Faith)); // era base 200 * 1.5

        city.CityFilters.Clear();
        Assert.False(building.CanBePurchasedWithStat(city, Stat.Faith));
        Assert.Null(building.GetStatBuyCost(city, Stat.Faith));
        Assert.Equal(PurchaseReason.NotAllowed, construction.CanBePurchasedWithStatReasons(city, Stat.Faith));
        Assert.True(building.CanBePurchasedWithStat(city, Stat.Gold));

        var wonder = new Building { Name = "Pyramids", IsWonder = true, Cost = 100 };
        Assert.False(wonder.CanBePurchasedWithStat(null, Stat.Gold));
        var buyableWonder = new Building { Name = "Wonder", IsNationalWonder = true, Uniques = ["Can be purchased for [150] [Culture] [in all cities]"] };
        city.CityFilters.Add("in all cities");
        Assert.True(buyableWonder.CanBePurchasedWithStat(city, Stat.Culture));
        Assert.Equal(300, buyableWonder.GetStatBuyCost(city, Stat.Culture));
        var unpurchasable = new Building { Name = "Hidden", Uniques = ["Cannot be purchased"] };
        Assert.False(unpurchasable.CanBePurchasedWithStat(city, Stat.Gold));
        Assert.Equal(PurchaseReason.Unpurchasable, ((INonPerpetualConstruction)unpurchasable).CanBePurchasedWithStatReasons(null, Stat.Gold));
        var unbuildable = new Building { Name = "Unbuildable", Uniques = ["Unbuildable"] };
        Assert.False(unbuildable.CanBePurchasedWithStat(null, Stat.Gold));
    }

    [Fact]
    public void BuildingBuyCostsPickTheCheapestSpecificUniqueAndApplyDiscounts()
    {
        var (game, civ, city) = MakeWorld();
        city.CityFilters.Add("in all cities");
        game.FakeSpeedData.StatCostModifiers[Stat.Gold] = 1f;
        civ.BoughtItems["Library"] = 3;
        var library = new Building { Name = "Library", Cost = 100 };
        city.LocalUniques.Add(U("May buy [Library] buildings with [Gold] for [2] times their normal Production cost"));
        Assert.Equal(200f, library.GetBaseBuyCost(city, Stat.Gold));
        Assert.Equal(200, library.GetStatBuyCost(city, Stat.Gold));
        city.LocalUniques.Add(U("May buy [Building] buildings for [150] [Gold] [in all cities]"));
        Assert.Equal(150f, library.GetBaseBuyCost(city, Stat.Gold)); // the minimum of the specific costs
        city.LocalUniques.Add(U("[Gold] cost of purchasing [Library] buildings [-10]%"));
        city.LocalUniques.Add(U("[Gold] cost of purchasing [Wonder] buildings [-90]%"));
        city.LocalUniques.Add(U("[Gold] cost of purchasing items in cities [-50]%"));
        Assert.Equal(60, library.GetStatBuyCost(city, Stat.Gold)); // 150 * 0.5 * 0.9 = 67.5 -> 60
    }

    [Fact]
    public void BuildingResourceRequirements()
    {
        var building = new Building { Name = "Factory", RequiredResource = "Iron", Uniques = ["Consumes [2] [Coal]", "Costs [5] [Oil]", "Costs [8] [Uranium] <(modified by game speed)>"] };
        var requirements = building.GetResourceRequirementsPerTurn();
        Assert.Equal(["Iron", "Coal"], requirements.Keys);
        Assert.Equal([1, 2], requirements.Values);
        Assert.Same(Counter.ZERO, new Building { Name = "B" }.GetResourceRequirementsPerTurn());

        var game = new FakeGameInfo();
        game.FakeSpeedData.Modifier = 0.75f;
        var state = new GameContext(gameInfo: game);
        var construction = (IConstruction)building;
        Assert.Equal(["Iron", "Coal", "Oil", "Uranium"], construction.RequiredResources(state));
        var stockpiled = construction.GetStockpiledResourceRequirements(state);
        Assert.Equal(["Oil", "Uranium"], stockpiled.Keys);
        Assert.Equal([5, 6], stockpiled.Values); // 8 * 0.75 = 6
    }

    [Fact]
    public void BuildingImprovementToCreate()
    {
        var ruleset = MakeRuleset();
        var farm = new TileImprovement { Name = "Farm" };
        ruleset.Improvements["Farm"] = farm;
        var (_, civ, _) = MakeWorld();
        var building = new Building { Name = "Farmstead", Uniques = ["Creates a [Farm] improvement on a specific tile"] };
        Assert.True(building.HasCreateOneImprovementUnique());
        Assert.Same(farm, building.GetImprovementToCreate(ruleset, civ));
        Assert.False(new Building { Name = "B" }.HasCreateOneImprovementUnique());
        Assert.Null(new Building { Name = "B" }.GetImprovementToCreate(ruleset, civ));
        Assert.Null(new Building { Name = "B", Uniques = ["Creates a [Ghost] improvement on a specific tile"] }.GetImprovementToCreate(ruleset, civ));
    }

    [Fact]
    public void BuildingUnavailableBySettings()
    {
        var game = new FakeGameInfo();
        game.GameParameters.NuclearWeaponsEnabled = false;
        game.FakeRulesetData.EraNumbers["Ancient era"] = 0;
        Assert.True(new Building { Name = "B", Uniques = ["Enables nuclear weapon"] }.IsUnavailableBySettings(game));
        game.GameParameters.NuclearWeaponsEnabled = true;
        Assert.False(new Building { Name = "B", Uniques = ["Enables nuclear weapon"] }.IsUnavailableBySettings(game));
        Assert.False(new Building { Name = "Pyramids", IsWonder = true }.IsUnavailableBySettings(game)); // the fake ruleset has no eras
    }

    // endregion
}
