using Unciv.Logic;

namespace Unciv.Core.Tests.Logic;

/// <summary>
/// Ported from <c>tests/src/com/unciv/logic/IdHelperTests.kt</c>.
/// </summary>
public class IdCheckerTests
{
    [Fact]
    public void GetCheckDigit_Uuid_Tests()
    {
        const string correctString = "2ddb3a34-0699-4126-b7a5-38603e665928";
        const string inCorrectString1 = "2ddb3a34-0699-4126-b7a5-38603e665929";
        const string inCorrectString2 = "2ddb3a34-0969-4126-b7a5-38603e665928";
        const string inCorrectString3 = "2ddb3a34-0699-4126-b7a";
        const string inCorrectString4 = "0699-4126-b7a5-38603e665928-2ddb3a34";

        int? correctLuhn = IdChecker.GetCheckDigit(correctString);
        int? correctLuhn2 = IdChecker.GetCheckDigit(correctString);
        int? inCorrectLuhn1 = IdChecker.GetCheckDigit(inCorrectString1);
        int? inCorrectLuhn2 = IdChecker.GetCheckDigit(inCorrectString2);
        int? inCorrectLuhn3 = IdChecker.GetCheckDigit(inCorrectString3);
        int? inCorrectLuhn4 = IdChecker.GetCheckDigit(inCorrectString4);

        Assert.Equal(2, correctLuhn);
        Assert.Equal(correctLuhn, correctLuhn2);
        Assert.NotEqual(inCorrectLuhn1, correctLuhn);
        Assert.NotEqual(inCorrectLuhn2, correctLuhn);
        Assert.NotEqual(inCorrectLuhn3, correctLuhn);
        Assert.NotEqual(inCorrectLuhn4, correctLuhn);

        Assert.NotEqual(inCorrectLuhn1, inCorrectLuhn2);
        Assert.NotEqual(inCorrectLuhn1, inCorrectLuhn3);
        Assert.NotEqual(inCorrectLuhn1, inCorrectLuhn4);

        Assert.NotEqual(inCorrectLuhn2, inCorrectLuhn3);
        Assert.NotEqual(inCorrectLuhn2, inCorrectLuhn4);

        Assert.NotEqual(inCorrectLuhn3, inCorrectLuhn4);
    }

    [Fact]
    public void CheckAndReturnUuiId_Uuid_Valid_Success()
    {
        const string correctString = "2ddb3a34-0699-4126-b7a5-38603e665928";

        Assert.Equal(correctString, IdChecker.CheckAndReturnPlayerUuid(correctString)?.PlayerID);
        Assert.Equal(
            "c872b8e0-f274-47d4-b761-ce684c5d224c",
            IdChecker.CheckAndReturnUuiId("c872b8e0-f274-47d4-b761-ce684c5d224c"));

        Assert.Equal(correctString, IdChecker.CheckAndReturnUuiId($"G-{correctString}-2"));
        Assert.Equal(correctString, IdChecker.CheckAndReturnPlayerUuid($"P-{correctString}-2")?.PlayerID);
    }

    [Fact]
    public void CheckAndReturnUuiId_GameId_TooShort_IsNull()
    {
        Assert.Null(IdChecker.CheckAndReturnUuiId("2ddb3a34-0699-4126-b7a5-38603e66592"));
    }

    [Fact]
    public void CheckAndReturnUuiId_GameId_WrongPrefix_IsNull()
    {
        Assert.Null(IdChecker.CheckAndReturnUuiId("P-2ddb3a34-0699-4126-b7a5-38603e665928-2"));
    }

    [Fact]
    public void CheckAndReturnUuiId_PlayerId_WrongPrefix_IsNull()
    {
        Assert.Null(IdChecker.CheckAndReturnPlayerUuid("G-2ddb3a34-0699-4126-b7a5-38603e665928-2")?.PlayerID);
    }

    [Fact]
    public void CheckAndReturnUuiId_GameId_ChangedCheckDigit_IsNull()
    {
        Assert.Null(IdChecker.CheckAndReturnUuiId("G-2ddb3a34-0699-4126-b7a5-38603e665928-3"));
    }

    [Fact]
    public void CheckAndReturnUuiId_GameId_ChangedUuid_IsNull()
    {
        Assert.Null(IdChecker.CheckAndReturnUuiId("G-2ddb3a34-0699-4126-b7a5-48603e665928-2"));
    }

    [Fact]
    public void CheckAndReturnUuiId_GameId_WithPunctuation_Succeeds()
    {
        string? result = IdChecker.CheckAndReturnUuiId(" (“G-2ddb3a34-0699-4126-b7a5-38603e665928-2”.)\n");
        Assert.Equal("2ddb3a34-0699-4126-b7a5-38603e665928", result);
    }

    [Fact]
    public void CheckAndReturnUuiId_Url_Valid_Success()
    {
        const string correctString = "2ddb3a34-0699-4126-b7a5-38603e665928";

        Assert.Equal(correctString, IdChecker.CheckAndReturnPlayerUuid(correctString)?.PlayerID);
        Assert.Equal(
            "c872b8e0-f274-47d4-b761-ce684c5d224c",
            IdChecker.CheckAndReturnUuiId("c872b8e0-f274-47d4-b761-ce684c5d224c"));

        Assert.Equal(correctString, IdChecker.CheckAndReturnUuiId($"https://yairm210.github.io/Unciv/G/G-{correctString}-2"));
        Assert.Equal(correctString, IdChecker.CheckAndReturnPlayerUuid($"https://yairm210.github.io/Unciv/P/P-{correctString}-2")?.PlayerID);
    }

    [Fact]
    public void CheckAndReturnUuiId_Url_TooShort_IsNull()
    {
        Assert.Null(IdChecker.CheckAndReturnUuiId("https://yairm210.github.io/Unciv/G/2ddb3a34-0699-4126-b7a5-38603e66592"));
    }

    [Fact]
    public void CheckAndReturnUuiId_GameUrl_WrongPrefix_IsNull()
    {
        Assert.Null(IdChecker.CheckAndReturnUuiId("https://yairm210.github.io/Unciv/P/P-2ddb3a34-0699-4126-b7a5-38603e665928-2"));
    }

    [Fact]
    public void CheckAndReturnUuiId_PlayerUrl_WrongPrefix_IsNull()
    {
        Assert.Null(IdChecker.CheckAndReturnPlayerUuid("https://yairm210.github.io/Unciv/G/G-2ddb3a34-0699-4126-b7a5-38603e665928-2")?.PlayerID);
    }

    [Fact]
    public void CheckAndReturnUuiId_Url_ChangedCheckDigit_IsNull()
    {
        Assert.Null(IdChecker.CheckAndReturnUuiId("https://yairm210.github.io/Unciv/G/G-2ddb3a34-0699-4126-b7a5-38603e665928-3"));
    }

    [Fact]
    public void CheckAndReturnUuiId_Url_ChangedUuid_IsNull()
    {
        Assert.Null(IdChecker.CheckAndReturnUuiId("https://yairm210.github.io/Unciv/G/G-2ddb3a34-0699-4126-b7a5-48603e665928-2"));
    }

    [Fact]
    public void CheckAndReturnUuiId_Url_WithPunctuation_Succeeds()
    {
        string? result = IdChecker.CheckAndReturnUuiId(" (“https://yairm210.github.io/Unciv/G/G-2ddb3a34-0699-4126-b7a5-38603e665928-2”.)\n");
        Assert.Equal("2ddb3a34-0699-4126-b7a5-38603e665928", result);
    }

    [Fact]
    public void CheckAndReturnUuiId_Url_WithName_Succeeds()
    {
        IdChecker.Friend? result = IdChecker.CheckAndReturnPlayerUuid(" (“https://yairm210.github.io/Unciv/P/P-2ddb3a34-0699-4126-b7a5-38603e665928-2?name=%C4%90%E1%BA%B7ng”.)\n");
        Assert.Equal("2ddb3a34-0699-4126-b7a5-38603e665928", result?.PlayerID);
        Assert.Equal("Đặng", result?.Name);
    }

    [Fact]
    public void DeepLinkHelpers_And_InvalidCheckDigitChars()
    {
        Assert.True(IdChecker.IsGameDeepLink("https://yairm210.github.io/Unciv/G/G-2ddb3a34-0699-4126-b7a5-38603e665928-2"));
        Assert.False(IdChecker.IsGameDeepLink("https://yairm210.github.io/Unciv/P/P-2ddb3a34-0699-4126-b7a5-38603e665928-2"));
        Assert.True(IdChecker.IsFriendDeepLink("https://yairm210.github.io/Unciv/P/P-2ddb3a34-0699-4126-b7a5-38603e665928-2"));
        Assert.False(IdChecker.IsFriendDeepLink("https://yairm210.github.io/Unciv/G/G-2ddb3a34-0699-4126-b7a5-38603e665928-2"));
        Assert.Null(IdChecker.GetCheckDigit("2ddb3a34_0699_4126_b7a5_38603e665928"));
    }
}
