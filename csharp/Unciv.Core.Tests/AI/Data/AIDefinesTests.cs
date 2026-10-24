using Unciv.AI.Data;

namespace Unciv.Core.Tests.AI.Data;

// Values below are read off migration/ai-spec/data/*.tsv (the shipped Civ 5 tables) and SPEC.md section 3.3.
public class AIDefinesTests
{
    private static readonly GlobalAIDefines Defines = GlobalAIDefines.LoadEmbedded();

    [Fact]
    public void LoadsEveryDefineInFileOrder()
    {
        Assert.Equal(453, Defines.Count);
        Assert.Equal("AGGRESSIVE_ANNEX", Defines.Names.First());
        Assert.Equal("AI_ATTEMPT_RUSH_OVER_X_TURNS_TO_BUILD", Defines.Names.ElementAt(1));
    }

    [Theory]
    [InlineData("AI_GS_RAND_ROLL", 50)]
    [InlineData("AI_GRAND_STRATEGY_CURRENT_STRATEGY_WEIGHT", 50)]
    [InlineData("AI_GRAND_STRATEGY_NUM_TURNS_STRATEGY_MUST_BE_ACTIVE", 10)]
    [InlineData("AI_GRAND_STRATEGY_OTHER_PLAYERS_GS_MULTIPLIER", 50)]
    [InlineData("AI_GRAND_STRATEGY_GUESS_POSITIVE_THRESHOLD", 120)]
    [InlineData("AI_GRAND_STRATEGY_GUESS_LIKELY_THRESHOLD", 70)]
    [InlineData("AI_GRAND_STRATEGY_GUESS_NO_CLUE_WEIGHT", 40)]
    public void GrandStrategyTunablesMatchTheSpecInAllEditions(string name, int expected)
    {
        foreach (var edition in Enum.GetValues<AIEdition>())
            Assert.Equal(expected, Defines.GetInt(name, edition));
    }

    [Fact]
    public void DefineMissingInAnEditionIsUndefinedThere()
    {
        Assert.False(Defines.IsDefined("AGGRESSIVE_ANNEX", AIEdition.Base));
        Assert.False(Defines.TryGetDouble("AGGRESSIVE_ANNEX", AIEdition.Base, out _));
        Assert.Throws<KeyNotFoundException>(() => Defines.GetDouble("AGGRESSIVE_ANNEX", AIEdition.Base));
        Assert.Equal(0.75, Defines.GetDouble("AGGRESSIVE_ANNEX", AIEdition.GK));
        Assert.Equal(0.75, Defines.GetDouble("AGGRESSIVE_ANNEX", AIEdition.BNW));

        Assert.True(Defines.IsDefined("AI_GS_CULTURE_MAX_CITIES", AIEdition.GK));
        Assert.False(Defines.IsDefined("AI_GS_CULTURE_MAX_CITIES", AIEdition.BNW));
        Assert.Equal(350, Defines.GetInt("AI_GOLD_PRIORITY_BUYOUT_CITY_STATE", AIEdition.BNW));
        Assert.False(Defines.IsDefined("AI_GOLD_PRIORITY_BUYOUT_CITY_STATE", AIEdition.GK));
    }

    [Fact]
    public void UnknownDefineIsNotDefined()
    {
        Assert.False(Defines.IsDefined("NO_SUCH_DEFINE", AIEdition.BNW));
        Assert.Throws<KeyNotFoundException>(() => Defines.GetInt("NO_SUCH_DEFINE", AIEdition.BNW));
    }

    [Fact]
    public void FractionalDefinesAreFloatsAndRejectedAsInts()
    {
        Assert.Equal(0.3, Defines.GetDouble("AI_TACTICAL_FLAVOR_DAMPENING_FOR_MOVE_PRIORITIZATION", AIEdition.BNW));
        Assert.Equal(0.3f, Defines.GetFloat("AI_TACTICAL_FLAVOR_DAMPENING_FOR_MOVE_PRIORITIZATION", AIEdition.BNW));
        Assert.Equal(2.0, Defines.GetDouble("AI_CITYSTRATEGY_YIELD_DEFICIENT_FOOD", AIEdition.GK));
        Assert.Throws<FormatException>(() => Defines.GetInt("AI_CITYSTRATEGY_YIELD_DEFICIENT_FOOD", AIEdition.GK));
        Assert.Throws<FormatException>(() => Defines.GetInt("AGGRESSIVE_ANNEX", AIEdition.GK));
        Assert.True(Defines.GetBool("AI_CAN_DISBAND_UNITS", AIEdition.Base));
    }

    [Fact]
    public void ParseRejectsMalformedTables()
    {
        const string head = "name\tbase\tgk\tbnw\n";
        Assert.Throws<FormatException>(() => GlobalAIDefines.Parse("name\tbase\n"));
        Assert.Throws<FormatException>(() => GlobalAIDefines.Parse(head + "A\t1\t2\n"));
        Assert.Throws<FormatException>(() => GlobalAIDefines.Parse(head + "A\tx\t2\t3\n"));
        Assert.Throws<FormatException>(() => GlobalAIDefines.Parse(head + "A\t1\t2\t3\nA\t1\t2\t3\n"));
        Assert.Throws<FormatException>(() => GlobalAIDefines.Parse(""));
        Assert.Equal(-4, GlobalAIDefines.Parse(head + "A\t-4\t\t\r\n").GetInt("A", AIEdition.Base));
    }

    [Fact]
    public void GrandStrategyTablesMatchTheShippedData()
    {
        var flavors = GrandStrategyFlavors.LoadEmbedded();
        Assert.Equal(["AIGRANDSTRATEGY_CONQUEST", "AIGRANDSTRATEGY_CULTURE", "AIGRANDSTRATEGY_UNITED_NATIONS", "AIGRANDSTRATEGY_SPACESHIP"],
            flavors.Strategies(GrandStrategyFlavors.FlavorsTable));

        Assert.Equal(9, flavors.Get(GrandStrategyFlavors.FlavorsTable, "AIGRANDSTRATEGY_CONQUEST", "FLAVOR_OFFENSE"));
        Assert.Equal(13, flavors.Get(GrandStrategyFlavors.FlavorsTable, "AIGRANDSTRATEGY_SPACESHIP", "FLAVOR_SPACESHIP"));
        Assert.Equal(-2, flavors.Get(GrandStrategyFlavors.FlavorModsTable, "AIGRANDSTRATEGY_CULTURE", "FLAVOR_EXPANSION"));
        Assert.Equal(-5, flavors.Get(GrandStrategyFlavors.FlavorModsTable, "AIGRANDSTRATEGY_SPACESHIP", "FLAVOR_RELIGION"));
        Assert.Equal(200, flavors.Get(GrandStrategyFlavors.YieldsTable, "AIGRANDSTRATEGY_CONQUEST", "YIELD_PRODUCTION"));
        Assert.Equal(0, flavors.Get(GrandStrategyFlavors.YieldsTable, "AIGRANDSTRATEGY_CONQUEST", "YIELD_GOLD"));

        var cultureMods = flavors.Entries(GrandStrategyFlavors.FlavorModsTable, "AIGRANDSTRATEGY_CULTURE");
        Assert.Equal(10, cultureMods.Count);
        Assert.Equal(new KeyValuePair<string, int>("FLAVOR_CULTURE", 2), cultureMods[0]);
        Assert.Equal(new KeyValuePair<string, int>("FLAVOR_DEFENSE", 1), cultureMods[^1]);
        Assert.Equal(6, new[] { "AIGRANDSTRATEGY_CONQUEST", "AIGRANDSTRATEGY_CULTURE", "AIGRANDSTRATEGY_UNITED_NATIONS", "AIGRANDSTRATEGY_SPACESHIP" }
            .Sum(s => flavors.Entries(GrandStrategyFlavors.YieldsTable, s).Count));
        Assert.Empty(flavors.Entries("NoSuchTable", "AIGRANDSTRATEGY_CONQUEST"));
    }

    [Fact]
    public void GrandStrategyParseRejectsMalformedTables()
    {
        const string head = "table\tstrategy\tkey\tvalue\n";
        Assert.Throws<FormatException>(() => GrandStrategyFlavors.Parse(head + "Other\tS\tK\t1\n"));
        Assert.Throws<FormatException>(() => GrandStrategyFlavors.Parse(head + "AIGrandStrategy_Yields\tS\tK\t1.5\n"));
        Assert.Throws<FormatException>(() => GrandStrategyFlavors.Parse(head + "AIGrandStrategy_Yields\tS\tK\t1\nAIGrandStrategy_Yields\tS\tK\t2\n"));
    }

    [Fact]
    public void TacticalMovesMatchTheShippedData()
    {
        var moves = TacticalMoveDefinition.LoadEmbedded();
        Assert.Equal(55, moves.Count);
        Assert.Equal("TACTICAL_EMERGENCY_PURCHASES", moves[0].Type);

        var capture = moves.Single(m => m.Type == "TACTICAL_CAPTURE_CITY");
        Assert.Equal(new TacticalMoveDefinition("TACTICAL_CAPTURE_CITY", 150, 100, null, false, false, true, true, true), capture);

        var emergency = moves[0];
        Assert.Equal(200, emergency.Priority);
        Assert.True(emergency.DominanceZoneMove);
        Assert.Null(emergency.OffenseFlavorWeight);

        var ruins = moves.Single(m => m.Type == "TACTICAL_ANCIENT_RUINS");
        Assert.Equal((50, 50, true), (ruins.OffenseFlavorWeight, ruins.DefenseFlavorWeight, ruins.OperationsCanRecruit));

        Assert.Equal(-1, moves.Single(m => m.Type == "TACTICAL_UNASSIGNED").Priority);
    }

    [Fact]
    public void TacticalMovesAreAvailablePerEdition()
    {
        var moves = TacticalMoveDefinition.LoadEmbedded();
        var citadel = moves.Single(m => m.Type == "TACTICAL_PILLAGE_CITADEL");
        Assert.False(citadel.IsInEdition(AIEdition.Base));
        Assert.False(citadel.IsInEdition(AIEdition.GK));
        Assert.True(citadel.IsInEdition(AIEdition.BNW));

        var embarked = moves.Single(m => m.Type == "TACTICAL_ATTACK_EMBARKED_MILITARY_UNIT");
        Assert.True(embarked.IsInEdition(AIEdition.Base));
        Assert.False(embarked.IsInEdition(AIEdition.GK));
        Assert.False(embarked.IsInEdition(AIEdition.BNW));

    }

    [Fact]
    public void TacticalParseRejectsMalformedTables()
    {
        const string head = "Type\tPriority\tOffenseFlavorWeight\tDefenseFlavorWeight\tOperationsCanRecruit\tDominanceZoneMove\tin_base\tin_gk\tin_bnw\n";
        Assert.Throws<FormatException>(() => TacticalMoveDefinition.Parse(head + "M\tx\t\t\tfalse\t\tTrue\tTrue\tTrue\n"));
        Assert.Throws<FormatException>(() => TacticalMoveDefinition.Parse(head + "M\t1\t\t\tmaybe\t\tTrue\tTrue\tTrue\n"));
        Assert.Throws<FormatException>(() => TacticalMoveDefinition.Parse(head + "M\t1\t\t\tfalse\t\tTrue\tTrue\tTrue\nM\t1\t\t\tfalse\t\tTrue\tTrue\tTrue\n"));
        Assert.Single(TacticalMoveDefinition.Parse(head + "M\t1\t\t\tfalse\t\tTrue\tTrue\tTrue\n"));
    }
}
