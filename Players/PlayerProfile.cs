using NullServers.Network;
using NullServers.Room;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace NullServers.Players
{
    internal sealed class PlayerProfile
    {
        public string Nickname { get; }
        public string ColorHex { get; }
        public NameColorMode ColorMode { get; }
        public string GradientColor { get; }
        public float ColorSpeed { get; }

        public PlayerProfile(
            string nickname,
            string colorHex,
            NameColorMode colorMode = NameColorMode.Static,
            string gradientColor = "#55AAFF",
            float colorSpeed = 1f)
        {
            Nickname = nickname;
            ColorHex = colorHex;
            ColorMode = colorMode;
            GradientColor = gradientColor;
            ColorSpeed = colorSpeed;
        }

        internal const int NameLimit = 24;
        internal static readonly PlayerProfile Default = new("", "#FFFFFF");

        internal string RichName => NameColors.RichName(this, NameColors.Now);
        internal string CurrentColor => NameColors.Color(this, NameColors.Now);

        internal static PlayerProfile Normalize(
            string nickname,
            string colorHex,
            NameColorMode colorMode = NameColorMode.Static,
            string gradientColor = "#55AAFF",
            float colorSpeed = 1f)
        {
            StringBuilder text = new();
            int count = 0;

            foreach (Rune rune in (nickname ?? "").Trim().EnumerateRunes())
            {
                if (Rune.IsControl(rune) || Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format)
                {
                    continue;
                }

                if (count++ == NameLimit) break;

                text.Append(rune.Value switch

                {
                    '<' => "‹",
                    '>' => "›",
                    '|' => "¦",
                    _ => rune.ToString()
                });
            }

            return new PlayerProfile(
                text.ToString().Trim(),
                RoomOptions.IsValidColor(colorHex) ? colorHex.ToUpperInvariant() : "#FFFFFF",
                Enum.IsDefined(typeof(NameColorMode), colorMode) ? colorMode : NameColorMode.Static,
                RoomOptions.IsValidColor(gradientColor) ? gradientColor.ToUpperInvariant() : "#55AAFF",
                float.IsFinite(colorSpeed) ? Math.Clamp(colorSpeed, 0.1f, 5f) : 1f);
        }

        internal bool Matches(PlayerProfile other) =>
            other != null && Nickname == other.Nickname && ColorHex == other.ColorHex &&
            ColorMode == other.ColorMode && GradientColor == other.GradientColor && ColorSpeed == other.ColorSpeed;

        internal bool IsValid =>
            Nickname != null &&
            Nickname.Length > 0 &&
            RoomOptions.IsValidColor(ColorHex) &&
            Matches(Normalize(Nickname, ColorHex, ColorMode, GradientColor, ColorSpeed));
    }

    internal enum ProfilePacketKind
    {
        Profile,
        Snapshot,
        Remove
    }

    internal sealed class ProfileEntry(int clientId, PlayerProfile profile)
    {
        public int ClientId { get; } = clientId;
        public PlayerProfile Profile { get; } = profile;
    }

    internal sealed class ProfilePacket(int version, ProfilePacketKind kind, ProfileEntry[] players)
    {
        public int Version { get; } = version;
        public ProfilePacketKind Kind { get; } = kind;
        public ProfileEntry[] Players { get; } = players;

        internal const ushort BroadcastId = 0x4E46;
        internal const int MaxBytes = 32768;

        internal static ProfilePacket Update(int clientId, PlayerProfile profile)
        {
            return new(1, ProfilePacketKind.Profile, [new ProfileEntry(clientId, profile)]);
        }

        internal string Serialize() => JsonSerializer.Serialize(this);

        internal static bool TryParse(string json, out ProfilePacket packet)
        {
            packet = null;
            ProfilePacket value = JsonPacket.Read<ProfilePacket>(json, MaxBytes);
            if (value == null ||
                value.Version != 1 ||
                value.Players == null ||
                value.Players.Length > RoomOptions.PlayerLimit ||
                !Enum.IsDefined(typeof(ProfilePacketKind), value.Kind) ||
                (value.Kind != ProfilePacketKind.Snapshot && value.Players.Length != 1))
            {
                return false;
            }

            HashSet<int> ids = new();
            foreach (ProfileEntry entry in value.Players)
            {
                if (entry == null ||
                    entry.ClientId < 0 ||
                    !ids.Add(entry.ClientId) ||
                    (value.Kind != ProfilePacketKind.Remove && (entry.Profile == null || !entry.Profile.IsValid)))
                {
                    return false;
                }
            }

            packet = value;

            return true;
        }
    }
}
