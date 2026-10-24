using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Translations;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Core.Tests.Models.Ruleset.Unique;

/// <summary>
/// Reaches the <c>DeprecatedType</c> paths of <see cref="UniqueObject"/> (<c>HasFlag</c>, <c>GetDeprecationAnnotation</c>, the sign and parameter
/// shuffling of <c>GetReplacementText</c>) through entries registered in the stub table. These are hand-derived from reading
/// <c>Unique.getReplacementText</c>, not compared with the JVM; T029 owes the golden over every real <c>DeprecatedUniqueType</c>.
/// Every test registers its own placeholder text, so tests running in parallel do not see each other's entries.
/// </summary>
public class DeprecatedUniqueSeamTests
{
    private static UniqueObject Deprecated(string deprecatedText, string replaceWith, string uniqueText, params UniqueFlag[] flags)
    {
        string placeholder = deprecatedText.GetPlaceholderText();
        DeprecatedUniqueTypes.UniqueTypeMap[placeholder] =
            new DeprecatedUniqueTypeEntry(deprecatedText, new HashSet<UniqueFlag>(flags), replaceWith);
        return new UniqueObject(uniqueText);
    }

    [Fact]
    public void ConstructorAssignsTheDeprecatedTypeOnlyWhenThereIsNoRealType()
    {
        var unique = Deprecated("Seam1 gain [amount] Gold", "Seam1 get [amount] Gold", "Seam1 gain [30] Gold");
        Assert.Null(unique.Type);
        Assert.NotNull(unique.DeprecatedType);
        Assert.Same(unique.DeprecatedType, unique.GetDeprecationAnnotation());
    }

    [Fact]
    public void HasFlagReadsTheFlagsOfTheDeprecatedType()
    {
        var unique = Deprecated("Seam2 [amount] thing", "Seam2 [amount] other", "Seam2 [1] thing", UniqueFlag.HiddenToUsers);
        Assert.True(unique.HasFlag(UniqueFlag.HiddenToUsers));
        Assert.False(unique.HasFlag(UniqueFlag.NoConditionals));
    }

    [Fact]
    public void ReplacementKeepsAnUnsignedNumberUnsigned()
    {
        var unique = Deprecated("Seam3 gain [amount] Gold", "Seam3 get [amount] Gold", "Seam3 gain [30] Gold");
        Assert.Equal("Seam3 get [30] Gold", unique.GetReplacementText(_ => true));
    }

    [Fact]
    public void ReplacementWithASignedPlaceholderKeepsTheNegativeSign()
    {
        var unique = Deprecated("Seam4 gain [amount] Gold", "Seam4 lose [-amount] Gold", "Seam4 gain [-30] Gold");
        Assert.Equal("Seam4 lose [-30] Gold", unique.GetReplacementText(_ => true));
    }

    [Fact]
    public void ReplacementDoesNotAddASignWhenNeitherSideIsSignedQuirk()
    {
        // Kotlin quirk kept: the sign logic only runs if the deprecated text, the new text or the replacement has a sign;
        // a bare 30 under a "[-amount]" placeholder stays 30.
        var unique = Deprecated("Seam5 gain [amount] Gold", "Seam5 lose [-amount] Gold", "Seam5 gain [30] Gold");
        Assert.Equal("Seam5 lose [30] Gold", unique.GetReplacementText(_ => true));
    }

    [Fact]
    public void RepeatedPlaceholdersAreReplacedInOrder()
    {
        var unique = Deprecated("Seam6 from [amount] to [amount]", "Seam6 [amount] and [amount] swapped", "Seam6 from [1] to [2]");
        Assert.Equal("Seam6 [1] and [2] swapped", unique.GetReplacementText(_ => true));
    }

    [Fact]
    public void SeveralReplacementsAreFilteredByTheValidator()
    {
        var unique = Deprecated("Seam7 foo [x]", "Seam7 bar [x]\" OR \"Seam7 baz [x]", "Seam7 foo [1]");
        Assert.Equal("Seam7 baz [1]", unique.GetReplacementText(it => it.Text.Contains("baz")));
        // all fail: the unreplaced alternatives, joined; all pass: the replaced alternatives, joined
        Assert.Equal("Seam7 bar [x]\", \"Seam7 baz [x]", unique.GetReplacementText(_ => false));
        Assert.Equal("Seam7 bar [1]\", \"Seam7 baz [1]", unique.GetReplacementText(_ => true));
    }
}
