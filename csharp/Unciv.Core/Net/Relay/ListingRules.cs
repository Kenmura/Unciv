using System.Globalization;
using System.Text;
using System.Text.Json;
using Unciv.Utils;

namespace Unciv.Net.Relay;

/// <summary>What a listing body says (design/RELAY.md section 7). Every field is a claim of the host, signed with its key; none is an address, password or token.</summary>
public sealed record ListingInfo(
    long Seq,
    string Name,
    string HostName,
    int Players,
    int Slots,
    int Ai,
    string Ruleset,
    string RulesetHash,
    IReadOnlyList<string> Mods,
    string Speed,
    string Map,
    bool PasswordRequired,
    string TurnMode,
    bool Started,
    string Version,
    int Proto);

/// <summary>
/// The schema and text rules of a listing (RELAY.md section 7 and A8). The relay runs them on registration and every client runs the very same ones on what a relay serves, so a malicious relay
/// cannot smuggle control or bidi characters into the lobby browser.
/// </summary>
public static class ListingRules
{

    /// <summary>True when the text is NFC, has <paramref name="min"/> to <paramref name="max"/> characters and holds no control, format (bidi, zero-width), separator, surrogate-half, private-use or unassigned character.</summary>
    public static bool TextOk(string? text, int min, int max)
    {
        if (text == null || text.Length < min || text.Length > max) return false;
        for (var i = 0; i < text.Length; i++)
        {
            var category = char.IsSurrogatePair(text, i) ? CharUnicodeInfo.GetUnicodeCategory(text, i) : CharUnicodeInfo.GetUnicodeCategory(text[i]);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
                or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned) return false;
            if (char.IsHighSurrogate(text[i])) i++;
        }
        return IsNfc(text);
    }

    private static bool IsNfc(string text)
    {
        try { return text.IsNormalized(NormalizationForm.FormC); }
        catch (ArgumentException) { return false; }
    }

    /// <summary>T339: a display text made acceptable to the listing rules: NFC, without the characters the listing rules refuse, trimmed, at most <paramref name="max"/> characters (never cutting a surrogate pair).</summary>
    public static string Clean(string text, int max)
    {
        string normalized;
        try { normalized = text.Normalize(NormalizationForm.FormC); }
        catch (ArgumentException) { return ""; }
        var builder = new StringBuilder();
        for (var i = 0; i < normalized.Length; i++)
        {
            var length = char.IsHighSurrogate(normalized[i]) && i + 1 < normalized.Length ? 2 : 1;
            var piece = normalized.Substring(i, length);
            i += length - 1;
            if (ListingRules.TextOk(piece, 1, 2)) builder.Append(piece);
            if (builder.Length >= max) break;
        }
        var cut = builder.ToString().Trim();
        if (cut.Length <= max) return cut;
        return cut[..(char.IsHighSurrogate(cut[max - 1]) ? max - 1 : max)].TrimEnd();
    }

    /// <summary>Checks a body and returns its fields, or the reason it is refused (the relay reports it as <c>listing-rejected</c>, a client drops the listing).</summary>
    public static (ListingInfo? Info, string? Error) Parse(string body)
    {
        if (Encoding.UTF8.GetByteCount(body) > RelayProtocol.ListingBodyMaxBytes) return (null, "body too large");
        JsonDocument document;
        try { document = JsonDocument.Parse(body); }
        catch (JsonException) { return (null, "body is not JSON"); }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, "body is not an object");
            bool Int(string name, out long value) { value = 0; return root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out value); }
            bool Bool(string name, out bool value) { value = false; if (!root.TryGetProperty(name, out var e) || e.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false; value = e.GetBoolean(); return true; }
            string? Str(string name) => root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

            if (!Int("v", out var v) || v != 1) return (null, "v");
            if (!Int("seq", out var seq) || seq < 0) return (null, "seq");
            if (!Int("players", out var players) || players < 0 || players > 64) return (null, "players");
            if (!Int("slots", out var slots) || slots < 0 || slots > 64) return (null, "slots");
            if (!Int("ai", out var ai) || ai < 0 || ai > 64) return (null, "ai");
            if (!Int("proto", out var proto) || proto is < 0 or > int.MaxValue) return (null, "proto");
            if (!Bool("passwordRequired", out var password) || !Bool("started", out var started)) return (null, "flags");
            var name = Str("name");
            var hostName = Str("hostName");
            if (!TextOk(name, 3, 40)) return (null, "name");
            if (!TextOk(hostName, 1, 32)) return (null, "hostName");
            var rulesetHash = Str("rulesetHash");
            if (!TextOk(rulesetHash, 1, 128)) return (null, "rulesetHash");
            foreach (var field in new[] { "ruleset", "speed", "map", "turnMode", "version" }) if (!TextOk(Str(field), 1, 64)) return (null, field);
            if (!root.TryGetProperty("mods", out var modsElement) || modsElement.ValueKind != JsonValueKind.Array || modsElement.GetArrayLength() > 16) return (null, "mods");
            var mods = new List<string>();
            foreach (var mod in modsElement.EnumerateArray())
            {
                var text = mod.ValueKind == JsonValueKind.String ? mod.GetString() : null;
                if (!TextOk(text, 1, 200)) return (null, "mods");
                mods.Add(text.NotNull());
            }
            string Need(string field) => Str(field).NotNull(); // each was accepted by TextOk above, which refuses null
            return (new ListingInfo(seq, name.NotNull(), hostName.NotNull(), (int)players, (int)slots, (int)ai, Need("ruleset"), rulesetHash.NotNull(), mods, Need("speed"), Need("map"), password, Need("turnMode"), started, Need("version"), (int)proto), null);
        }
    }
}
