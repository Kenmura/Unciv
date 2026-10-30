using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using Unciv.Utils;

namespace Unciv.Core.Tests.Support;

/// <summary>
/// T296: a reflective snapshot of every instance field reachable from a root object, and of the static collections and primitives of the engine assembly.
/// Two fingerprints of the same graph are equal only when no field (public, private, transient or lazy cache) was written in between, so a test can
/// prove that a code path is read-only for the shared game state. One entry per object: <c>Type#id{field=value;...}</c>; references print the visit id
/// of the target, collections print their count and elements, delegates only whether they are set.
/// </summary>
public static class ObjectGraphFingerprint
{
    private const BindingFlags AllFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static List<string> Of(object root)
    {
        var entries = new List<string>();
        var ids = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<(object Value, string Via)>();
        int IdOf(object value, string via)
        {
            if (ids.TryGetValue(value, out var id)) return id;
            id = ids.Count;
            ids[value] = id;
            queue.Enqueue((value, via));
            return id;
        }

        IdOf(root, "root");
        while (queue.Count > 0)
        {
            var (obj, via) = queue.Dequeue();
            var sb = new StringBuilder();
            var type = obj.GetType();
            var owner = Short(type);
            sb.Append(CultureInfo.InvariantCulture, $"{type.FullName}#{ids[obj]}<-{via}{{");
            if (obj is IDictionary dictionary)
            {
                sb.Append(CultureInfo.InvariantCulture, $"count={dictionary.Count};");
                foreach (DictionaryEntry entry in dictionary) sb.Append(Format(entry.Key, v => IdOf(v, owner + "[]"))).Append("=>").Append(Format(entry.Value, v => IdOf(v, owner + "[]"))).Append(';');
            }
            else if (obj is IEnumerable enumerable and not string)
            {
                var index = 0;
                foreach (var item in enumerable) { sb.Append(Format(item, v => IdOf(v, owner + "[]"))).Append(','); index++; }
                sb.Append(CultureInfo.InvariantCulture, $"count={index};");
            }
            if (!IsFramework(type) || obj is ICollection)
                foreach (var field in FieldsOf(type))
                    sb.Append(field.Name).Append('=').Append(Format(field.GetValue(obj), v => IdOf(v, owner + "." + field.Name))).Append(';');
            sb.Append('}');
            entries.Add(sb.ToString());
        }
        return entries;
    }

    /// <summary>Static fields of the <paramref name="assembly"/>'s types: primitives and strings by value, collections by count, <see cref="KotlinLazy{T}"/> by created state.</summary>
    public static List<string> OfStatics(Assembly assembly)
    {
        var entries = new List<string>();
        foreach (var type in assembly.GetTypes().OrderBy(it => it.FullName, StringComparer.Ordinal))
        {
            if (type.IsGenericTypeDefinition || type.Name.Contains('<', StringComparison.Ordinal)) continue;
            foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                         .OrderBy(it => it.Name, StringComparer.Ordinal))
            {
                if (field.Name.Contains('<', StringComparison.Ordinal) || field.IsLiteral || field.FieldType.IsPointer) continue;
                object? value;
                try { value = field.GetValue(null); }
                catch (TargetInvocationException) { continue; } // a type initializer that throws is not a cache
                entries.Add($"{type.FullName}.{field.Name}={StaticSummary(value)}");
            }
        }
        return entries;
    }

    private static string StaticSummary(object? value) => value switch
    {
        null => "null",
        string or ValueType => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
        ICollection collection => $"count={collection.Count}",
        Delegate => "delegate",
        _ when value.GetType() is { IsGenericType: true } generic && generic.GetGenericTypeDefinition() == typeof(KotlinLazy<>) =>
            $"created={generic.GetProperty(nameof(KotlinLazy<int>.IsValueCreated))!.GetValue(value)}",
        _ => "ref"
    };

    private static string Short(Type type) => type.Name.Split('`')[0];

    private static IEnumerable<FieldInfo> FieldsOf(Type type)
    {
        for (var current = type; current != null && current != typeof(object); current = current.BaseType)
            foreach (var field in current.GetFields(AllFields | BindingFlags.DeclaredOnly).OrderBy(it => it.Name, StringComparer.Ordinal))
                if (!field.FieldType.IsPointer) yield return field;
    }

    private static bool IsFramework(Type type) =>
        type.Namespace != null && (type.Namespace == "System" || type.Namespace.StartsWith("System.", StringComparison.Ordinal) || type.Namespace.StartsWith("Microsoft.", StringComparison.Ordinal));

    private static string Format(object? value, Func<object, int> idOf)
    {
        switch (value)
        {
            case null: return "null";
            case string text: return "\"" + text + "\"";
            case Type or Delegate or Assembly or MemberInfo: return "set";
            case Enum or ValueType when value.GetType().IsPrimitive || value is Enum or decimal: return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            case ValueType:
                // a struct (HexCoord, tuple, KeyValuePair, Nullable): print its fields inline
                var sb = new StringBuilder("(");
                foreach (var field in FieldsOf(value.GetType())) sb.Append(Format(field.GetValue(value), idOf)).Append(',');
                return sb.Append(')').ToString();
            default:
                return "@" + idOf(value);
        }
    }
}
