using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unciv.Core.Tests.Policy;

// T119: Kotlin `x!!` throws at once; the C# null-forgiving `x!` only silences the compiler, so the failure
// shows up later or never. Production code must use `.NotNull()` (NullExtensions) or a real null check.
// A `!` that is genuinely safe (e.g. definite assignment after a guard) needs `// JUSTIFY: T###` within 3 lines above.
public partial class NullForgivingTests
{
    private static readonly string[] ScannedProjects = ["Unciv.Core", "Unciv.Harness", "Unciv.Godot.Harness"];

    [GeneratedRegex(@"JUSTIFY:\s*T\d{3}\b")]
    private static partial Regex JustifyRegex();

    [GeneratedRegex(@"#pragma\s+warning\s+disable|SuppressMessage\s*\(|throw\s+new\b")]
    private static partial Regex OtherSuppressionRegex();

    [Fact]
    public void ProductionCodeHasNoUnjustifiedNullForgivingOperator()
    {
        var root = FindRoot();
        var offenders = new List<string>();
        foreach (var project in ScannedProjects)
        {
            var dir = Path.Combine(root, project);
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var sep = Path.DirectorySeparatorChar;
                if (file.Contains(sep + "obj" + sep, StringComparison.Ordinal) || file.Contains(sep + "bin" + sep, StringComparison.Ordinal)) continue;
                foreach (var line in FindUnjustifiedLines(File.ReadAllText(file)))
                    offenders.Add($"{Path.GetRelativePath(root, file)}:{line}");
            }
        }

        Assert.True(offenders.Count == 0,
            "Null-forgiving '!' hides a null (Kotlin '!!' throws). Use .NotNull() or a null-safe rewrite, or add `// JUSTIFY: T### why` within 3 lines above: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void ScannerFindsOnlyRealNullForgivingOperators()
    {
        const string source = """
            var a = b!;
            var c = d!.e;
            var f = g![0];
            var h = (i)!;
            var j = k!!;
            var l = m<int>()!;
            var n = o! + 1;
            """;
        Assert.Equal([1, 2, 3, 4, 5, 6, 7], FindNullForgivingLines(source).Distinct());

        const string benign = """"
            if (a != b && !c) { }
            var s = "x!";
            var v = @"x!""y";
            var i = $"hello {a}!";
            var r = """raw!""";
            var ch = '!';
            // comment!
            /* block! */
            var t = x ? !y : !z;
            var f = (Func<bool>)(() => !q);
            var g = !(a);
            var h = (bool)!x;
            var j = a >!b;
            var k = a > !b;
            var m = (bool)(!x);
            var p = (bool)!x.Foo();
            """";
        Assert.Empty(FindNullForgivingLines(benign));
    }

    [Fact]
    public void ScannerFindsNullForgivingInsideInterpolationHoles()
    {
        const string source = """"
            var s = $"hello {name!}";
            var raw = $$"""multi {{"inner" + value!}}""";
            var nested = $"outer {x ?? $"inner {y!}"}";
            var formatted = $"{z!,10:format}";
            """";
        Assert.Equal([1, 2, 3, 4], FindNullForgivingLines(source));
    }

    [Fact]
    public void ScannerFindsNullForgivingAtLineStartAfterNewline()
    {
        const string source = """
            var a = b
                !;
            """;
        Assert.Equal([2], FindNullForgivingLines(source));
    }

    [Fact]
    public void JustificationRequiresValidTicketId()
    {
        const string invalidTicket = """
            // JUSTIFY: missing ticket id
            var a = b!;
            // JUSTIFY: Temporary
            var c = d!;
            """;
        Assert.Equal([2, 4], FindUnjustifiedLines(invalidTicket));

        const string validTicket = """
            // JUSTIFY: T125 valid ticket id
            var a = b!;
            """;
        Assert.Empty(FindUnjustifiedLines(validTicket));
    }

    [Fact]
    public void JustificationIsTiedToOneSpecificSite()
    {
        const string sharedJustification = """
            // JUSTIFY: T125 should only cover the first operator
            var a = b!;
            var c = d!;
            """;
        Assert.Equal([3], FindUnjustifiedLines(sharedJustification));

        const string distinctJustifications = """
            // JUSTIFY: T125 first operator
            var a = b!;
            // JUSTIFY: T125 second operator
            var c = d!;
            """;
        Assert.Empty(FindUnjustifiedLines(distinctJustifications));

        const string multipleOnSameLineOneJustify = """
            var x = a! + b!; // JUSTIFY: T125 only one justification
            """;
        Assert.Equal([1], FindUnjustifiedLines(multipleOnSameLineOneJustify));

        const string multipleOnSameLineTwoJustify = """
            // JUSTIFY: T125 first
            // JUSTIFY: T125 second
            var x = a! + b!;
            """;
        Assert.Empty(FindUnjustifiedLines(multipleOnSameLineTwoJustify));
    }

    [Fact]
    public void OtherSuppressionsDoNotJustifyNullForgiving()
    {
        const string source = """
            #pragma warning disable CA1031 // JUSTIFY: T039 pragma suppression
            var a = b!;
            throw new NotSupportedException(); // JUSTIFY: T118 throw suppression
            var c = d!;
            """;
        Assert.Equal([2, 4], FindUnjustifiedLines(source));
    }

    [Fact]
    public void JustificationMustBeWithinThreeLinesAbove()
    {
        const string tooFar = """
            // JUSTIFY: T125 too far above
            var w = 1;
            var x = 2;
            var y = 3;
            var a = b!;
            """;
        Assert.Equal([5], FindUnjustifiedLines(tooFar));

        const string withinThree = """
            // JUSTIFY: T125 within three lines above
            var w = 1;
            var x = 2;
            var a = b!;
            """;
        Assert.Empty(FindUnjustifiedLines(withinThree));
    }

    internal static IEnumerable<int> FindNullForgivingLines(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            if (node is PostfixUnaryExpressionSyntax postfix && postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression))
            {
                yield return postfix.OperatorToken.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            }
            else if (node is CastExpressionSyntax cast
                     && cast.Expression is PrefixUnaryExpressionSyntax prefix
                     && prefix.IsKind(SyntaxKind.LogicalNotExpression)
                     && prefix.Operand.IsMissing)
            {
                yield return prefix.OperatorToken.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            }
        }
    }

    internal static IEnumerable<int> FindUnjustifiedLines(string source)
    {
        var rawLines = source.Split('\n');
        var opLines = FindNullForgivingLines(source).ToList();
        if (opLines.Count == 0) yield break;

        var commentLines = new List<int>();
        for (var i = 0; i < rawLines.Length; i++)
        {
            var lineText = rawLines[i];
            if (JustifyRegex().IsMatch(lineText) && !OtherSuppressionRegex().IsMatch(lineText))
            {
                commentLines.Add(i + 1);
            }
        }

        var usedComments = new HashSet<int>();

        foreach (var opLine in opLines)
        {
            int? match = null;
            for (var c = opLine; c >= int.Max(1, opLine - 3); c--)
            {
                if (commentLines.Contains(c) && !usedComments.Contains(c))
                {
                    match = c;
                    break;
                }
            }

            if (match.HasValue)
            {
                usedComments.Add(match.Value);
            }
            else
            {
                yield return opLine;
            }
        }
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Unciv.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Unciv.sln not found");
    }
}
