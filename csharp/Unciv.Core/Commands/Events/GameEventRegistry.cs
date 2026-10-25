using System.Reflection;
using System.Text.Json;
using Unciv.Json;
using Unciv.Utils;

namespace Unciv.Commands.Events;

/// <summary>The wire form of an event: its registered name and its fields.</summary>
public sealed record GameEventEnvelope(string Type, JsonElement Data);

/// <summary>
/// Every <see cref="EngineEvent"/> marked with <see cref="EventTypeAttribute"/>, found by scanning an assembly, plus the JSON
/// round trip. Registration order is the ordinal order of the wire names, so it never depends on reflection order.
/// </summary>
public static class GameEventRegistry
{
    private static readonly KotlinLazy<Dictionary<string, Type>> DefaultTypes = new(() => Scan().ToDictionary(it => it.Name, it => it.EventClass));

    /// <summary>All concrete <see cref="EngineEvent"/> classes of <paramref name="assembly"/> (default: this engine), ordered by wire name.</summary>
    public static IReadOnlyList<(string Name, Type EventClass)> Scan(Assembly? assembly = null)
    {
        var found = (assembly ?? typeof(EngineEvent).Assembly).GetTypes()
            .Where(it => it is { IsClass: true, IsAbstract: false } && typeof(EngineEvent).IsAssignableFrom(it))
            .Select(it => (Name: EventTypeAttribute.NameOf(it), EventClass: it))
            .OrderBy(it => it.Name, StringComparer.Ordinal)
            .ToList();
        for (int i = 0; i < found.Count; i++)
        {
            var (name, eventClass) = found[i];
            if (!GameCommandRegistry.IsKebabCase(name))
                throw new InvalidOperationException($"{eventClass.Name}: event type \"{name}\" must be kebab-case");
            if (i > 0 && found[i - 1].Name == name)
                throw new InvalidOperationException($"Event type \"{name}\" is used by {found[i - 1].EventClass.Name} and {eventClass.Name}");
            if (!eventClass.IsSealed)
                throw new InvalidOperationException($"{eventClass.Name} must be sealed");
        }
        return found;
    }

    public static GameEventEnvelope ToEnvelope(EngineEvent gameEvent)
    {
        string name = EventTypeAttribute.NameOf(gameEvent.GetType());
        if (!DefaultTypes.Value.TryGetValue(name, out var registered) || registered != gameEvent.GetType())
            throw new InvalidOperationException($"{gameEvent.GetType().Name} is not registered under \"{name}\"");
        return new GameEventEnvelope(name, JsonSerializer.SerializeToElement(gameEvent, gameEvent.GetType(), UncivJson.DefaultOptions));
    }

    /// <summary>Untrusted input: reports problems instead of throwing.</summary>
    public static bool TryFromEnvelope(GameEventEnvelope envelope, out EngineEvent? gameEvent, out string error)
    {
        gameEvent = null;
        error = "";
        if (!DefaultTypes.Value.TryGetValue(envelope.Type, out var type))
        {
            error = $"Unknown event type \"{envelope.Type}\"";
            return false;
        }
        if (envelope.Data.ValueKind != JsonValueKind.Object)
        {
            error = $"Data of \"{envelope.Type}\" must be a json object";
            return false;
        }
        try
        {
            gameEvent = (EngineEvent?)envelope.Data.Deserialize(type, UncivJson.DefaultOptions);
        }
        catch (Exception e) when (e is JsonException or FormatException or NotSupportedException or InvalidOperationException)
        {
            error = $"Data of \"{envelope.Type}\" is invalid: {e.Message}";
            return false;
        }
        if (gameEvent != null) return true;
        error = $"Data of \"{envelope.Type}\" is empty";
        return false;
    }

    public static EngineEvent FromEnvelope(GameEventEnvelope envelope) =>
        TryFromEnvelope(envelope, out var gameEvent, out string error)
            ? gameEvent ?? throw new InvalidOperationException(error)
            : throw new InvalidOperationException(error);
}
