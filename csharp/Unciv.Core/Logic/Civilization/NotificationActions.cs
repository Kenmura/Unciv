using System.Text;
using System.Text.Json;
using Unciv.Json;
using Unciv.Logic.Map;
using Unciv.UI.Screens.OverviewScreen;
using Unciv.Utils;

namespace Unciv.Logic.Civilization;

// core/src/com/unciv/logic/civilization/NotificationActions.kt
// Pure serializable data: what to do when the user clicks on a notification. Stripped: execute(WorldScreen) of every
// action (client track), and the constructors/factories that need City, MapUnit or Civilization (MapUnitAction(unit),
// MapUnitAction(units), PromoteUnitAction(unit), DiplomacyAction(civ), CityAction.withLocation(city)) until those are ported.
// Not realized as lambdas, so no references to objects that should not be serialized sneak in.

/// <summary>defines what to do if the user clicks on a notification</summary>
public abstract class NotificationAction : IsPartOfGameInfoSerialization
{
}

/// <summary>A notification action that shows map places.</summary>
public sealed class LocationAction(HexCoord location = default) : NotificationAction
{
    internal HexCoord Location { get; } = location;

    // Companion invoke() factories of the Kotlin class, which simulate the old LocationAction storing several locations.
    // The params version accepts and ignores nulls, often making pre-filtering of conditionally available locations unnecessary.
    public static IEnumerable<LocationAction> Of(IEnumerable<HexCoord> locations) =>
        locations.Select(it => new LocationAction(it));

    public static IEnumerable<LocationAction> Of(params HexCoord?[] locations) =>
        locations.Where(it => it != null).Select(it => new LocationAction(it.NotNull()));
}

/// <summary>show tech screen</summary>
public sealed class TechAction(string techName = "") : NotificationAction
{
    internal string TechName { get; } = techName;
}

/// <summary>enter city</summary>
public sealed class CityAction(HexCoord city = default) : NotificationAction
{
    internal HexCoord City { get; } = city;
}

/// <summary>enter diplomacy screen</summary>
public sealed class DiplomacyAction(string otherCivName = "", bool showTrade = false) : NotificationAction
{
    internal string OtherCivName { get; } = otherCivName;
    internal bool ShowTrade { get; } = showTrade;
}

/// <summary>enter Maya Long Count popup</summary>
public sealed class MayaLongCountAction : NotificationAction
{
}

/// <summary>
/// A notification action that shows **and selects** things on the map.
/// Saves and serializes only the location and optionally a MapUnit id.
/// </summary>
public sealed class MapUnitAction(HexCoord location = default, int id = Constants.NoId) : NotificationAction
{
    internal HexCoord Location { get; } = location;
    internal int Id { get; } = id;
}

/// <summary>A notification action that shows a Civilopedia entry, e.g. for a Wonder.</summary>
public sealed class CivilopediaAction(string link = "") : NotificationAction
{
    internal string Link { get; } = link;
}

/// <summary>Show Promotion picker for a MapUnit - by name and location, as they lack a serialized unique ID</summary>
public sealed class PromoteUnitAction(string name = "", HexCoord location = default, int id = Constants.NoId) : NotificationAction
{
    internal string Name { get; } = name;
    internal HexCoord Location { get; } = location;
    public int Id { get; } = id;
}

/// <summary>Open the Empire Overview to a specific page, potentially "selecting" some entry</summary>
public sealed class OverviewAction(EmpireOverviewCategories page = EmpireOverviewCategories.Resources, string select = "") : NotificationAction
{
    internal EmpireOverviewCategories Page { get; } = page;
    internal string Select { get; } = select;
}

/// <summary>Open policy picker, optionally preselecting <c>select</c></summary>
public sealed class PolicyAction(string? select = null) : NotificationAction
{
    internal string? Select { get; } = select;
}

/// <summary>Open EspionageScreen</summary>
public sealed class EspionageAction : NotificationAction
{
    public static IEnumerable<NotificationAction> WithLocation(HexCoord? location) =>
        LocationAction.Of(location).Cast<NotificationAction>().Append(new EspionageAction());
}

/// <summary>Open <c>url</c> externally in the browser</summary>
public sealed class LinkAction(string url = "") : NotificationAction
{
    internal string Url { get; } = url;
}

/// <summary>Open the Empire Overview on the Religion tab</summary>
public sealed class ReligionAction(string? religionName = null) : NotificationAction
{
    internal string? ReligionName { get; } = religionName;

    public static IEnumerable<NotificationAction> WithLocation(HexCoord? location, string? religionName) =>
        LocationAction.Of(location).Cast<NotificationAction>().Append(new ReligionAction(religionName));
}

/// <summary>
/// Json for <see cref="NotificationAction"/>s, in libGDX's format with the prototype optimization: each action is an
/// object <c>{"ClassName":{fields differing from the no-arg instance}}</c>.
/// Reading mirrors Kotlin's NotificationActionsDeserializer: every known class name found in the object yields one action
/// (in the fixed order below), unknown names and fields are ignored.
/// </summary>
internal static class NotificationActionsSerializer
{
    private static readonly HexCoordConverter HexCoords = new();

    public static string ToJson(NotificationAction action)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            Write(writer, action);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static void Write(Utf8JsonWriter writer, NotificationAction action)
    {
        writer.WriteStartObject();
        writer.WriteStartObject(action.GetType().Name);
        switch (action)
        {
            case LocationAction a:
                WriteHexCoord(writer, "location", a.Location, default);
                break;
            case TechAction a:
                WriteString(writer, "techName", a.TechName, "");
                break;
            case CityAction a:
                WriteHexCoord(writer, "city", a.City, default);
                break;
            case DiplomacyAction a:
                WriteString(writer, "otherCivName", a.OtherCivName, "");
                if (a.ShowTrade) writer.WriteBoolean("showTrade", true);
                break;
            case MapUnitAction a:
                WriteHexCoord(writer, "location", a.Location, default);
                WriteInt(writer, "id", a.Id);
                break;
            case CivilopediaAction a:
                WriteString(writer, "link", a.Link, "");
                break;
            case PromoteUnitAction a:
                WriteString(writer, "name", a.Name, "");
                WriteHexCoord(writer, "location", a.Location, default);
                WriteInt(writer, "id", a.Id);
                break;
            case OverviewAction a:
                if (a.Page != EmpireOverviewCategories.Resources) writer.WriteString("page", a.Page.ToString());
                WriteString(writer, "select", a.Select, "");
                break;
            case PolicyAction a:
                WriteString(writer, "select", a.Select, null);
                break;
            case LinkAction a:
                WriteString(writer, "url", a.Url, "");
                break;
            case ReligionAction a:
                WriteString(writer, "religionName", a.ReligionName, null);
                break;
            case MayaLongCountAction:
            case EspionageAction:
                break;
            default:
                throw new JsonException($"Cannot serialize unknown NotificationAction {action.GetType().Name}");
        }
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteString(Utf8JsonWriter writer, string name, string? value, string? defaultValue)
    {
        if (value != defaultValue) writer.WriteString(name, value);
    }

    private static void WriteInt(Utf8JsonWriter writer, string name, int value)
    {
        if (value != Constants.NoId) writer.WriteNumber(name, value);
    }

    private static void WriteHexCoord(Utf8JsonWriter writer, string name, HexCoord value, HexCoord defaultValue)
    {
        if (value == defaultValue) return;
        writer.WritePropertyName(name);
        HexCoords.Write(writer, value, UncivJson.DefaultOptions);
    }

    /// <summary>Reads one entry of the actions array: an object that normally has exactly one class-named member.</summary>
    public static List<NotificationAction> Read(JsonElement entry)
    {
        var result = new List<NotificationAction>();
        if (entry.ValueKind != JsonValueKind.Object)
            throw new JsonException($"Expected an object for a NotificationAction, got {entry.ValueKind}");
        Add(result, entry, "LocationAction", f => new LocationAction(HexCoordField(f, "location")));
        Add(result, entry, "TechAction", f => new TechAction(StringField(f, "techName") ?? ""));
        Add(result, entry, "CityAction", f => new CityAction(HexCoordField(f, "city")));
        Add(result, entry, "DiplomacyAction", f => new DiplomacyAction(StringField(f, "otherCivName") ?? "", BoolField(f, "showTrade")));
        Add(result, entry, "MayaLongCountAction", _ => new MayaLongCountAction());
        Add(result, entry, "MapUnitAction", f => new MapUnitAction(HexCoordField(f, "location"), IntField(f, "id")));
        Add(result, entry, "CivilopediaAction", f => new CivilopediaAction(StringField(f, "link") ?? ""));
        Add(result, entry, "PromoteUnitAction", f => new PromoteUnitAction(StringField(f, "name") ?? "", HexCoordField(f, "location"), IntField(f, "id")));
        Add(result, entry, "OverviewAction", f => new OverviewAction(PageField(f), StringField(f, "select") ?? ""));
        Add(result, entry, "PolicyAction", f => new PolicyAction(StringField(f, "select")));
        Add(result, entry, "EspionageAction", _ => new EspionageAction());
        Add(result, entry, "LinkAction", f => new LinkAction(StringField(f, "url") ?? ""));
        Add(result, entry, "ReligionAction", f => new ReligionAction(StringField(f, "religionName")));
        return result;
    }

    private static void Add(List<NotificationAction> result, JsonElement entry, string className, Func<JsonElement, NotificationAction> create)
    {
        if (!entry.TryGetProperty(className, out var fields) || fields.ValueKind == JsonValueKind.Null) return;
        if (fields.ValueKind != JsonValueKind.Object)
            throw new JsonException($"Expected an object for {className}, got {fields.ValueKind}");
        result.Add(create(fields));
    }

    private static JsonElement? Field(JsonElement fields, string name) =>
        fields.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value : null;

    internal static string? ReadString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
        _ => throw new JsonException($"Cannot read a string from {value.ValueKind}")
    };

    private static string? StringField(JsonElement fields, string name) =>
        Field(fields, name) is { } value ? ReadString(value) : null;

    private static bool BoolField(JsonElement fields, string name) => Field(fields, name) is not { } value
        ? false
        : value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => string.Equals(value.GetString(), "true", StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Number => value.GetDouble() != 0.0,
            _ => throw new JsonException($"Cannot read a boolean from {value.ValueKind}")
        };

    private static int IntField(JsonElement fields, string name)
    {
        if (Field(fields, name) is not { } value) return Constants.NoId;
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(value.GetRawText()));
        reader.Read();
        return JsonValueReader.ReadInt(ref reader, name);
    }

    private static HexCoord HexCoordField(JsonElement fields, string name)
    {
        if (Field(fields, name) is not { } value) return default;
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(value.GetRawText()));
        reader.Read();
        return HexCoords.Read(ref reader, typeof(HexCoord), UncivJson.DefaultOptions);
    }

    private static EmpireOverviewCategories PageField(JsonElement fields)
    {
        if (Field(fields, "page") is not { } value) return EmpireOverviewCategories.Resources;
        string name = ReadString(value).NotNull();
        return Enum.GetValues<EmpireOverviewCategories>().Cast<EmpireOverviewCategories?>().FirstOrDefault(it => it.ToString() == name)
            ?? throw new JsonException($"Unable to find enum constant '{name}' of EmpireOverviewCategories");
    }
}
