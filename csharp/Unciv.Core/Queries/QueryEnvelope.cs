using System.Text.Json;

namespace Unciv.Queries;

// The wire form of a query. ViewerId (a civ name) is assigned by the authenticated session, never read from the payload.
public sealed record QueryEnvelope(string Type, string ViewerId, JsonElement Payload);

// The wire form of the answer. Result is a json value when Ok.
public sealed record QueryResponse(bool Ok, string Code, string Message, JsonElement? Result)
{
    public static QueryResponse Success(JsonElement result) => new(true, "", "", result);

    public static QueryResponse Fail(string code, string message) => new(false, code, message, null);
}

// Machine-readable codes for failures the infrastructure itself produces; queries throw QueryException for their own.
public static class QueryCodes
{
    public const string UnknownQuery = "unknown-query";
    public const string MalformedQuery = "malformed-query";
    public const string UnknownViewer = "unknown-viewer";
    public const string InternalError = "internal-error";
}

// A query rejects a request it can answer only with an error (for example an id that does not exist for this viewer).
public sealed class QueryException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
