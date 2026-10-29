using HarmonyLib;
using Il2CppFishNet.Connection;
using Il2CppFishNet.Managing.Server;
using Il2CppFishNet.Serializing;
using Il2CppFishNet.Transporting;
using NullServers.Players;
using NullServers.Server;

namespace NullServers.Patches
{
    [HarmonyPatch(typeof(ServerManager), nameof(ServerManager.ParseBroadcast))]
    internal static class ServerManagerParseBroadcastForPlayerProfilesPatch
    {
        public static bool Prefix(ServerManager __instance, PooledReader reader, NetworkConnection conn, Channel channel) => PlayerProfiles.BeforeServerBroadcast(__instance, reader, conn, channel);
    }

    [HarmonyPatch(typeof(ServerManager), nameof(ServerManager.ParseBroadcast))]
    internal static class ServerManagerParseBroadcastForAntiCrashPatch
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(PooledReader reader, NetworkConnection conn, Channel channel, out AntiCrash.Frame __state) =>
            AntiCrash.BeforeBroadcast(reader, conn, channel, out __state);

        [HarmonyPriority(Priority.First)]
        public static Exception Finalizer(Exception __exception, AntiCrash.Frame __state) => AntiCrash.AfterPacket(__exception, __state);
    }
}
