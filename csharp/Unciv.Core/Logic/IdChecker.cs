using System.Globalization;
using Unciv.Utils;

namespace Unciv.Logic;

/// <summary>
/// Checks whether a Game- or Player-ID matches the old (36-char UUID) or new
/// (<c>G-UUID-CheckDigit</c> / <c>P-UUID-CheckDigit</c> or deep-link URL) format.
/// Ported from <c>core/src/com/unciv/logic/IdChecker.kt</c>.
/// </summary>
public static class IdChecker
{
    public const string UncivUrlPrefix = "https://yairm210.github.io/Unciv/";

    private const string ValidCheckDigitChars = "0123456789ABCDEFGHIJKLMNOPQRSTUVYWXZ-";

    public sealed record Friend(string Name, string PlayerID)
    {
        public Friend() : this("", "") { }
    }

    public static bool IsGameDeepLink(string url) =>
        url.StartsWith(UncivUrlPrefix + "G/", StringComparison.OrdinalIgnoreCase);

    public static bool IsFriendDeepLink(string url) =>
        url.StartsWith(UncivUrlPrefix + "P/", StringComparison.OrdinalIgnoreCase);

    public static Friend? CheckAndReturnPlayerUuid(string playerId) =>
        CheckAndReturnId(playerId, "P");

    public static string? CheckAndReturnUuiId(string gameId) =>
        CheckAndReturnId(gameId, "G")?.PlayerID;

    private static Friend? CheckAndReturnId(string id, string prefix)
    {
        string trimmedId = TrimSpaceOrWrapper(id);
        if (trimmedId.StartsWith(UncivUrlPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return CheckAndReturnIdFromUrl(trimmedId, prefix);
        }

        string? uuid = CheckAndReturnUuiId(trimmedId, prefix);
        return uuid != null ? new Friend("", uuid) : null;
    }

    private static Friend? CheckAndReturnIdFromUrl(string id, string prefix)
    {
        if (!Uri.TryCreate(id, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        string[] rawSegments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (rawSegments.Length != 3)
        {
            return null;
        }

        string[] segments = new string[3];
        try
        {
            for (int i = 0; i < 3; i++)
            {
                segments[i] = Uri.UnescapeDataString(rawSegments[i]);
            }
        }
        catch (UriFormatException)
        {
            return null;
        }

        if (!segments[0].Equals("Unciv", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!segments[1].Equals(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string? gameId = CheckAndReturnUuiId(segments[2], prefix);
        if (gameId == null)
        {
            return null;
        }

        string name;
        try
        {
            name = ExtractQueryParameter(uri.Query, "name") ?? "";
        }
        catch (UriFormatException)
        {
            return null;
        }

        return new Friend(name, gameId);
    }

    private static string? CheckAndReturnUuiId(string id, string prefix)
    {
        string trimmedPlayerId = TrimSpaceOrWrapper(id);
        if (trimmedPlayerId.Length == 40)
        {
            if (!trimmedPlayerId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string checkDigit = trimmedPlayerId.Substring(trimmedPlayerId.Length - 1, 1);
            string shortenedPlayerId = trimmedPlayerId.Substring(2, 36);
            int? calculatedCheckDigit = GetCheckDigit(shortenedPlayerId);
            if (calculatedCheckDigit == null
                || calculatedCheckDigit.Value.ToString(CultureInfo.InvariantCulture) != checkDigit)
            {
                return null;
            }

            if (!shortenedPlayerId.IsUUID())
            {
                return null;
            }

            return shortenedPlayerId;
        }

        if (trimmedPlayerId.Length == 36)
        {
            if (!trimmedPlayerId.IsUUID())
            {
                return null;
            }

            return trimmedPlayerId;
        }

        return null;
    }

    /// <summary>
    /// Adapted from https://wiki.openmrs.org/display/docs/Check+Digit+Algorithm
    /// </summary>
    public static int? GetCheckDigit(string uuid)
    {
        string idWithoutCheckdigit = uuid.Trim().ToUpperInvariant();
        int sum = 0;

        for (int i = 0; i < idWithoutCheckdigit.Length; i++)
        {
            char ch = idWithoutCheckdigit[idWithoutCheckdigit.Length - i - 1];
            if (!ValidCheckDigitChars.Contains(ch, StringComparison.Ordinal))
            {
                return null;
            }

            int digit = ch - 48;
            int weight = i % 2 == 0
                ? (2 * digit) - (digit / 5) * 9
                : digit;
            sum += weight;
        }

        sum = Math.Abs(sum) + 10;
        return (10 - (sum % 10)) % 10;
    }

    private static string? ExtractQueryParameter(string query, string parameterName)
    {
        if (string.IsNullOrEmpty(query))
        {
            return null;
        }

        ReadOnlySpan<char> span = query.AsSpan();
        if (span[0] == '?')
        {
            span = span[1..];
        }

        string[] pairs = span.ToString().Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (string pair in pairs)
        {
            int eqIndex = pair.IndexOf('=', StringComparison.Ordinal);
            string rawKey = eqIndex >= 0 ? pair[..eqIndex] : pair;
            string key = Uri.UnescapeDataString(rawKey);
            if (key == parameterName)
            {
                return eqIndex >= 0 ? Uri.UnescapeDataString(pair[(eqIndex + 1)..]) : "";
            }
        }

        return null;
    }

    private static string TrimSpaceOrWrapper(string input)
    {
        int start = 0;
        int end = input.Length - 1;
        while (start <= end && IsSpaceOrWrapper(input[start]))
        {
            start++;
        }

        while (end >= start && IsSpaceOrWrapper(input[end]))
        {
            end--;
        }

        return input.Substring(start, end - start + 1);
    }

    private static bool IsSpaceOrWrapper(char c) =>
        CharUnicodeInfo.GetUnicodeCategory(c) is
            UnicodeCategory.SpaceSeparator or
            UnicodeCategory.OpenPunctuation or
            UnicodeCategory.ClosePunctuation or
            UnicodeCategory.InitialQuotePunctuation or
            UnicodeCategory.FinalQuotePunctuation or
            UnicodeCategory.OtherPunctuation or
            UnicodeCategory.Control;
}
