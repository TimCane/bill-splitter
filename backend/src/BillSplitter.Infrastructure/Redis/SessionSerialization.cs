using System.Text.Json;
using System.Text.Json.Serialization;
using BillSplitter.Domain;

namespace BillSplitter.Infrastructure.Redis;

/// <summary>Serializes the <see cref="Session"/> aggregate to the Redis document
/// (camelCase, string enums) - the shape in docs/03-redis-schema.md. The CAS Lua
/// reads <c>version</c> as a JSON number from this output.</summary>
public static class SessionSerialization
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(Session session) => JsonSerializer.Serialize(session, Options);

    public static Session Deserialize(string json) =>
        JsonSerializer.Deserialize<Session>(json, Options)
        ?? throw new InvalidOperationException("Session document deserialized to null.");
}
