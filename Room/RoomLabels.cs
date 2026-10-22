namespace NullServers.Room
{
    internal static class RoomLabels
    {
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

            if 
            (
                slash <= start ||
                end <= slash ||
                !int.TryParse(text.AsSpan(start, slash - start), out var count) ||
                !int.TryParse(text.AsSpan(slash + 1, end - slash - 1), out _) ||
                count != playerCount
            )
            {
                return text;
            }

            return text[..(slash + 1)] + maxPlayers + text[end..];
        }
    }
}
