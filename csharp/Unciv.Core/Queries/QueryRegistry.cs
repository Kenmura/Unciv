using System.Reflection;
using System.Text.Json;
using Unciv.Utils;

namespace Unciv.Queries;

// Maps wire type names to query classes (T185). Like CommandRegistry, but query classes can also be found by scanning an
// assembly for [QueryType], so a query ticket never edits a shared registry file.
public sealed class QueryRegistry(JsonSerializerOptions options)
{
    private readonly SortedDictionary<string, Type> typeByName = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, string> nameByType = [];

    public IReadOnlyCollection<string> RegisteredTypes => typeByName.Keys;

    public void Register<TQuery>(string type) where TQuery : IQuery => Register(typeof(TQuery), type);

    public void Register(Type queryClass, string type)
    {
        if (string.IsNullOrEmpty(type))
            throw new InvalidOperationException("Query type name must not be empty");
        if (!typeof(IQuery).IsAssignableFrom(queryClass) || queryClass.IsAbstract)
            throw new InvalidOperationException($"{queryClass.Name} is not a concrete IQuery");
        if (typeByName.ContainsKey(type))
            throw new InvalidOperationException($"Query type \"{type}\" is already registered");
        if (nameByType.ContainsKey(queryClass))
            throw new InvalidOperationException($"{queryClass.Name} is already registered");
        typeByName.Add(type, queryClass);
        nameByType.Add(queryClass, type);
    }

    /// <summary>Registers every non-abstract <see cref="IQuery"/> class of the assembly carrying <see cref="QueryTypeAttribute"/>.</summary>
    public void RegisterFromAssembly(Assembly assembly)
    {
        var found = assembly.GetTypes()
            .Select(it => (Class: it, Attribute: it.GetCustomAttribute<QueryTypeAttribute>(inherit: false)))
            .Where(it => it.Attribute != null)
            .Select(it => (it.Class, Name: it.Attribute.NotNull().Name))
            .OrderBy(it => it.Name, StringComparer.Ordinal);
        var all = found.ToList();
        // Validate everything before registering anything, so a bad class leaves the registry untouched.
        foreach (var (queryClass, name) in all)
        {
            if (queryClass.IsAbstract || !typeof(IQuery).IsAssignableFrom(queryClass))
                throw new InvalidOperationException($"{queryClass.Name} has [QueryType] but is not a concrete IQuery");
            if (typeByName.ContainsKey(name) || nameByType.ContainsKey(queryClass))
                throw new InvalidOperationException($"{queryClass.Name} (\"{name}\") is already registered");
        }
        foreach (var (queryClass, name) in all)
            Register(queryClass, name);
    }

    public QueryEnvelope ToEnvelope(string viewerId, IQuery query)
    {
        if (!nameByType.TryGetValue(query.GetType(), out string? registered) || registered != query.Type)
            throw new InvalidOperationException($"{query.GetType().Name} is not registered under its type \"{query.Type}\"");
        return new QueryEnvelope(query.Type, viewerId, JsonSerializer.SerializeToElement(query, query.GetType(), options));
    }

    // Untrusted input: reports problems as a failed response instead of throwing.
    public bool TryFromEnvelope(QueryEnvelope envelope, out IQuery? query, out QueryResponse failure)
    {
        query = null;
        if (envelope.Type == null || !typeByName.TryGetValue(envelope.Type, out var type))
        {
            failure = QueryResponse.Fail(QueryCodes.UnknownQuery, $"Unknown query type \"{envelope.Type}\"");
            return false;
        }
        if (envelope.Payload.ValueKind != JsonValueKind.Object)
        {
            failure = QueryResponse.Fail(QueryCodes.MalformedQuery, $"Payload of \"{envelope.Type}\" must be a json object");
            return false;
        }
        try
        {
            query = (IQuery?)envelope.Payload.Deserialize(type, options);
        }
        catch (Exception e) when (e is JsonException or FormatException or NotSupportedException)
        {
            failure = QueryResponse.Fail(QueryCodes.MalformedQuery, $"Payload of \"{envelope.Type}\" is invalid: {e.Message}");
            return false;
        }
        // An object payload never deserializes to null.
        if (query.NotNull().Type != envelope.Type)
        {
            query = null;
            failure = QueryResponse.Fail(QueryCodes.MalformedQuery, $"Class registered as \"{envelope.Type}\" reports another type");
            return false;
        }
        failure = QueryResponse.Fail("", "");
        return true;
    }

    /// <summary>Decodes, runs and serializes one query against the live state. Never throws for bad input.</summary>
    public QueryResponse Execute(QueryEnvelope envelope, QueryContext context)
    {
        if (!TryFromEnvelope(envelope, out var query, out var failure)) return failure;
        try
        {
            if (envelope.ViewerId == null || !context.TryCreateView(envelope.ViewerId, out var view))
                return QueryResponse.Fail(QueryCodes.UnknownViewer, $"Unknown viewer \"{envelope.ViewerId}\"");
            object? result = query.NotNull().RunBoxed(view.NotNull());
            if (result == null) return QueryResponse.Fail(QueryCodes.InternalError, $"Query \"{envelope.Type}\" returned no result");
            return QueryResponse.Success(JsonSerializer.SerializeToElement(result, result.GetType(), options));
        }
        catch (QueryException e)
        {
            return QueryResponse.Fail(e.Code, e.Message);
        }
        catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException or InsufficientExecutionStackException))
        {
            // A bug in a query (or a payload that provokes one) must not take the host down.
            return QueryResponse.Fail(QueryCodes.InternalError, $"Query \"{envelope.Type}\" failed: {e.GetType().Name}");
        }
    }
}
