using Unciv.Logic.Battle;
using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Map;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Validation;
using Unciv.Models.StatTypes;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Core.Tests.Models.Ruleset.Unique;

/// <summary>
/// Conditionals has no Kotlin test class (the conditionals are exercised through TestGame-based tests in T035). These tests drive
/// <see cref="Conditionals.ConditionalApplies"/> over the data-driven fakes; expected values are hand-derived from Conditionals.kt.
/// </summary>
[Collection("ConditionalsSettings")]
public class ConditionalsTests
{
    private static bool Applies(string modifier, GameContext state, UniqueObject? unique = null) =>
        Conditionals.ConditionalApplies(unique, new UniqueObject(modifier), state);

    private static (FakeGameInfo Game, FakeCiv Civ, FakeCity City) World()
    {
        var game = new FakeGameInfo { Turns = 10, GameId = "game" };
        var civ = new FakeCiv("civ", game);
        game.FakeCivs.Add(civ);
        var city = new FakeCity("city", civ, new FakeTile(new HexCoord(0, 0)));
        civ.FakeCities.Add(city);
        return (game, civ, city);
    }

    [Fact]
    public void OtherModifierTypesAlwaysApplyAndUnknownConditionalsNever()
    {
        Assert.True(Applies("hidden from users", GameContext.EmptyState));
        Assert.False(Applies("some made up conditional", GameContext.EmptyState));
    }

    [Fact]
    public void TurnConditionalsNeedAGameAndUseIntModulo()
    {
        var (game, civ, _) = World();
        var state = new GameContext(civInfo: civ);
        Assert.True(Applies("every [5] turns", state));
        Assert.False(Applies("every [4] turns", state));
        Assert.True(Applies("before turn number [11]", state));
        Assert.False(Applies("before turn number [10]", state));
        Assert.True(Applies("after turn number [10]", state));
        game.Turns = 0;
        Assert.True(Applies("every [7] turns", state));
        Assert.False(Applies("after turn number [1]", state));
        Assert.False(Applies("every [5] turns", GameContext.EmptyState));
        Assert.False(Applies("before turn number [1]", GameContext.EmptyState));
        // Kotlin `%` by zero throws ArithmeticException, .NET DivideByZeroException
        Assert.Throws<DivideByZeroException>(() => Applies("every [0] turns", state));
    }

    [Fact]
    public void ChanceIsDeterministicForTheSameStateAndUnique()
    {
        var (_, civ, _) = World();
        var state = new GameContext(civInfo: civ);
        var unique = new UniqueObject("[+1 Gold] <with [50]% chance>", UniqueTarget.Building, "Library");
        bool first = Applies("with [50]% chance", state, unique);
        for (int i = 0; i < 5; i++) Assert.Equal(first, Applies("with [50]% chance", state, unique));
        Assert.False(Applies("with [0]% chance", state, unique));
        Assert.True(Applies("with [100]% chance", state, unique));
        // different sources reach different seeds: over many names both outcomes occur
        var outcomes = Enumerable.Range(0, 40)
            .Select(i => Applies("with [50]% chance", state, new UniqueObject("[+1 Gold]", UniqueTarget.Building, "B" + i))).Distinct().ToList();
        Assert.Equal(2, outcomes.Count);
    }

    [Fact]
    public void ResourceAndStatThresholdsCompareAgainstTheStateAndScaleWithGameSpeed()
    {
        var (game, civ, city) = World();
        game.FakeRulesetData.AddResource("Iron");
        civ.Resources["Iron"] = 3;
        civ.StatReserves[Stat.Gold] = 100;
        var state = new GameContext(civInfo: civ);
        Assert.True(Applies("when above [2] [Iron]", state));
        Assert.False(Applies("when above [3] [Iron]", state));
        Assert.True(Applies("when below [4] [Iron]", state));
        Assert.False(Applies("when below [3] [Iron]", state));
        Assert.True(Applies("when between [3] and [3] [Iron]", state));
        Assert.True(Applies("when above [99] [Gold]", state));
        Assert.False(Applies("when above [100] [Gold]", state));
        Assert.False(Applies("when above [1] [Bogus]", state));
        Assert.False(Applies("when above [1] [Iron]", GameContext.EmptyState));
        Assert.True(Applies("with [Iron]", state));
        Assert.False(Applies("without [Iron]", state));
        // game speed: only with the ModifiedByGameSpeed modifier on the unique
        game.FakeSpeedData.Modifier = 2f;
        game.FakeSpeedData.StatCostModifiers[Stat.Gold] = 0.5f;
        var scaled = new UniqueObject("[+1 Gold] <when above [150] [Gold]> <(modified by game speed)>");
        Assert.True(Conditionals.ConditionalApplies(scaled, scaled.Modifiers[0], state)); // 150 * 0.5 = 75 < 100
        var scaledResource = new UniqueObject("[+1 Gold] <when above [1] [Iron]> <(modified by game speed)>");
        Assert.True(Conditionals.ConditionalApplies(scaledResource, scaledResource.Modifiers[0], state)); // 1 * 2 = 2 < 3
        var tooHigh = new UniqueObject("[+1 Gold] <when above [2] [Iron]> <(modified by game speed)>");
        Assert.False(Conditionals.ConditionalApplies(tooHigh, tooHigh.Modifiers[0], state)); // 2 * 2 = 4 !< 3
        // below uses Float.MIN_VALUE (float.Epsilon) as the lower limit, which scales to ~0, never to a huge negative
        var below = new UniqueObject("[+1 Gold] <when below [150] [Gold]> <(modified by game speed)>");
        Assert.False(Conditionals.ConditionalApplies(below, below.Modifiers[0], state)); // 150 * 0.5 = 75, 100 !< 75
        _ = city;
    }

    [Fact]
    public void ResourceConditionalsUseTheCityBeforeTheCiv()
    {
        var (_, civ, city) = World();
        civ.Resources["Iron"] = 3;
        city.Resources["Iron"] = 0;
        Assert.True(Applies("with [Iron]", new GameContext(civInfo: civ)));
        Assert.False(Applies("with [Iron]", new GameContext(civInfo: civ, city: city)));
        Assert.True(Applies("without [Iron]", new GameContext(civInfo: civ, city: city)));
        Assert.False(Applies("with [Iron]", GameContext.EmptyState));
        Assert.True(Applies("without [Iron]", GameContext.EmptyState));
    }

    [Fact]
    public void EraConditionalsCompareTheCivEraNumber()
    {
        var (game, civ, _) = World();
        game.FakeRulesetData.EraNumbers["Medieval era"] = 2;
        civ.EraNumber = 2;
        var state = new GameContext(civInfo: civ);
        Assert.False(Applies("before the [Medieval era]", state));
        Assert.True(Applies("starting from the [Medieval era]", state));
        Assert.True(Applies("during the [Medieval era]", state));
        civ.EraNumber = 1;
        Assert.True(Applies("before the [Medieval era]", state));
        Assert.False(Applies("during the [Medieval era]", state));
        Assert.False(Applies("during the [Nonexistent era]", state));
        Assert.False(Applies("during the [Medieval era]", GameContext.EmptyState));
        // gameInfo and era exist but no civ at all: Kotlin's `relevantCiv!!` throws
        Assert.Throws<NullReferenceException>(() => Applies("during the [Medieval era]", new GameContext(gameInfo: game)));
    }

    [Fact]
    public void GameSettingConditionals()
    {
        var (game, civ, _) = World();
        game.GameParameters.StartingEra = "Ancient era";
        game.GameParameters.Speed = "Quick";
        game.GameParameters.Difficulty = "Prince";
        game.GameParameters.VictoryTypes.Add("Scientific");
        game.GameParameters.NuclearWeaponsEnabled = false;
        game.GameParameters.BaseRuleset = "Civ V - Gods & Kings";
        game.GameParameters.Mods.Add("Cool Mod");
        game.ReligionEnabled = true;
        game.FakeRulesetData.DifficultyList.AddRange(["Settler", "Prince", "King"]);
        game.DifficultyName = "Prince";
        var state = new GameContext(civInfo: civ);
        Assert.True(Applies("if starting in the [Ancient era]", state));
        Assert.False(Applies("if starting in the [Modern era]", state));
        Assert.True(Applies("on [Quick] game speed", state));
        Assert.True(Applies("on [Prince] difficulty", state));
        Assert.True(Applies("on [Settler] difficulty or higher", state));
        Assert.True(Applies("on [Prince] difficulty or higher", state));
        Assert.False(Applies("on [King] difficulty or higher", state));
        Assert.True(Applies("on [King] difficulty or lower", state));
        Assert.False(Applies("on [Settler] difficulty or lower", state));
        Assert.False(Applies("on [Bogus] difficulty or higher", state));
        Assert.True(Applies("when [Scientific] Victory is enabled", state));
        Assert.False(Applies("when [Scientific] Victory is disabled", state));
        Assert.True(Applies("when [Domination] Victory is disabled", state));
        Assert.True(Applies("when religion is enabled", state));
        Assert.False(Applies("when religion is disabled", state));
        Assert.False(Applies("when espionage is enabled", state));
        Assert.True(Applies("when espionage is disabled", state));
        Assert.False(Applies("when nuclear weapons are enabled", state));
        Assert.True(Applies("when nuclear weapons are disabled", state));
        Assert.True(Applies("if [Cool Mod] is enabled", state));
        Assert.True(Applies("if [*cool*] is enabled", state));
        Assert.True(Applies("if [Civ V - Gods & Kings] is enabled", state));
        Assert.False(Applies("if [Other] is enabled", state));
        Assert.True(Applies("if [Other] is not enabled", state));
        Assert.False(Applies("when religion is enabled", GameContext.EmptyState));
    }

    [Fact]
    public void ModNameFilterMatchesExactAndStarWrappedNames()
    {
        Assert.True(ModCompatibility.ModNameFilter("Cool Mod", "Cool Mod"));
        Assert.True(ModCompatibility.ModNameFilter("Cool Mod", "*ool M*"));
        Assert.True(ModCompatibility.ModNameFilter("Cool Mod", "*COOL*"));
        Assert.False(ModCompatibility.ModNameFilter("Cool Mod", "**")); // shorter than 3
        Assert.False(ModCompatibility.ModNameFilter("Cool Mod", "*cool"));
        Assert.False(ModCompatibility.ModNameFilter("Cool Mod", "cool"));
        Assert.True(ModCompatibility.ModNameFilter("a*b", "a*b"));
    }

    [Fact]
    public void CivConditionals()
    {
        var (_, civ, _) = World();
        var state = new GameContext(civInfo: civ);
        civ.AtWar = true;
        Assert.True(Applies("when at war", state));
        Assert.False(Applies("when not at war", state));
        civ.Happiness = -1;
        Assert.False(Applies("while the empire is happy", state));
        civ.Happiness = 0;
        Assert.True(Applies("while the empire is happy", state));
        civ.GoldenAge = true;
        Assert.True(Applies("during a Golden Age", state));
        Assert.False(Applies("when not in a Golden Age", state));
        civ.MatchingCivFilters.Add("Major");
        Assert.True(Applies("for [Major] Civilizations", state));
        Assert.False(Applies("for [Minor] Civilizations", state));
        Assert.False(Applies("when at war", GameContext.EmptyState));

        civ.ResearchedTechs.Add("Agriculture");
        civ.TechFilterMatches.Add("Ancient era");
        var game = (FakeGameInfo)civ.GameInfo!;
        game.FakeRulesetData.AddTech("Agriculture");
        game.FakeRulesetData.AddTech("Optics");
        Assert.True(Applies("after discovering [Agriculture]", state));
        Assert.False(Applies("after discovering [Optics]", state));
        Assert.True(Applies("before discovering [Optics]", state));
        Assert.False(Applies("before discovering [Agriculture]", state));
        Assert.True(Applies("after discovering [Ancient era]", state)); // not a tech name: filter path
        Assert.False(Applies("before discovering [Ancient era]", state));
        civ.CurrentTech = "Optics";
        Assert.True(Applies("while researching [Optics]", state));
        Assert.False(Applies("while researching [Agriculture]", state));
    }

    [Fact]
    public void PolicyBeliefAndReligionStateConditionals()
    {
        var (game, civ, _) = World();
        var state = new GameContext(civInfo: civ);
        civ.AdoptedPolicies.Add("Tradition");
        Assert.True(Applies("after adopting [Tradition]", state));
        Assert.False(Applies("before adopting [Tradition]", state));
        Assert.False(Applies("after adopting [Liberty]", state));
        Assert.True(Applies("before adopting [Liberty]", state));
        Assert.False(Applies("if no Civilization has adopted [Tradition]", state));
        Assert.True(Applies("if no Civilization has adopted [Liberty]", state));
        civ.Religion = new FakeReligion { Beliefs = { "Tithe" } };
        Assert.True(Applies("after adopting [Tithe]", state));
        Assert.False(Applies("before adopting [Tithe]", state));

        civ.ReligionState = ReligionState.None;
        Assert.True(Applies("before founding a Pantheon", state));
        Assert.False(Applies("after founding a Pantheon", state));
        civ.ReligionState = ReligionState.FoundingReligion;
        Assert.True(Applies("after founding a Pantheon", state));
        Assert.True(Applies("before founding a religion", state));
        Assert.False(Applies("after founding a religion", state));
        civ.ReligionState = ReligionState.Religion;
        Assert.True(Applies("after founding a religion", state));
        Assert.True(Applies("before enhancing a religion", state));
        civ.ReligionState = ReligionState.EnhancingReligion;
        Assert.True(Applies("before enhancing a religion", state));
        Assert.False(Applies("after enhancing a religion", state));
        civ.ReligionState = ReligionState.EnhancedReligion;
        Assert.True(Applies("after enhancing a religion", state));
        civ.GreatProphets = 1;
        Assert.True(Applies("after generating a Great Prophet", state));
        _ = game;
    }

    [Fact]
    public void FirstCivToResearchAndAdoptNeedATypedSource()
    {
        var (game, civ, _) = World();
        var other = new FakeCiv("other", game);
        game.FakeCivs.Add(other);
        var state = new GameContext(civInfo: civ);
        var tech = new UniqueObject("[+1 Gold] <if no other Civilization has researched this>", UniqueTarget.Tech, "Optics");
        var policy = new UniqueObject("[+1 Gold]", UniqueTarget.Policy, "Tradition");
        var modifierResearch = new UniqueObject("if no other Civilization has researched this");
        Assert.True(Conditionals.ConditionalApplies(tech, modifierResearch, state));
        other.ResearchedTechs.Add("Optics");
        Assert.False(Conditionals.ConditionalApplies(tech, modifierResearch, state));
        other.Major = false; // minor civs are ignored
        Assert.True(Conditionals.ConditionalApplies(tech, modifierResearch, state));
        Assert.False(Conditionals.ConditionalApplies(policy, modifierResearch, state)); // wrong source type
        Assert.False(Conditionals.ConditionalApplies(null, modifierResearch, state));
    }

    [Fact]
    public void CityConditionals()
    {
        var (game, civ, city) = World();
        var state = new GameContext(civInfo: civ, city: city);
        Assert.True(Applies("in this city", state));
        Assert.False(Applies("in this city", new GameContext(civInfo: civ)));
        Assert.False(Applies("in cities connected to the capital", state));
        city.Connected = true;
        Assert.True(Applies("in cities connected to the capital", state));
        city.Buildings.Add("Library");
        Assert.True(Applies("in cities with a [Library]", state));
        Assert.False(Applies("in cities without a [Library]", state));
        Assert.True(Applies("in cities without a [Temple]", state));
        city.PopulationByFilter["Citizens"] = 5;
        Assert.True(Applies("in cities with at least [5] [Citizens]", state));
        Assert.False(Applies("in cities with at least [6] [Citizens]", state));
        Assert.True(Applies("in cities with [5] [Citizens]", state));
        Assert.False(Applies("in cities with [4] [Citizens]", state));
        Assert.True(Applies("in cities with between [3] and [5] [Citizens]", state));
        Assert.False(Applies("in cities with between [6] and [9] [Citizens]", state));
        Assert.True(Applies("in cities with less than [6] [Citizens]", state));
        Assert.False(Applies("in cities with less than [5] [Citizens]", state));
        Assert.False(Applies("during We Love The King Day", state));
        city.Wltkd = true;
        Assert.True(Applies("during We Love The King Day", state));
        Assert.False(Applies("with a garrison", state));
        city.CanGarrison = true;
        Assert.True(Applies("with a garrison", state));
        city.CityFilters.Add("Coastal");
        Assert.True(Applies("in [Coastal] cities", state));
        Assert.False(Applies("in [Holy] cities", state));
        Assert.False(Applies("in [Coastal] cities", GameContext.EmptyState));

        // building conditionals over the civ's cities and over all cities of the game
        var civState = new GameContext(civInfo: civ);
        Assert.True(Applies("if [Library] is constructed", civState));
        Assert.False(Applies("if [Temple] is constructed", civState));
        Assert.False(Applies("if [Library] is not constructed", civState));
        Assert.True(Applies("if [Library] is constructed in all [Coastal] cities", civState));
        var inland = new FakeCity("inland", civ);
        civ.FakeCities.Add(inland); // not Coastal, so it is not asked
        Assert.True(Applies("if [Library] is constructed in all [Coastal] cities", civState));
        inland.CityFilters.Add("Coastal");
        Assert.False(Applies("if [Library] is constructed in all [Coastal] cities", civState));
        inland.Buildings.Add("Library");
        Assert.True(Applies("if [Library] is constructed in at least [2] of [Coastal] cities", civState));
        Assert.False(Applies("if [Library] is constructed in at least [3] of [Coastal] cities", civState));
        Assert.True(Applies("if [Library] is constructed by anybody", civState));
        Assert.False(Applies("if [Temple] is constructed by anybody", civState));
        Assert.True(Applies("if [Temple] is not constructed by anybody", civState));
        Assert.False(Applies("if [Library] is not constructed by anybody", civState));
        // without a game, "constructed by anybody" is false and its negation true
        Assert.False(Applies("if [Library] is constructed by anybody", GameContext.EmptyState));
        Assert.True(Applies("if [Library] is not constructed by anybody", GameContext.EmptyState));
        _ = game;
    }

    [Fact]
    public void ReligionOfTheMajorityInTheCity()
    {
        var (_, civ, city) = World();
        var state = new GameContext(civInfo: civ, city: city);
        Assert.False(Applies("in cities with a major religion", state));
        Assert.True(Applies("in cities not following a [your] religion", state));
        var religion = new FakeReligion { Major = true, Enhanced = false, Filters = { "your" } };
        city.MajorityReligion = religion;
        Assert.True(Applies("in cities with a [your] religion", state));
        Assert.False(Applies("in cities not following a [your] religion", state));
        Assert.False(Applies("in cities with a [foreign] religion", state));
        Assert.True(Applies("in cities with a major religion", state));
        Assert.False(Applies("in cities with an enhanced religion", state));
        // "following our religion" compares the majority religion with the civ's religion (null == null counts)
        Assert.False(Applies("in cities following our religion", state));
        civ.Religion = religion;
        Assert.True(Applies("in cities following our religion", state));
        civ.Religion = new FakeReligion();
        Assert.False(Applies("in cities following our religion", state));
        city.MajorityReligion = null;
        civ.Religion = null;
        Assert.True(Applies("in cities following our religion", state));
    }

    [Fact]
    public void UnitConditionals()
    {
        var (_, civ, _) = World();
        var tile = new FakeTile(new HexCoord(0, 0));
        var unit = new FakeUnit("Warrior", 2f, civ, tile) { Health = 50 };
        unit.UnitFilters.Add("Melee");
        unit.PromotionsAndStatuses.Add("Shock I");
        var state = new GameContext(civInfo: civ, unit: unit);
        Assert.True(Applies("for units with [Shock I]", state));
        Assert.False(Applies("for units without [Shock I]", state));
        Assert.True(Applies("for units without [Drill I]", state));
        Assert.False(Applies("for units with [Shock I]", GameContext.EmptyState));
        Assert.False(Applies("for units without [Shock I]", GameContext.EmptyState));
        Assert.True(Applies("for [Melee] units", state));
        Assert.True(Applies("when [Melee]", state));
        Assert.False(Applies("for [Ranged] units", state));
        Assert.True(Applies("when above [49] HP", state));
        Assert.False(Applies("when above [50] HP", state));
        Assert.True(Applies("when below [51] HP", state));
        Assert.False(Applies("when below [50] HP", state));
        Assert.False(Applies("when above [49] HP", GameContext.EmptyState));
        // Float movement against an Int parameter
        Assert.True(Applies("when above [1] movement", state));
        Assert.False(Applies("when above [2] movement", state));
        Assert.True(Applies("when below [3] movement", state));
        Assert.True(Applies("if it hasn't used other actions yet", GameContext.EmptyState));
        Assert.True(Applies("if it hasn't used other actions yet", state));
        unit.AbilityToTimesUsedIsEmpty = false;
        Assert.False(Applies("if it hasn't used other actions yet", state));
        // combatant health through ourCombatant
        var combatant = new FakeCombatant(civ, tile, "Warrior", unit) { Health = 80 };
        var combat = GameContext.ForCombatants(combatant, combatAction: CombatAction.Attack);
        Assert.True(Applies("when above [79] HP", GameContext.ForCombatants(new FakeCombatant(civ, tile, "City") { Health = 80 })));
        Assert.True(Applies("when attacking", combat));
        Assert.False(Applies("when defending", combat));
        Assert.True(Applies("when defending", GameContext.ForCombatants(combatant, combatAction: CombatAction.Defend)));
        Assert.False(Applies("when attacking", GameContext.EmptyState));
    }

    [Fact]
    public void StackingAdjacencyAndCarrierConditionals()
    {
        var (_, civ, _) = World();
        var tile = new FakeTile(new HexCoord(0, 0));
        var neighbor = new FakeTile(new HexCoord(1, 0));
        tile.NeighborTiles.Add(neighbor);
        var unit = new FakeUnit("Warrior", 2f, civ, tile);
        var other = new FakeUnit("Archer", 2f, civ, tile);
        other.UnitFilters.Add("Ranged");
        tile.UnitsOnTile.AddRange([unit, other]);
        var state = new GameContext(civInfo: civ, unit: unit);
        Assert.True(Applies("when stacked with a [Ranged] unit", state));
        Assert.False(Applies("when not stacked with a [Ranged] unit", state));
        Assert.False(Applies("when stacked with a [Melee] unit", state));
        Assert.True(Applies("when not stacked with a [Melee] unit", state));
        Assert.True(Applies("when not stacked with a [Melee] unit", GameContext.EmptyState));
        Assert.False(Applies("when stacked with a [Melee] unit", GameContext.EmptyState));

        var neighborUnit = new FakeUnit("Spearman", 2f, civ, neighbor);
        neighborUnit.UnitFilters.Add("Melee");
        neighbor.UnitsOnTile.Add(neighborUnit);
        Assert.True(Applies("when adjacent to a [Melee] unit", state));
        Assert.False(Applies("when adjacent to a [Ranged] unit", state));
        var foreign = new FakeCiv("foreign");
        neighborUnit = new FakeUnit("Spearman", 2f, foreign, neighbor);
        neighbor.UnitsOnTile.Clear();
        neighbor.UnitsOnTile.Add(neighborUnit);
        neighborUnit.UnitFilters.Add("Melee");
        Assert.False(Applies("when adjacent to a [Melee] unit", state)); // not our civ

        Assert.False(Applies("when carried by [Carrier] units", state));
        var carried = new FakeUnit("Fighter", 2f, civ, tile) { IsTransported = true };
        var carrier = new FakeUnit("Carrier", 2f, civ, tile);
        carrier.UnitFilters.Add("Carrier");
        tile.MilitaryUnit = carrier;
        var carriedState = new GameContext(civInfo: civ, unit: carried);
        Assert.True(Applies("when carried by [Carrier] units", carriedState));
        Assert.False(Applies("when carried by [Tanker] units", carriedState));
        tile.MilitaryUnit = carried; // "carrier" is the unit itself
        Assert.False(Applies("when carried by [Carrier] units", carriedState));
    }

    [Fact]
    public void VersusConditionalsUseTheirCombatant()
    {
        var (_, civ, _) = World();
        var tile = new FakeTile(new HexCoord(0, 0));
        var ours = new FakeCombatant(civ, tile, "Warrior", new FakeUnit("Warrior", 2f, civ, tile));
        var theirs = new FakeCombatant(civ, tile, "Archer");
        theirs.Filters.Add("City");
        theirs.Filters.Add("Ranged");
        var state = GameContext.ForCombatants(ours, theirs);
        Assert.True(Applies("vs cities", state));
        Assert.True(Applies("vs [Ranged] units", state));
        Assert.True(Applies("vs [Ranged]", state));
        Assert.False(Applies("vs [Melee] units", state));
        Assert.False(Applies("vs cities", GameContext.ForCombatants(ours)));
        // yourCities (1) < theirCities
        var bigCiv = new FakeCiv("big");
        bigCiv.FakeCities.AddRange([new FakeCity("a", bigCiv), new FakeCity("b", bigCiv)]);
        var big = new FakeCombatant(bigCiv, tile, "Big");
        Assert.True(Applies("when fighting units from a Civilization with more Cities than you", GameContext.ForCombatants(ours, big)));
        Assert.False(Applies("when fighting units from a Civilization with more Cities than you", GameContext.ForCombatants(big, ours)));
        Assert.False(Applies("when fighting units from a Civilization with more Cities than you", GameContext.EmptyState)); // 1 < 0
    }

    [Fact]
    public void CountableComparisonConditionals()
    {
        var (game, civ, _) = World();
        game.FakeRulesetData.AddResource("Iron");
        civ.Resources["Iron"] = 3;
        var state = new GameContext(civInfo: civ);
        Assert.True(Applies("when number of [turns] is equal to [10]", state));
        Assert.True(Applies("when number of [turns] is different than [11]", state));
        Assert.True(Applies("when number of [Cities] is more than [0]", state));
        Assert.False(Applies("when number of [Cities] is more than [1]", state));
        Assert.True(Applies("when number of [Iron] is less than [turns]", state));
        Assert.True(Applies("when number of [turns] is between [10] and [10]", state));
        Assert.False(Applies("when number of [turns] is between [11] and [12]", state));
        Assert.True(Applies("when number of [turns] is between [9] and [11]", state));
        // an unknown countable on either side makes the comparison false
        Assert.False(Applies("when number of [Bogus] is equal to [Bogus]", state));
        Assert.False(Applies("when number of [turns] is equal to [Bogus]", state));
        Assert.False(Applies("when number of [turns] is between [1] and [Bogus]", state));
        Assert.True(Applies("when number of [[turns] * 2] is equal to [20]", state));
    }

    [Fact]
    public void TileConditionals()
    {
        var (_, civ, _) = World();
        var tile = new FakeTile(new HexCoord(0, 0));
        var neighbor1 = new FakeTile(new HexCoord(1, 0));
        var neighbor2 = new FakeTile(new HexCoord(0, 1));
        neighbor1.Matching.Add("Forest");
        neighbor2.Matching.Add("Forest");
        tile.NeighborTiles.AddRange([neighbor1, neighbor2]);
        tile.Matching.Add("Hill");
        tile.AdjacentFilters.Add("Forest");
        tile.Continent = 1;
        var state = new GameContext(civInfo: civ, tile: tile);
        Assert.True(Applies("in [Hill] tiles", state));
        Assert.False(Applies("in tiles without [Hill]", state));
        Assert.True(Applies("in tiles without [Forest]", state));
        Assert.True(Applies("in tiles adjacent to [Forest] tiles", state));
        Assert.False(Applies("in tiles not adjacent to [Forest] tiles", state));
        Assert.False(Applies("in tiles adjacent to [Desert] tiles", state));
        Assert.True(Applies("in tiles not adjacent to [Desert] tiles", state));
        Assert.True(Applies("within [1] tiles of a [Forest]", state));
        Assert.False(Applies("within [1] tiles of a [Desert]", state));
        Assert.True(Applies("with [2] to [3] neighboring [Forest] tiles", state));
        Assert.False(Applies("with [3] to [4] neighboring [Forest] tiles", state));
        Assert.False(Applies("in [Hill] tiles", GameContext.EmptyState));
        Assert.False(Applies("in tiles without [Hill]", GameContext.EmptyState)); // null == false is false
        Assert.False(Applies("with [0] to [3] neighboring [Forest] tiles", GameContext.EmptyState));
        // fighting in: the attacked tile
        var attacked = new FakeTile(new HexCoord(5, 5));
        attacked.Matching.Add("Hill");
        Assert.True(Applies("when fighting in [Hill] tiles", new GameContext(civInfo: civ, attackedTile: attacked)));
        Assert.False(Applies("when fighting in [Hill] tiles", state));

        // foreign continent: relevant civ's capital sits on another continent
        var capital = new FakeCity("cap", civ, new FakeTile(new HexCoord(9, 9)) { Continent = 2 });
        Assert.True(Applies("on foreign continents", state)); // no cities at all
        civ.FakeCities.Add(capital);
        Assert.True(Applies("on foreign continents", state)); // no capital set
        civ.Capital = capital;
        Assert.True(Applies("on foreign continents", state)); // 2 != 1
        tile.Continent = 2;
        Assert.False(Applies("on foreign continents", state));
        Assert.False(Applies("on foreign continents", new GameContext(civInfo: civ))); // no relevant tile
    }

    [Fact]
    public void RegionConditionals()
    {
        var regionLand = new RegionFake(3, "Hill");
        var regionWater = new RegionFake(-1, "Ocean");
        Assert.True(Applies("on water maps", new GameContext(region: regionWater)));
        Assert.False(Applies("on water maps", new GameContext(region: regionLand)));
        Assert.False(Applies("on water maps", GameContext.EmptyState));
        Assert.True(Applies("in [Hill] Regions", new GameContext(region: regionLand)));
        Assert.False(Applies("in [Hill] Regions", new GameContext(region: regionWater)));
        Assert.True(Applies("in all except [Hill] Regions", new GameContext(region: regionWater)));
        Assert.False(Applies("in all except [Hill] Regions", new GameContext(region: regionLand)));
        Assert.True(Applies("in all except [Hill] Regions", GameContext.EmptyState)); // null != "Hill"
    }

    private sealed class RegionFake(int continentId, string type) : IGameContextRegion
    {
        public int RectHashCode => 0;
        public int ContinentId { get; } = continentId;
        public string Type { get; } = type;
    }

    [Fact]
    public void TutorialConditionalsReadTheSettingsSeam()
    {
        var previous = Conditionals.Settings;
        try
        {
            Assert.False(Applies("if tutorials are enabled", GameContext.EmptyState)); // default: no client settings
            Assert.False(Applies("if tutorial [Done] is completed", GameContext.EmptyState));
            Conditionals.Settings = new FakeSettings(true, "Done");
            Assert.True(Applies("if tutorials are enabled", GameContext.EmptyState));
            Assert.True(Applies("if tutorial [Done] is completed", GameContext.EmptyState));
            Assert.False(Applies("if tutorial [Other] is completed", GameContext.EmptyState));
        }
        finally
        {
            Conditionals.Settings = previous;
        }
    }

    private sealed class FakeSettings(bool show, string completed) : IConditionalsSettings
    {
        public bool ShowTutorials { get; } = show;
        public bool IsTutorialTaskCompleted(string task) => task == completed;
    }
}

internal sealed class FakeReligion : IGameContextReligion
{
    public HashSet<string> Beliefs { get; } = new();
    public HashSet<string> Filters { get; init; } = new();
    public bool Major { get; init; }
    public bool Enhanced { get; init; }
    public bool MatchesFilter(string filter, GameContext state, IGameContextCivilization? civ) => Filters.Contains(filter);
    public bool IsMajorReligion() => Major;
    public bool IsEnhancedReligion() => Enhanced;
    public bool HasBelief(string belief) => Beliefs.Contains(belief);
}
