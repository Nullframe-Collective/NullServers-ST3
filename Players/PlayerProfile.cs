using S3_NullServers.Room;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace S3_NullServers.Players
{
    internal sealed class PlayerProfile
    {
        public string Nickname
        {
            get;
        }
        public string ColorHex
        {
            get;
        }

        public PlayerProfile(string nickname, string colorHex)
        {
            Nickname = nickname;
            ColorHex = colorHex;
        }

        internal const int NameLimit = 24;
        internal static readonly PlayerProfile Default = new("", "#FFFFFF");

        internal string RichName => $"<color={ColorHex}>{Nickname}</color>";

        internal static PlayerProfile Normalize(string nickname, string colorHex)
        {
            StringBuilder text = new();
            int count = 0;
            foreach (Rune rune in (nickname ?? "").Trim().EnumerateRunes())
            {
                if (Rune.IsControl(rune) || Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format)
                {
                    continue;
                }

                if (count++ == NameLimit)
                {
                    break;
                }

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
                RoomOptions.IsValidColor(colorHex) ? colorHex.ToUpperInvariant() : "#FFFFFF");
        }

        internal bool Matches(PlayerProfile other) => other != null && Nickname == other.Nickname && ColorHex == other.ColorHex;

        internal bool IsValid => Nickname != null &&
            Nickname.Length > 0 &&
            RoomOptions.IsValidColor(ColorHex) &&
            Matches(Normalize(Nickname, ColorHex));
    }

    internal enum ProfilePacketKind
    {
        Profile,
        Snapshot,
        Remove
    }

    internal sealed class ProfileEntry
    {
        public int ClientId
        {
            get;
        }
        public PlayerProfile Profile
        {
            get;
        }

        public ProfileEntry(int clientId, PlayerProfile profile)
        {
            ClientId = clientId;
            Profile = profile;
        }
    }

    internal sealed class ProfilePacket
    {
        public int Version
        {
            get;
        }
        public ProfilePacketKind Kind
        {
            get;
        }
        public ProfileEntry[] Players
        {
            get;
        }

        public ProfilePacket(int version, ProfilePacketKind kind, ProfileEntry[] players)
        {
            Version = version;
            Kind = kind;
            Players = players;
        }

        internal const ushort BroadcastId = 0x4E46;
        internal const int MaxBytes = 32768;

        internal static ProfilePacket Update(int clientId, PlayerProfile profile)
        {
            return new(1, ProfilePacketKind.Profile, new[] { new ProfileEntry(clientId, profile) });
        }

        internal string Serialize() => JsonSerializer.Serialize(this);

        internal static bool TryParse(string json, out ProfilePacket packet)
        {
            packet = null;
            if (string.IsNullOrEmpty(json) || Encoding.UTF8.GetByteCount(json) > MaxBytes)
            {
                return false;
            }

            try
            {
                ProfilePacket value = JsonSerializer.Deserialize<ProfilePacket>(json);
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
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
