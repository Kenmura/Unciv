using Unciv.Utils;

namespace Unciv.Core.Tests.Utils;

/// <summary>Log facade (T047) and its printf-style formatting. Expected strings come from <c>String.format</c> on the JVM (JDK 25).</summary>
[Collection("TranslationState")] // the process-wide statics (Translations.Current, Log.Backend) are shared by all test classes in this collection
public class LogTests : IDisposable
{
    private sealed class CapturingBackend : ILogBackend
    {
        public List<string> Lines { get; } = [];

        public bool IsRelease { get; set; }

        public void Debug(Tag tag, string curThreadName, string msg) => Lines.Add($"D [{tag.Name}] {msg}");

        public void Error(Tag tag, string curThreadName, string msg) => Lines.Add($"E [{tag.Name}] {msg}");

        public string GetSystemInfo() => "sys";
    }

    private readonly CapturingBackend backend = new();

    public LogTests()
    {
        Log.Backend = backend;
        Log.DisableLogsFrom.Clear();
        Log.EnableLogsFrom.Clear();
    }

    public void Dispose()
    {
        Log.Backend = NullLogBackend.Instance;
        Log.DisableLogsFrom.Clear();
        foreach (string disabled in new[] { "Battle", "Music", "Sounds", "Translations", "WorkerAutomation", "assignRegions", "RoadBetweenCitiesAutomation", "StageMouseOverDebug", "DiplomacyAutomation" })
        {
            Log.DisableLogsFrom.Add(disabled);
        }

        Log.EnableLogsFrom.Clear();
    }

    private static readonly (string Format, object?[] Args, string Expected)[] FormatCases =
    [
        ("%s", [true], "true"),
        ("%s", [null], "null"),
        ("%s", [1.0f], "1.0"),
        ("%s", [1.0E10], "1.0E10"),
        ("%s", [0.001], "0.001"),
        ("%s", [0.0001], "1.0E-4"),
        ("%s", [1234567.0], "1234567.0"),
        ("%s", [12345678.0], "1.2345678E7"),
        ("%s", [-0.5], "-0.5"),
        ("%s", [100.0f], "100.0"),
        ("%s", [1.5E-7], "1.5E-7"),
        ("%s", [0.1f], "0.1"),
        ("%s", [new List<int> { 1, 2, 3 }], "[1, 2, 3]"),
        ("%s", ['x'], "x"),
        ("%s", [42], "42"),
        ("%s", [42L], "42"),
        ("%s and %s", ["a", 7], "a and 7"),
        ("%d", [5], "5"),
        ("%5d|", [42], "   42|"),
        ("%-5d|", [42], "42   |"),
        ("%05d", [42], "00042"),
        ("%05d", [-42], "-0042"),
        ("%,d", [1234567], "1,234,567"),
        ("%+d", [5], "+5"),
        ("%d", [long.MinValue], "-9223372036854775808"),
        ("%f", [1.5], "1.500000"),
        ("%.2f", [0.125], "0.13"),
        ("%.2f", [0.135], "0.14"),
        ("%.2f", [2.675], "2.68"),
        ("%.0f", [2.5], "3"),
        ("%.0f", [0.5], "1"),
        ("%.3f", [-0.0004], "-0.000"),
        ("%.1f", [0.05], "0.1"),
        ("%10.3f|", [3.14159], "     3.142|"),
        ("%-10.1f|", [3.14159], "3.1       |"),
        ("%.2f", [0.1f], "0.10"),
        ("%,.2f", [1234567.891], "1,234,567.89"),
        ("%08.2f", [-3.5], "-0003.50"),
        ("%x", [255], "ff"),
        ("%x", [-1], "ffffffff"),
        ("%X", [255L], "FF"),
        ("%x", [-1L], "ffffffffffffffff"),
        ("%c", ['a'], "a"),
        ("%b", [true], "true"),
        ("%b", [null], "false"),
        ("%%", [], "%"),
        ("100%%", [], "100%"),
        ("a%nb", [], "a\nb"),
        ("%2$s %1$s", ["x", "y"], "y x"),
        ("%.3s", ["abcdef"], "abc"),
        ("%8s|", ["ab"], "      ab|"),
        ("%-8s|", ["ab"], "ab      |"),
        ("%S", ["ab"], "AB"),
        ("%s took %sms", ["Battle", 12L], "Battle took 12ms"),
        ("%s", [new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 }], "{a=1, b=2}"),
    ];

    [Fact]
    public void FormattingMatchesJavaStringFormat()
    {
        foreach (var (format, args, expected) in FormatCases)
        {
            Assert.True(expected == JavaFormat.Format(format, args), $"'{format}': expected '{expected}' but was '{JavaFormat.Format(format, args)}'");
        }
    }

    [Fact]
    public void MismatchedFormatIsLoggedRawInsteadOfThrowing()
    {
        Log.Error("needs [%s] and [%s]", "one");
        Log.Error("%d", "not a number");
        Assert.Equal(["E [LogTests] needs [%s] and [%s] [one]", "E [LogTests] %d [not a number]"], backend.Lines);
    }

    [Fact]
    public void DebugAndErrorCarryTheCallingClassAsTag()
    {
        Log.Debug("hello %s", "world");
        Log.Error("bad %d", 3);
        Log.Debug("no args: 100% safe");
        Assert.Equal(["D [LogTests] hello world", "E [LogTests] bad 3", "D [LogTests] no args: 100% safe"], backend.Lines);
    }

    [Fact]
    public void ThrowablesAreAppended()
    {
        Log.Error("failed", new InvalidOperationException("boom"));
        Assert.Single(backend.Lines);
        Assert.StartsWith("E [LogTests] failed | System.InvalidOperationException: boom", backend.Lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void LambdaParametersAreEvaluatedOnlyWhenLogging()
    {
        int calls = 0;
        Func<object?> expensive = () => { calls++; return "value"; };
        Log.Debug("x=%s", expensive);
        Assert.Equal(1, calls);
        Assert.Equal(["D [LogTests] x=value"], backend.Lines);

        backend.IsRelease = true;
        Log.Debug("x=%s", expensive);
        Assert.Equal(1, calls);
        Log.Error("x=%s", expensive); // errors always log
        Assert.Equal(2, calls);
        Assert.Equal(2, backend.Lines.Count);
    }

    [Fact]
    public void TagFiltersFollowDisableAndEnableLists()
    {
        Log.DisableLogsFrom.Add("Log"); // matches the tag "LogTests" by containment
        Log.Debug("hidden");
        Assert.Empty(backend.Lines);
        Log.Debug(new Tag("Other"), "shown");
        Log.DisableLogsFrom.Clear();
        Log.EnableLogsFrom.Add("Only");
        Log.Debug("hidden by onlyLog");
        Log.Debug(new Tag("OnlyThis"), "shown too");
        Log.Error(new Tag("Whatever"), "errors are filtered by nothing: %s", "yes");
        Assert.Equal(["D [Other] shown", "D [OnlyThis] shown too", "E [Whatever] errors are filtered by nothing: yes"], backend.Lines);
    }

    [Fact]
    public void SoftRequireLogsAnErrorAndReturnsTheCondition()
    {
        Assert.True(Log.SoftRequire(true, "never %s", "logged"));
        Assert.False(Log.SoftRequire(false, "input %s must be positive", -1));
        Assert.Equal(["E [LogTests] input -1 must be positive"], backend.Lines);
    }

    [Fact]
    public void DefaultBackendDropsEverythingAndNestedTypesKeepTheirName()
    {
        new Nested().Say(backend);
        Assert.Equal(["D [LogTests$Nested] from nested", "D [LogTests$Nested] from closure"], backend.Lines);
        Log.Backend = NullLogBackend.Instance;
        Log.Debug("dropped");
        Log.Error("dropped");
        Assert.Equal(2, backend.Lines.Count);
        Assert.False(Log.ShouldLog(new Tag("X")));
        Assert.Equal("", Log.GetSystemInfo());
    }

    private sealed class Nested
    {
        public void Say(CapturingBackend capture)
        {
            Log.Backend = capture;
            Log.Debug("from nested");
            Func<int> closure = () => { Log.Debug("from closure"); return 1; };
            closure();
        }
    }
}
