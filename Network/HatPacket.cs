namespace NullServers.Network
{
    internal enum HatPacketKind
    {
        Submit,
        Snapshot,
        Remove
    }

    internal sealed record HatEntry(int Player, string[] Hats)
    {
        internal const int ExtraSlots = 3;

        internal bool Valid => Player is >= 0 and <= 4095 && ValidHats(Hats);

        internal static bool ValidHats(string[] hats) =>
            hats is { Length: ExtraSlots } &&
            hats.All(ValidKey) &&
            hats.Where(key => key.Length > 0).Distinct(StringComparer.Ordinal).Count() == hats.Count(key => key.Length > 0);

        internal static bool ValidKey(string key)
        {
            if (key == "") return true;
            if (key is not { Length: > 2 and <= 128 }) return false;

            if (key.StartsWith("base:", StringComparison.Ordinal))
            {
                return int.TryParse(key[5..], out int hat) && hat is > 0 and <= 255 && key == "base:" + hat;
            }

            return key.Contains(':') && key.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or ':' or '-' or '_' or '.');
        }
    }

    internal sealed record HatPacket(int Version, string Room, int RoomObject, HatPacketKind Kind, HatEntry[] Players)
    {
        internal const ushort BroadcastId = 0x4E4B;
        internal const int MaxBytes = 16384;

        internal bool Submission(int player) => Kind == HatPacketKind.Submit && Players.Length == 1 && Players[0].Player == player;

        internal static bool TryParse(string json, out HatPacket packet)
        {
            packet = JsonPacket.Read<HatPacket>(json, MaxBytes);

            if (packet == null || packet.Version != 1 || packet.Room is not { Length: > 0 and <= 64 } ||
                packet.RoomObject < 0 || !Enum.IsDefined(typeof(HatPacketKind), packet.Kind) ||
                packet.Players is not { Length: > 0 and <= 16 } ||
                packet.Players.Any(player => player?.Valid != true) ||
                packet.Players.Select(player => player.Player).Distinct().Count() != packet.Players.Length ||
                packet.Kind == HatPacketKind.Submit && packet.Players.Length != 1)
            {
                packet = null;
                return false;
            }

            return true;
        }
    }
}
