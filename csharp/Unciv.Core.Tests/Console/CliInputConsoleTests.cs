using Unciv.DevConsole;
using Unciv.Models.StatTypes;

namespace Unciv.Core.Tests.DevConsole;

// T229: the console's parameter parser (Kotlin CliInput) and its error type behaviour.
public sealed class CliInputConsoleTests
{
    private static List<string> Split(string text) => CliInput.SplitToCliInput(text).Select(it => it.ToString()).ToList();

    [Fact]
    public void SplitKeepsQuotedPhrasesAndTheTrailingEmptyToken()
    {
        Assert.Equal(["unit", "add", "babylon", "\"Mechanized Infantry\""], Split("unit add Babylon \"Mechanized  Infantry\""));
        Assert.Equal(["city", "setname", "\"Open quote\""], Split("city setname \"open quote"));
        var withTrailing = CliInput.SplitToCliInput("civ addstat ");
        Assert.Equal(3, withTrailing.Count);
        Assert.True(withTrailing[^1].IsEmpty());
        Assert.Empty(CliInput.SplitToCliInput(""));
    }

    [Fact]
    public void DashedAndQuotedFormsCompareWithEachOtherAndWithStrings()
    {
        var dashed = new CliInput("mechanized-infantry");
        var quoted = new CliInput("\"mechanized infantry\"");
        Assert.Equal(CliInput.Method.Dashed, dashed.MethodUsed);
        Assert.Equal(CliInput.Method.Quoted, quoted.MethodUsed);
        Assert.Equal("\"Mechanized infantry\"", quoted.ToString());
        Assert.True(dashed.Equals(quoted));
        Assert.True(quoted.Equals(dashed));
        Assert.True(dashed.Equals("Mechanized Infantry"));
        Assert.True(quoted.Equals("Mechanized Infantry"));
        Assert.False(dashed.Equals("Rifleman"));
        Assert.False(dashed.Equals((object)3));
        Assert.True(dashed.Equals((object)quoted));
        Assert.Equal(dashed.GetHashCode(), quoted.GetHashCode());
        Assert.Equal(0, dashed.CompareTo(quoted));
        Assert.True(dashed.CompareTo(new CliInput("zulu")) < 0);
        Assert.True(quoted.CompareTo(new CliInput("\"Zulu\"")) < 0);
        Assert.True(new CliInput("zulu").CompareTo(quoted) > 0);
        Assert.True(dashed.CompareTo(null) > 0);
        Assert.True(dashed.StartsWith(new CliInput("mech")));
        Assert.True(quoted.StartsWith(new CliInput("\"MECH")));
        Assert.True(quoted.StartsWith(new CliInput("mech")));
        Assert.True(dashed.StartsWith(new CliInput("\"Mech")));
        Assert.Equal("mechanized-infantry", dashed.ToMethod(CliInput.Method.Dashed).Content);
        Assert.Equal("Mechanized-infantry", dashed.ToMethod(CliInput.Method.Quoted).Content);
        Assert.Equal("[mechanized-infantry]", dashed.ToStringAsPlaceholder());
        Assert.Equal("\"[mechanized infantry]\"", quoted.ToStringAsPlaceholder());
        Assert.Equal("mechanized infantry", quoted.OriginalUnquoted());
        Assert.Equal(21, quoted.OriginalLength());
        Assert.True(CliInput.Empty.IsEmpty());
        Assert.True(dashed.IsNotEmpty());
    }

    [Fact]
    public void NumbersBooleansAndEnumsParseOrThrowAConsoleError()
    {
        Assert.Equal(-5, new CliInput("-5").ToInt());
        Assert.Equal(2.5f, new CliInput("2.5").ToFloat());
        Assert.True(new CliInput("true").ToBoolean());
        Assert.False(new CliInput("FALSE").ToBoolean());
        Assert.Equal(Stat.Gold, new CliInput("gold").ToStat());
        Assert.Equal(Stat.Faith, new CliInput("Faith").EnumValue<Stat>());
        Assert.Null(new CliInput("nonsense").EnumValueOrNull<Stat>());

        Assert.Equal("'x' is not a valid number.", Assert.Throws<ConsoleErrorException>(() => new CliInput("x").ToInt()).Error);
        Assert.Throws<ConsoleErrorException>(() => new CliInput("1.5").ToInt());
        Assert.Throws<ConsoleErrorException>(() => new CliInput("NaN").ToFloat());
        Assert.Throws<ConsoleErrorException>(() => new CliInput("Infinity").ToFloat());
        Assert.Throws<ConsoleErrorException>(() => new CliInput("yes").ToBoolean());
        Assert.Throws<ConsoleErrorException>(() => new CliInput("nonsense").ToStat());
        var enumError = Assert.Throws<ConsoleErrorException>(() => new CliInput("nonsense").EnumValue<Stat>());
        Assert.Contains("Options are: [Production, Food, Gold", enumError.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void FindMatchesNamesAndListsOptionsOnFailure()
    {
        string[] names = ["Warrior", "Great Library", "Spearman"];
        Assert.Equal("Great Library", new CliInput("great-library").FindOrNull(names));
        Assert.Equal("Great Library", new CliInput("\"great library\"").FindOrNull(names));
        Assert.Null(new CliInput("archer").FindOrNull(names));
        Assert.Equal("Warrior", new CliInput("warrior").Find(names, it => it));
        Assert.Equal("Spearman", new CliInput("spearman").FindOrNull(names, it => it));
        var error = Assert.Throws<ConsoleErrorException>(() => new CliInput("archer").Find(names, it => it));
        Assert.Equal("'archer' is not a valid String. Options are: Warrior, Great Library, Spearman.", error.Error);
        var many = Enumerable.Range(0, 50).Select(it => "n" + it).ToList();
        Assert.EndsWith(", ... (8 not shown).", Assert.Throws<ConsoleErrorException>(() => new CliInput("zz").Find(many, it => it)).Error, StringComparison.Ordinal);
    }

    [Fact]
    public void AutocompleteCompletesUniqueMatchesAndCommonPrefixes()
    {
        var unique = CliInput.GetAutocompleteString(new CliInput("war"), ["warrior", "spearman"]);
        Assert.Equal("warrior ", unique.Text);
        Assert.Null(unique.Message);

        var prefix = CliInput.GetAutocompleteString(new CliInput("dr"), ["drill-i", "drill-ii", "drill-iii"]);
        Assert.Equal("drill-i", prefix.Text); // no space: the user may want drill-ii
        Assert.Equal("Matching completions: drill-i, drill-ii, drill-iii", prefix.Message);

        var common = CliInput.GetAutocompleteString(new CliInput("d"), ["drill-i", "dog"]);
        Assert.Equal("d", common.Text);

        Assert.Null(CliInput.GetAutocompleteString(new CliInput("x"), ["warrior"]).Text);

        var quoted = CliInput.GetAutocompleteString(new CliInput("\"great"), ["Great Library", "Great Wall"]);
        Assert.Equal("\"Great ", quoted.Text);
        Assert.Equal("Matching completions: \"Great Library\", \"Great Wall\"", quoted.Message);

        var quotedUnique = CliInput.GetAutocompleteString(new CliInput("great-l"), ["Great Library", "Warrior"]);
        Assert.Equal("great-library ", quotedUnique.Text);

        var truncated = CliInput.GetAutocompleteString(new CliInput("n"), Enumerable.Range(0, 50).Select(it => "n" + it));
        Assert.Contains("... (8 not shown)", truncated.Message, StringComparison.Ordinal);
    }
}
