using System.Text.Json;
using System.Text.Json.Nodes;

namespace NullServers.Room
{
    internal static class RoomData
    {
        internal static bool TryParse(string data, out JsonObject properties)
        {
            properties = null;
            if (string.IsNullOrWhiteSpace(data)) return false;

            try
            {
                properties = JsonNode.Parse(data) as JsonObject;
                return properties != null;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        internal static string Text(JsonObject properties, string name) =>
            properties?[name] is JsonValue value && value.TryGetValue(out string text) ? text : null;

        internal static JsonNode Clone(JsonNode node) => node == null ? null : JsonNode.Parse(node.ToJsonString());

        internal static void CopyMissing(JsonObject target, JsonObject source)
        {
            foreach (var property in source)
            {
                if (!target.ContainsKey(property.Key))
                {
                    target[property.Key] = Clone(property.Value);
                }
            }
        }
    }
}
