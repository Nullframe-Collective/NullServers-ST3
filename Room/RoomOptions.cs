using System.Text.Json;

namespace NullServers.Room
{
    internal sealed class RoomOptions
    {
        internal const int PlayerLimit = 50;

        internal int MaxPlayers { get; set; } = 12;
        internal string NameColor { get; set; } = "#FFFFFF";
        internal string DisplayName { get; set; }

        internal static bool IsValidColor(string color)
        {
            return color is { Length: 7 } && color[0] == '#' && color.Skip(1).All(Uri.IsHexDigit);
        }

        internal static string ReadNameColor(string roomData)
        {
            if (string.IsNullOrWhiteSpace(roomData)) return null;

            try
            {
                using JsonDocument document = JsonDocument.Parse(roomData);
                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !document.RootElement.TryGetProperty(nameof(NameColor), out var property) ||
                    property.ValueKind != JsonValueKind.String)
                {
                    return null;
                }

                string color = property.GetString();
                return IsValidColor(color) ? color : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
