using System.Globalization;
using System.Text;
using System.Text.Json;
using Unciv.Net.Relay;

namespace Unciv.Relay;

/// <summary>The schema and text rules of a listing (RELAY.md section 7 and A8). A relay and a client both run these, so a hostile relay cannot smuggle control or bidi characters into the browser.</summary>
public static class ListingValidator
{
    private static readonly string[] ShortTexts = ["ruleset", "speed", "map", "turnMode", "version"];

    /// <summary>True when the text is NFC, has 1 to <paramref name="max"/> characters and holds no control, format (bidi, zero-width), separator, surrogate-half, private-use or unassigned character.</summary>
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

    /// <summary>Checks a body and returns its fields and <c>seq</c>, or the reason (always <c>listing-rejected</c> on the wire).</summary>
    internal static (ListingFields? Fields, long Seq, string? Error) Validate(string body)
    {
        if (Encoding.UTF8.GetByteCount(body) > RelayProtocol.ListingBodyMaxBytes) return (null, 0, "body too large");
        JsonDocument document;
        try { document = JsonDocument.Parse(body); }
        catch (JsonException) { return (null, 0, "body is not JSON"); }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, 0, "body is not an object");
            bool Int(string name, out long value) { value = 0; return root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out value); }
            bool Bool(string name, out bool value) { value = false; if (!root.TryGetProperty(name, out var e) || e.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false; value = e.GetBoolean(); return true; }
            string? Str(string name) => root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

            if (!Int("v", out var v) || v != 1) return (null, 0, "v");
            if (!Int("seq", out var seq) || seq < 0) return (null, 0, "seq");
            if (!Int("players", out var players) || players < 0 || players > 64) return (null, 0, "players");
            if (!Int("slots", out var slots) || slots < 0 || slots > 64) return (null, 0, "slots");
            if (!Int("ai", out var ai) || ai < 0 || ai > 64) return (null, 0, "ai");
            if (!Int("proto", out _)) return (null, 0, "proto");
            if (!Bool("passwordRequired", out var password) || !Bool("started", out var started)) return (null, 0, "flags");
            var name = Str("name");
            var hostName = Str("hostName");
            if (!TextOk(name, 3, 40)) return (null, 0, "name");
            if (!TextOk(hostName, 1, 32)) return (null, 0, "hostName");
            if (!TextOk(Str("rulesetHash"), 1, 128)) return (null, 0, "rulesetHash");
            foreach (var field in ShortTexts) if (!TextOk(Str(field), 1, 64)) return (null, 0, field);
            if (!root.TryGetProperty("mods", out var modsElement) || modsElement.ValueKind != JsonValueKind.Array || modsElement.GetArrayLength() > 16) return (null, 0, "mods");
            var mods = new List<string>();
            foreach (var mod in modsElement.EnumerateArray())
            {
                var text = mod.ValueKind == JsonValueKind.String ? mod.GetString() : null;
                if (!TextOk(text, 1, 200)) return (null, 0, "mods");
                mods.Add(text!);
            }
            return (new ListingFields(name!, hostName!, (int)players, (int)slots, Str("ruleset")!, mods, password, started, Str("version")!), seq, null);
        }
    }
}
