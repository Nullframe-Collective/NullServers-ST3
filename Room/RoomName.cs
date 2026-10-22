using System.Text;
using System.Text.Json;

namespace NullServers.Room
{
    internal static class RoomName
    {
        internal const int MaxBytes = 64;
        internal const int MaxDisplayBytes = 512;
        private static readonly Dictionary<string, string> NamedColors = new(StringComparer.OrdinalIgnoreCase)

        {
            ["red"] = "#FF0000",
            ["green"] = "#008000",
            ["blue"] = "#0000FF",
            ["white"] = "#FFFFFF",
            ["black"] = "#000000",
            ["yellow"] = "#FFFF00",
            ["cyan"] = "#00FFFF",
            ["magenta"] = "#FF00FF",
            ["orange"] = "#FFA500",
            ["purple"] = "#800080",
            ["lime"] = "#00FF00",
            ["brown"] = "#A52A2A",
            ["grey"] = "#808080",
            ["gray"] = "#808080",
            ["silver"] = "#C0C0C0",
            ["navy"] = "#000080",
            ["teal"] = "#008080",
            ["olive"] = "#808000",
            ["maroon"] = "#800000",
            ["aqua"] = "#00FFFF",
            ["fuchsia"] = "#FF00FF"
        };

        internal static bool TryParse(string text, out string plain, out string display, out string error)
        {
            plain = display = null;
            error = "Enter a room name.";
            text = text?.Trim();

            if (string.IsNullOrEmpty(text)) return false;

            if (Encoding.UTF8.GetByteCount(text) > MaxDisplayBytes)
            {
                error = "The formatted name is too long (512 UTF-8 bytes maximum).";

                return false;
            }

            StringBuilder visible = new();
            StringBuilder formatted = new();
            int depth = 0;

            for (int index = 0; index < text.Length; index++)
            {
                char character = text[index];
                if (character == '<')
                {
                    if (text.AsSpan(index).StartsWith("</color>", StringComparison.OrdinalIgnoreCase) && depth > 0)
                    {
                        depth--;
                        formatted.Append("</color>");
                        index += 7;

                        continue;
                    }

                    int end = text.IndexOf('>', index);
                    if 
                    (
                        end > index + 7 &&
                        text.AsSpan(index).StartsWith("<color=", StringComparison.OrdinalIgnoreCase) &&
                        TryColor(text.Substring(index + 7, end - index - 7), out var color) &&
                        depth < 8
                    )
                    {
                        depth++;
                        formatted.Append("<color=").Append(color).Append('>');
                        index = end;

                        continue;
                    }

                    error = "Use matching <color=#RRGGBB> and </color> tags only.";

                    return false;
                }

                if (char.IsHighSurrogate(character) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
                {
                    visible.Append(character).Append(text[index + 1]);
                    formatted.Append(character).Append(text[++index]);

                    continue;
                }

                if (character == '>' || character == '|' || char.IsControl(character) || char.IsSurrogate(character))
                {
                    error = "The name contains an unsupported character.";

                    return false;
                }

                visible.Append(character);
                formatted.Append(character);
            }

            if (depth != 0)
            {
                error = "Close every color tag with </color>.";

                return false;
            }

            plain = visible.ToString().Trim();
            if (plain.Length == 0 || Encoding.UTF8.GetByteCount(plain) > MaxBytes)
            {
                error = "The visible name must contain 1–64 UTF-8 bytes.";

                return false;
            }

            display = formatted.ToString();
            if (Encoding.UTF8.GetByteCount(display) > MaxDisplayBytes)
            {
                error = "The formatted name is too long (512 UTF-8 bytes maximum).";

                return false;
            }

            error = null;

            return true;
        }

        private static bool TryColor(string value, out string color)
        {
            color = null;

            if (RoomOptions.IsValidColor(value))
            {
                color = value.ToUpperInvariant();
            }
            else if (value.Length == 4 && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit))
            {
                color = new string
                (
                    [
                        '#',
                        value[1],
                        value[1],
                        value[2],
                        value[2],
                        value[3],
                        value[3]
                    ]
                ).ToUpperInvariant();
            }
            else
            {
                NamedColors.TryGetValue(value, out color);
            }

            return color != null;
        }

        internal static string ReadDisplay(string data, string plain)
        {
            if (!string.IsNullOrWhiteSpace(data))
            {
                try
                {
                    using var document = JsonDocument.Parse(data);
                    if 
                    (
                        document.RootElement.ValueKind == JsonValueKind.Object &&
                        document.RootElement.TryGetProperty("DisplayName", out var value) &&
                        value.ValueKind == JsonValueKind.String &&
                        TryParse(value.GetString(), out var name, out var display, out _) &&
                        name == plain
                    )
                    {
                        return display;
                    }
                }
                catch (JsonException) { }
            }

            return (plain ?? "").Replace('<', '‹').Replace('>', '›');
        }

        internal static string Render(string plain, string data)
        {
            string display = ReadDisplay(data, plain);
            string color = RoomOptions.ReadNameColor(data);
            return color == null ? display : $"<color={color}>{display}</color>";
        }
    }
}
