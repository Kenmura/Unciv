using System.Text.Json;
using System.Text.Json.Serialization;
using Unciv.Json;

namespace Unciv.Logic.Civilization;

// core/src/com/unciv/logic/civilization/Notification.kt
// Stripped: addNotificationIconsTo(Table, ...) and execute(WorldScreen) with its round-robin index/resetExecuteRoundRobin
// (client track; they only drive UI). The top-level typealias NotificationCategory has no C# equivalent: use
// Notification.NotificationCategory.

[JsonConverter(typeof(NotificationConverter))]
public class Notification : IsPartOfGameInfoSerialization
{
    /// <summary>Category - UI grouping, within a Category the most recent Notification will be shown on top</summary>
    public NotificationCategory Category { get; private set; } = NotificationCategory.General;

    /// <summary>The notification text, untranslated - will be translated on the fly</summary>
    public string Text { get; private set; } = "";

    /// <summary>Icons to be shown</summary>
    public List<string> Icons { get; private set; } = [];

    /// <summary>Actions on clicking a Notification - will be activated round-robin style</summary>
    public List<NotificationAction> Actions { get; private set; } = [];

    public Notification()
    {
    }

    public Notification(
        string text,
        string[] notificationIcons,
        IEnumerable<NotificationAction>? actions,
        NotificationCategory category = NotificationCategory.General)
    {
        Category = category;
        Text = text;
        Icons.AddRange(notificationIcons);
        if (actions != null) Actions.AddRange(actions);
    }

    public enum NotificationCategory
    {
        // These names are displayed, so remember to add a translation template
        // - if there's no other source for one.
        General,
        Trade,
        Diplomacy,
        Production,
        Units,
        War,
        Religion,
        Espionage,
        Cities,
    }

    public static NotificationCategory? SafeValueOf(string name) =>
        Enum.GetValues<NotificationCategory>().Cast<NotificationCategory?>().FirstOrDefault(it => it.ToString() == name);

    // Called by NotificationConverter.Read, after the document was parsed: libGDX Json.Serializable.read
    internal void Read(JsonElement jsonData)
    {
        if (jsonData.ValueKind != JsonValueKind.Object)
            throw new JsonException($"Expected an object for Notification, got {jsonData.ValueKind}");
        if (Field(jsonData, "category") is { } category)
        {
            string name = NotificationActionsSerializer.ReadString(category)!;
            Category = SafeValueOf(name) ?? throw new JsonException($"Unable to find enum constant '{name}' of NotificationCategory");
        }
        if (Field(jsonData, "text") is { } text)
            Text = NotificationActionsSerializer.ReadString(text)!;
        if (Field(jsonData, "actions") is { } actions)
        {
            // children of the array (or object) value
            var children = actions.ValueKind == JsonValueKind.Object
                ? actions.EnumerateObject().Select(it => it.Value)
                : actions.ValueKind == JsonValueKind.Array ? actions.EnumerateArray() : [];
            foreach (var entry in children)
                Actions.AddRange(NotificationActionsSerializer.Read(entry));
        }
        if (Field(jsonData, "icons") is { } icons)
        {
            if (icons.ValueKind != JsonValueKind.Array)
                throw new JsonException($"Expected an array for Notification icons, got {icons.ValueKind}");
            Icons = icons.EnumerateArray().Select(it => NotificationActionsSerializer.ReadString(it)!).ToList();
        }
    }

    private static JsonElement? Field(JsonElement data, string name) =>
        data.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value : null;

    /// <summary>
    /// Custom serialization, omitting default values. Example of the serialized format:
    /// <code>
    /// {
    ///     "category":"Production",
    ///     "text":"[Nobel Foundation] has been built in [Stockholm]",
    ///     "icons":["BuildingIcons/Nobel Foundation"],
    ///     "actions":[
    ///         {"LocationAction":{"location":{"x":9,"y":3}}},
    ///         {"CivilopediaAction":{"link":"Wonder/Nobel Foundation"}},
    ///         {"CityAction":{"city":{"x":9,"y":3}}}
    ///     ]
    /// }
    /// </code>
    /// </summary>
    internal void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        if (Category != NotificationCategory.General)
            writer.WriteString("category", Category.ToString());
        if (Text.Length != 0)
            writer.WriteString("text", Text);
        if (Icons.Count != 0)
        {
            writer.WriteStartArray("icons");
            foreach (var icon in Icons) writer.WriteStringValue(icon);
            writer.WriteEndArray();
        }
        if (Actions.Count != 0)
        {
            writer.WriteStartArray("actions");
            foreach (var action in Actions) NotificationActionsSerializer.Write(writer, action);
            writer.WriteEndArray();
        }
        writer.WriteEndObject();
    }
}

internal sealed class NotificationConverter : JsonConverter<Notification>
{
    public override Notification Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var notification = new Notification();
        notification.Read(document.RootElement);
        return notification;
    }

    public override void Write(Utf8JsonWriter writer, Notification value, JsonSerializerOptions options) =>
        value.Write(writer);
}
