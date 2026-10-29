using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Unciv.Json;
using Unciv.Utils;

namespace Unciv.Logic.Map.MapUnits;

// T110: the Gdx-compatible JSON of MapUnit. Kotlin has no custom serializer: the libGDX Json writes the persisted fields in declaration
// order, omitting those equal to the field of a fresh MapUnit() (usePrototypes), and ignores unknown fields on read (ignoreUnknownFields).

public partial class MapUnit
{
    /// <summary>
    /// Reads and writes the persisted fields the way the libGDX Json does for Kotlin <c>MapUnit</c>: Kotlin field names, declaration order,
    /// default-valued fields omitted (an empty <c>promotions</c> object is omitted through <c>UnitPromotions.equals</c>), unknown fields ignored.
    /// Lists may be a plain array or the <c>{"class":..., "items":[...]}</c> form. Transient members are never written; hash collections
    /// are written in their Java iteration order.
    /// </summary>
    internal sealed class MapUnitJsonConverter : JsonConverter<MapUnit>
    {
        private delegate T ElementReader<out T>(ref Utf8JsonReader reader, JsonSerializerOptions options);

        public override MapUnit Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException($"Expected StartObject for MapUnit, got {reader.TokenType}");
            var unit = new MapUnit();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject) return unit;
                var name = reader.GetString();
                reader.Read();
                var isNull = reader.TokenType == JsonTokenType.Null;
                switch (name)
                {
                    case "owner":
                        unit.owner = isNull ? null : reader.GetString();
                        break;
                    case "originalOwner":
                        unit.OriginalOwner = isNull ? null : reader.GetString();
                        break;
                    case "name":
                        unit.name = isNull ? null : reader.GetString();
                        break;
                    case "instanceName":
                        unit.InstanceName = isNull ? null : reader.GetString();
                        break;
                    case "currentMovement":
                        unit.CurrentMovement = JsonValueReader.ReadFloat(ref reader, "MapUnit.currentMovement");
                        break;
                    case "health":
                        unit.Health = JsonValueReader.ReadInt(ref reader, "MapUnit.health");
                        break;
                    case "id":
                        unit.Id = JsonValueReader.ReadInt(ref reader, "MapUnit.id");
                        break;
                    case "action":
                        unit.Action = isNull ? null : reader.GetString();
                        break;
                    case "automated":
                        unit.Automated = reader.GetBoolean();
                        break;
                    case "escorting":
                        unit.escorting = reader.GetBoolean();
                        break;
                    case "automatedRoadConnectionDestination":
                        unit.AutomatedRoadConnectionDestination = isNull ? null : ReadHexCoord(ref reader, options);
                        break;
                    case "attacksThisTurn":
                        unit.AttacksThisTurn = JsonValueReader.ReadInt(ref reader, "MapUnit.attacksThisTurn");
                        break;
                    case "promotions":
                        unit.Promotions = isNull ? new UnitPromotions() : JsonSerializer.Deserialize<UnitPromotions>(ref reader, options) ?? new UnitPromotions();
                        break;
                    case "due":
                        unit.Due = reader.GetBoolean();
                        break;
                    case "isTransported":
                        unit.IsTransported = reader.GetBoolean();
                        break;
                    case "turnsFortified":
                        unit.TurnsFortified = JsonValueReader.ReadInt(ref reader, "MapUnit.turnsFortified");
                        break;
                    case "abilityToTimesUsed":
                        unit.AbilityToTimesUsed = isNull ? new() : JsonSerializer.Deserialize<JavaHashMap<string, int>>(ref reader, options) ?? new();
                        break;
                    case "religion":
                        unit.Religion = isNull ? null : reader.GetString();
                        break;
                    case "religiousStrengthLost":
                        unit.ReligiousStrengthLost = JsonValueReader.ReadInt(ref reader, "MapUnit.religiousStrengthLost");
                        break;
                    case "exoticGoodsSold":
                        unit.ExoticGoodsSold = JsonValueReader.ReadInt(ref reader, "MapUnit.exoticGoodsSold");
                        break;
                    case "movementMemories":
                        unit.MovementMemories = isNull ? [] : ReadList(ref reader, options, ReadMovementMemory);
                        break;
                    case "mostRecentMoveType":
                        unit.MostRecentMoveType = ReadMoveType(ref reader);
                        break;
                    case "attacksSinceTurnStart":
                        unit.AttacksSinceTurnStart = isNull ? [] : ReadList(ref reader, options, ReadHexCoord);
                        break;
                    case "statusMap":
                        unit.StatusMap = isNull ? new() : ReadStatusMap(ref reader);
                        break;
                    default:
                        reader.Skip(); // ignoreUnknownFields
                        break;
                }
            }
            throw new JsonException("Unexpected end of JSON while reading MapUnit");
        }

        private static HexCoord ReadHexCoord(ref Utf8JsonReader reader, JsonSerializerOptions options) =>
            new HexCoordConverter().Read(ref reader, typeof(HexCoord), options);

        private static List<T> ReadList<T>(ref Utf8JsonReader reader, JsonSerializerOptions options, ElementReader<T> readElement)
        {
            var result = new List<T>();
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    var key = reader.GetString();
                    reader.Read();
                    if (key == "items") result = ReadList(ref reader, options, readElement);
                    else reader.Skip();
                }
                return result;
            }
            if (reader.TokenType != JsonTokenType.StartArray)
                throw new JsonException($"Expected StartArray for a list, got {reader.TokenType}");
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                result.Add(readElement(ref reader, options));
            return result;
        }

        private static UnitMovementMemory ReadMovementMemory(ref Utf8JsonReader reader, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException($"Expected StartObject for UnitMovementMemory, got {reader.TokenType}");
            var position = HexCoord.Zero;
            var type = UnitMovementMemoryType.UnitMoved;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var key = reader.GetString();
                reader.Read();
                if (key == "position") position = ReadHexCoord(ref reader, options);
                else if (key == "type") type = ReadMoveType(ref reader);
                else reader.Skip();
            }
            return new UnitMovementMemory(position, type);
        }

        private static UnitMovementMemoryType ReadMoveType(ref Utf8JsonReader reader)
        {
            var text = reader.GetString();
            if (text == null || !Enum.TryParse<UnitMovementMemoryType>(text, true, out var type) || !Enum.IsDefined(type))
                throw new JsonException($"Unknown UnitMovementMemoryType '{text}'");
            return type;
        }

        private static JavaHashMap<string, UnitStatus> ReadStatusMap(ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException($"Expected StartObject for statusMap, got {reader.TokenType}");
            var map = new JavaHashMap<string, UnitStatus>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var key = reader.GetString().NotNull();
                reader.Read();
                if (reader.TokenType != JsonTokenType.StartObject)
                    throw new JsonException($"Expected StartObject for UnitStatus, got {reader.TokenType}");
                var statusName = "";
                var turnsLeft = 1;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    var field = reader.GetString();
                    reader.Read();
                    if (field == "name") statusName = reader.GetString() ?? "";
                    else if (field == "turnsLeft") turnsLeft = JsonValueReader.ReadInt(ref reader, "UnitStatus.turnsLeft");
                    else reader.Skip();
                }
                map.Put(key, new UnitStatus(statusName, turnsLeft));
            }
            return map;
        }

        /// <summary>
        /// Kotlin <c>Float.toString()</c> (what the Gdx JsonWriter emits for a Float): shortest round-trip digits, ".0" for integral values,
        /// <c>1.0E7</c> style exponents below 1e-3 and from 1e7.
        /// </summary>
        internal static string FloatToJavaString(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return value.ToString(CultureInfo.InvariantCulture);
            var shortest = value.ToString("R", CultureInfo.InvariantCulture);
            var magnitude = Math.Abs(value);
            if (magnitude == 0f || (magnitude >= 1e-3f && magnitude < 1e7f))
            {
                if (shortest.Contains('E')) shortest = ((decimal)value).ToString(CultureInfo.InvariantCulture); // e.g. 1E-03
                return shortest.Contains('.') ? shortest : shortest + ".0";
            }
            var negative = shortest.StartsWith('-');
            var unsigned = shortest.TrimStart('-');
            var exponentAt = unsigned.IndexOf('E');
            var mantissa = exponentAt < 0 ? unsigned : unsigned[..exponentAt];
            var exponent = exponentAt < 0 ? 0 : int.Parse(unsigned[(exponentAt + 1)..], CultureInfo.InvariantCulture);
            var pointAt = mantissa.IndexOf('.');
            var integerLength = pointAt < 0 ? mantissa.Length : pointAt;
            var allDigits = mantissa.Replace(".", "", StringComparison.Ordinal);
            var leadingZeros = allDigits.Length - allDigits.TrimStart('0').Length;
            var digits = allDigits.TrimStart('0').TrimEnd('0');
            exponent += integerLength - 1 - leadingZeros;
            var text = digits[..1] + "." + (digits.Length > 1 ? digits[1..] : "0") + "E" + exponent.ToString(CultureInfo.InvariantCulture);
            return negative ? "-" + text : text;
        }

        public override void Write(Utf8JsonWriter writer, MapUnit value, JsonSerializerOptions options)
        {
            var coordConverter = new HexCoordConverter();
            writer.WriteStartObject();
            if (value.owner != null) writer.WriteString("owner", value.owner);
            if (value.OriginalOwner != null) writer.WriteString("originalOwner", value.OriginalOwner);
            if (value.name != null) writer.WriteString("name", value.name);
            if (value.InstanceName != null) writer.WriteString("instanceName", value.InstanceName);
            if (value.CurrentMovement != 0f)
            {
                writer.WritePropertyName("currentMovement");
                writer.WriteRawValue(FloatToJavaString(value.CurrentMovement));
            }
            if (value.Health != 100) writer.WriteNumber("health", value.Health);
            if (value.Id != Constants.NoId) writer.WriteNumber("id", value.Id);
            if (value.Action != null) writer.WriteString("action", value.Action);
            if (value.Automated) writer.WriteBoolean("automated", true);
            if (value.escorting) writer.WriteBoolean("escorting", true);
            if (value.AutomatedRoadConnectionDestination is { } destination)
            {
                writer.WritePropertyName("automatedRoadConnectionDestination");
                coordConverter.Write(writer, destination, options);
            }
            if (value.AttacksThisTurn != 0) writer.WriteNumber("attacksThisTurn", value.AttacksThisTurn);
            if (!value.Promotions.Equals(new UnitPromotions()))
            {
                writer.WriteStartObject("promotions");
                if (value.Promotions.XP != 0) writer.WriteNumber("XP", value.Promotions.XP);
                if (value.Promotions.Promotions.Count > 0)
                {
                    writer.WriteStartArray("promotions");
                    foreach (var promotion in value.Promotions.Promotions) writer.WriteStringValue(promotion);
                    writer.WriteEndArray();
                }
                if (value.Promotions.NumberOfPromotions != 0) writer.WriteNumber("numberOfPromotions", value.Promotions.NumberOfPromotions);
                writer.WriteEndObject();
            }
            if (!value.Due) writer.WriteBoolean("due", false);
            if (value.IsTransported) writer.WriteBoolean("isTransported", true);
            if (value.TurnsFortified != 0) writer.WriteNumber("turnsFortified", value.TurnsFortified);
            if (value.AbilityToTimesUsed.Count > 0)
            {
                writer.WriteStartObject("abilityToTimesUsed");
                foreach (var (key, used) in value.AbilityToTimesUsed) writer.WriteNumber(key, used);
                writer.WriteEndObject();
            }
            if (value.Religion != null) writer.WriteString("religion", value.Religion);
            if (value.ReligiousStrengthLost != 0) writer.WriteNumber("religiousStrengthLost", value.ReligiousStrengthLost);
            if (value.ExoticGoodsSold != 0) writer.WriteNumber("exoticGoodsSold", value.ExoticGoodsSold);
            if (value.MovementMemories.Count > 0)
            {
                writer.WriteStartArray("movementMemories");
                foreach (var memory in value.MovementMemories)
                {
                    writer.WriteStartObject();
                    if (memory.Position != HexCoord.Zero)
                    {
                        writer.WritePropertyName("position");
                        coordConverter.Write(writer, memory.Position, options);
                    }
                    if (memory.Type != UnitMovementMemoryType.UnitMoved) writer.WriteString("type", memory.Type.ToString());
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            if (value.MostRecentMoveType != UnitMovementMemoryType.UnitMoved) writer.WriteString("mostRecentMoveType", value.MostRecentMoveType.ToString());
            if (value.AttacksSinceTurnStart.Count > 0)
            {
                writer.WriteStartArray("attacksSinceTurnStart");
                foreach (var coord in value.AttacksSinceTurnStart) coordConverter.Write(writer, coord, options);
                writer.WriteEndArray();
            }
            if (value.StatusMap.Count > 0)
            {
                writer.WriteStartObject("statusMap");
                foreach (var (key, status) in value.StatusMap)
                {
                    writer.WriteStartObject(key);
                    if (status.Name != "") writer.WriteString("name", status.Name);
                    if (status.TurnsLeft != 1) writer.WriteNumber("turnsLeft", status.TurnsLeft);
                    writer.WriteEndObject();
                }
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
    }
}
