using Unciv.GodotClient.Setup;
using Unciv.Harness;
using Unciv.Logic.Civilization;
using Unciv.Queries.Setup;

namespace Unciv.Tests.Godot;

// C015: the new-game form model (Godot-free): defaults, mutators refusing bad values, Build and the launcher.
public sealed class NewGameFormTests
{
    private const string Vanilla = "Civ V - Vanilla";
    private const string Gnk = "Civ V - Gods & Kings";

    private static NewGameForm Form(int maxHumans = 1)
    {
        ScenarioGame.LoadVanilla();
        return NewGameForm.FromRulesetCache(maxHumans);
    }

    private static NewGameForm VanillaForm(int maxHumans = 1)
    {
        var form = Form(maxHumans);
        Assert.Null(form.SetBaseRuleset(Vanilla));
        return form;
    }

    private static string[] Nations(NewGameForm form) => form.Slots.Select(it => it.Nation).ToArray();

    [Fact]
    public void DefaultsAreOneHumanAndThreeRandomAiAndTheQueryDefaults()
    {
        var form = Form();
        Assert.Equal(Gnk, form.BaseRuleset);
        Assert.Equal([new PlayerSlot("Random", true), new PlayerSlot("Random", false), new PlayerSlot("Random", false), new PlayerSlot("Random", false)], form.Slots);
        Assert.Equal(1, form.Humans);
        Assert.Equal(6, form.CityStates);
        Assert.Equal("Prince", form.Difficulty);
        Assert.Equal("Quick", form.Speed);
        Assert.Equal("Ancient era", form.Era);
        Assert.Equal("Pangaea", form.MapType);
        Assert.Equal("Rectangular", form.Shape);
        Assert.Equal("Small", form.Size);
        Assert.Equal("Default", form.Resources);
        Assert.True(form.WorldWrap);
        Assert.True(form.NuclearWeapons);
        Assert.True(form.Espionage);
        Assert.False(form.NoBarbarians || form.RagingBarbarians || form.OneCityChallenge || form.NoCityRazing || form.NoRuins || form.NoNaturalWonders);
        Assert.Equal("", form.SeedText);
        Assert.Equal(["Scientific", "Cultural", "Domination", "Diplomatic", "Time"], form.SelectableVictories);
        Assert.Equal(form.SelectableVictories.Order(StringComparer.Ordinal), form.Victories.Order(StringComparer.Ordinal));
        Assert.Empty(form.Problems());
        Assert.Equal(1, form.MaxHumans);
    }

    [Fact]
    public void ListsComeFromTheRuleset()
    {
        var form = Form();
        Assert.Equal(34, form.MaxPlayers);
        Assert.Equal(form.MaxPlayers, form.PlayableNations.Count);
        Assert.DoesNotContain("Spectator", form.PlayableNations);
        Assert.DoesNotContain("Barbarians", form.PlayableNations);
        Assert.Contains("Babylon", form.PlayableNations);
        Assert.True(form.MaxCityStates > 10);
        Assert.Equal(["Civ V - Brave New World", Gnk, Vanilla], form.BaseRulesets.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(Vanilla, form.AvailableMods);
    }

    [Fact]
    public void PlayerCountGrowsWithRandomAiAndRefusesOutOfRange()
    {
        var form = Form();
        Assert.Null(form.SetPlayerCount(6));
        Assert.Equal(6, form.Slots.Count);
        Assert.All(form.Slots.Skip(4), it => Assert.Equal(new PlayerSlot("Random", false), it));
        Assert.Null(form.SetPlayerCount(NewGameForm.MinPlayers));
        Assert.Equal(2, form.Slots.Count);
        Assert.Null(form.SetPlayerCount(form.MaxPlayers));
        Assert.Equal(form.MaxPlayers, form.Slots.Count);

        string expected = $"The number of players must be between 2 and {form.MaxPlayers}.";
        Assert.Equal(expected, form.SetPlayerCount(1));
        Assert.Equal(expected, form.SetPlayerCount(form.MaxPlayers + 1));
        Assert.Equal(expected, form.SetPlayerCount(-5));
        Assert.Equal(expected, form.SetPlayerCount(int.MinValue));
        Assert.Equal(form.MaxPlayers, form.Slots.Count);
    }

    [Fact]
    public void ShrinkingRemovesAiSlotsFromTheEndAndKeepsHumans()
    {
        var form = Form();
        Assert.Null(form.SetNation(1, "Rome"));
        Assert.Null(form.SetNation(2, "Greece"));
        Assert.Null(form.SetNation(3, "Egypt"));
        Assert.Null(form.SetHuman(1, true)); // the human moves to slot 1
        Assert.Null(form.SetPlayerCount(3));
        Assert.Equal(["Random", "Rome", "Greece"], Nations(form));
        Assert.Equal([false, true, false], form.Slots.Select(it => it.IsHuman));
        Assert.Null(form.SetPlayerCount(2));
        Assert.Equal(["Random", "Rome"], Nations(form));
        Assert.Equal([false, true], form.Slots.Select(it => it.IsHuman));
    }

    [Fact]
    public void ShrinkingWithOnlyHumansLeftRemovesTheLast()
    {
        var form = Form(maxHumans: 4);
        Assert.Null(form.SetPlayerCount(3));
        for (int i = 0; i < 3; i++) Assert.Null(form.SetHuman(i, true));
        Assert.Null(form.SetNation(0, "Rome"));
        Assert.Null(form.SetNation(2, "Greece"));
        Assert.Null(form.SetPlayerCount(2));
        Assert.Equal(["Rome", "Random"], Nations(form));
    }

    [Fact]
    public void NationsAreUniqueExceptRandomAndMustBePlayable()
    {
        var form = Form();
        Assert.Null(form.SetNation(0, "Babylon"));
        Assert.Equal("Babylon is already player 1.", form.SetNation(1, "Babylon"));
        Assert.Equal("Random", form.Slots[1].Nation);
        Assert.Null(form.SetNation(0, "Babylon")); // the slot's own nation again
        Assert.Null(form.SetNation(1, "Random"));
        Assert.Null(form.SetNation(2, "Random"));
        Assert.Equal("Spectator is not a nation you can play in this ruleset.", form.SetNation(1, "Spectator"));
        Assert.Equal("Barbarians is not a nation you can play in this ruleset.", form.SetNation(1, "Barbarians"));
        Assert.Equal("Atlantis is not a nation you can play in this ruleset.", form.SetNation(1, "Atlantis"));
        Assert.Equal(" is not a nation you can play in this ruleset.", form.SetNation(1, ""));
        Assert.Equal("There is no player 5.", form.SetNation(4, "Rome"));
        Assert.Equal("There is no player 0.", form.SetNation(-1, "Rome"));
    }

    [Fact]
    public void ACityStateIsNotAPlayableNation()
    {
        var form = Form();
        var cityState = form.Options.Nations.First(it => it.IsCityState).Name;
        Assert.DoesNotContain(cityState, form.PlayableNations);
        Assert.Equal($"{cityState} is not a nation you can play in this ruleset.", form.SetNation(0, cityState));
    }

    [Fact]
    public void ChoosingAnotherHumanMovesTheSingleHuman()
    {
        var form = Form();
        Assert.Null(form.SetHuman(2, true));
        Assert.Equal([false, false, true, false], form.Slots.Select(it => it.IsHuman));
        Assert.Null(form.SetHuman(2, true)); // already the human: unchanged
        Assert.Equal([false, false, true, false], form.Slots.Select(it => it.IsHuman));
        Assert.Null(form.SetHuman(2, false));
        Assert.Equal(0, form.Humans);
        Assert.Contains("No human players selected!", form.Problems());
        Assert.Equal("There is no player 9.", form.SetHuman(8, true));
        Assert.Equal("There is no player 0.", form.SetHuman(-1, false));
    }

    [Fact]
    public void WithTwoHumansAllowedTheOldestHumanIsDemotedWhenAThirdIsChosen()
    {
        var form = Form(maxHumans: 2);
        Assert.Null(form.SetHuman(1, true));
        Assert.Equal([true, true, false, false], form.Slots.Select(it => it.IsHuman));
        Assert.Null(form.SetHuman(3, true));
        Assert.Equal([false, true, false, true], form.Slots.Select(it => it.IsHuman));
        Assert.Null(form.SetHuman(1, true)); // already human, nothing demoted
        Assert.Equal([false, true, false, true], form.Slots.Select(it => it.IsHuman));
    }

    [Fact]
    public void TooManyHumansIsAProblem()
    {
        var form = Form(maxHumans: 2);
        Assert.Null(form.SetHuman(1, true));
        Assert.Empty(form.Problems());
        var strict = Form(maxHumans: 1);
        Assert.Null(strict.SetHuman(1, true));
        Assert.Equal(1, strict.Humans);
    }

    [Fact]
    public void CityStateCountIsBoundedByTheRuleset()
    {
        var form = Form();
        Assert.Null(form.SetCityStates(0));
        Assert.Equal(0, form.CityStates);
        Assert.Null(form.SetCityStates(form.MaxCityStates));
        Assert.Equal(form.MaxCityStates, form.CityStates);
        string expected = $"The number of city-states must be between 0 and {form.MaxCityStates}.";
        Assert.Equal(expected, form.SetCityStates(-1));
        Assert.Equal(expected, form.SetCityStates(form.MaxCityStates + 1));
        Assert.Equal(form.MaxCityStates, form.CityStates);
    }

    [Fact]
    public void VictoriesToggleAndRefuseUnknownOnes()
    {
        var form = Form();
        Assert.Null(form.SetVictory("Domination", false));
        Assert.DoesNotContain("Domination", form.Victories);
        Assert.Equal(4, form.Victories.Count);
        Assert.Null(form.SetVictory("Domination", true));
        Assert.Contains("Domination", form.Victories);
        Assert.Null(form.SetVictory("Domination", true));
        Assert.Equal(5, form.Victories.Count);
        Assert.Equal("Conquest is not a victory type of this ruleset.", form.SetVictory("Conquest", true));
        Assert.Equal("domination is not a victory type of this ruleset.", form.SetVictory("domination", true));
        foreach (string name in form.SelectableVictories) form.SetVictory(name, false);
        Assert.Contains("No victory conditions were selected!", form.Problems());
    }

    [Fact]
    public void SwitchingTheBaseRulesetKeepsValidChoicesAndResetsTheRest()
    {
        var form = Form();
        string gnkOnly = form.PlayableNations.Except(VanillaForm().PlayableNations).First();
        Assert.Null(form.SetNation(1, gnkOnly));
        Assert.Null(form.SetNation(2, "Babylon"));
        Assert.Null(form.SetPlayerCount(4));
        form.Difficulty = "Deity";
        form.Speed = "NoSuchSpeed";
        form.Era = "Nope era";
        Assert.Null(form.SetCityStates(12));
        Assert.Null(form.SetVictory("Cultural", false));

        Assert.Null(form.SetBaseRuleset(Vanilla));
        Assert.Equal(Vanilla, form.BaseRuleset);
        Assert.Equal(["Random", "Random", "Babylon", "Random"], Nations(form));
        Assert.Equal("Deity", form.Difficulty);
        Assert.Equal("Quick", form.Speed);
        Assert.Equal("Ancient era", form.Era);
        Assert.Equal(12, form.CityStates);
        Assert.DoesNotContain("Cultural", form.Victories);
        Assert.Contains("Scientific", form.Victories);
        Assert.Equal(form.PlayableNations.Count, form.MaxPlayers);
    }

    [Fact]
    public void ReloadRestoresAllVictoriesWhenNoneRemain()
    {
        var form = Form();
        foreach (string name in form.SelectableVictories) form.SetVictory(name, false);
        Assert.Null(form.SetBaseRuleset(Vanilla));
        Assert.Equal(5, form.Victories.Count);
    }

    [Fact]
    public void UnknownBaseRulesetOrModIsRefused()
    {
        var form = Form();
        Assert.Equal("Nonsense is not a base ruleset.", form.SetBaseRuleset("Nonsense"));
        Assert.Equal(Gnk, form.BaseRuleset);
        Assert.Equal("Civ V - Vanilla is not a mod that can be used.", form.SetMod(Vanilla, true));
        Assert.Equal("Ghost is not a mod that can be used.", form.SetMod("Ghost", true));
        Assert.Empty(form.Mods);
    }

    private static NewGameForm FormWithFakeMod(Func<IReadOnlyList<string>, bool> throws)
    {
        ScenarioGame.LoadVanilla();
        return new NewGameForm((baseRuleset, mods) =>
        {
            if (throws(mods)) throw new InvalidOperationException("clash");
            var real = new NewGameOptionsQuery(baseRuleset, []).Run();
            return real with { Mods = mods.ToList(), AvailableRulesets = [.. real.AvailableRulesets, new SetupMod("FakeMod", false, true), new SetupMod("OtherMod", false, true)] };
        });
    }

    [Fact]
    public void ModsAreToggledInOrderAndReloadTheOptions()
    {
        var form = FormWithFakeMod(_ => false);
        Assert.Equal(["FakeMod", "OtherMod"], form.AvailableMods);
        Assert.Null(form.SetMod("OtherMod", true));
        Assert.Null(form.SetMod("FakeMod", true));
        Assert.Equal(["OtherMod", "FakeMod"], form.Mods);
        Assert.Null(form.SetMod("OtherMod", false));
        Assert.Equal(["FakeMod"], form.Mods);
        Assert.Null(form.SetMod("FakeMod", true)); // on again: not duplicated
        Assert.Equal(["FakeMod"], form.Mods);
        Assert.Null(form.SetMod("FakeMod", false));
        Assert.Empty(form.Mods);
    }

    [Fact]
    public void AModComboThatCannotLoadLeavesTheFormUnchanged()
    {
        var form = FormWithFakeMod(mods => mods.Contains("FakeMod"));
        Assert.Equal("These rulesets cannot be combined: clash", form.SetMod("FakeMod", true));
        Assert.Empty(form.Mods);
        Assert.Equal(Gnk, form.BaseRuleset);
        Assert.Null(form.SetMod("OtherMod", true));
        Assert.Equal(["OtherMod"], form.Mods);
    }

    [Theory]
    [InlineData("", true, 0L)]
    [InlineData("   ", true, 0L)]
    [InlineData("0", true, 0L)]
    [InlineData("42", true, 42L)]
    [InlineData(" 42 ", true, 42L)]
    [InlineData("9223372036854775807", true, long.MaxValue)]
    [InlineData("9223372036854775808", false, 0L)]
    [InlineData("-1", false, 0L)]
    [InlineData("+5", false, 0L)]
    [InlineData("1,000", false, 0L)]
    [InlineData("1.5", false, 0L)]
    [InlineData("abc", false, 0L)]
    [InlineData("١٢", false, 0L)]
    public void SeedTextParsing(string text, bool ok, long expected)
    {
        Assert.Equal(ok, NewGameForm.TryParseSeed(text, out long seed, out string? problem));
        Assert.Equal(expected, seed);
        if (ok) Assert.Null(problem);
        else Assert.Equal("The seed must be a whole number from 0 to 9223372036854775807 (leave it empty for a random map).", problem);
    }

    [Fact]
    public void NullSeedTextIsBlank()
    {
        Assert.True(NewGameForm.TryParseSeed(null, out long seed, out _));
        Assert.Equal(0, seed);
    }

    [Fact]
    public void ProblemsNameEveryBadValueWithoutLoadingRulesets()
    {
        var form = Form();
        form.Difficulty = "Impossible";
        form.Speed = "Warp";
        form.Era = "Stone";
        form.MapType = "Cube";
        form.Shape = "Torus";
        form.Size = "Gigantic";
        form.Resources = "Everything";
        form.SeedText = "x1";
        Assert.Equal(
        [
            "Unknown difficulty Impossible",
            "Unknown game speed Warp",
            "Unknown starting era Stone",
            "Unknown map type Cube",
            "Unknown map shape Torus",
            "Unknown map size Gigantic",
            "Unknown resource setting Everything",
            "The seed must be a whole number from 0 to 9223372036854775807 (leave it empty for a random map).",
        ], form.Problems());
        var built = form.Build(1000);
        Assert.False(built.Ok);
        Assert.Null(built.Setup);
        Assert.Equal(form.Problems(), built.Errors);
    }

    [Fact]
    public void SlotAndCountProblems()
    {
        var form = Form(maxHumans: 1);
        form.SetPlayerCount(2);
        form.SetHuman(0, false);
        Assert.Equal(["No human players selected!"], form.Problems());
        form.SetHuman(0, true);
        Assert.Null(form.SetCityStates(0));
        Assert.Empty(form.Problems());
    }

    [Fact]
    public void BuildDescribesTheChoicesExactly()
    {
        var form = VanillaForm();
        Assert.Null(form.SetNation(0, "Babylon"));
        Assert.Null(form.SetNation(1, "Rome"));
        Assert.Null(form.SetPlayerCount(3));
        Assert.Null(form.SetCityStates(2));
        Assert.Null(form.SetVictory("Domination", false));
        form.Difficulty = "King";
        form.Speed = "Quick";
        form.Era = "Classical era";
        form.MapType = "Archipelago";
        form.Shape = "Hexagonal";
        form.Size = "Tiny";
        form.Resources = "Abundant";
        form.WorldWrap = false;
        form.NoRuins = true;
        form.NoNaturalWonders = true;
        form.SeedText = "1234";
        form.NoBarbarians = true;
        form.RagingBarbarians = true;
        form.OneCityChallenge = true;
        form.NoCityRazing = true;
        form.NuclearWeapons = false;
        form.Espionage = false;

        var built = form.Build(99);
        Assert.True(built.Ok);
        Assert.Empty(built.Errors);
        Assert.Equal(1234, built.Seed);
        var g = built.Setup!.GameParameters;
        var m = built.Setup.MapParameters;
        Assert.Equal(Vanilla, g.BaseRuleset);
        Assert.Empty(g.Mods);
        Assert.Equal(["Babylon", "Rome", "Random"], g.Players.Select(it => it.ChosenCiv));
        Assert.Equal([PlayerType.Human, PlayerType.AI, PlayerType.AI], g.Players.Select(it => it.PlayerType));
        Assert.Equal(2, g.NumberOfCityStates);
        Assert.False(g.RandomNumberOfCityStates);
        Assert.Equal(["Scientific", "Cultural", "Diplomatic", "Time"], g.VictoryTypes);
        Assert.Equal("King", g.Difficulty);
        Assert.Equal("Quick", g.Speed);
        Assert.Equal("Classical era", g.StartingEra);
        Assert.True(g.NoBarbarians);
        Assert.True(g.RagingBarbarians);
        Assert.True(g.OneCityChallenge);
        Assert.True(g.NoCityRazing);
        Assert.False(g.NuclearWeaponsEnabled);
        Assert.False(g.EspionageEnabled);
        Assert.Equal("Archipelago", m.Type);
        Assert.Equal("Hexagonal", m.Shape);
        Assert.Equal("Tiny", m.MapSize.Name);
        Assert.Equal(10, m.MapSize.Radius);
        Assert.Equal("Abundant", m.MapResources);
        Assert.False(m.WorldWrap);
        Assert.True(m.NoRuins);
        Assert.True(m.NoNaturalWonders);
        Assert.Equal(1234, m.Seed);
        Assert.Equal(Vanilla, m.BaseRuleset);
        Assert.Empty(m.Mods);
    }

    [Fact]
    public void BuildFlagsAreFalseAndTrueAsSet()
    {
        var form = VanillaForm();
        var g = form.Build(5).Setup!.GameParameters;
        Assert.False(g.NoBarbarians || g.RagingBarbarians || g.OneCityChallenge || g.NoCityRazing);
        Assert.True(g.NuclearWeaponsEnabled);
        Assert.True(g.EspionageEnabled);
        var m = form.Build(5).Setup!.MapParameters;
        Assert.True(m.WorldWrap);
        Assert.False(m.NoRuins || m.NoNaturalWonders);
    }

    [Fact]
    public void ABlankOrZeroSeedTakesTheClockAndATypedOneWins()
    {
        var form = VanillaForm();
        var blank = form.Build(777);
        Assert.Equal(777, blank.Seed);
        Assert.Equal(777, blank.Setup!.MapParameters.Seed);
        form.SeedText = "0";
        Assert.Equal(888, form.Build(888).Seed);
        form.SeedText = "5";
        var typed = form.Build(888);
        Assert.Equal(5, typed.Seed);
        Assert.Equal(5, typed.Setup!.MapParameters.Seed);
    }

    [Fact]
    public void EachBuildIsAFreshSetup()
    {
        var form = VanillaForm();
        var a = form.Build(1).Setup!;
        var b = form.Build(1).Setup!;
        Assert.NotSame(a, b);
        Assert.NotSame(a.GameParameters.Players, b.GameParameters.Players);
        a.GameParameters.Players.Clear();
        Assert.Equal(4, form.Build(1).Setup!.GameParameters.Players.Count);
    }

    private static NewGameForm FormWithPhantomNation()
    {
        ScenarioGame.LoadVanilla();
        return new NewGameForm((baseRuleset, mods) =>
        {
            var real = new NewGameOptionsQuery(baseRuleset, mods).Run();
            return real with { Nations = [.. real.Nations, new SetupNation("Atlantis", true, false, false, true)] };
        });
    }

    [Fact]
    public void TheEnginesValidationHasTheLastWord()
    {
        var form = FormWithPhantomNation();
        Assert.Null(form.SetNation(0, "Atlantis"));
        Assert.Empty(form.Problems());
        var built = form.Build(1);
        Assert.False(built.Ok);
        Assert.Null(built.Setup);
        Assert.Equal(0, built.Seed);
        Assert.Equal(["Unknown nation Atlantis"], built.Errors);
    }

    [Fact]
    public void LaunchStartsAGameWithTheChosenCivsAndSeed()
    {
        var form = VanillaForm();
        form.SetPlayerCount(2);
        form.SetNation(0, "Babylon");
        form.SetNation(1, "Rome");
        form.SetCityStates(0);
        form.Size = "Tiny";
        form.SeedText = "3";
        var result = NewGameLauncher.Launch(form, 1000);
        Assert.True(result.Ok);
        Assert.Empty(result.Errors);
        Assert.Equal("Babylon", result.HumanCiv);
        Assert.Equal(3, result.Seed);
        Assert.Equal(["Babylon", "Rome"], result.Game!.Civilizations.Where(it => it.IsMajorCiv()).Select(it => it.CivName).Order(StringComparer.Ordinal));
        Assert.Equal(1000, result.Game.CurrentTurnStartTime);
        Assert.Equal("Tiny", result.Game.TileMap.MapParameters.MapSize.Name);
    }

    [Fact]
    public void LaunchResolvesARandomHumanNation()
    {
        var form = VanillaForm();
        form.SetPlayerCount(2);
        form.SetCityStates(0);
        form.Size = "Tiny";
        var result = NewGameLauncher.Launch(form, 4242);
        Assert.True(result.Ok);
        Assert.Contains(result.HumanCiv, form.PlayableNations);
        Assert.Equal(4242, result.Seed);
        var again = NewGameLauncher.Launch(form, 4242);
        Assert.Equal(result.HumanCiv, again.HumanCiv); // same seed, same draw
    }

    [Fact]
    public void LaunchReportsFormProblemsInsteadOfThrowing()
    {
        var form = VanillaForm();
        form.SeedText = "-3";
        var result = NewGameLauncher.Launch(form, 1);
        Assert.False(result.Ok);
        Assert.Null(result.Game);
        Assert.Equal("", result.HumanCiv);
        Assert.Equal(0, result.Seed);
        Assert.Equal(["The seed must be a whole number from 0 to 9223372036854775807 (leave it empty for a random map)."], result.Errors);
    }

    [Fact]
    public void LaunchReportsEngineValidationErrors()
    {
        var form = FormWithPhantomNation();
        form.SetNation(0, "Atlantis");
        var result = NewGameLauncher.Launch(form, 1);
        Assert.False(result.Ok);
        Assert.Equal(["Unknown nation Atlantis"], result.Errors);
    }

    [Fact]
    public void LaunchTurnsAStartCrashIntoAMessage()
    {
        ScenarioGame.LoadVanilla();
        var form = new NewGameForm((baseRuleset, mods) =>
        {
            var real = new NewGameOptionsQuery(baseRuleset, mods).Run();
            return real with { MapOptions = real.MapOptions with { Types = [.. real.MapOptions.Types, "Bogus"] } };
        });
        form.SetBaseRuleset(Vanilla);
        form.SetPlayerCount(2);
        form.SetCityStates(0);
        form.Size = "Tiny";
        form.MapType = "Bogus";
        var result = NewGameLauncher.Launch(form, 10);
        Assert.False(result.Ok);
        var error = Assert.Single(result.Errors);
        Assert.StartsWith("The game could not be started: ", error);
        Assert.True(error.Length > "The game could not be started: ".Length);
    }

    [Fact]
    public void NationColoursComeFromTheRuleset()
    {
        var form = VanillaForm();
        Assert.Equal((new Unciv.Assets.Rgb(43, 81, 98), new Unciv.Assets.Rgb(201, 248, 255)), form.NationColours("Babylon"));
        Assert.Null(form.NationColours("Random"));
        Assert.Null(form.NationColours("Atlantis"));
        var rome = form.NationColours("Rome");
        Assert.NotEqual(form.NationColours("Babylon"), rome);
    }

    [Fact]
    public void NationDetailAndSummaryDescribeTheChoices()
    {
        var form = VanillaForm();
        var babylon = form.DescribeNation("Babylon");
        Assert.NotNull(babylon);
        Assert.Equal("Babylon", babylon.Name);
        Assert.False(string.IsNullOrEmpty(babylon.Leader));
        Assert.False(string.IsNullOrEmpty(babylon.UniqueName));
        Assert.Null(form.DescribeNation("Random"));
        Assert.Null(form.DescribeNation("Atlantis"));
        Assert.Equal($"{form.Slots.Count} players, {form.CityStates} city-states, {form.Difficulty}, {form.Speed}, {form.MapType} {form.Size}", form.Summary);
        Assert.Null(form.SetPlayerCount(2));
        Assert.StartsWith("2 players", form.Summary, StringComparison.Ordinal);
    }

    // Review additions (C015 r1): swapped fields, exact boundaries, argument passing and cache invalidation, each of which mutation survey found unpinned.

    private static readonly Action<NewGameForm>[] FlagToggles =
    [
        f => f.NoBarbarians = true, f => f.RagingBarbarians = true, f => f.OneCityChallenge = true, f => f.NoCityRazing = true,
        f => f.NuclearWeapons = false, f => f.Espionage = false, f => f.WorldWrap = false, f => f.NoRuins = true, f => f.NoNaturalWonders = true,
    ];

    private static bool[] Flags(Unciv.Models.Metadata.GameSetupInfo s) =>
    [
        s.GameParameters.NoBarbarians, s.GameParameters.RagingBarbarians, s.GameParameters.OneCityChallenge, s.GameParameters.NoCityRazing,
        s.GameParameters.NuclearWeaponsEnabled, s.GameParameters.EspionageEnabled, s.MapParameters.WorldWrap, s.MapParameters.NoRuins, s.MapParameters.NoNaturalWonders,
    ];

    [Fact]
    public void EachFlagReachesOnlyItsOwnParameter()
    {
        bool[] defaults = [false, false, false, false, true, true, true, false, false];
        Assert.Equal(defaults, Flags(VanillaForm().Build(1).Setup!));
        for (int i = 0; i < FlagToggles.Length; i++)
        {
            var form = VanillaForm();
            FlagToggles[i](form);
            var expected = (bool[])defaults.Clone();
            expected[i] = !expected[i];
            Assert.Equal(expected, Flags(form.Build(1).Setup!));
        }
    }

    [Theory]
    [InlineData("1", 1L)]
    [InlineData("2", 2L)]
    [InlineData("  7 ", 7L)]
    public void SmallTypedSeedsWinOverTheClock(string text, long expected)
    {
        var form = VanillaForm();
        form.SeedText = text;
        var built = form.Build(500);
        Assert.Equal(expected, built.Seed);
        Assert.Equal(expected, built.Setup!.MapParameters.Seed);
    }

    [Fact]
    public void CountsExactlyAtTheirMaximumAreValid()
    {
        var form = VanillaForm();
        var vanilla = Unciv.Models.Ruleset.RulesetCache.GetVanillaRuleset();
        Assert.Equal(vanilla.Nations.Values.Count(it => it.IsCityState), form.MaxCityStates);
        Assert.Equal(vanilla.Nations.Values.Count(it => it.IsMajorCiv && !it.IsSpectator), form.MaxPlayers);
        Assert.Null(form.SetPlayerCount(form.MaxPlayers));
        Assert.Null(form.SetCityStates(form.MaxCityStates));
        Assert.Empty(form.Problems());
        Assert.Equal(form.MaxPlayers, form.Slots.Count);
        Assert.Equal(form.MaxCityStates, form.CityStates);
    }

    [Fact]
    public void ShrinkingRemovesAnAiAtTheFrontBeforeAnyHuman()
    {
        var form = Form(2);
        form.SetPlayerCount(3);
        form.SetHuman(1, true);
        form.SetHuman(2, true); // two humans allowed: the oldest (slot 1) is demoted only when a third is chosen
        Assert.Equal([false, true, true], form.Slots.Select(it => it.IsHuman));
        Assert.Null(form.SetPlayerCount(2));
        Assert.Equal([true, true], form.Slots.Select(it => it.IsHuman));
    }

    private static NewGameForm FormRecordingCalls(List<(string? Base, string Mods)> calls)
    {
        ScenarioGame.LoadVanilla();
        return new NewGameForm((baseRuleset, mods) =>
        {
            calls.Add((baseRuleset, string.Join(",", mods)));
            var real = new NewGameOptionsQuery(baseRuleset, []).Run();
            return real with { Mods = mods.ToList(), AvailableRulesets = [.. real.AvailableRulesets, new SetupMod("FakeMod", false, true)] };
        });
    }

    [Fact]
    public void ReloadsAskForTheCurrentBaseRulesetAndMods()
    {
        var calls = new List<(string? Base, string Mods)>();
        var form = FormRecordingCalls(calls);
        Assert.Equal((null, ""), calls.Single());
        form.SetBaseRuleset(Vanilla);
        Assert.Equal((Vanilla, ""), calls[^1]);
        form.SetMod("FakeMod", true);
        Assert.Equal((Vanilla, "FakeMod"), calls[^1]);
        form.SetBaseRuleset(Gnk);
        Assert.Equal((Gnk, "FakeMod"), calls[^1]);
        form.SetMod("FakeMod", false);
        Assert.Equal((Gnk, ""), calls[^1]);
    }

    [Fact]
    public void NationColoursAndSlotsFollowTheBaseRuleset()
    {
        var form = Form();
        Assert.NotNull(form.NationColours("Austria")); // a Gods & Kings nation
        Assert.Null(form.SetNation(1, "Austria"));
        Assert.Null(form.SetBaseRuleset(Vanilla));
        Assert.Null(form.NationColours("Austria"));
        Assert.Equal("Random", form.Slots[1].Nation);
        Assert.Null(form.SetBaseRuleset(Gnk));
        Assert.NotNull(form.NationColours("Austria"));
    }

    [Fact]
    public void EveryEngineErrorIsReported()
    {
        ScenarioGame.LoadVanilla();
        var form = new NewGameForm((baseRuleset, mods) =>
        {
            var real = new NewGameOptionsQuery(baseRuleset, mods).Run();
            return real with { Nations = [.. real.Nations, new SetupNation("Atlantis", true, false, false, true), new SetupNation("Lemuria", true, false, false, true)] };
        });
        Assert.Null(form.SetNation(0, "Atlantis"));
        Assert.Null(form.SetNation(1, "Lemuria"));
        Assert.Equal(["Unknown nation Atlantis", "Unknown nation Lemuria"], form.Build(1).Errors);
    }

    [Fact]
    public void ALaunchedGameUsesTheFormsSeedNotTheClock()
    {
        var form = VanillaForm();
        form.SetPlayerCount(3);
        form.SetCityStates(3);
        form.Size = "Tiny";
        form.SeedText = "7";
        var launched = NewGameLauncher.Launch(form, 1_000_000);
        Assert.True(launched.Ok);
        var direct = Unciv.Logic.NewGame.Start(form.Build(1_000_000).Setup!, 7, 1_000_000);
        Assert.Equal(direct.Civilizations.Select(it => it.CivName), launched.Game!.Civilizations.Select(it => it.CivName));
        Assert.Equal(direct.Civilizations.First(it => it.IsHuman()).CivName, launched.HumanCiv);
        var otherSeed = Unciv.Logic.NewGame.Start(form.Build(1_000_000).Setup!, 1_000_000, 1_000_000);
        Assert.NotEqual(direct.Civilizations.Select(it => it.CivName), otherSeed.Civilizations.Select(it => it.CivName));
    }
}
