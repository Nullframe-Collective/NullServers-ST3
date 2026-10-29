using HarmonyLib;
using Il2Cpp;
using NullServers.Players;
using NullServers.Room;

namespace NullServers.Patches
{
    [HarmonyPatch(typeof(RoomMultiplayerMenu), nameof(RoomMultiplayerMenu.Start))]
    internal static class RoomMultiplayerMenuStartPatch
    {
        public static bool Prefix(RoomMultiplayerMenu __instance) => RoundSync.BeforeRoomStart(__instance);
    }

    [HarmonyPatch(typeof(RoomMultiplayerMenu), nameof(RoomMultiplayerMenu.SpawnPlayer))]
    internal static class RoomMultiplayerMenuSpawnPlayerPatch
    {
        public static bool Prefix(RoomMultiplayerMenu __instance, string teamName) => RoundSync.BeforePlayerSpawn(__instance, teamName);
    }

    [HarmonyPatch(typeof(RoomMultiplayerMenu), nameof(RoomMultiplayerMenu.LMHENDCDNHN))]
    internal static class RoomMultiplayerMenuListBuiltPatch
    {
        public static void Postfix(RoomMultiplayerMenu __instance) => PlayerList.AfterListBuilt(__instance);
    }

    [HarmonyPatch(typeof(RoomMultiplayerMenu), nameof(RoomMultiplayerMenu.Update))]
    internal static class RoomMultiplayerMenuUpdatePatch
    {
        public static void Postfix(RoomMultiplayerMenu __instance) => PlayerList.AfterMenuUpdate(__instance);
    }
}
