using Unciv.Assets;

namespace Unciv.Core.Tests.Assets;

public class CityRenameModelTests
{
    [Theory]
    [InlineData("  Ur  ", "Ur")]
    [InlineData("\tUruk\n", "Uruk")]
    [InlineData("New Babylon", "New Babylon")]
    public void NormalizeTrimsOnlyTheEnds(string typed, string expected) => Assert.Equal(expected, CityRenameModel.Normalize(typed));

    [Theory]
    [InlineData("Ur")]
    [InlineData("  Ur  ")]
    [InlineData("Nippur-on-Euphrates")]
    [InlineData("Zürich")]
    public void AGoodNameHasNoProblem(string typed) => Assert.Null(CityRenameModel.Problem(typed, "Babylon"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void AnEmptyNameIsRefused(string typed) => Assert.Equal("Enter a name", CityRenameModel.Problem(typed, "Babylon"));

    [Fact]
    public void TheLongestNameIsAcceptedAndOneMoreIsNot()
    {
        Assert.Null(CityRenameModel.Problem(new string('a', CityRenameModel.MaxLength), "Babylon"));
        Assert.Equal("The name can have at most 32 characters", CityRenameModel.Problem(new string('a', CityRenameModel.MaxLength + 1), "Babylon"));
    }

    [Fact]
    public void TheLengthLimitCountsTheTrimmedName()
    {
        Assert.Null(CityRenameModel.Problem("  " + new string('a', CityRenameModel.MaxLength) + "  ", "Babylon"));
    }

    [Theory]
    [InlineData("[Ur]")]
    [InlineData("A{b")]
    [InlineData("A}b")]
    [InlineData("A]b")]
    [InlineData("A[b")]
    [InlineData("A\"b")]
    [InlineData("A\\b")]
    [InlineData("A<b")]
    [InlineData("A>b")]
    [InlineData("A\u0007b")]
    [InlineData("A\nb")]
    public void ForbiddenCharactersAreRefused(string typed) =>
        Assert.Equal("The name cannot contain any of [ ] { } \" \\ < >", CityRenameModel.Problem(typed, "Babylon"));

    [Fact]
    public void TheCurrentNameIsNotARename()
    {
        Assert.Equal("That is already the city's name", CityRenameModel.Problem("Babylon", "Babylon"));
        Assert.Equal("That is already the city's name", CityRenameModel.Problem(" Babylon ", "Babylon"));
        Assert.Null(CityRenameModel.Problem("babylon", "Babylon"));
    }

    [Fact]
    public void ACommandCarriesTheCityIdAndTheTrimmedName()
    {
        var command = CityRenameModel.Command("city-7", "Babylon", "  Ur ");
        Assert.NotNull(command);
        Assert.Equal("city-7", command.CityId);
        Assert.Equal("Ur", command.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[x]")]
    [InlineData("Babylon")]
    public void NoCommandWhileThereIsAProblem(string typed) => Assert.Null(CityRenameModel.Command("city-7", "Babylon", typed));
}
