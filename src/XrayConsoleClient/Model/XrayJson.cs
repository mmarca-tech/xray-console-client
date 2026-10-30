using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace XrayConsoleClient;

static class XrayJson
{
    // camelCase, null properties left out (like undefined in the npm client), non-ascii text sent as is.
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static StringContent Content<T>(T value) => new(Serialize(value), Encoding.UTF8, "application/json");

    // A string or number value as text, null when missing or of another kind.
    public static string? Text(JsonNode? node) =>
        node is JsonValue value
            ? value.GetValueKind() switch
            {
                JsonValueKind.String => value.GetValue<string>(),
                JsonValueKind.Number => value.ToJsonString(),
                _ => null
            }
            : null;

    public static JsonArray Items(JsonNode? node) => node as JsonArray ?? [];
}
