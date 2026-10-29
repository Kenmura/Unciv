using System.Collections.Frozen;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Unciv.Net;

/// <summary>
/// JSON frame codec: one <c>type</c> per frame, strict schema (unknown or missing fields rejected), bounded size.
/// Decoding never throws on bad input; it yields an <see cref="Error"/> message instead.
/// </summary>
public static class ProtocolCodec
{
    private const string TypeKey = "type";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        RespectNullableAnnotations = true,
        MaxDepth = 64,
        WriteIndented = false
    };

    private static readonly FrozenDictionary<string, Type> TypesByName = typeof(Message).Assembly.GetTypes()
        .Where(t => t.IsSubclassOf(typeof(Message)) && !t.IsAbstract)
        .ToFrozenDictionary(t => t.Name, t => t, StringComparer.Ordinal);

    public static IReadOnlyCollection<Type> MessageTypes => TypesByName.Values;

    public static string Encode(Message message)
    {
        var type = message.GetType();
        var body = JsonSerializer.SerializeToNode(message, type, Options) as JsonObject
            ?? throw new InvalidOperationException("Message did not serialize to a JSON object");
        var frame = new JsonObject { [TypeKey] = type.Name };
        foreach (var property in body.ToList())
        {
            body.Remove(property.Key);
            frame[property.Key] = property.Value;
        }
        var text = frame.ToJsonString(Options);
        if (Encoding.UTF8.GetByteCount(text) > ProtocolVersion.MaxMessageBytes)
            throw new InvalidOperationException("Encoded message exceeds ProtocolVersion.MaxMessageBytes");
        return text;
    }

    /// <param name="frame"></param>
    /// <param name="maxBytes">Cap on the frame; the host passes <see cref="ProtocolVersion.MaxFrameBytes"/> for what clients send.</param>
    public static Message Decode(string frame, int maxBytes = ProtocolVersion.MaxMessageBytes)
    {
        if (Encoding.UTF8.GetByteCount(frame) > maxBytes)
            return Fail(ErrorCodes.FrameTooLarge);

        JsonObject? node;
        try
        {
            node = JsonNode.Parse(frame, documentOptions: new JsonDocumentOptions { MaxDepth = Options.MaxDepth, AllowDuplicateProperties = false }) as JsonObject;
        }
        catch (Exception e) when (e is JsonException or ArgumentException)
        {
            return Fail(ErrorCodes.Malformed);
        }
        if (node == null) return Fail(ErrorCodes.Malformed);

        if (!node.TryGetPropertyValue(TypeKey, out var typeNode)
            || typeNode is not JsonValue typeValue || !typeValue.TryGetValue<string>(out var typeName))
            return Fail(ErrorCodes.Malformed);
        if (!TypesByName.TryGetValue(typeName, out var type)) return Fail(ErrorCodes.UnknownType);

        node.Remove(TypeKey);
        try
        {
            return (Message?)node.Deserialize(type, Options) ?? Fail(ErrorCodes.InvalidMessage);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or InvalidOperationException or TargetInvocationException)
        {
            return Fail(ErrorCodes.InvalidMessage);
        }
    }

    private static Error Fail(string code) => new() { Code = code };
}
