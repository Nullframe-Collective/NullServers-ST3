using System.Text.Json;

namespace NullServers.Room
{
    internal static class RoomLabels
    {
        private static readonly Dictionary<(string Text, string Data), string> ModeCache = new();

        internal static string FormatGameMode(string text, string data)
        {
            var key = (text ?? "", data ?? "");
            if (ModeCache.TryGetValue(key, out var cached)) return cached;

            if (ModeCache.Count >= 256)
            {
                ModeCache.Clear();
            }

            return ModeCache[key] = ParseGameMode(text, data);
        }

        private static string ParseGameMode(string text, string data)
        {
            if (string.IsNullOrEmpty(text) || !text.StartsWith("<size=", StringComparison.Ordinal) ||
                !text.EndsWith("</size>", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(data)) return text;

            try
            {
                using JsonDocument document = JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = 12 });
                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !document.RootElement.TryGetProperty("CustomMode", out var custom) || custom.ValueKind != JsonValueKind.Object ||
                    !custom.TryGetProperty("Protocol", out var protocol) || protocol.ValueKind != JsonValueKind.Number ||
                    !protocol.TryGetInt32(out int version) || version != 1 ||
                    !custom.TryGetProperty("Id", out var id) || id.ValueKind != JsonValueKind.String) return text;

                string name = id.GetString();
                if (string.IsNullOrWhiteSpace(name) || name.Length > 64 || name.Any(char.IsControl)) return text;

                if (custom.TryGetProperty("Name", out var display) && display.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(display.GetString())) name = display.GetString();

                name = name.Replace('<', '‹').Replace('>', '›');
                name = new string(name.Select(character => char.IsControl(character) ? ' ' : character).ToArray()).Trim();
                int start = text.IndexOf('>') + 1;
                if (start <= 0 || start >= text.Length - 7) return text;

                string content = text.Substring(start, text.Length - start - 7);
                if (content.TrimStart().StartsWith("<color=grey>", StringComparison.Ordinal) &&
                    content.EndsWith("</color>", StringComparison.Ordinal)) name = $"<color=grey>{name}</color>";

                return text[..start] + name + "</size>";
            }
            catch (JsonException)
            {
                return text;
            }
        }

        internal static bool TryFormatName(string text, string name, string color, out string formatted, string display = null)
        {
            formatted = text;

            string escaped = (name ?? "").Replace('<', '‹').Replace('>', '›');
            string suffix = $"> {escaped}</size>";

            if (!text.EndsWith(suffix, StringComparison.Ordinal))
            {
                return text.EndsWith($"> <color=grey>{escaped}</color></size>", StringComparison.Ordinal);
            }

            string visible = display ?? escaped;
            if (RoomOptions.IsValidColor(color))
            {
                visible = $"<color={color}>{visible}</color>";
            }

            formatted = text[..^suffix.Length] + $"> {visible}</size>";

            return true;
        }

        internal static string FormatPlayerCount(string text, int playerCount, int maxPlayers)
        {
            if (!text.StartsWith("<size=", StringComparison.Ordinal)) return text;

            int start = text.IndexOf('>') + 1;
            int slash = text.IndexOf('/', start);
            int end = text.LastIndexOf("</size>", StringComparison.Ordinal);

            if (slash <= start ||
                end <= slash ||
                !int.TryParse(text.AsSpan(start, slash - start), out var count) ||
                !int.TryParse(text.AsSpan(slash + 1, end - slash - 1), out _) ||
                count != playerCount)
            {
                return text;
            }

            return text[..(slash + 1)] + maxPlayers + text[end..];
        }
    }
}
