using System.Globalization;
using System.Text;
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

    // Upstream parses with Ktor's Url(). Only URLs that start with the fixed prefix get here, so scheme and host
    // are already known to be fine and what is left is Ktor's path/query/fragment handling, ported by hand:
    // dot segments and empty segments are kept (System.Uri would normalize them away), a single trailing empty
    // segment is dropped, query values decode '+' as a space, and the fragment is ignored.
    // Upstream note: malformed percent escapes make Ktor throw URLDecodeException out of IdChecker;
    // here they are treated as an invalid id (null), a deliberate hardening.
    private static Friend? CheckAndReturnIdFromUrl(string id, string prefix)
    {
        const string PathStartMarker = "/Unciv/";
        string rest = id[(UncivUrlPrefix.Length - PathStartMarker.Length)..];

        int fragmentStart = rest.IndexOf('#', StringComparison.Ordinal);
        if (fragmentStart >= 0)
        {
            rest = rest[..fragmentStart];
        }

        string query = "";
        int queryStart = rest.IndexOf('?', StringComparison.Ordinal);
        if (queryStart >= 0)
        {
            query = rest[(queryStart + 1)..];
            rest = rest[..queryStart];
        }

        List<string> rawSegments = [.. rest.Split('/')];
        rawSegments.RemoveAt(0); // the empty part before the leading '/'
        if (rawSegments.Count > 1 && rawSegments[^1].Length == 0)
        {
            rawSegments.RemoveAt(rawSegments.Count - 1);
        }

        if (rawSegments.Count != 3)
        {
            return null;
        }

        string[] segments;
        string name;
        try
        {
            segments = [.. rawSegments.Select(segment => PercentDecode(segment, plusAsSpace: false))];
            name = ExtractQueryParameter(query, "name") ?? "";
        }
        catch (FormatException)
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
        return gameId == null ? null : new Friend(name, gameId);
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

    // Ktor decodes the whole query eagerly, so a malformed escape in any parameter invalidates the url
    private static string? ExtractQueryParameter(string query, string parameterName)
    {
        string? result = null;
        foreach (string pair in query.Split('&'))
        {
            if (pair.Length == 0)
            {
                continue;
            }

            int eqIndex = pair.IndexOf('=', StringComparison.Ordinal);
            string key = PercentDecode(eqIndex >= 0 ? pair[..eqIndex] : pair, plusAsSpace: true);
            string value = eqIndex >= 0 ? PercentDecode(pair[(eqIndex + 1)..], plusAsSpace: true) : "";
            if (key == parameterName)
            {
                result ??= value;
            }
        }

        return result;
    }

    /// <summary>UTF-8 percent decoding as Ktor does it; throws <see cref="FormatException"/> on a malformed escape.</summary>
    private static string PercentDecode(string text, bool plusAsSpace)
    {
        var bytes = new List<byte>(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '%')
            {
                if (i + 2 >= text.Length)
                {
                    throw new FormatException("Incomplete percent escape");
                }

                int hi = HexValue(text[i + 1]);
                int lo = HexValue(text[i + 2]);
                bytes.Add((byte)((hi << 4) | lo));
                i += 2;
            }
            else if (c == '+' && plusAsSpace)
            {
                bytes.Add((byte)' ');
            }
            else if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                bytes.AddRange(Encoding.UTF8.GetBytes(text.Substring(i, 2)));
                i++;
            }
            else
            {
                bytes.AddRange(Encoding.UTF8.GetBytes(c.ToString()));
            }
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => throw new FormatException("Invalid percent escape"),
    };

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
