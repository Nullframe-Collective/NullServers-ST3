using Il2Cpp;
using Il2CppFishNet.Transporting.Relay;

namespace NullServers.Room
{
    internal static class RoomJoin
    {
        internal static void Handle(LobbyMenu menu, RelayRoomInfo room, string text, ref bool pressed)
        {
            if (!pressed || menu == null || room == null || string.IsNullOrEmpty(room.Id) ||
                room.MaxPlayers < 1 || room.MaxPlayers > RoomOptions.PlayerLimit ||
                room.PlayerCount < 0 || room.PlayerCount >= room.MaxPlayers ||
                string.IsNullOrEmpty(menu.JoinCaption) || text == null ||
                !text.StartsWith("<size=", StringComparison.Ordinal) ||
                !text.EndsWith(">" + menu.JoinCaption + "</size>", StringComparison.Ordinal)) return;

            pressed = false;
            menu.JoinRoom(room.Id);
        }
    }
}
