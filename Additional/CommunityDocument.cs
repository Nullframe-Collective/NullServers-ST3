using System.Security.Cryptography;
using System.Text;

namespace NullServers.Additional
{
    internal enum CommunityPrompt
    {
        None,
        Rules,
        Updates
    }

    internal static class CommunityDocument
    {
        internal const string Rules = "Welcome to NullFrame Collective!\n\n" +
            "1. Mods and cheats are allowed as long as they do not spoil the game for other players. Respect the host's rules.\n\n" +
            "2. Do not crash servers, disrupt matches, harass other players, or use exploits to harm others.\n\n" +
            "3. Advertising, spam, and unsolicited promotions are not allowed.\n\n" +
            "4. Treat other players with respect and do not share their private information.\n\n" +
            "5. Hosts and administrators may remove disruptive players. Collective-wide bans apply across NullFrame servers.";

        internal static string Normalize(string text) => (text ?? "").TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n').Trim('\n');

        internal static string Revision(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(text))));

        internal static IEnumerable<string> Blocks(string text)
        {
            text = Normalize(text);
            int offset = 0;

            while (offset < text.Length)
            {
                int count = Math.Min(1536, text.Length - offset);
                if (offset + count < text.Length)
                {
                    int newline = text.LastIndexOf('\n', offset + count - 1, count);
                    if (newline >= offset + count / 2)
                    {
                        count = newline - offset + 1;
                    }

                    if (char.IsHighSurrogate(text[offset + count - 1]))
                    {
                        count--;
                    }
                }

                yield return text.Substring(offset, count);
                offset += count;
            }
        }
    }

    internal sealed class CommunityPreferences
    {
        internal const string RulesKey = "NullFrame.Community.RulesAccepted";
        internal const string UpdateKey = "NullFrame.Community.UpdateRevision";
        private readonly Func<string, string> _read;
        private readonly Action<string, string> _write;
        private readonly Action _save;

        internal CommunityPreferences(Func<string, string> read, Action<string, string> write, Action save)
        {
            _read = read;
            _write = write;
            _save = save;
        }

        internal CommunityPrompt Next(string text) => _read(RulesKey) != "1" ? CommunityPrompt.Rules :
            !string.IsNullOrWhiteSpace(text) && _read(UpdateKey) != CommunityDocument.Revision(text) ? CommunityPrompt.Updates : CommunityPrompt.None;

        internal void Acknowledge(CommunityPrompt prompt, string text)
        {
            if (prompt == CommunityPrompt.Rules)
            {
                _write(RulesKey, "1");
            }
            else if (prompt == CommunityPrompt.Updates && !string.IsNullOrWhiteSpace(text))
            {
                _write(UpdateKey, CommunityDocument.Revision(text));
            }
            else
            {
                return;
            }

            _save();
        }
    }
}
