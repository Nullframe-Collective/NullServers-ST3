using S3_NullServers.Room;
using S3_NullServers.Server;
using System.Text;
using System.Text.Json;

namespace S3_NullServers.Network
{
    internal enum ServerAction
    {
        Kick,
        Ban,
        Unban,
        Room,
        Round,
        Reopen,
        Mute,
        Unmute
    }

    internal sealed record ServerRequest(
        ServerAction Action,
        int PlayerId = -1,
        string Account = null,
        string Name = null,
        int Capacity = 0,
        string Color = null,
        string Scene = null,
        string Mode = null,
        int Minutes = 0,
        int Custards = 0,
        int Difficulty = 0,
        bool MuteGuests = false);

    internal sealed record RoomPlayer(int Id, string Account, string Nickname, bool Guest, bool Trusted, bool Muted);

    internal sealed record ServerState(bool Trusted, RoomPlayer[] Players, BanEntry[] Bans, BanEntry[] Mutes);

    internal sealed record ServerMessage(
        int Version,
        string RoomId,
        ServerRequest Request = null,
        ServerState State = null,
        string Notice = null)
    {
        internal const ushort BroadcastId = 0x4E48;
        internal const int MaxBytes = 131072;

        internal static bool TryParse(string json, out ServerMessage message)
        {
            message = null;
            if (string.IsNullOrEmpty(json) || Encoding.UTF8.GetByteCount(json) > MaxBytes)
            {
                return false;
            }

            try
            {
                ServerMessage value = JsonSerializer.Deserialize<ServerMessage>(json);
                if (value == null ||
                    value.Version != 1 ||
                    string.IsNullOrEmpty(value.RoomId) ||
                    value.RoomId.Length > 64 ||
                    (value.Request != null ? 1 : 0) + (value.State != null ? 1 : 0) + (value.Notice != null ? 1 : 0) != 1)
                {
                    return false;
                }

                if (value.Request is { } request &&
                    (!Enum.IsDefined(typeof(ServerAction), request.Action) ||
                    request.PlayerId < -1 ||
                    request.Account?.Length > 256 ||
                    request.Name?.Length > RoomName.MaxDisplayBytes ||
                    request.Color?.Length > 7 ||
                    request.Scene?.Length > 128 ||
                    request.Mode?.Length > 32))
                {
                    return false;
                }

                if (value.Notice?.Length > 512)
                {
                    return false;
                }

                if (value.State is { } state &&
                    (state.Players == null ||
                    state.Bans == null ||
                    state.Mutes == null ||
                    state.Players.Length > RoomOptions.PlayerLimit ||
                    state.Bans.Length > 500 ||
                    state.Mutes.Length > 500 ||
                    state.Players.Any(player => player == null ||
                    player.Id < 0 ||
                    string.IsNullOrEmpty(player.Account) ||
                    player.Account.Length > 256 ||
                    player.Nickname == null ||
                    player.Nickname.Length > 128) ||
                    state.Players.Select(player => player.Id).Distinct().Count() != state.Players.Length ||
                    state.Bans.Concat(state.Mutes).Any(entry => entry == null ||
                    string.IsNullOrWhiteSpace(entry.Account) ||
                    entry.Account.Length > 256)))
                {
                    return false;
                }

                message = value;
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
