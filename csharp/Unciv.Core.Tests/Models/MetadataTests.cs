using System.Text.Json;
using Unciv.Json;
using Unciv.Logic.Civilization;
using Unciv.Models.Metadata;
using Unciv.Utils;

namespace Unciv.Core.Tests.Models;

// Goldens: LocaleCode, GameParameters.toString/clone/json come from running the real compiled classes of the installed
// Unciv.jar (json() = the game's libGDX Json) on OpenJDK 25.
public class MetadataTests
{
    private const string LocaleTable = "Afrikaans|af-ZA|af|Afrikaans|false\nArabic|ar-IQ|ar|Arabic|true\nBangla|bn-BD|bn|Bangla|false\nBelarusian|be-BY|be|Belarusian|false\nBosnian|bs-BA|bs|Bosnian|false\nBrazilianPortuguese|pt-BR|pt|Brazilian_Portuguese|false\nBulgarian|bg-BG|bg|Bulgarian|false\nCatalan|ca-ES|ca|Catalan|false\nCroatian|hr-HR|hr|Croatian|false\nCzech|cs-CZ|cs|Czech|false\nDanish|da-DK|da|Danish|true\nDutch|nl-NL|nl|Dutch|false\nEnglish|en-US|en|English|false\nEsperanto|eo|eo|Esperanto|false\nEstonian|et-EE|et|Estonian|true\nFilipino|fil-PH-u-co-search|fil|Filipino|false\nFinnish|fi-FI|fi|Finnish|false\nFrench|fr-FR|fr|French|false\nGalician|gl-ES|gl|Galician|false\nGerman|de-DE|de|German|false\nGreek|el-GR|el|Greek|false\nHindi|hi-IN|hi|Hindi|false\nHungarian|hu-HU|hu|Hungarian|false\nIndonesian|in-ID|id|Indonesian|false\nItalian|it-IT|it|Italian|false\nJapanese|ja-JP|ja|Japanese|false\nKorean|ko-KR|ko|Korean|false\nLatin|la-IT|la|Latin|false\nLatvian|lv-LV|lv|Latvian|true\nLithuanian|lt-LT|lt|Lithuanian|false\nMalay|ms-MY|ms|Malay|false\nMaltese|mt-MT|mt|Maltese|false\nNavajo|nv-US|nv|Navajo|false\nNorwegian|no-NO|no|Norwegian|false\nNorwegianNynorsk|nn-NO|nn|NorwegianNynorsk|true\nPersianPinglishDIN|fa-IR|fa|Persian_(Pinglish-DIN)|false\nPersianPinglishUN|fa-IR|fa|Persian_(Pinglish-UN)|false\nPolish|pl-PL|pl|Polish|false\nPortuguese|pt-PT|pt|Portuguese|false\nRomanian|ro-RO|ro|Romanian|false\nRussian|ru-RU|ru|Russian|false\nRusyn|rue-SK-u-kr-cyrl-latn-digit|rue|Rusyn|false\nSerbian|sr-RS|sr|Serbian|true\nSimplifiedChinese|zh-CN|zh|Simplified_Chinese|false\nSlovak|sk-SK|sk|Slovak|true\nSpanish|es-ES|es|Spanish|false\nSwedish|sv-SE|sv|Swedish|false\nThai|th-TH|th|Thai|false\nTokiPona|tok|tok|Toki_Pona|false\nTraditionalChinese|zh-TW|zh|Traditional_Chinese|false\nTurkish|tr-TR|tr|Turkish|false\nUkrainian|uk-UA|uk|Ukrainian|false\nVietnamese|vi-VN|vi|Vietnamese|false\nZulu|zu-ZA|zu|Zulu|false";

    [Fact]
    public void LocaleCodeTableMatchesKotlin()
    {
        var actual = LocaleCodeExtensions.Entries.Select(code =>
            $"{code}|{code.LanguageTag()}|{code.FastlaneFolder()}|{code.LanguageName()}|{(code.Unused() ? "true" : "false")}");
        Assert.Equal(LocaleTable.Split('\n'), actual);
    }

    [Fact]
    public void LocaleCodeLookups()
    {
        Assert.Equal(LocaleCode.BrazilianPortuguese, LocaleCodeExtensions.Find("Brazilian_Portuguese"));
        Assert.Null(LocaleCodeExtensions.Find("Nope"));
        Assert.Null(LocaleCodeExtensions.Find("BrazilianPortuguese")); // the file name form, not the enum name
        Assert.Equal("en", LocaleCodeExtensions.FastlaneFolder("Nope"));
        Assert.Equal("rue", LocaleCodeExtensions.FastlaneFolder("Rusyn"));
        Assert.Equal(47, LocaleCodeExtensions.GetSupportedLanguages().Count());
        Assert.DoesNotContain("Arabic", LocaleCodeExtensions.GetSupportedLanguages()); // unused
    }

    [Fact]
    public void DefaultBaseRulesetIsBrave_New_World_T359()
    {
        const string bnw = "Civ V - Brave New World";
        Assert.Equal(bnw, new GameParameters().BaseRuleset);
        Assert.Equal(bnw, new Unciv.Logic.Map.MapParameters().BaseRuleset);
        Assert.Equal(bnw, new Unciv.Net.Lobby.LobbySettings().Ruleset);
    }

    [Fact]
    public void BaseRulesetAndPlayerTypeNames()
    {
        Assert.Equal("Civ V - Vanilla", BaseRuleset.Civ_V_Vanilla.FullName());
        Assert.Equal("Civ V - Gods & Kings", BaseRuleset.Civ_V_GnK.FullName());
        Assert.Equal("Civ V - Brave New World", BaseRuleset.Civ_V_BNW.FullName());
        Assert.Equal("\"Human\"", UncivJson.Serialize(PlayerType.Human));
        Assert.Equal(PlayerType.AI, UncivJson.Deserialize<PlayerType>("\"AI\""));
    }

    [Fact]
    public void ConstantsSpotChecks()
    {
        Assert.Equal("Random", Constants.Random);
        Assert.Equal(-1, Constants.NoId);
        Assert.Equal(["All", "all"], Constants.All);
        Assert.Equal(["Forest", "Jungle"], Constants.Vegetation);
        Assert.Equal('\u241F', Constants.StringSplitCharacter);
        Assert.Equal("\" OR \"", Constants.UniqueOrDelimiter);
        Assert.Equal(0.05f, Constants.MinimumMovementEpsilon);
        Assert.Equal("Fresh water", Constants.FreshWater);
        Assert.Equal("Fresh Water", Constants.FreshWaterFilter);
        Assert.Equal("The religion of TheLegend27", Constants.NoReligionName);
    }

    // --- GameParameters / Player ---

    private static GameParameters CustomParameters()
    {
        var g = new GameParameters
        {
            Difficulty = "Emperor",
            Speed = "Epic",
            RandomNumberOfPlayers = true,
            MinNumberOfPlayers = 2,
            MaxNumberOfPlayers = 5,
            NumberOfCityStates = 9,
            NoBarbarians = true,
            GodMode = true,
            BaseRuleset = "Civ V - Vanilla",
            StartingEra = "Medieval era",
            MultiplayerServerUrl = "https://example.org"
        };
        g.ShowCivilizationStats = false;
        g.Mods.Add("ModB");
        g.Mods.Add("ModA");
        g.VictoryTypes.Add("Cultural");
        g.VictoryTypes.Add("Time");
        g.Players.Add(new Player("Rome", PlayerType.Human, "abc-123"));
        return g;
    }

    // json.toJson(custom) as written by the Kotlin game (defaults left out)
    private const string KotlinCustomJson = """{"difficulty":"Emperor","speed":"Epic","randomNumberOfPlayers":true,"minNumberOfPlayers":2,"maxNumberOfPlayers":5,"players":[{"playerType":"Human"},{},{},{},{"chosenCiv":"Rome","playerType":"Human","playerId":"abc-123"}],"numberOfCityStates":9,"noBarbarians":true,"godMode":true,"victoryTypes":["Cultural","Time"],"startingEra":"Medieval era","showVictoryStats":false,"showCivilizationStats":false,"multiplayerServerUrl":"https://example.org","baseRuleset":"Civ V - Vanilla","mods":["ModB","ModA"]}""";

    [Fact]
    public void ReadsJsonWrittenByTheKotlinGame()
    {
        var g = UncivJson.Deserialize<GameParameters>(KotlinCustomJson)!;
        Assert.Equal("Emperor", g.Difficulty);
        Assert.Equal("Epic", g.Speed);
        Assert.True(g.RandomNumberOfPlayers);
        Assert.Equal((2, 5, 9), (g.MinNumberOfPlayers, g.MaxNumberOfPlayers, g.NumberOfCityStates));
        Assert.True(g.NoBarbarians && g.GodMode);
        Assert.False(g.NoCityRazing);
        Assert.True(g.NuclearWeaponsEnabled); // omitted = default
        Assert.Equal(["Cultural", "Time"], g.VictoryTypes);
        Assert.Equal(["ModB", "ModA"], g.Mods);
        Assert.Equal("https://example.org", g.MultiplayerServerUrl);
        Assert.Equal(5, g.Players.Count);
        Assert.Equal((Constants.Random, PlayerType.Human, ""), (g.Players[0].ChosenCiv, g.Players[0].PlayerType, g.Players[0].PlayerId));
        Assert.Equal((Constants.Random, PlayerType.AI, ""), (g.Players[1].ChosenCiv, g.Players[1].PlayerType, g.Players[1].PlayerId));
        Assert.Equal(("Rome", PlayerType.Human, "abc-123"), (g.Players[4].ChosenCiv, g.Players[4].PlayerType, g.Players[4].PlayerId));
        Assert.False(g.ShowCivilizationStats);
    }

    [Fact]
    public void ReadsOldStyleJsonWithoutShowCivilizationStats()
    {
        // from the Kotlin run: the getter falls back to showVictoryStats; players default chosenCiv "Random"
        var g = UncivJson.Deserialize<GameParameters>("""{"showVictoryStats":false,"difficulty":"Chieftain","mods":["X","Y"],"players":[{"chosenCiv":"Greece"},{"playerType":"Human"}]}""")!;
        Assert.Equal("Chieftain", g.Difficulty);
        Assert.Null(g.ShowCivilizationStatsRaw);
        Assert.False(g.ShowCivilizationStats);
        Assert.Equal(["X", "Y"], g.Mods);
        Assert.Equal(2, g.Players.Count);
        Assert.Equal(("Greece", PlayerType.AI), (g.Players[0].ChosenCiv, g.Players[0].PlayerType));
        Assert.Equal((Constants.Random, PlayerType.Human), (g.Players[1].ChosenCiv, g.Players[1].PlayerType));
    }

    [Fact]
    public void JsonReadDoesNotRunTheShowCivilizationStatsSetterSideEffect()
    {
        // libGDX sets the backing field only; the order of the two members in the file must not matter
        var a = UncivJson.Deserialize<GameParameters>("""{"showVictoryStats":false,"showCivilizationStats":true}""")!;
        var b = UncivJson.Deserialize<GameParameters>("""{"showCivilizationStats":true,"showVictoryStats":false}""")!;
        foreach (var g in new[] { a, b })
        {
            Assert.True(g.ShowCivilizationStatsRaw);
            Assert.False(g.ShowVictoryStats);
            Assert.True(g.ShowCivilizationStats);
        }
        // the Kotlin-style setter does update both
        var c = new GameParameters { ShowCivilizationStats = false };
        Assert.False(c.ShowVictoryStats);
    }

    [Fact]
    public void WrittenJsonUsesKotlinNamesInDeclarationOrderAndRoundTrips()
    {
        string json = UncivJson.Serialize(CustomParameters());
        string[] kotlinKeys = JsonSerializer.Deserialize<JsonElement>(KotlinCustomJson).EnumerateObject().Select(p => p.Name).ToArray();
        string[] ourKeys = JsonSerializer.Deserialize<JsonElement>(json).EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(kotlinKeys, ourKeys.Where(kotlinKeys.Contains).ToArray()); // same names, same relative order
        Assert.Contains("isOnlineMultiplayer", ourKeys);
        Assert.Contains("acceptedModCheckErrors", ourKeys);

        var back = UncivJson.Deserialize<GameParameters>(json)!;
        Assert.Equal(CustomParameters().ToString(), back.ToString());
        Assert.Equal(UncivJson.Serialize(CustomParameters()), UncivJson.Serialize(back));
    }

    [Fact]
    public void ToStringMatchesKotlin()
    {
        Assert.Equal("(Emperor Epic Medieval era, 2 Human, 3 AI, Random number of Players: 2..5, 9 CS, No barbs, God mode, Enabled Victories: Cultural, Time, Civ V - Vanilla, mods=(ModB,ModA))", CustomParameters().ToString());
        Assert.Equal("(Prince Quick Ancient era, 1 Human, 3 AI, 6 CS, Enabled Victories: , Civ V - Brave New World, no mods)", new GameParameters().ToString());
        foreach (var (count, expected) in new[] { (6, "M1,M2,M3,M4,M5,M6"), (7, "M1,M2,M3,M4,M5,M6,..."), (8, "M1,M2,M3,M4,M5,M6,...") })
        {
            var g = new GameParameters();
            for (int i = 1; i <= count; i++) g.Mods.Add("M" + i);
            Assert.Equal($"(Prince Quick Ancient era, 1 Human, 3 AI, 6 CS, Enabled Victories: , Civ V - Brave New World, mods=({expected}))", g.ToString());
        }
    }

    [Fact]
    public void CloneCopiesEverythingExceptGodMode()
    {
        var original = CustomParameters();
        var clone = original.Clone();

        Assert.False(clone.GodMode); // reset on clone, as the Kotlin run's cloneJson shows
        Assert.True(original.GodMode);
        Assert.Equal(original.ToString().Replace(", God mode", ""), clone.ToString());
        Assert.False(clone.ShowCivilizationStats);
        Assert.False(clone.ShowVictoryStats);
        Assert.Equal(false, clone.ShowCivilizationStatsRaw);
        Assert.Equal(original.Players.Count, clone.Players.Count);
        Assert.NotSame(original.Players, clone.Players);
        Assert.NotSame(original.Players[4], clone.Players[4]); // T219: the Player instances are copied too
        Assert.Equal(original.Players[4].ChosenCiv, clone.Players[4].ChosenCiv);
        Assert.NotSame(original.Mods, clone.Mods);
        Assert.Equal(["ModB", "ModA"], clone.Mods);
        // a never-set showCivilizationStats becomes explicit in the clone (the Kotlin setter runs with the resolved value)
        var plain = new GameParameters { ShowVictoryStats = false };
        Assert.Null(plain.ShowCivilizationStatsRaw);
        Assert.Equal(false, plain.Clone().ShowCivilizationStatsRaw);
    }

    [Fact]
    public void ModsAndBaseRulesetListsBaseFirst()
    {
        var all = CustomParameters().GetModsAndBaseRuleset();
        Assert.Equal(["Civ V - Vanilla", "ModB", "ModA"], all);
        all.Add("Extra"); // a fresh instance the caller may mutate
        Assert.Equal(2, CustomParameters().Mods.Count);
    }

    // --- ModCategory ---

    [Fact]
    public void ModCategoryEqualityLooksAtTheTopicOnly()
    {
        var a = new ModCategory("A label", "unciv-mod-a", true, "2020", "2021");
        var b = new ModCategory("Another", "unciv-mod-a", false, "", "");
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, ModCategory.All);
        Assert.Equal("A label", a.ToString());
        Assert.Equal("unciv-mod", ModCategory.All.Topic);
        Assert.Equal(JavaHash.HashCode("unciv-mod-a"), a.GetHashCode());
    }

    // --- LinkedHashSet ---

    [Fact]
    public void LinkedHashSetKeepsInsertionOrder()
    {
        var set = new LinkedHashSet<string>();
        Assert.True(set.Add("b"));
        Assert.True(set.Add("a"));
        Assert.False(set.Add("b")); // already there: not moved
        Assert.True(set.Add("c"));
        Assert.Equal(["b", "a", "c"], set);
        Assert.True(set.Remove("b"));
        Assert.False(set.Remove("b"));
        Assert.True(set.Add("b")); // removed and re-added goes last
        Assert.Equal(["a", "c", "b"], set);
        Assert.Equal(3, set.Count);
        Assert.Contains("c", set);
        Assert.Equal("[a, c, b]", set.ToString());
        set.Clear();
        Assert.True(set.IsEmpty);
    }

    [Fact]
    public void LinkedHashSetEqualityIgnoresOrderLikeAKotlinSet()
    {
        var x = new LinkedHashSet<int>([1, 2, 3]);
        var y = new LinkedHashSet<int>([3, 2, 1]);
        Assert.True(x.Equals(y));
        Assert.Equal(x.GetHashCode(), y.GetHashCode());
        Assert.False(x.Equals(new LinkedHashSet<int>([1, 2])));
        Assert.True(x.SetEquals([2, 3, 1]));
        Assert.True(new LinkedHashSet<int>([1]).IsProperSubsetOf(x));
        Assert.True(x.AddAll([3, 4, 5]));
        Assert.False(x.AddAll([4, 5]));
        Assert.Equal([1, 2, 3, 4, 5], x);
    }

    [Fact]
    public void LinkedHashSetSerializesAsAnOrderedArray()
    {
        var set = new LinkedHashSet<string>(["z", "a", "m"]);
        string json = UncivJson.Serialize(set);
        Assert.Equal("[\"z\",\"a\",\"m\"]", json);
        Assert.Equal(["z", "a", "m"], UncivJson.Deserialize<LinkedHashSet<string>>(json)!);
    }
}
